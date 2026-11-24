using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Consumer.AzureSQL.Target;

namespace PenguinConverters.Syntra.Consumer.AzureSQL;

/// <summary>
/// Azure SQL consumer that writes entities to a SQL Server target table in set-based batches.
/// </summary>
/// <remarks>
/// <para>
/// Entities are buffered up to the configured batch size, bulk-copied into a staging table and
/// applied to the target with a single <c>MERGE</c> per batch. The server decides per staged key
/// whether the row is inserted, updated, deleted or left alone, and reads only the target rows
/// whose key is staged. The last, partial batch is applied the same way, and a run that receives
/// no entities sends no <c>MERGE</c> at all.
/// </para>
/// <para>
/// The staging table is a local temporary table named after the synchronization. It holds the
/// configured columns that exist on the target, declared with the target's own types and
/// collations. It is dropped if present and created before the first entity is read, and dropped
/// again when the run ends, whether it completed, failed or was cancelled.
/// </para>
/// <para>
/// One connection is held open for the whole run and the entity stream is processed
/// sequentially.
/// </para>
/// <para>
/// Deletions the source reports are applied with their batch. A full synchronization configured
/// to reconcile deletions also collects every key it sees in a second temporary table and, once
/// the stream has completed without errors, removes the target rows in scope whose key was not
/// seen, unless they exceed the configured threshold.
/// </para>
/// </remarks>
public class Consumer : Core.Target.Consumer, IAsyncDisposable
{
    #region Constants

    /// <summary>
    /// SQL Server error number reported to the session chosen as a deadlock victim.
    /// </summary>
    private const int DeadlockErrorNumber = 1205;

    /// <summary>
    /// SQL Server error numbers reported when an insert collides with a key another session
    /// inserted first: a violated primary key or unique constraint, and a violated unique index.
    /// </summary>
    private static readonly int[] DuplicateKeyErrorNumbers = [2627, 2601];

    /// <summary>
    /// Number of times a batch is merged before a deadlock or a key collision is reported as a failure.
    /// </summary>
    private const int MaxMergeAttempts = 3;

    /// <summary>
    /// Time, in seconds, allowed for dropping the staging table when the session is released.
    /// </summary>
    private const int ReleaseTimeoutSeconds = 30;

    #endregion

    #region Fields

    /// <summary>
    /// The deserialized consumer configuration.
    /// </summary>
    private Configuration? _configuration;

    /// <summary>
    /// The connection owning the staging table for the duration of the run.
    /// </summary>
    private SqlConnection? _connection;

    /// <summary>
    /// The staging table name, set once the session connection is open.
    /// </summary>
    private string? _stagingTable;

    /// <summary>
    /// The staged data columns in staging order, primary key columns included.
    /// </summary>
    private List<TargetColumn> _columns = new List<TargetColumn>();

    /// <summary>
    /// The CLR type each staged data column is converted to, in staging order.
    /// </summary>
    private List<Type> _columnTypes = new List<Type>();

    /// <summary>
    /// The source property each staged data column is read from, in staging order.
    /// </summary>
    private List<string> _sourceProperties = new List<string>();

    /// <summary>
    /// The positions of the primary key columns within the staged data columns.
    /// </summary>
    private List<int> _keyOrdinals = new List<int>();

    /// <summary>
    /// The <c>MERGE</c> statement applied to every staged batch.
    /// </summary>
    private string? _mergeStatement;

    /// <summary>
    /// The in-memory buffer of rows awaiting the next bulk copy.
    /// </summary>
    private DataTable? _batch;

    /// <summary>
    /// The arrival number given to the most recently buffered row.
    /// </summary>
    private long _sequence;

    /// <summary>
    /// The number of rows staged during the run.
    /// </summary>
    private long _rowsStaged;

    /// <summary>
    /// The number of target rows inserted, updated or deleted during the run.
    /// </summary>
    private long _rowsAffected;

    /// <summary>
    /// The number of entities left out because a value could not be staged.
    /// </summary>
    private long _rowsSkipped;

    /// <summary>
    /// The number of batches merged during the run.
    /// </summary>
    private long _batchesMerged;

    /// <summary>
    /// The quoted, schema-qualified name of the target table.
    /// </summary>
    private string? _targetObject;

    /// <summary>
    /// The target column recording a deletion time, or <c>null</c> when rows are deleted physically.
    /// </summary>
    private string? _softDeleteColumn;

    /// <summary>
    /// The primary key column names as the catalog spells them.
    /// </summary>
    private List<string> _primaryKeys = new List<string>();

    /// <summary>
    /// The table collecting every key the run has seen; set only when the run reconciles deletions.
    /// </summary>
    private string? _keyTable;

    /// <summary>
    /// The table listing the target rows a reconciliation removes; set together with the key table.
    /// </summary>
    private string? _candidateTable;

    /// <summary>
    /// The statement recording the keys of a staged batch in the key table.
    /// </summary>
    private string? _insertKeysStatement;

    /// <summary>
    /// The target columns and values that limit which rows take part in reconciliation.
    /// </summary>
    private List<KeyValuePair<TargetColumn, string?>> _constraints = new List<KeyValuePair<TargetColumn, string?>>();

    /// <summary>
    /// Whether the source stream was read to its end and every batch was applied.
    /// </summary>
    private bool _streamCompleted;

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
    /// <exception cref="InvalidOperationException">Thrown when no configuration can be deserialized.</exception>
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

            IEnumerable<string> properties = _sourceProperties.Distinct(StringComparer.OrdinalIgnoreCase);

            await foreach (IEntity entity in provider
                .RetrieveAsync(properties, cancellationToken)
                .ConfigureAwait(false))
            {
                AppendToBatch(entity);

                if (_batch!.Rows.Count >= _configuration.BatchSize)
                    await FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await FlushAsync(cancellationToken).ConfigureAwait(false);

            _streamCompleted = true;

            Logger.LogInformation(
                "Azure SQL synchronization completed: {Staged} row(s) staged, {Affected} target row(s) written, " +
                "{Skipped} entity(ies) skipped, {Batches} batch(es).",
                _rowsStaged,
                _rowsAffected,
                _rowsSkipped,
                _batchesMerged);
        }
        catch (OperationCanceledException)
        {
            await ReleaseSessionAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            HadErrors = true;
            Logger.LogError(ex, "Azure SQL synchronization failed for table '{TableName}'.", _configuration.TableName);
            await ReleaseSessionAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task FinalizeAsync(IProvider provider, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_keyTable is not null && _connection is not null)
            {
                if (_streamCompleted && !HadErrors)
                {
                    await ReconcileAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    Logger.LogWarning(
                        "Deletion reconciliation for table '{TableName}' was skipped because the " +
                        "synchronization did not complete without errors.",
                        _configuration!.TableName);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HadErrors = true;
            Logger.LogError(ex, "Deletion reconciliation failed for table '{TableName}'.", _configuration!.TableName);
        }
        finally
        {
            await ReleaseSessionAsync().ConfigureAwait(false);
        }

        if (_configuration is not null)
            Logger.LogInformation("Azure SQL finalization completed for table '{TableName}'.", _configuration.TableName);
    }

    /// <summary>
    /// Drops the staging table and closes the session connection.
    /// </summary>
    /// <returns>A task that completes when the session has been released.</returns>
    public async ValueTask DisposeAsync()
    {
        await ReleaseSessionAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Checks that the configuration carries everything the bulk path needs.
    /// </summary>
    /// <returns><c>true</c> when the run can start; otherwise <c>false</c>, with the reason logged.</returns>
    private bool ValidateConfiguration()
    {
        string? problem = null;

        if (_configuration is null)
            problem = "Azure SQL consumer configuration is not set.";
        else if (string.IsNullOrWhiteSpace(_configuration.TableName))
            problem = "Azure SQL consumer requires a target table name.";
        else if (_configuration.Columns is null || _configuration.Columns.Count == 0)
            problem = "Azure SQL consumer requires at least one column definition.";
        else if (_configuration.PrimaryKeys is null || _configuration.PrimaryKeys.Count == 0)
            problem = "Azure SQL consumer requires at least one primary key column.";
        else if (_configuration.BatchSize < 1)
            problem = "Azure SQL consumer requires a batch size of at least 1.";

        if (problem is null)
            return true;

        Logger.LogError("{Problem}", problem);
        HadErrors = true;
        return false;
    }

    /// <summary>
    /// Opens the session connection, reads the target definition and creates the staging table.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <returns>A task that completes when the staging table exists and is empty.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the connection string cannot be resolved, the target table is not found, a
    /// primary key or the soft-delete column is missing from it, or no configured column matches it.
    /// </exception>
    private async Task OpenAndPrepareAsync(CancellationToken cancellationToken)
    {
        if (_configuration!.ConnectionString is null)
            throw new InvalidOperationException("Azure SQL consumer requires a connection string.");

        if (!TryDisclose(_configuration.ConnectionString, out char[] connectionChars))
            throw new InvalidOperationException("Failed to resolve the Azure SQL connection string.");

        _connection = new SqlConnection(new string(connectionChars));
        Array.Clear(connectionChars, 0, connectionChars.Length);

        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        string synchronizationName = string.IsNullOrWhiteSpace(_configuration.ObjectNamespace)
            ? _configuration.TableName!
            : _configuration.ObjectNamespace;

        _stagingTable = SqlStatementBuilder.BuildTempTableName(synchronizationName);

        (string targetObject, Dictionary<string, TargetColumn> targetSchema) =
            await ReadTargetSchemaAsync(cancellationToken).ConfigureAwait(false);

        string? softDeleteColumn = ResolveSoftDeleteColumn(targetSchema);
        List<string> primaryKeys = ResolveColumns(targetSchema, softDeleteColumn);

        await WarnWhenKeysAreNotIndexedAsync(primaryKeys, cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
            SqlStatementBuilder.BuildDropTempTableIfExists(_stagingTable),
            _configuration.CommandTimeoutSeconds,
            cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
            SqlStatementBuilder.BuildCreateTempTable(_stagingTable, _columns),
            _configuration.CommandTimeoutSeconds,
            cancellationToken).ConfigureAwait(false);

        _mergeStatement = SqlStatementBuilder.BuildMerge(
            targetObject, _stagingTable, _columns, primaryKeys, softDeleteColumn);

        _targetObject = targetObject;
        _softDeleteColumn = softDeleteColumn;
        _primaryKeys = primaryKeys;

        if (_configuration.ReconcileDeletions && !_configuration.Delta)
            await PrepareReconciliationAsync(synchronizationName, targetSchema, cancellationToken).ConfigureAwait(false);

        _batch = CreateBatchTable();
        _sequence = 0;

        Logger.LogDebug(
            "Staging table '{Staging}' prepared for {Target} with {Columns} column(s), {Deletes}.",
            _stagingTable,
            targetObject,
            _columns.Count,
            softDeleteColumn is null ? "deletions remove rows" : $"deletions stamp '{softDeleteColumn}'");
    }

    /// <summary>
    /// Reads the writable columns of the target table from the database catalog.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <returns>
    /// The quoted, schema-qualified name of the target table and a case-insensitive map of its
    /// writable columns by name.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the target table is not found or exposes no writable column.
    /// </exception>
    private async Task<(string TargetObject, Dictionary<string, TargetColumn> Schema)> ReadTargetSchemaAsync(
        CancellationToken cancellationToken)
    {
        Dictionary<string, TargetColumn> schema =
            new Dictionary<string, TargetColumn>(StringComparer.OrdinalIgnoreCase);
        string? targetObject = null;

        await using SqlCommand command = CreateTableCommand(SqlStatementBuilder.BuildTargetSchemaQuery());
        await using SqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            targetObject ??= SqlStatementBuilder.QuoteObjectName(reader.GetString(0), reader.GetString(1));

            string columnName = reader.GetString(2);
            string typeName = reader.GetString(3);
            string declaration = SqlStatementBuilder.RenderSqlType(
                typeName, reader.GetInt16(4), reader.GetByte(5), reader.GetByte(6));
            string? collation = await reader.IsDBNullAsync(7, cancellationToken).ConfigureAwait(false)
                ? null
                : reader.GetString(7);

            schema[columnName] = new TargetColumn(
                columnName, typeName.ToUpperInvariant(), declaration, collation, reader.GetInt16(4));
        }

        if (targetObject is null)
        {
            throw new InvalidOperationException(
                $"Target table '{_configuration!.TableName}' was not found, or exposes no writable columns.");
        }

        Logger.LogDebug("Read {Count} writable column(s) from target table {Target}.", schema.Count, targetObject);

        return (targetObject, schema);
    }

    /// <summary>
    /// Determines the target column that records deletions, when soft delete is configured.
    /// </summary>
    /// <param name="targetSchema">The writable columns of the target table.</param>
    /// <returns>The column name as the catalog spells it, or <c>null</c> when rows are deleted physically.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when soft delete is configured and the column does not exist on the target.
    /// </exception>
    private string? ResolveSoftDeleteColumn(Dictionary<string, TargetColumn> targetSchema)
    {
        if (!_configuration!.HasDeletedColumn)
            return null;

        string configured = string.IsNullOrWhiteSpace(_configuration.DeletedColumnName)
            ? Configuration.DefaultDeletedColumnName
            : _configuration.DeletedColumnName;

        if (!targetSchema.TryGetValue(configured, out TargetColumn? column))
        {
            throw new InvalidOperationException(
                $"Soft delete is enabled but column '{configured}' does not exist on target table " +
                $"'{_configuration.TableName}'.");
        }

        return column.Name;
    }

    /// <summary>
    /// Selects the columns to stage: the configured columns that exist on the target, plus every
    /// primary key column.
    /// </summary>
    /// <param name="targetSchema">The writable columns of the target table.</param>
    /// <param name="softDeleteColumn">The soft-delete column, which is never staged, or <c>null</c>.</param>
    /// <returns>The primary key column names as the catalog spells them.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a primary key column is missing from the target.
    /// </exception>
    private List<string> ResolveColumns(Dictionary<string, TargetColumn> targetSchema, string? softDeleteColumn)
    {
        _columns = new List<TargetColumn>();
        _columnTypes = new List<Type>();
        _sourceProperties = new List<string>();
        _keyOrdinals = new List<int>();

        List<string> missingKeys = _configuration!.PrimaryKeys!
            .Where(name => !targetSchema.ContainsKey(name))
            .ToList();

        if (missingKeys.Count > 0)
        {
            throw new InvalidOperationException(
                $"Primary key column(s) missing or not writable on target table " +
                $"'{_configuration.TableName}': {string.Join(", ", missingKeys)}.");
        }

        List<string> skipped = new List<string>();
        HashSet<string> staged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string requested in _configuration.Columns!.Keys.Concat(_configuration.PrimaryKeys!))
        {
            if (string.Equals(requested, softDeleteColumn, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!targetSchema.TryGetValue(requested, out TargetColumn? column))
            {
                skipped.Add(requested);
                continue;
            }

            if (!staged.Add(column.Name))
                continue;

            string sourceProperty = requested;
            if (_configuration.Columns.TryGetValue(requested, out ColumnDefinition? definition) &&
                !string.IsNullOrWhiteSpace(definition?.SourceProperty))
            {
                sourceProperty = definition.SourceProperty;
            }

            _columns.Add(column);
            _columnTypes.Add(SqlValueConverter.GetClrType(column.BaseType));
            _sourceProperties.Add(sourceProperty);
        }

        if (skipped.Count > 0)
        {
            Logger.LogWarning(
                "{Count} configured column(s) are not present or not writable on target table " +
                "'{TableName}' and will not be synchronized: {Columns}.",
                skipped.Count,
                _configuration.TableName,
                string.Join(", ", skipped));
        }

        List<string> primaryKeys = new List<string>();

        foreach (string key in _configuration.PrimaryKeys!)
        {
            string name = targetSchema[key].Name;
            if (primaryKeys.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            primaryKeys.Add(name);
            _keyOrdinals.Add(_columns.FindIndex(column =>
                string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase)));
        }

        return primaryKeys;
    }

    /// <summary>
    /// Logs a warning when no unique index of the target lets the server seek the staged keys.
    /// </summary>
    /// <param name="primaryKeys">The columns forming the match condition.</param>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <returns>A task that completes when the check has finished.</returns>
    private async Task WarnWhenKeysAreNotIndexedAsync(List<string> primaryKeys, CancellationToken cancellationToken)
    {
        Dictionary<int, List<string>> indexes = new Dictionary<int, List<string>>();

        await using (SqlCommand command = CreateTableCommand(SqlStatementBuilder.BuildUniqueIndexQuery()))
        await using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                int indexId = reader.GetInt32(0);

                if (!indexes.TryGetValue(indexId, out List<string>? keyColumns))
                {
                    keyColumns = new List<string>();
                    indexes[indexId] = keyColumns;
                }

                keyColumns.Add(reader.GetString(1));
            }
        }

        bool covered = indexes.Values.Any(keyColumns =>
            keyColumns.All(column => primaryKeys.Contains(column, StringComparer.OrdinalIgnoreCase)));

        if (!covered)
        {
            Logger.LogWarning(
                "Target table '{TableName}' has no unique index whose key columns are all among the " +
                "configured primary keys ({Keys}). Every batch will scan the table instead of seeking " +
                "the staged keys, and duplicate keys are not prevented.",
                _configuration!.TableName,
                string.Join(", ", primaryKeys));
        }
    }

    /// <summary>
    /// Creates the in-memory buffer matching the staging table.
    /// </summary>
    /// <returns>
    /// An empty table with one column per staged data column, typed as the target stores it,
    /// followed by the deletion flag and the arrival sequence.
    /// </returns>
    private DataTable CreateBatchTable()
    {
        DataTable table = new DataTable();

        for (int i = 0; i < _columns.Count; i++)
            table.Columns.Add(_columns[i].Name, _columnTypes[i]);

        table.Columns.Add(SqlStatementBuilder.DeletedFlagColumn, typeof(bool));
        table.Columns.Add(SqlStatementBuilder.SequenceColumn, typeof(long));

        return table;
    }

    /// <summary>
    /// Converts one entity into a staging row and adds it to the buffer.
    /// </summary>
    /// <remarks>
    /// An entity the source reports as deleted is staged with its primary key only. An entity
    /// whose primary key is missing, or one of whose values cannot be converted to the type of
    /// its column, is left out, logged and recorded as an error.
    /// </remarks>
    /// <param name="entity">The entity to stage.</param>
    private void AppendToBatch(IEntity entity)
    {
        bool isDeleted = entity.State == EntityState.Deleted;
        object[] values = new object[_columns.Count + 2];

        for (int i = 0; i < _columns.Count; i++)
        {
            bool isKey = _keyOrdinals.Contains(i);

            if (isDeleted && !isKey)
            {
                values[i] = DBNull.Value;
                continue;
            }

            try
            {
                values[i] = SqlValueConverter.Convert(entity[_sourceProperties[i]], _columnTypes[i]);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                SkipEntity(entity, $"property '{_sourceProperties[i]}' cannot be stored in column " +
                    $"'{_columns[i].Name}' ({_columns[i].Declaration}): {ex.Message}");
                return;
            }

            if (isKey && values[i] is DBNull)
            {
                SkipEntity(entity, $"primary key property '{_sourceProperties[i]}' has no value");
                return;
            }
        }

        values[_columns.Count] = isDeleted;
        values[_columns.Count + 1] = ++_sequence;

        _batch!.Rows.Add(values);
    }

    /// <summary>
    /// Records an entity that could not be staged.
    /// </summary>
    /// <param name="entity">The entity left out.</param>
    /// <param name="reason">Why it could not be staged.</param>
    private void SkipEntity(IEntity entity, string reason)
    {
        HadErrors = true;
        _rowsSkipped++;

        Logger.LogError(
            "Entity '{Identifier}' was not written to '{TableName}': {Reason}.",
            entity.Identifier,
            _configuration!.TableName,
            reason);
    }

    /// <summary>
    /// Bulk-copies the buffered rows into the staging table, merges them into the target and
    /// empties both the staging table and the buffer.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <returns>A task that completes when the batch has been applied; immediately when the buffer is empty.</returns>
    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_batch is null || _batch.Rows.Count == 0)
            return;

        int rows = _batch.Rows.Count;

        await BulkCopyAsync(cancellationToken).ConfigureAwait(false);

        int affected = await MergeAsync(cancellationToken).ConfigureAwait(false);

        if (_insertKeysStatement is not null)
        {
            await ExecuteAsync(
                _insertKeysStatement,
                _configuration!.CommandTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);
        }

        await ExecuteAsync(
            SqlStatementBuilder.BuildTruncate(_stagingTable!),
            _configuration!.CommandTimeoutSeconds,
            cancellationToken).ConfigureAwait(false);

        _batch.Clear();
        _rowsStaged += rows;
        _rowsAffected += affected;
        _batchesMerged++;

        Logger.LogTrace(
            "Merged batch of {Rows} staged row(s) into '{TableName}': {Affected} target row(s) written.",
            rows,
            _configuration.TableName,
            affected);
    }

    /// <summary>
    /// Streams the buffered rows into the staging table.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <returns>A task that completes when every buffered row is staged.</returns>
    private async Task BulkCopyAsync(CancellationToken cancellationToken)
    {
        using SqlBulkCopy bulkCopy = new SqlBulkCopy(_connection!, SqlBulkCopyOptions.TableLock, null)
        {
            DestinationTableName = SqlStatementBuilder.QuoteIdentifier(_stagingTable!),
            BatchSize = _batch!.Rows.Count,
            BulkCopyTimeout = _configuration!.CommandTimeoutSeconds
        };

        foreach (DataColumn column in _batch.Columns)
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);

        await bulkCopy.WriteToServerAsync(_batch, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the staged batch to the target, repeating the statement when the session is chosen
    /// as a deadlock victim or another session inserted one of the staged keys first.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <returns>The number of target rows inserted, updated or deleted.</returns>
    private async Task<int> MergeAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await ExecuteAsync(
                    _mergeStatement!,
                    _configuration!.CommandTimeoutSeconds,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (SqlException ex) when (attempt < MaxMergeAttempts && IsRepeatable(ex))
            {
                Logger.LogWarning(
                    "Batch merge into '{TableName}' failed with error {Number} and is repeated; attempt {Attempt} of {Max}.",
                    _configuration!.TableName,
                    ex.Number,
                    attempt,
                    MaxMergeAttempts);

                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Resolves the reconciliation constraints against the target and creates the key table.
    /// </summary>
    /// <param name="synchronizationName">The name the temporary tables are derived from.</param>
    /// <param name="targetSchema">The writable columns of the target table.</param>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <returns>A task that completes when the key table exists and is empty.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a constraint names a column the target does not have.
    /// </exception>
    private async Task PrepareReconciliationAsync(
        string synchronizationName,
        Dictionary<string, TargetColumn> targetSchema,
        CancellationToken cancellationToken)
    {
        _constraints = new List<KeyValuePair<TargetColumn, string?>>();

        foreach (KeyValuePair<string, string?> constraint in
            _configuration!.DeletionConstraints ?? new Dictionary<string, string?>())
        {
            if (!targetSchema.TryGetValue(constraint.Key, out TargetColumn? column))
            {
                throw new InvalidOperationException(
                    $"Deletion constraint column '{constraint.Key}' does not exist on target table " +
                    $"'{_configuration.TableName}'.");
            }

            _constraints.Add(new KeyValuePair<TargetColumn, string?>(column, constraint.Value));
        }

        List<TargetColumn> keys = _keyOrdinals.Select(ordinal => _columns[ordinal]).ToList();

        _keyTable = SqlStatementBuilder.BuildTempTableName(synchronizationName, SqlStatementBuilder.KeyTableSuffix);
        _candidateTable = SqlStatementBuilder.BuildTempTableName(
            synchronizationName, SqlStatementBuilder.CandidateTableSuffix);

        foreach (string table in new[] { _keyTable, _candidateTable })
        {
            await ExecuteAsync(
                SqlStatementBuilder.BuildDropTempTableIfExists(table),
                _configuration.CommandTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);
        }

        await ExecuteAsync(
            SqlStatementBuilder.BuildCreateKeyTable(_keyTable, keys),
            _configuration.CommandTimeoutSeconds,
            cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
            SqlStatementBuilder.BuildCreateCandidateTable(_candidateTable, keys),
            _configuration.CommandTimeoutSeconds,
            cancellationToken).ConfigureAwait(false);

        _insertKeysStatement = SqlStatementBuilder.BuildInsertKeys(_keyTable, _stagingTable!, _primaryKeys);

        Logger.LogDebug(
            "Deletion reconciliation prepared for {Target}: keys are collected in '{KeyTable}', {Constraints} constraint(s).",
            _targetObject,
            _keyTable,
            _constraints.Count);
    }

    /// <summary>
    /// Removes the target rows in scope whose key the completed run did not see.
    /// </summary>
    /// <remarks>
    /// The rows in scope are those satisfying every deletion constraint and, with a soft-delete
    /// column, not yet marked as deleted. When the rows to remove exceed the configured share of
    /// the rows in scope, nothing is removed and an error is recorded. Otherwise the rows are
    /// removed in ranges of the configured batch size.
    /// </remarks>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <returns>A task that completes when reconciliation has finished.</returns>
    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        List<string> scope = new List<string>();

        if (_softDeleteColumn is not null)
            scope.Add($"DST.{SqlStatementBuilder.QuoteIdentifier(_softDeleteColumn)} IS NULL");

        for (int i = 0; i < _constraints.Count; i++)
        {
            scope.Add(SqlStatementBuilder.BuildConstraint(
                _constraints[i].Key, _constraints[i].Value is null ? null : $"@C{i}"));
        }

        long total;
        long missing;

        await using (SqlCommand count = new SqlCommand(
            SqlStatementBuilder.BuildReconciliationCount(_targetObject!, _keyTable!, _primaryKeys, scope), _connection)
        {
            CommandTimeout = _configuration!.CommandTimeoutSeconds
        })
        {
            count.Parameters.AddRange(CreateConstraintParameters());

            await using SqlDataReader reader = await count.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            total = reader.GetInt64(0);
            missing = reader.GetInt64(1);
        }

        if (missing == 0)
        {
            Logger.LogInformation(
                "Deletion reconciliation for '{TableName}': all {Total} row(s) in scope were seen; nothing to remove.",
                _configuration.TableName,
                total);
            return;
        }

        int threshold = _configuration.Threshold ?? Configuration.DefaultThreshold;
        double share = missing * 100.0 / total;

        if (share > threshold)
        {
            HadErrors = true;
            Logger.LogError(
                "Deletion reconciliation for '{TableName}' was refused: {Missing} of {Total} row(s) in scope " +
                "({Share:F2}%) were not returned by the source, which exceeds the threshold of {Threshold}%. " +
                "No row was removed.",
                _configuration.TableName,
                missing,
                total,
                share,
                threshold);
            return;
        }

        long candidates = await ExecuteAsync(
            SqlStatementBuilder.BuildCollectCandidates(_targetObject!, _keyTable!, _candidateTable!, _primaryKeys, scope),
            _configuration.CommandTimeoutSeconds,
            cancellationToken,
            CreateConstraintParameters()).ConfigureAwait(false);

        string apply = SqlStatementBuilder.BuildApplyCandidates(
            _targetObject!, _candidateTable!, _primaryKeys, _softDeleteColumn);
        long removed = 0;

        for (long from = 1; from <= candidates; from += _configuration.BatchSize)
        {
            removed += await ExecuteAsync(
                apply,
                _configuration.CommandTimeoutSeconds,
                cancellationToken,
                new SqlParameter("@From", SqlDbType.BigInt) { Value = from },
                new SqlParameter("@To", SqlDbType.BigInt) { Value = from + _configuration.BatchSize - 1 }).ConfigureAwait(false);
        }

        _rowsAffected += removed;

        Logger.LogInformation(
            "Deletion reconciliation for '{TableName}': {Removed} of {Total} row(s) in scope were {Action}.",
            _configuration.TableName,
            removed,
            total,
            _softDeleteColumn is null ? "deleted" : $"marked in '{_softDeleteColumn}'");
    }

    /// <summary>
    /// Creates the parameters carrying the deletion constraint values.
    /// </summary>
    /// <returns>One parameter named <c>@C</c> and its position for every constraint with a value.</returns>
    private SqlParameter[] CreateConstraintParameters()
    {
        List<SqlParameter> parameters = new List<SqlParameter>();

        for (int i = 0; i < _constraints.Count; i++)
        {
            if (_constraints[i].Value is not null)
                parameters.Add(new SqlParameter($"@C{i}", SqlDbType.NVarChar, -1) { Value = _constraints[i].Value });
        }

        return parameters.ToArray();
    }

    /// <summary>
    /// Determines whether a failed batch merge can be repeated with the same staged rows.
    /// </summary>
    /// <param name="exception">The failure reported by the server.</param>
    /// <returns>
    /// <c>true</c> for a deadlock and for a duplicate key, both of which leave the target and the
    /// staging table as they were before the statement; otherwise <c>false</c>.
    /// </returns>
    private static bool IsRepeatable(SqlException exception)
    {
        return exception.Number == DeadlockErrorNumber || DuplicateKeyErrorNumbers.Contains(exception.Number);
    }

    /// <summary>
    /// Drops the staging table, closes the session connection and discards the buffer.
    /// </summary>
    /// <remarks>
    /// Safe to call repeatedly. A failure to drop the table is logged and does not prevent the
    /// connection from being closed.
    /// </remarks>
    /// <returns>A task that completes when the session has been released.</returns>
    private async ValueTask ReleaseSessionAsync()
    {
        SqlConnection? connection = _connection;
        if (connection is null)
            return;

        _connection = null;

        try
        {
            if (connection.State == ConnectionState.Open)
            {
                foreach (string? table in new[] { _stagingTable, _keyTable, _candidateTable })
                {
                    if (table is null)
                        continue;

                    await using SqlCommand command = new SqlCommand(
                        SqlStatementBuilder.BuildDropTempTableIfExists(table), connection)
                    {
                        CommandTimeout = ReleaseTimeoutSeconds
                    };

                    await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            Logger.LogWarning(ex, "Failed to drop the temporary tables of '{Staging}'.", _stagingTable);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            _batch?.Dispose();
            _batch = null;
        }
    }

    /// <summary>
    /// Creates a catalog command whose <c>@TableName</c> parameter carries the configured target table.
    /// </summary>
    /// <param name="sql">The parameterised query text.</param>
    /// <returns>The command, bound to the session connection.</returns>
    private SqlCommand CreateTableCommand(string sql)
    {
        SqlCommand command = new SqlCommand(sql, _connection)
        {
            CommandTimeout = _configuration!.CommandTimeoutSeconds
        };

        command.Parameters.Add(new SqlParameter("@TableName", SqlDbType.NVarChar, 776)
        {
            Value = _configuration.TableName
        });

        return command;
    }

    /// <summary>
    /// Executes a statement on the session connection.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <param name="timeoutSeconds">The command timeout, in seconds.</param>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <param name="parameters">The parameters the statement refers to, if any.</param>
    /// <returns>The number of rows the statement affected.</returns>
    private async Task<int> ExecuteAsync(
        string sql,
        int timeoutSeconds,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters)
    {
        await using SqlCommand command = new SqlCommand(sql, _connection)
        {
            CommandTimeout = timeoutSeconds
        };

        command.Parameters.AddRange(parameters);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion
}
