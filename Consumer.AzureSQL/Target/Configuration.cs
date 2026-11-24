using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Consumer.AzureSQL.Target;

/// <summary>
/// Configuration settings for the Azure SQL consumer.
/// Defines MSSQL destination parameters including connection, table schema,
/// MERGE behavior, and parallel processing options.
/// </summary>
public class Configuration
{
    #region Constants

    /// <summary>
    /// Default maximum degree of parallelism for MERGE operations.
    /// </summary>
    public const int DefaultMaxDegreeOfParallelism = 4;

    /// <summary>
    /// Default number of rows staged before each bulk copy and MERGE.
    /// </summary>
    public const int DefaultBatchSize = 2000;

    /// <summary>
    /// Default SQL command timeout, in seconds. Bulk MERGE batches can be long-running.
    /// </summary>
    public const int DefaultCommandTimeoutSeconds = 300;

    /// <summary>
    /// Default name of the soft-delete column when <see cref="HasDeletedColumn"/> is set.
    /// </summary>
    public const string DefaultDeletedColumnName = "Deleted";

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the SQL Server connection string.
    /// Uses <see cref="Secret"/> for optional Keyra encryption support.
    /// </summary>
    public Secret? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the destination table name.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// Gets or sets the name identifying the synchronization this consumer runs for, as given by
    /// <c>ObjectNamespace</c> at the root of the job configuration.
    /// </summary>
    /// <remarks>
    /// The staging table is named after this value. The host supplies it and guarantees that only
    /// one run carrying a given name is active at a time. When it is empty the staging table is
    /// named after <see cref="TableName"/>.
    /// </remarks>
    public string? ObjectNamespace { get; set; }

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
    /// Defaults to <c>2000</c>.
    /// </summary>
    /// <remarks>
    /// A batch locks the target rows it reads until its <c>MERGE</c> completes. Above roughly 3,000
    /// rows the server replaces those row locks with one exclusive lock on the whole
    /// table for the duration of the batch, which blocks every other writer of the table and,
    /// unless the database uses row versioning, its readers. Batches below that size lock
    /// only the rows they write. A larger value suits a table with a single writer and
    /// readers on row versioning, or a table whose lock escalation is disabled.
    /// </remarks>
    public int BatchSize { get; set; } = DefaultBatchSize;

    /// <summary>
    /// Gets or sets the SQL command timeout in seconds. Defaults to <c>300</c>.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = DefaultCommandTimeoutSeconds;

    /// <summary>
    /// Gets or sets a value indicating whether the target table has a <c>Deleted</c> column
    /// for soft-delete tracking instead of physical row deletion.
    /// </summary>
    /// <remarks>
    /// When set, an entity the source reports as deleted is staged like any other row with
    /// <see cref="DeletedColumnName"/> stamped, and the MERGE applies it as an UPDATE. No
    /// separate delete staging table is created.
    /// </remarks>
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

    #endregion
}

/// <summary>
/// Defines a column in the target SQL table.
/// </summary>
public class ColumnDefinition
{
    #region Properties

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

    #endregion
}
