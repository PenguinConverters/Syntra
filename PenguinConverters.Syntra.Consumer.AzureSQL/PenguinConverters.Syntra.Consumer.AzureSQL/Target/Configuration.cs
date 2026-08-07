using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Consumer.AzureSQL.Target;

/// <summary>
/// Configuration settings for the Azure SQL consumer.
/// Defines MSSQL destination parameters including connection, table schema,
/// MERGE behavior, and parallel processing options.
/// </summary>
public class Configuration
{
    /// <summary>
    /// Default maximum degree of parallelism for MERGE operations.
    /// </summary>
    public const int DefaultMaxDegreeOfParallelism = 4;

    /// <summary>
    /// Default number of rows staged before each bulk copy and MERGE.
    /// </summary>
    public const int DefaultBatchSize = 10000;

    /// <summary>
    /// Default SQL command timeout, in seconds. Bulk MERGE batches can be long-running.
    /// </summary>
    public const int DefaultCommandTimeoutSeconds = 300;

    /// <summary>
    /// Default name of the soft-delete column when <see cref="HasDeletedColumn"/> is set.
    /// </summary>
    public const string DefaultDeletedColumnName = "Deleted";

    /// <summary>
    /// Gets or sets the SQL Server connection string.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the destination table name.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// Gets or sets the unique name of this synchronization configuration, for example
    /// <c>ActiveDirectory.groups2AzureSQL.ADGroup_DELTA</c>.
    /// </summary>
    /// <remarks>
    /// The staging table name is derived from this, so it identifies the run rather than the
    /// destination. Several configurations may target the same table — a full and a delta sync of
    /// the same entity, or two sources feeding one table — and each needs its own staging area.
    /// Only one run of a given configuration is expected at a time, which is what makes the name
    /// safe to reuse. Falls back to <see cref="TableName"/> when not supplied.
    /// </remarks>
    public string? ConfigurationName { get; set; }

    /// <summary>
    /// Gets or sets the column definitions for the target table.
    /// Keys are column names, values define column metadata.
    /// </summary>
    public Dictionary<string, ColumnDefinition>? Columns { get; set; }

    /// <summary>
    /// Gets or sets the list of column names that form the primary key
    /// used for MERGE matching.
    /// </summary>
    public List<string>? PrimaryKeys { get; set; }

    /// <summary>
    /// Gets or sets the deletion threshold as a percentage (0-100).
    /// Prevents mass deletions during full sync by aborting when
    /// the deletion count exceeds this threshold of existing rows.
    /// </summary>
    public int? Threshold { get; set; }

    /// <summary>
    /// Gets or sets the maximum degree of parallelism for MERGE operations.
    /// Defaults to <c>4</c>.
    /// </summary>
    /// <remarks>
    /// No longer used by this consumer. Writes go through a session-scoped temporary staging
    /// table, which requires a single connection held open for the run, and
    /// <c>SqlConnection</c> is not thread-safe. Parallelism previously served to hide per-row
    /// round-trip latency; bulk loading removes the per-row round-trip, so there is nothing
    /// left to hide. Retained so existing configuration files continue to bind.
    /// Use <see cref="BatchSize"/> to tune throughput.
    /// </remarks>
    [Obsolete("Not used by the bulk write path. Tune throughput with BatchSize instead.")]
    public int MaxDegreeOfParallelism { get; set; } = DefaultMaxDegreeOfParallelism;

    /// <summary>
    /// Gets or sets the number of rows staged before each bulk copy and MERGE.
    /// Larger batches reduce round-trips at the cost of memory and longer-held locks.
    /// Defaults to <c>10000</c>.
    /// </summary>
    public int BatchSize { get; set; } = DefaultBatchSize;

    /// <summary>
    /// Gets or sets the SQL command timeout in seconds. Defaults to <c>300</c>.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = DefaultCommandTimeoutSeconds;

    /// <summary>
    /// Gets or sets a value indicating whether this is a delta synchronization.
    /// Deletion reconciliation is skipped when set, since an entity absent from a delta
    /// run has not been deleted, it simply did not change.
    /// </summary>
    public bool Delta { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the target table has a <c>Deleted</c> column
    /// for soft-delete tracking instead of physical row deletion.
    /// </summary>
    public bool HasDeletedColumn { get; set; }

    /// <summary>
    /// Gets or sets the name of the soft-delete column used when <see cref="HasDeletedColumn"/>
    /// is set. Defaults to <c>Deleted</c>.
    /// </summary>
    public string DeletedColumnName { get; set; } = DefaultDeletedColumnName;

    /// <summary>
    /// Gets or sets a value indicating whether a shadow column is used
    /// to store a JSON snapshot of the source entity for audit purposes.
    /// </summary>
    public bool ShadowColumn { get; set; }
}

/// <summary>
/// Defines a column in the target SQL table.
/// </summary>
public class ColumnDefinition
{
    /// <summary>
    /// Gets or sets the SQL data type for this column (e.g., "nvarchar(255)", "int").
    /// </summary>
    /// <remarks>
    /// Advisory only. The staging table is built from the target table's live definition in
    /// <c>sys.columns</c>, not from this value, so staged types always match the destination
    /// exactly and the MERGE performs no implicit conversion. A stale or wrong value here can no
    /// longer cause a type mismatch at run time. Retained for documentation and for tooling that
    /// generates a target table from the same configuration.
    /// </remarks>
    public string? SqlType { get; set; }

    /// <summary>
    /// Gets or sets the source property name that maps to this column.
    /// </summary>
    public string? SourceProperty { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this column is part of the primary key.
    /// </summary>
    public bool IsPrimaryKey { get; set; }
}
