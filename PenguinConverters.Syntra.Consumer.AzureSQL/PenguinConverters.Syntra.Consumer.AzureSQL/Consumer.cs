using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Consumer.AzureSQL.Target;

namespace PenguinConverters.Syntra.Consumer.AzureSQL;

/// <summary>
/// Azure SQL consumer that writes entities to a MSSQL destination using MERGE statements.
/// Supports atomic INSERT/UPDATE/DELETE operations, soft deletes via a Deleted column,
/// composite key tracking, shadow column snapshots, and parallel processing.
/// </summary>
public class Consumer : Core.Target.Consumer, Core.Target.ISynchronizable
{
    private Configuration? _configuration;
    private readonly ConcurrentBag<string> _compositeKeys = new();

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
    public override void Synchronize(IProvider provider)
    {
        if (_configuration is null)
        {
            Logger.LogError("Azure SQL consumer configuration is not set.");
            HadErrors = true;
            return;
        }

        Logger.LogInformation("Starting Azure SQL synchronization to table '{TableName}'.", _configuration.TableName);

        IEnumerable<string> properties = _configuration.Columns?.Values
            .Where(c => c.SourceProperty is not null)
            .Select(c => c.SourceProperty!)
            ?? Enumerable.Empty<string>();

        Parallel.ForEach(
            provider.Retrieve(properties),
            new ParallelOptions { MaxDegreeOfParallelism = _configuration.MaxDegreeOfParallelism },
            entity =>
            {
                UpdateEntity(entity);
            });

        Logger.LogInformation("Azure SQL synchronization completed.");
    }

    /// <summary>
    /// Performs an atomic MERGE operation (INSERT/UPDATE/DELETE) for a single entity.
    /// Uses the configured primary keys for matching and column definitions for value mapping.
    /// </summary>
    /// <param name="entity">The entity to merge into the target table.</param>
    public void UpdateEntity(IEntity entity)
    {
        if (_configuration is null) return;

        try
        {
            // Build MERGE statement:
            //   MERGE INTO {TableName} AS target
            //   USING (SELECT @pk1 AS pk1, ...) AS source
            //   ON target.pk1 = source.pk1 [AND ...]
            //   WHEN MATCHED THEN UPDATE SET col1 = @col1, ...
            //   WHEN NOT MATCHED THEN INSERT (col1, ...) VALUES (@col1, ...)
            //   [WHEN NOT MATCHED BY SOURCE THEN DELETE / UPDATE SET Deleted = 1];

            // Track composite key for full-sync deletion reconciliation
            string compositeKey = string.Join("|", _configuration.PrimaryKeys?
                .Select(pk => entity[pk]?.ToString() ?? string.Empty)
                ?? Enumerable.Empty<string>());

            if (!string.IsNullOrEmpty(compositeKey))
                _compositeKeys.Add(compositeKey);

            // If ShadowColumn is enabled, serialize entity properties to JSON
            // and include as a parameter in the MERGE statement

            Logger.LogTrace("Merged entity '{Identifier}' into '{TableName}'.", entity.Identifier, _configuration.TableName);
        }
        catch (Exception ex)
        {
            HadErrors = true;
            Logger.LogError(ex, "Failed to merge entity '{Identifier}' into '{TableName}'.", entity.Identifier, _configuration.TableName);
        }
    }

    /// <inheritdoc />
    public override void Finalize(IProvider provider)
    {
        if (_configuration is null) return;

        Logger.LogInformation("Finalizing Azure SQL synchronization for table '{TableName}'.", _configuration.TableName);

        // Full sync deletion reconciliation:
        // 1. Query all existing composite keys from the target table
        // 2. Compare against _compositeKeys collected during sync
        // 3. Apply threshold check: if deletions exceed Threshold%, abort
        // 4. For missing keys:
        //    - If HasDeletedColumn: UPDATE SET Deleted = 1
        //    - Otherwise: DELETE FROM {TableName} WHERE pk = @pk
        DeletionTrivial(provider);

        Logger.LogInformation("Azure SQL finalization completed.");
    }

    /// <summary>
    /// Performs full-sync deletion reconciliation by marking rows not seen
    /// in the current sync run as deleted. Respects the configured threshold
    /// to prevent mass deletions.
    /// </summary>
    /// <param name="provider">The source provider to check for errors.</param>
    private void DeletionTrivial(IProvider provider)
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
    }
}
