using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Consumer.AzureSQL.Target;

namespace PenguinConverters.Syntra.Consumer.AzureSQL;

/// <summary>
/// Azure SQL consumer that writes entities to a MSSQL destination in bulk.
/// Entities are accumulated into batches, bulk-copied into a session-scoped staging table,
/// and folded into the target with a single set-based MERGE per batch.
/// </summary>
/// <remarks>
/// The staging table is a local temporary table, which is session-scoped. That requires one
/// <see cref="SqlConnection"/> held open for the whole run, and <see cref="SqlConnection"/> is
/// not thread-safe, so this consumer processes the provider stream sequentially. That is not a
/// regression: parallelism previously existed to hide per-row round-trip latency, and bulk
/// loading removes the per-row round-trip entirely.
/// </remarks>
public class Consumer : Core.Target.Consumer
{
    private Configuration? _configuration;

    private SqlConnection? _connection;
    private string? _stagingTable;
    private string? _seenKeysTable;
    private List<string> _columnOrder = new List<string>();
    private DataTable? _batch;
    private DataTable? _seenKeyBatch;
    private long _rowsStaged;
    private long _batchesFlushed;

    /// <summary>
    /// Gets or sets the consumer configuration.
    /// </summary>
    internal Configuration? Configuration
    {
        get => _configuration;
        set => _configuration = value;
    }

    /// <summary>
    /// Deserializes the raw configuration bytes and applies them to this consumer.
    /// </summary>
    internal void DeserializeAndApplyConfiguration()
    {
        _configuration = DeserializeConfiguration<Configuration>()
            ?? throw new InvalidOperationException("Failed to deserialize Azure SQL consumer configuration.");
    }

    /// <inheritdoc />
    public override async Task SynchronizeAsync(IProvider provider, CancellationToken cancellationToken = default)
    {
        if (_configuration is null)
        {
            Logger.LogError("Azure SQL consumer configuration is not set.");
            HadErrors = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(_configuration.TableName))
        {
            Logger.LogError("Azure SQL consumer requires a target table name.");
            HadErrors = true;
            return;
        }

        if (_configuration.Columns is null || _configuration.Columns.Count == 0)
        {
            Logger.LogError("Azure SQL consumer requires at least one column definition.");
            HadErrors = true;
            return;
        }

        if (_configuration.PrimaryKeys is null || _configuration.PrimaryKeys.Count == 0)
        {
            Logger.LogError("Azure SQL consumer requires at least one primary key column.");
            HadErrors = true;
            return;
        }

        Logger.LogInformation(
            "Starting Azure SQL bulk synchronization to table '{TableName}' with batch size {BatchSize}.",
            _configuration.TableName,
            _configuration.BatchSize);

        try
        {
            await OpenAndPrepareAsync(cancellationToken).ConfigureAwait(false);

            IEnumerable<string> properties = _configuration.Columns.Values
                .Where(c => c.SourceProperty is not null)
                .Select(c => c.SourceProperty!);

            await foreach (IEntity entity in provider
                .RetrieveAsync(properties, cancellationToken)
                .ConfigureAwait(false))
            {
                AppendToBatch(entity);

                if (_batch!.Rows.Count >= _configuration.BatchSize)
                    await FlushBatchAsync(cancellationToken).ConfigureAwait(false);
            }

            // Flush whatever the final partial batch holds.
            await FlushBatchAsync(cancellationToken).ConfigureAwait(false);

            Logger.LogInformation(
                "Azure SQL synchronization completed: {Rows} row(s) in {Batches} batch(es).",
                _rowsStaged,
                _batchesFlushed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            HadErrors = true;
            Logger.LogError(ex, "Azure SQL synchronization failed for table '{TableName}'.", _configuration.TableName);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task FinalizeAsync(IProvider provider, CancellationToken cancellationToken = default)
    {
        if (_configuration is null)
            return;

        try
        {
            if (_connection is not null && !HadErrors)
                await ReconcileDeletionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            HadErrors = true;
            Logger.LogError(ex, "Azure SQL deletion reconciliation failed for '{TableName}'.", _configuration.TableName);
        }
        finally
        {
            await DisposeSessionAsync().ConfigureAwait(false);
        }

        Logger.LogInformation("Azure SQL finalization completed.");
    }

    /// <summary>
    /// Opens the session connection and creates the staging tables.
    /// </summary>
    private async Task OpenAndPrepareAsync(CancellationToken cancellationToken)
    {
        if (_configuration?.ConnectionString is null)
            throw new InvalidOperationException("Azure SQL consumer requires a connection string.");

        if (!_configuration.ConnectionString.TryGetValue(Discloser, out char[] connectionChars))
            throw new InvalidOperationException("Failed to resolve the Azure SQL connection string.");

        _connection = new SqlConnection(new string(connectionChars));
        Array.Clear(connectionChars, 0, connectionChars.Length);

        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        string synchronizationName = _configuration.TableName!;
        _stagingTable = SqlStatementBuilder.BuildTempTableName(synchronizationName);
        _seenKeysTable = SqlStatementBuilder.BuildTempTableName(synchronizationName + "_Keys");

        _columnOrder = _configuration.Columns!.Keys.ToList();

        List<KeyValuePair<string, string>> stagingColumns = _columnOrder
            .Select(name => new KeyValuePair<string, string>(
                name,
                _configuration.Columns![name].SqlType ?? "NVARCHAR(MAX)"))
            .ToList();

        List<KeyValuePair<string, string>> keyColumns = _configuration.PrimaryKeys!
            .Select(name => new KeyValuePair<string, string>(
                name,
                _configuration.Columns!.TryGetValue(name, out ColumnDefinition? definition)
                    ? definition.SqlType ?? "NVARCHAR(450)"
                    : "NVARCHAR(450)"))
            .ToList();

        await ExecuteAsync(SqlStatementBuilder.BuildDropTempTableIfExists(_stagingTable), cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(SqlStatementBuilder.BuildDropTempTableIfExists(_seenKeysTable), cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(SqlStatementBuilder.BuildCreateTempTable(_stagingTable, stagingColumns), cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(SqlStatementBuilder.BuildCreateTempTable(_seenKeysTable, keyColumns), cancellationToken).ConfigureAwait(false);

        _batch = CreateBatchTable(_columnOrder);
        _seenKeyBatch = CreateBatchTable(_configuration.PrimaryKeys!);

        Logger.LogDebug("Staging tables '{Staging}' and '{Keys}' created.", _stagingTable, _seenKeysTable);
    }

    /// <summary>
    /// Creates the in-memory batch buffer. All columns are typed as <see cref="object"/> so the
    /// server performs the conversion during bulk copy, matching the target column types.
    /// </summary>
    private static DataTable CreateBatchTable(IEnumerable<string> columns)
    {
        DataTable table = new DataTable();
        foreach (string column in columns)
            table.Columns.Add(column, typeof(object));

        return table;
    }

    /// <summary>
    /// Projects a single entity into the pending batch.
    /// </summary>
    private void AppendToBatch(IEntity entity)
    {
        if (_configuration?.Columns is null || _batch is null || _seenKeyBatch is null)
            return;

        DataRow row = _batch.NewRow();
        for (int i = 0; i < _columnOrder.Count; i++)
        {
            ColumnDefinition definition = _configuration.Columns[_columnOrder[i]];
            object? value = definition.SourceProperty is null ? null : entity[definition.SourceProperty];
            row[i] = value ?? DBNull.Value;
        }

        _batch.Rows.Add(row);

        DataRow keyRow = _seenKeyBatch.NewRow();
        for (int i = 0; i < _configuration.PrimaryKeys!.Count; i++)
        {
            string keyColumn = _configuration.PrimaryKeys[i];
            ColumnDefinition? definition = _configuration.Columns.TryGetValue(keyColumn, out ColumnDefinition? found)
                ? found
                : null;

            object? value = definition?.SourceProperty is null
                ? entity[keyColumn]
                : entity[definition.SourceProperty];

            keyRow[i] = value ?? DBNull.Value;
        }

        _seenKeyBatch.Rows.Add(keyRow);
    }

    /// <summary>
    /// Bulk-copies the pending batch into the staging table, merges it into the target and clears
    /// the staging table ready for the next batch.
    /// </summary>
    private async Task FlushBatchAsync(CancellationToken cancellationToken)
    {
        if (_batch is null || _batch.Rows.Count == 0 || _connection is null)
            return;

        int rows = _batch.Rows.Count;

        await BulkCopyAsync(_batch, _stagingTable!, cancellationToken).ConfigureAwait(false);
        await BulkCopyAsync(_seenKeyBatch!, _seenKeysTable!, cancellationToken).ConfigureAwait(false);

        string merge = SqlStatementBuilder.BuildMerge(
            _configuration!.TableName!,
            _stagingTable!,
            _columnOrder,
            _configuration.PrimaryKeys!);

        await ExecuteAsync(merge, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(SqlStatementBuilder.BuildTruncate(_stagingTable!), cancellationToken).ConfigureAwait(false);

        _batch.Clear();
        _seenKeyBatch!.Clear();

        _rowsStaged += rows;
        _batchesFlushed++;

        Logger.LogTrace("Flushed batch {Batch} ({Rows} row(s)) into '{TableName}'.",
            _batchesFlushed, rows, _configuration.TableName);
    }

    /// <summary>
    /// Streams one buffered batch to the server.
    /// </summary>
    private async Task BulkCopyAsync(DataTable table, string destination, CancellationToken cancellationToken)
    {
        using SqlBulkCopy bulkCopy = new SqlBulkCopy(_connection!)
        {
            DestinationTableName = SqlStatementBuilder.QuoteIdentifier(destination),
            BatchSize = table.Rows.Count,
            BulkCopyTimeout = _configuration!.CommandTimeoutSeconds
        };

        // Map by name so column order in the buffer never has to match the table.
        foreach (DataColumn column in table.Columns)
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);

        await bulkCopy.WriteToServerAsync(table, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes target rows whose keys were not observed during this run, honouring the configured
    /// threshold. Skipped entirely for delta synchronization, where absence carries no meaning.
    /// </summary>
    private async Task ReconcileDeletionsAsync(CancellationToken cancellationToken)
    {
        if (_configuration is null || _connection is null)
            return;

        if (_configuration.Delta)
        {
            Logger.LogDebug("Delta synchronization: deletion reconciliation skipped.");
            return;
        }

        string? deletedColumn = _configuration.HasDeletedColumn ? _configuration.DeletedColumnName : null;

        string countSql = SqlStatementBuilder.BuildDeleteCandidateCount(
            _configuration.TableName!, _seenKeysTable!, _configuration.PrimaryKeys!, deletedColumn);

        long totalRows = 0;
        long candidates = 0;

        await using (SqlCommand countCommand = CreateCommand(countSql))
        await using (SqlDataReader reader = await countCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                totalRows = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                candidates = reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1));
            }
        }

        if (candidates == 0)
        {
            Logger.LogInformation("Deletion reconciliation: nothing to remove from '{TableName}'.", _configuration.TableName);
            return;
        }

        if (_configuration.Threshold.HasValue && totalRows > 0)
        {
            double percentage = candidates * 100d / totalRows;
            if (percentage > _configuration.Threshold.Value)
            {
                throw new InvalidOperationException(
                    $"Deletion reconciliation aborted for '{_configuration.TableName}': " +
                    $"{candidates} of {totalRows} row(s) ({percentage:F1}%) would be deleted, " +
                    $"exceeding the configured threshold of {_configuration.Threshold.Value}%.");
            }
        }

        string deleteSql = SqlStatementBuilder.BuildDeleteReconciliation(
            _configuration.TableName!, _seenKeysTable!, _configuration.PrimaryKeys!, deletedColumn);

        await ExecuteAsync(deleteSql, cancellationToken).ConfigureAwait(false);

        Logger.LogInformation(
            "Deletion reconciliation removed {Count} of {Total} row(s) from '{TableName}'.",
            candidates, totalRows, _configuration.TableName);
    }

    /// <summary>
    /// Drops the staging tables and closes the session connection.
    /// </summary>
    private async ValueTask DisposeSessionAsync()
    {
        if (_connection is null)
            return;

        try
        {
            if (_connection.State == ConnectionState.Open)
            {
                if (_stagingTable is not null)
                    await ExecuteAsync(SqlStatementBuilder.BuildDropTempTableIfExists(_stagingTable), CancellationToken.None).ConfigureAwait(false);

                if (_seenKeysTable is not null)
                    await ExecuteAsync(SqlStatementBuilder.BuildDropTempTableIfExists(_seenKeysTable), CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (SqlException ex)
        {
            // The session is going away regardless; temp tables die with it.
            Logger.LogWarning(ex, "Failed to drop staging tables; they are released with the session.");
        }
        finally
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
            _batch?.Dispose();
            _seenKeyBatch?.Dispose();
            _batch = null;
            _seenKeyBatch = null;
        }
    }

    private SqlCommand CreateCommand(string sql)
    {
        SqlCommand command = new SqlCommand(sql, _connection)
        {
            CommandTimeout = _configuration!.CommandTimeoutSeconds
        };

        return command;
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using SqlCommand command = CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
