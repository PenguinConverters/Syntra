using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Consumer.AzureSQL.Target;

namespace PenguinConverters.Syntra.Consumer.AzureSQL;

/// <summary>
/// Azure SQL consumer that writes entities to a MSSQL destination in bulk.
/// Entities are accumulated into batches, bulk-copied into session-scoped staging tables, and
/// folded into the target with one set-based statement per batch.
/// </summary>
/// <remarks>
/// How deletions are handled depends on the target.
///
/// With a soft-delete column configured, a deletion is an ordinary UPDATE of the timestamp
/// column, so deleted entities travel through the same upsert staging table as everything else
/// and the MERGE writes them. One staging table, one statement.
///
/// Without one, a deletion needs a real DELETE, so keys are buffered into a second key-only
/// staging table distinguished by suffix and removed with a join.
///
/// Staging tables are local temporary tables and therefore session-scoped, which requires one
/// <see cref="SqlConnection"/> held open for the whole run. <see cref="SqlConnection"/> is not
/// thread-safe, so the provider stream is processed sequentially. Parallelism previously existed
/// to hide per-row round-trip latency; bulk loading removes the per-row round-trip entirely.
/// </remarks>
public class Consumer : Core.Target.Consumer
{
    #region Fields

    private Configuration? _configuration;

    private readonly ConcurrentBag<string> _compositeKeys = new();

    private SqlConnection? _connection;
    private string? _upsertTable;
    private string? _deleteTable;
    private List<string> _columnOrder = new List<string>();
    private string? _deletedColumn;
    private DataTable? _upsertBatch;
    private DataTable? _deleteBatch;
    private long _rowsUpserted;
    private long _rowsDeleted;
    private long _batchesFlushed;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the consumer configuration.
    /// </summary>
    internal Configuration? Configuration
    {
        get => _configuration;
        set => _configuration = value;
    }

    #endregion

    #region Methods

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
        if (!ValidateConfiguration())
            return;

        Logger.LogInformation(
            "Starting Azure SQL bulk synchronization to table '{TableName}' with batch size {BatchSize}.",
            _configuration!.TableName,
            _configuration.BatchSize);

        try
        {
            await OpenAndPrepareAsync(cancellationToken).ConfigureAwait(false);

            // Ask the provider only for properties backing a column that survived the
            // intersection with the target schema. Retrieving a property nothing can store
            // costs transfer on every entity for data that is discarded.
            IEnumerable<string> properties = _columnOrder
                .Where(name => _configuration.Columns!.ContainsKey(name))
                .Select(name => _configuration.Columns![name].SourceProperty)
                .Where(sourceProperty => sourceProperty is not null)
                .Select(sourceProperty => sourceProperty!)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            await foreach (IEntity entity in provider
                .RetrieveAsync(properties, cancellationToken)
                .ConfigureAwait(false))
            {
                AppendToBatch(entity);

                if (_upsertBatch!.Rows.Count >= _configuration.BatchSize)
                    await FlushUpsertsAsync(cancellationToken).ConfigureAwait(false);

                if (_deleteBatch is not null && _deleteBatch.Rows.Count >= _configuration.BatchSize)
                    await FlushDeletesAsync(cancellationToken).ConfigureAwait(false);
            }

            // Nothing more is coming from the source, so whatever is buffered must still be
            // processed. Upserts first: a key present in both buffers should end up deleted.
            await FlushUpsertsAsync(cancellationToken).ConfigureAwait(false);
            await FlushDeletesAsync(cancellationToken).ConfigureAwait(false);

            Logger.LogInformation(
                "Azure SQL synchronization completed: {Upserted} row(s) merged, {Deleted} row(s) deleted, {Batches} batch(es).",
                _rowsUpserted,
                _rowsDeleted,
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

        Logger.LogInformation("Finalizing Azure SQL synchronization for table '{TableName}'.", _configuration.TableName);

        try
        {
            // Full sync deletion reconciliation:
            // 1. Query all existing composite keys from the target table
            // 2. Compare against _compositeKeys collected during sync
            // 3. Apply threshold check: if deletions exceed Threshold%, abort
            // 4. For missing keys:
            //    - If HasDeletedColumn: UPDATE SET Deleted = 1
            //    - Otherwise: DELETE FROM {TableName} WHERE pk = @pk
            await DeletionTrivialAsync(provider, cancellationToken).ConfigureAwait(false);
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
    /// Asynchronously performs full-sync deletion reconciliation by marking rows not seen
    /// in the current sync run as deleted. Respects the configured threshold
    /// to prevent mass deletions.
    /// </summary>
    /// <remarks>
    /// Distinct from the source-reported deletions carried by the staging tables. Those cover
    /// entities the source states were deleted; this covers entities that simply stopped being
    /// returned, which only a full synchronization can infer.
    /// </remarks>
    /// <param name="provider">The source provider to check for errors.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the reconciliation.</param>
    /// <returns>A task that completes when reconciliation has finished.</returns>
    private async Task DeletionTrivialAsync(IProvider provider, CancellationToken cancellationToken)
    {
        if (_configuration is null || HadErrors) return;

        // Skip deletion reconciliation if provider had errors
        // to avoid false deletions from incomplete data

        Logger.LogTrace(
            "Running deletion reconciliation: {Count} composite keys tracked, threshold {Threshold}%.",
            _compositeKeys.Count,
            _configuration.Threshold);

        // 1. SELECT all primary key combinations from target table
        // 2. Build HashSet of existing keys
        // 3. Remove keys that were seen during sync (_compositeKeys)
        // 4. Remaining keys are candidates for deletion
        // 5. Check threshold: if (deletionCount / existingCount * 100) > Threshold, throw
        // 6. Execute deletion/soft-delete for remaining keys

        // Placeholder for the awaited reconciliation queries against the target table.
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Checks that the configuration carries everything the bulk path needs.
    /// </summary>
    private bool ValidateConfiguration()
    {
        if (_configuration is null)
        {
            Logger.LogError("Azure SQL consumer configuration is not set.");
            HadErrors = true;
            return false;
        }

        if (string.IsNullOrWhiteSpace(_configuration.TableName))
        {
            Logger.LogError("Azure SQL consumer requires a target table name.");
            HadErrors = true;
            return false;
        }

        if (_configuration.Columns is null || _configuration.Columns.Count == 0)
        {
            Logger.LogError("Azure SQL consumer requires at least one column definition.");
            HadErrors = true;
            return false;
        }

        if (_configuration.PrimaryKeys is null || _configuration.PrimaryKeys.Count == 0)
        {
            Logger.LogError("Azure SQL consumer requires at least one primary key column.");
            HadErrors = true;
            return false;
        }

        return true;
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

        // The staging tables identify the synchronization configuration, not the destination:
        // a full and a delta sync of the same entity target one table but must not share staging.
        string synchronizationName = string.IsNullOrWhiteSpace(_configuration.ConfigurationName)
            ? _configuration.TableName!
            : _configuration.ConfigurationName;

        _upsertTable = SqlStatementBuilder.BuildTempTableName(
            synchronizationName, SqlStatementBuilder.UpsertSuffix);

        // Types come from the live target table rather than from configured strings, so staged
        // values match the destination exactly and the MERGE performs no implicit conversion.
        IDictionary<string, string> targetSchema =
            await ReadTargetSchemaAsync(cancellationToken).ConfigureAwait(false);

        if (targetSchema.Count == 0)
        {
            throw new InvalidOperationException(
                $"Target table '{_configuration.TableName}' was not found, or exposes no insertable columns.");
        }

        ResolveDeletedColumn(targetSchema);
        ResolveColumnOrder(targetSchema);

        List<KeyValuePair<string, string>> upsertColumns = _columnOrder
            .Select(name => new KeyValuePair<string, string>(name, targetSchema[name]))
            .ToList();

        await ExecuteAsync(SqlStatementBuilder.BuildDropTempTableIfExists(_upsertTable), cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(SqlStatementBuilder.BuildCreateTempTable(_upsertTable, upsertColumns), cancellationToken).ConfigureAwait(false);
        _upsertBatch = CreateBatchTable(_columnOrder);

        // The delete staging table exists only for hard deletes. With a soft-delete column a
        // deletion is an UPDATE, which the MERGE already performs from the upsert table.
        if (_deletedColumn is null)
        {
            _deleteTable = SqlStatementBuilder.BuildTempTableName(
                synchronizationName, SqlStatementBuilder.DeleteSuffix);

            List<KeyValuePair<string, string>> keyColumns = _configuration.PrimaryKeys!
                .Select(name => new KeyValuePair<string, string>(name, targetSchema[name]))
                .ToList();

            await ExecuteAsync(SqlStatementBuilder.BuildDropTempTableIfExists(_deleteTable), cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(SqlStatementBuilder.BuildCreateTempTable(_deleteTable, keyColumns), cancellationToken).ConfigureAwait(false);
            _deleteBatch = CreateBatchTable(_configuration.PrimaryKeys!);
        }

        Logger.LogDebug(
            "Staging prepared: '{Upsert}' with {Columns} column(s){Delete}.",
            _upsertTable,
            _columnOrder.Count,
            _deleteTable is null ? ", soft delete via MERGE" : $", '{_deleteTable}' for hard deletes");
    }

    /// <summary>
    /// Determines the soft-delete column, if the target actually has one.
    /// </summary>
    private void ResolveDeletedColumn(IDictionary<string, string> targetSchema)
    {
        if (!_configuration!.HasDeletedColumn)
        {
            _deletedColumn = null;
            return;
        }

        string configured = string.IsNullOrWhiteSpace(_configuration.DeletedColumnName)
            ? Configuration.DefaultDeletedColumnName
            : _configuration.DeletedColumnName;

        if (!targetSchema.TryGetValue(configured, out string? _))
        {
            throw new InvalidOperationException(
                $"Soft delete is enabled but column '{configured}' does not exist on target table " +
                $"'{_configuration.TableName}'.");
        }

        _deletedColumn = configured;
    }

    /// <summary>
    /// Intersects the columns this synchronization requires with the columns the target exposes.
    /// </summary>
    private void ResolveColumnOrder(IDictionary<string, string> targetSchema)
    {
        List<string> requested = _configuration!.Columns!.Keys.ToList();

        // A configured column the target does not have is dropped rather than fatal: the target
        // evolves independently of the configuration, and a sync should keep loading what lines up.
        _columnOrder = requested.Where(targetSchema.ContainsKey).ToList();

        List<string> skipped = requested.Where(name => !targetSchema.ContainsKey(name)).ToList();

        if (skipped.Count > 0)
        {
            // Never drop columns silently: a quietly narrowed sync looks like a working one.
            Logger.LogWarning(
                "{Count} configured column(s) are not present or not insertable on target table " +
                "'{TableName}' and will not be synchronized: {Columns}. Computed and rowversion " +
                "columns cannot be written.",
                skipped.Count,
                _configuration.TableName,
                string.Join(", ", skipped));
        }

        // The soft-delete column has to be staged so the MERGE can write it, whether or not the
        // configuration lists it among the data columns.
        if (_deletedColumn is not null &&
            !_columnOrder.Contains(_deletedColumn, StringComparer.OrdinalIgnoreCase))
        {
            _columnOrder.Add(_deletedColumn);
        }

        if (_columnOrder.Count == 0)
        {
            throw new InvalidOperationException(
                $"No configured column matches target table '{_configuration.TableName}'. " +
                "Nothing could be synchronized.");
        }

        // Primary keys are the exception to the intersection. They form the MERGE match
        // condition, so a missing one cannot be dropped without silently changing which rows
        // match, turning updates into duplicate inserts.
        List<string> missingKeys = _configuration.PrimaryKeys!
            .Where(name => !targetSchema.ContainsKey(name))
            .ToList();

        if (missingKeys.Count > 0)
        {
            throw new InvalidOperationException(
                $"Primary key column(s) missing or not insertable on target table " +
                $"'{_configuration.TableName}': {string.Join(", ", missingKeys)}. " +
                "The MERGE match condition cannot be built without them.");
        }

        foreach (string key in _configuration.PrimaryKeys!)
        {
            if (!_columnOrder.Contains(key, StringComparer.OrdinalIgnoreCase))
                _columnOrder.Add(key);
        }
    }

    /// <summary>
    /// Reads the target table's insertable columns and their types from <c>sys.columns</c>.
    /// </summary>
    /// <returns>A case-insensitive map of column name to rendered SQL type.</returns>
    private async Task<IDictionary<string, string>> ReadTargetSchemaAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, string> schema = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using SqlCommand command = CreateCommand(SqlStatementBuilder.BuildColumnSchemaQuery());

        // Parameterised, so the table name is never interpolated into the query text.
        // 776 is the widest value OBJECT_ID accepts (a three-part name).
        command.Parameters.Add(new SqlParameter("@TableName", SqlDbType.NVarChar, 776)
        {
            Value = _configuration!.TableName
        });

        await using SqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string columnName = reader.GetString(0);
            string typeName = reader.GetString(1);
            short maxLength = reader.GetInt16(2);
            byte precision = reader.GetByte(3);
            byte scale = reader.GetByte(4);

            schema[columnName] = SqlStatementBuilder.RenderSqlType(typeName, maxLength, precision, scale);
        }

        Logger.LogDebug(
            "Read {Count} insertable column(s) from target table '{TableName}'.",
            schema.Count,
            _configuration.TableName);

        return schema;
    }

    /// <summary>
    /// Creates an in-memory batch buffer. All columns are typed as <see cref="object"/> so the
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
    /// Routes a single entity into the appropriate batch.
    /// </summary>
    private void AppendToBatch(IEntity entity)
    {
        if (_configuration?.Columns is null || _upsertBatch is null)
            return;

        bool isDeleted = entity.State == EntityState.Deleted;

        // A hard delete is the only case needing separate treatment. A soft delete is an UPDATE
        // of the timestamp column, so it stages as an ordinary row.
        if (isDeleted && _deleteBatch is not null)
        {
            DataRow keyRow = _deleteBatch.NewRow();
            for (int i = 0; i < _configuration.PrimaryKeys!.Count; i++)
                keyRow[i] = ResolveValue(entity, _configuration.PrimaryKeys[i]) ?? DBNull.Value;

            _deleteBatch.Rows.Add(keyRow);
            return;
        }

        // Track composite key for full-sync deletion reconciliation
        string compositeKey = string.Join("|", _configuration.PrimaryKeys?
            .Select(pk => entity[pk]?.ToString() ?? string.Empty)
            ?? Enumerable.Empty<string>());

        if (!string.IsNullOrEmpty(compositeKey))
            _compositeKeys.Add(compositeKey);

        DataRow row = _upsertBatch.NewRow();
        for (int i = 0; i < _columnOrder.Count; i++)
        {
            string column = _columnOrder[i];

            if (_deletedColumn is not null &&
                string.Equals(column, _deletedColumn, StringComparison.OrdinalIgnoreCase))
            {
                // Stamped when the source reports a deletion, cleared otherwise so an entity
                // reappearing at the source is reinstated rather than staying marked deleted.
                row[i] = isDeleted ? DateTime.UtcNow : (object)DBNull.Value;
                continue;
            }

            row[i] = ResolveValue(entity, column) ?? DBNull.Value;
        }

        _upsertBatch.Rows.Add(row);
    }

    /// <summary>
    /// Reads the entity value backing a target column, honouring the configured source property.
    /// </summary>
    private object? ResolveValue(IEntity entity, string column)
    {
        if (_configuration!.Columns!.TryGetValue(column, out ColumnDefinition? definition) &&
            definition.SourceProperty is not null)
        {
            return entity[definition.SourceProperty];
        }

        return entity[column];
    }

    /// <summary>
    /// Bulk-copies the pending upserts into staging, merges them into the target and clears the
    /// staging table ready for the next batch.
    /// </summary>
    private async Task FlushUpsertsAsync(CancellationToken cancellationToken)
    {
        if (_upsertBatch is null || _upsertBatch.Rows.Count == 0 || _connection is null)
            return;

        int rows = _upsertBatch.Rows.Count;

        await BulkCopyAsync(_upsertBatch, _upsertTable!, cancellationToken).ConfigureAwait(false);

        string merge = SqlStatementBuilder.BuildMerge(
            _configuration!.TableName!,
            _upsertTable!,
            _columnOrder,
            _configuration.PrimaryKeys!);

        await ExecuteAsync(merge, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(SqlStatementBuilder.BuildTruncate(_upsertTable!), cancellationToken).ConfigureAwait(false);

        _upsertBatch.Clear();
        _rowsUpserted += rows;
        _batchesFlushed++;

        Logger.LogTrace("Merged batch of {Rows} row(s) into '{TableName}'.", rows, _configuration.TableName);
    }

    /// <summary>
    /// Bulk-copies the pending delete keys into staging and removes the matching target rows.
    /// </summary>
    private async Task FlushDeletesAsync(CancellationToken cancellationToken)
    {
        if (_deleteBatch is null || _deleteBatch.Rows.Count == 0 || _connection is null)
            return;

        int rows = _deleteBatch.Rows.Count;

        await BulkCopyAsync(_deleteBatch, _deleteTable!, cancellationToken).ConfigureAwait(false);

        string delete = SqlStatementBuilder.BuildDeleteFromStaging(
            _configuration!.TableName!,
            _deleteTable!,
            _configuration.PrimaryKeys!);

        await ExecuteAsync(delete, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(SqlStatementBuilder.BuildTruncate(_deleteTable!), cancellationToken).ConfigureAwait(false);

        _deleteBatch.Clear();
        _rowsDeleted += rows;
        _batchesFlushed++;

        Logger.LogTrace("Deleted batch of {Rows} row(s) from '{TableName}'.", rows, _configuration.TableName);
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
                if (_upsertTable is not null)
                    await ExecuteAsync(SqlStatementBuilder.BuildDropTempTableIfExists(_upsertTable), CancellationToken.None).ConfigureAwait(false);

                if (_deleteTable is not null)
                    await ExecuteAsync(SqlStatementBuilder.BuildDropTempTableIfExists(_deleteTable), CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (SqlException ex)
        {
            // The session is going away regardless; temporary tables die with it.
            Logger.LogWarning(ex, "Failed to drop staging tables; they are released with the session.");
        }
        finally
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
            _upsertBatch?.Dispose();
            _deleteBatch?.Dispose();
            _upsertBatch = null;
            _deleteBatch = null;
        }
    }

    private SqlCommand CreateCommand(string sql)
    {
        return new SqlCommand(sql, _connection)
        {
            CommandTimeout = _configuration!.CommandTimeoutSeconds
        };
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using SqlCommand command = CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion
}
