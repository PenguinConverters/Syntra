using System.Text;

namespace PenguinConverters.Syntra.Consumer.AzureSQL.Target;

/// <summary>
/// Builds the T-SQL of the bulk synchronization path: the staging table, the statement that
/// merges a staged batch into the target, and the catalog query describing the target.
/// </summary>
/// <remarks>
/// Every identifier is passed through <see cref="QuoteIdentifier"/> before it reaches statement
/// text. Row values never appear in statement text; they reach the server through bulk copy or
/// as command parameters.
/// </remarks>
internal static class SqlStatementBuilder
{
    #region Constants

    /// <summary>
    /// Name of the staging column that orders the rows of a batch by arrival.
    /// </summary>
    public const string SequenceColumn = "__S1Sequence";

    /// <summary>
    /// Name of the staging column that marks a row as a deletion reported by the source.
    /// </summary>
    public const string DeletedFlagColumn = "__S1Deleted";

    /// <summary>
    /// Prefix of every staging table name.
    /// </summary>
    public const string TempTablePrefix = "#S1_";

    /// <summary>
    /// Longest staging table name produced, below the 116 characters the server accepts for a
    /// local temporary table.
    /// </summary>
    public const int MaxTempTableNameLength = 100;

    #endregion

    #region Methods

    /// <summary>
    /// Quotes an identifier by wrapping it in brackets and doubling every embedded closing bracket.
    /// </summary>
    /// <param name="identifier">The raw identifier.</param>
    /// <returns>The bracket-quoted identifier.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="identifier"/> is null, empty or whitespace.
    /// </exception>
    public static string QuoteIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier must not be null or empty.", nameof(identifier));

        return string.Concat("[", identifier.Replace("]", "]]"), "]");
    }

    /// <summary>
    /// Builds the two-part quoted name of a database object.
    /// </summary>
    /// <param name="schemaName">The schema the object belongs to.</param>
    /// <param name="objectName">The object name.</param>
    /// <returns>A name such as <c>[dbo].[ADUser]</c>.</returns>
    public static string QuoteObjectName(string schemaName, string objectName)
    {
        return string.Concat(QuoteIdentifier(schemaName), ".", QuoteIdentifier(objectName));
    }

    /// <summary>
    /// Derives the local temporary staging table name from the name of a synchronization.
    /// </summary>
    /// <param name="objectNamespace">The name identifying the synchronization.</param>
    /// <returns>
    /// A name such as <c>#S1_ActiveDirectory_Users</c>: the prefix followed by the given name with
    /// every character other than a letter or digit replaced by an underscore, cut to
    /// <see cref="MaxTempTableNameLength"/> characters. An empty name yields <c>#S1_Sync</c>.
    /// </returns>
    public static string BuildTempTableName(string? objectNamespace)
    {
        StringBuilder builder = new StringBuilder(TempTablePrefix);

        if (string.IsNullOrWhiteSpace(objectNamespace))
        {
            builder.Append("Sync");
        }
        else
        {
            foreach (char character in objectNamespace.Trim())
                builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        if (builder.Length > MaxTempTableNameLength)
            builder.Length = MaxTempTableNameLength;

        return builder.ToString();
    }

    /// <summary>
    /// Builds the <c>CREATE TABLE</c> statement of the staging table.
    /// </summary>
    /// <param name="tempTableName">The staging table name.</param>
    /// <param name="columns">The target columns the staging table mirrors.</param>
    /// <returns>
    /// A statement declaring every given column nullable with the type and collation of its target
    /// column, followed by the <see cref="DeletedFlagColumn"/> and <see cref="SequenceColumn"/>
    /// control columns.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when no columns are supplied.</exception>
    public static string BuildCreateTempTable(string tempTableName, IReadOnlyList<TargetColumn> columns)
    {
        if (columns is null || columns.Count == 0)
            throw new ArgumentException("At least one column is required.", nameof(columns));

        StringBuilder builder = new StringBuilder();
        builder.Append("CREATE TABLE ").Append(QuoteIdentifier(tempTableName)).AppendLine(" (");

        foreach (TargetColumn column in columns)
        {
            builder.Append("    ").Append(QuoteIdentifier(column.Name)).Append(' ').Append(column.Declaration);

            if (!string.IsNullOrWhiteSpace(column.Collation))
                builder.Append(" COLLATE ").Append(column.Collation);

            builder.AppendLine(" NULL,");
        }

        builder.Append("    ").Append(QuoteIdentifier(DeletedFlagColumn)).AppendLine(" BIT NOT NULL,");
        builder.Append("    ").Append(QuoteIdentifier(SequenceColumn)).AppendLine(" BIGINT NOT NULL");
        builder.Append(");");

        return builder.ToString();
    }

    /// <summary>
    /// Builds the <c>MERGE</c> statement that applies one staged batch to the target table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source of the statement is the staging table reduced to the latest row per primary
    /// key, so a key staged several times in one batch is applied once with its last state.
    /// </para>
    /// <para>
    /// A matched row is updated only when at least one non-key column differs. The comparison is
    /// exact: character data is compared by its bytes, independent of collation.
    /// </para>
    /// <para>
    /// A staged deletion removes the matched row when <paramref name="softDeleteColumn"/> is
    /// <c>null</c>. Otherwise it stamps that column with the current UTC time, leaves every other
    /// column untouched and does not stamp a row that is already marked. A staged non-deletion
    /// clears the column again. A staged deletion with no matching row writes nothing.
    /// </para>
    /// <para>
    /// The statement touches only rows whose key is staged. It holds a range lock on the target
    /// for its duration, so concurrent statements against the same target cannot both insert the
    /// same key.
    /// </para>
    /// </remarks>
    /// <param name="targetObject">The quoted, schema-qualified target table name.</param>
    /// <param name="tempTableName">The staging table name.</param>
    /// <param name="columns">Every staged data column, primary key columns included.</param>
    /// <param name="primaryKeys">The names of the columns forming the match condition.</param>
    /// <param name="softDeleteColumn">
    /// The target column recording a deletion time, or <c>null</c> to delete rows physically.
    /// </param>
    /// <returns>The <c>MERGE</c> statement.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when no columns or no primary keys are supplied.
    /// </exception>
    public static string BuildMerge(
        string targetObject,
        string tempTableName,
        IReadOnlyList<TargetColumn> columns,
        IReadOnlyList<string> primaryKeys,
        string? softDeleteColumn)
    {
        if (columns is null || columns.Count == 0)
            throw new ArgumentException("At least one column is required.", nameof(columns));
        if (primaryKeys is null || primaryKeys.Count == 0)
            throw new ArgumentException("At least one primary key is required.", nameof(primaryKeys));

        HashSet<string> keySet = new HashSet<string>(primaryKeys, StringComparer.OrdinalIgnoreCase);
        List<TargetColumn> updatable = columns.Where(column => !keySet.Contains(column.Name)).ToList();

        string deletedFlag = QuoteIdentifier(DeletedFlagColumn);
        string sequence = QuoteIdentifier(SequenceColumn);
        string columnList = string.Join(", ", columns.Select(column => QuoteIdentifier(column.Name)));
        string keyList = string.Join(", ", primaryKeys.Select(QuoteIdentifier));
        string newLine = Environment.NewLine;

        StringBuilder builder = new StringBuilder();
        builder.Append("MERGE INTO ").Append(targetObject).AppendLine(" WITH (HOLDLOCK) AS target");
        builder.AppendLine("USING (");
        builder.Append("    SELECT ").Append(columnList).Append(", ").AppendLine(deletedFlag);
        builder.AppendLine("      FROM (");
        builder.Append("        SELECT ").Append(columnList).Append(", ").Append(deletedFlag).AppendLine(",");
        builder.Append("               ROW_NUMBER() OVER (PARTITION BY ").Append(keyList)
            .Append(" ORDER BY ").Append(sequence).AppendLine(" DESC) AS [__S1Rank]");
        builder.Append("          FROM ").AppendLine(QuoteIdentifier(tempTableName));
        builder.AppendLine("      ) AS staged");
        builder.AppendLine("     WHERE staged.[__S1Rank] = 1");
        builder.AppendLine(") AS source");

        builder.Append("    ON ");
        builder.AppendLine(string.Join(
            newLine + "   AND ",
            primaryKeys.Select(key => $"target.{QuoteIdentifier(key)} = source.{QuoteIdentifier(key)}")));

        string? changed = updatable.Count == 0
            ? null
            : string.Concat(
                "EXISTS (SELECT ",
                string.Join(", ", updatable.Select(column => BuildComparable("source", column))),
                " EXCEPT SELECT ",
                string.Join(", ", updatable.Select(column => BuildComparable("target", column))),
                ")");

        if (softDeleteColumn is null)
        {
            builder.Append("WHEN MATCHED AND source.").Append(deletedFlag).AppendLine(" = 1 THEN");
            builder.AppendLine("    DELETE");

            if (changed is not null)
            {
                builder.Append("WHEN MATCHED AND ").Append(changed).AppendLine(" THEN");
                builder.Append("    UPDATE SET ");
                builder.AppendLine(string.Join(
                    "," + newLine + "               ",
                    updatable.Select(column =>
                        $"target.{QuoteIdentifier(column.Name)} = source.{QuoteIdentifier(column.Name)}")));
            }
        }
        else
        {
            string softDelete = QuoteIdentifier(softDeleteColumn);
            string reinstated = changed is null
                ? $"target.{softDelete} IS NOT NULL"
                : $"(target.{softDelete} IS NOT NULL OR {changed})";

            builder.AppendLine("WHEN MATCHED AND (");
            builder.Append("        (source.").Append(deletedFlag).Append(" = 1 AND target.")
                .Append(softDelete).AppendLine(" IS NULL)");
            builder.Append("     OR (source.").Append(deletedFlag).Append(" = 0 AND ")
                .Append(reinstated).AppendLine(")) THEN");
            builder.Append("    UPDATE SET ");

            List<string> assignments = updatable
                .Select(column => string.Concat(
                    "target.", QuoteIdentifier(column.Name),
                    " = CASE WHEN source.", deletedFlag, " = 1 THEN target.", QuoteIdentifier(column.Name),
                    " ELSE source.", QuoteIdentifier(column.Name), " END"))
                .ToList();

            assignments.Add(string.Concat(
                "target.", softDelete,
                " = CASE WHEN source.", deletedFlag, " = 1 THEN SYSUTCDATETIME() ELSE NULL END"));

            builder.AppendLine(string.Join("," + newLine + "               ", assignments));
        }

        builder.Append("WHEN NOT MATCHED BY TARGET AND source.").Append(deletedFlag).AppendLine(" = 0 THEN");
        builder.Append("    INSERT (").Append(columnList).AppendLine(")");
        builder.Append("    VALUES (")
            .Append(string.Join(", ", columns.Select(column => $"source.{QuoteIdentifier(column.Name)}")))
            .Append(");");

        return builder.ToString();
    }

    /// <summary>
    /// Returns the query that reads the writable columns of the target table from the catalog.
    /// </summary>
    /// <remarks>
    /// The table is named by the <c>@TableName</c> parameter, which accepts a one-, two- or
    /// three-part name. Each row carries the schema and object name the parameter resolved to,
    /// the column name, its system type name, byte length, precision, scale and collation.
    /// Alias types are reported as their underlying system type. Computed, identity, generated
    /// and <c>rowversion</c> columns are left out because they cannot be written.
    /// </remarks>
    /// <returns>The parameterised catalog query.</returns>
    public static string BuildTargetSchemaQuery()
    {
        return """
            SELECT OBJECT_SCHEMA_NAME(c.object_id) AS SchemaName,
                   OBJECT_NAME(c.object_id)        AS ObjectName,
                   c.name                          AS ColumnName,
                   TYPE_NAME(c.system_type_id)     AS TypeName,
                   c.max_length                    AS MaxLength,
                   c.precision                     AS Precision,
                   c.scale                         AS Scale,
                   c.collation_name                AS CollationName
              FROM sys.columns AS c
             WHERE c.object_id = OBJECT_ID(@TableName)
               AND c.is_computed = 0
               AND c.is_identity = 0
               AND c.generated_always_type = 0
               AND TYPE_NAME(c.system_type_id) <> 'timestamp'
             ORDER BY c.column_id;
            """;
    }

    /// <summary>
    /// Returns the query that lists the key columns of every usable unique index on the target table.
    /// </summary>
    /// <remarks>
    /// The table is named by the <c>@TableName</c> parameter. Each row carries an index identifier
    /// and one of its key column names. Disabled and filtered indexes are left out, as are
    /// included columns.
    /// </remarks>
    /// <returns>The parameterised catalog query.</returns>
    public static string BuildUniqueIndexQuery()
    {
        return """
            SELECT i.index_id AS IndexId,
                   c.name     AS ColumnName
              FROM sys.indexes AS i
              JOIN sys.index_columns AS ic
                ON ic.object_id = i.object_id
               AND ic.index_id = i.index_id
              JOIN sys.columns AS c
                ON c.object_id = ic.object_id
               AND c.column_id = ic.column_id
             WHERE i.object_id = OBJECT_ID(@TableName)
               AND i.is_unique = 1
               AND i.is_disabled = 0
               AND i.has_filter = 0
               AND ic.is_included_column = 0
             ORDER BY i.index_id, ic.key_ordinal;
            """;
    }

    /// <summary>
    /// Renders a SQL type declaration from catalog metadata.
    /// </summary>
    /// <param name="typeName">The system type name.</param>
    /// <param name="maxLength">The length in bytes; <c>-1</c> denotes a MAX type.</param>
    /// <param name="precision">The numeric precision.</param>
    /// <param name="scale">The numeric or temporal scale.</param>
    /// <returns>A type declaration such as <c>NVARCHAR(255)</c> or <c>DECIMAL(18,4)</c>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="typeName"/> is null, empty or whitespace.
    /// </exception>
    public static string RenderSqlType(string typeName, short maxLength, byte precision, byte scale)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentException("Type name must not be null or empty.", nameof(typeName));

        string type = typeName.ToUpperInvariant();

        switch (type)
        {
            case "NVARCHAR":
            case "NCHAR":
                return maxLength < 0 ? $"{type}(MAX)" : $"{type}({maxLength / 2})";

            case "VARCHAR":
            case "CHAR":
            case "VARBINARY":
            case "BINARY":
                return maxLength < 0 ? $"{type}(MAX)" : $"{type}({maxLength})";

            case "DECIMAL":
            case "NUMERIC":
                return $"{type}({precision},{scale})";

            case "DATETIME2":
            case "DATETIMEOFFSET":
            case "TIME":
                return $"{type}({scale})";

            case "FLOAT":
                return $"{type}({precision})";

            default:
                return type;
        }
    }

    /// <summary>
    /// Builds the statement that empties the staging table.
    /// </summary>
    /// <param name="tempTableName">The staging table name.</param>
    /// <returns>The <c>TRUNCATE TABLE</c> statement.</returns>
    public static string BuildTruncate(string tempTableName)
    {
        return $"TRUNCATE TABLE {QuoteIdentifier(tempTableName)};";
    }

    /// <summary>
    /// Builds the statement that drops the staging table when the current session has one.
    /// </summary>
    /// <param name="tempTableName">The staging table name.</param>
    /// <returns>The guarded <c>DROP TABLE</c> statement.</returns>
    public static string BuildDropTempTableIfExists(string tempTableName)
    {
        string literal = tempTableName.Replace("'", "''");
        return $"IF OBJECT_ID(N'tempdb..{literal}') IS NOT NULL DROP TABLE {QuoteIdentifier(tempTableName)};";
    }

    /// <summary>
    /// Builds the expression under which a column takes part in change detection.
    /// </summary>
    /// <param name="alias">The table alias the column is read from.</param>
    /// <param name="column">The column to compare.</param>
    /// <returns>
    /// The column converted to binary for character, XML and large-object types, and the plain
    /// column reference for every other type.
    /// </returns>
    private static string BuildComparable(string alias, TargetColumn column)
    {
        string reference = $"{alias}.{QuoteIdentifier(column.Name)}";

        switch (column.BaseType)
        {
            case "CHAR":
            case "VARCHAR":
            case "NCHAR":
            case "NVARCHAR":
            case "IMAGE":
                return $"CAST({reference} AS VARBINARY(MAX))";

            case "NTEXT":
            case "XML":
                return $"CAST(CAST({reference} AS NVARCHAR(MAX)) AS VARBINARY(MAX))";

            case "TEXT":
                return $"CAST(CAST({reference} AS VARCHAR(MAX)) AS VARBINARY(MAX))";

            default:
                return reference;
        }
    }

    #endregion
}
