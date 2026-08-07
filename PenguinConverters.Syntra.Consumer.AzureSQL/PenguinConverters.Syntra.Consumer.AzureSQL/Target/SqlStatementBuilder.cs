using System.Text;

namespace PenguinConverters.Syntra.Consumer.AzureSQL.Target;

/// <summary>
/// Builds the T-SQL used by the bulk synchronization path. Pure text generation with no I/O,
/// so the statements can be asserted directly in tests.
/// </summary>
/// <remarks>
/// Table and column names originate from user configuration and are interpolated into dynamic
/// SQL. Every identifier is therefore routed through <see cref="QuoteIdentifier"/>, which
/// bracket-quotes and doubles any embedded closing bracket. Row <em>values</em> never appear in
/// generated text: they reach the server through <see cref="System.Data.SqlClient"/> bulk copy
/// or as command parameters.
/// </remarks>
internal static class SqlStatementBuilder
{
    /// <summary>
    /// Quotes an identifier for safe interpolation into dynamic SQL, following the same rule as
    /// T-SQL <c>QUOTENAME</c>: wrap in brackets and double any embedded <c>]</c>.
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
    /// Derives a temporary table name from the synchronization namespace or configuration name.
    /// Non-alphanumeric characters are replaced with underscores so the result is always a legal
    /// identifier regardless of the namespace format.
    /// </summary>
    /// <param name="synchronizationName">The namespace or configuration name.</param>
    /// <returns>A local temporary table name such as <c>#S1_Contoso_Users</c>.</returns>
    public static string BuildTempTableName(string? synchronizationName)
    {
        if (string.IsNullOrWhiteSpace(synchronizationName))
            return "#S1_Sync";

        StringBuilder builder = new StringBuilder(synchronizationName.Length + 4);
        builder.Append("#S1_");

        foreach (char character in synchronizationName)
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');

        // SQL Server truncates #temp names at 116 characters; stay well inside that.
        const int maxLength = 100;
        if (builder.Length > maxLength)
            builder.Length = maxLength;

        return builder.ToString();
    }

    /// <summary>
    /// Builds the <c>CREATE TABLE</c> for the staging table, mirroring the configured columns.
    /// </summary>
    /// <param name="tempTableName">The temporary table name.</param>
    /// <param name="columns">The ordered column names paired with their SQL types.</param>
    /// <returns>The <c>CREATE TABLE</c> statement.</returns>
    /// <exception cref="ArgumentException">Thrown when no columns are supplied.</exception>
    public static string BuildCreateTempTable(
        string tempTableName,
        IReadOnlyList<KeyValuePair<string, string>> columns)
    {
        if (columns is null || columns.Count == 0)
            throw new ArgumentException("At least one column is required.", nameof(columns));

        StringBuilder builder = new StringBuilder();
        builder.Append("CREATE TABLE ").Append(QuoteIdentifier(tempTableName)).AppendLine(" (");

        for (int i = 0; i < columns.Count; i++)
        {
            builder
                .Append("    ")
                .Append(QuoteIdentifier(columns[i].Key))
                .Append(' ')
                .Append(columns[i].Value);

            if (i < columns.Count - 1)
                builder.Append(',');

            builder.AppendLine();
        }

        builder.Append(");");
        return builder.ToString();
    }

    /// <summary>
    /// Builds the set-based <c>MERGE</c> that moves one staged batch into the target table.
    /// </summary>
    /// <param name="targetTable">The destination table name.</param>
    /// <param name="tempTableName">The staging table name.</param>
    /// <param name="allColumns">Every column participating in the merge.</param>
    /// <param name="primaryKeys">The columns forming the match condition.</param>
    /// <returns>The <c>MERGE</c> statement.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when no columns or no primary keys are supplied.
    /// </exception>
    public static string BuildMerge(
        string targetTable,
        string tempTableName,
        IReadOnlyList<string> allColumns,
        IReadOnlyList<string> primaryKeys)
    {
        if (allColumns is null || allColumns.Count == 0)
            throw new ArgumentException("At least one column is required.", nameof(allColumns));
        if (primaryKeys is null || primaryKeys.Count == 0)
            throw new ArgumentException("At least one primary key is required.", nameof(primaryKeys));

        HashSet<string> keySet = new HashSet<string>(primaryKeys, StringComparer.OrdinalIgnoreCase);
        List<string> updatable = allColumns.Where(c => !keySet.Contains(c)).ToList();

        StringBuilder builder = new StringBuilder();
        builder.Append("MERGE INTO ").Append(QuoteIdentifier(targetTable)).AppendLine(" AS target");
        builder.Append("USING ").Append(QuoteIdentifier(tempTableName)).AppendLine(" AS source");

        builder.Append("    ON ");
        builder.AppendLine(string.Join(
            Environment.NewLine + "   AND ",
            primaryKeys.Select(k =>
                $"target.{QuoteIdentifier(k)} = source.{QuoteIdentifier(k)}")));

        // A key-only table has nothing to update; emitting an empty SET is a syntax error.
        if (updatable.Count > 0)
        {
            builder.AppendLine("WHEN MATCHED THEN");
            builder.Append("    UPDATE SET ");
            builder.AppendLine(string.Join(
                "," + Environment.NewLine + "               ",
                updatable.Select(c =>
                    $"target.{QuoteIdentifier(c)} = source.{QuoteIdentifier(c)}")));
        }

        string columnList = string.Join(", ", allColumns.Select(QuoteIdentifier));
        string sourceList = string.Join(", ", allColumns.Select(c => $"source.{QuoteIdentifier(c)}"));

        builder.AppendLine("WHEN NOT MATCHED BY TARGET THEN");
        builder.Append("    INSERT (").Append(columnList).AppendLine(")");
        builder.Append("    VALUES (").Append(sourceList).AppendLine(")");

        builder.Append(';');
        return builder.ToString();
    }

    /// <summary>
    /// Builds the statement that reconciles deletions after a full synchronization: rows present
    /// in the target but absent from the set of keys seen during the run.
    /// </summary>
    /// <param name="targetTable">The destination table name.</param>
    /// <param name="keyTableName">
    /// The staging table holding every key observed during the run.
    /// </param>
    /// <param name="primaryKeys">The columns forming the match condition.</param>
    /// <param name="deletedColumn">
    /// When supplied, rows are soft-deleted by setting this column to <c>SYSUTCDATETIME()</c>
    /// instead of being physically removed.
    /// </param>
    /// <returns>The reconciliation statement.</returns>
    public static string BuildDeleteReconciliation(
        string targetTable,
        string keyTableName,
        IReadOnlyList<string> primaryKeys,
        string? deletedColumn)
    {
        if (primaryKeys is null || primaryKeys.Count == 0)
            throw new ArgumentException("At least one primary key is required.", nameof(primaryKeys));

        string correlation = string.Join(
            Environment.NewLine + "          AND ",
            primaryKeys.Select(k =>
                $"target.{QuoteIdentifier(k)} = seen.{QuoteIdentifier(k)}"));

        StringBuilder builder = new StringBuilder();

        if (string.IsNullOrWhiteSpace(deletedColumn))
        {
            builder.Append("DELETE target").AppendLine();
            builder.Append("  FROM ").Append(QuoteIdentifier(targetTable)).AppendLine(" AS target");
        }
        else
        {
            builder.Append("UPDATE target").AppendLine();
            builder.Append("   SET target.").Append(QuoteIdentifier(deletedColumn)).AppendLine(" = SYSUTCDATETIME()");
            builder.Append("  FROM ").Append(QuoteIdentifier(targetTable)).AppendLine(" AS target");
            builder.Append(" WHERE target.").Append(QuoteIdentifier(deletedColumn)).AppendLine(" IS NULL");
        }

        builder.Append(string.IsNullOrWhiteSpace(deletedColumn) ? " WHERE" : "   AND").AppendLine(" NOT EXISTS (");
        builder.Append("        SELECT 1 FROM ").Append(QuoteIdentifier(keyTableName)).AppendLine(" AS seen");
        builder.Append("         WHERE ").AppendLine(correlation);
        builder.Append("      );");

        return builder.ToString();
    }

    /// <summary>
    /// Builds a count of rows that the reconciliation would affect, used for the threshold check.
    /// </summary>
    /// <param name="targetTable">The destination table name.</param>
    /// <param name="keyTableName">The staging table holding every key observed during the run.</param>
    /// <param name="primaryKeys">The columns forming the match condition.</param>
    /// <param name="deletedColumn">When supplied, already-deleted rows are excluded.</param>
    /// <returns>A statement selecting the candidate count and the total row count.</returns>
    public static string BuildDeleteCandidateCount(
        string targetTable,
        string keyTableName,
        IReadOnlyList<string> primaryKeys,
        string? deletedColumn)
    {
        if (primaryKeys is null || primaryKeys.Count == 0)
            throw new ArgumentException("At least one primary key is required.", nameof(primaryKeys));

        string correlation = string.Join(
            Environment.NewLine + "          AND ",
            primaryKeys.Select(k =>
                $"target.{QuoteIdentifier(k)} = seen.{QuoteIdentifier(k)}"));

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("SELECT");
        builder.AppendLine("    COUNT_BIG(*) AS TotalRows,");
        builder.AppendLine("    SUM(CASE WHEN NOT EXISTS (");
        builder.Append("        SELECT 1 FROM ").Append(QuoteIdentifier(keyTableName)).AppendLine(" AS seen");
        builder.Append("         WHERE ").AppendLine(correlation);
        builder.AppendLine("    ) THEN 1 ELSE 0 END) AS DeleteCandidates");
        builder.Append("  FROM ").Append(QuoteIdentifier(targetTable)).Append(" AS target");

        if (!string.IsNullOrWhiteSpace(deletedColumn))
        {
            builder.AppendLine();
            builder.Append(" WHERE target.").Append(QuoteIdentifier(deletedColumn)).Append(" IS NULL");
        }

        builder.Append(';');
        return builder.ToString();
    }

    /// <summary>
    /// Returns the query that reads the target table's column definitions from the live database.
    /// The staging table is built from this rather than from configured type strings, so staged
    /// values always match the destination type exactly and the MERGE performs no implicit
    /// conversion.
    /// </summary>
    /// <remarks>
    /// The table name is passed as the <c>@TableName</c> parameter to <c>OBJECT_ID</c>, so this
    /// query carries no interpolated identifier at all.
    ///
    /// <c>TYPE_NAME(system_type_id)</c> is used deliberately in preference to joining
    /// <c>sys.types</c> on <c>user_type_id</c>. The latter returns the <em>declared</em> type,
    /// which for an alias type (<c>CREATE TYPE</c>) is a name defined in the user database. The
    /// staging table is created in <c>tempdb</c>, where that name does not resolve, so the
    /// <c>CREATE TABLE</c> would fail. Resolving to the base system type keeps staging valid
    /// regardless of alias types on the target.
    ///
    /// Computed and <c>rowversion</c> columns are excluded: neither can be inserted, so a MERGE
    /// that tried to write them would fail.
    /// </remarks>
    /// <returns>The parameterised schema query.</returns>
    public static string BuildColumnSchemaQuery()
    {
        return """
            SELECT c.name                          AS ColumnName,
                   TYPE_NAME(c.system_type_id)     AS TypeName,
                   c.max_length                    AS MaxLength,
                   c.precision                     AS Precision,
                   c.scale                         AS Scale
              FROM sys.columns AS c
             WHERE c.object_id = OBJECT_ID(@TableName)
               AND c.is_computed = 0
               AND TYPE_NAME(c.system_type_id) <> 'timestamp'
             ORDER BY c.column_id;
            """;
    }

    /// <summary>
    /// Renders a SQL type declaration from <c>sys.columns</c> metadata.
    /// </summary>
    /// <param name="typeName">The type name from <c>sys.types</c>.</param>
    /// <param name="maxLength">
    /// The <c>max_length</c> value, in bytes; <c>-1</c> denotes a MAX type.
    /// </param>
    /// <param name="precision">The numeric precision.</param>
    /// <param name="scale">The numeric or temporal scale.</param>
    /// <returns>A type declaration such as <c>NVARCHAR(255)</c> or <c>DECIMAL(18,4)</c>.</returns>
    public static string RenderSqlType(string typeName, short maxLength, byte precision, byte scale)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentException("Type name must not be null or empty.", nameof(typeName));

        string type = typeName.ToUpperInvariant();

        switch (type)
        {
            case "NVARCHAR":
            case "NCHAR":
                // max_length is in bytes; national types store two bytes per character.
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
                // Fixed-width types (INT, BIGINT, BIT, UNIQUEIDENTIFIER, DATE, MONEY, ...)
                // carry no length or precision specifier.
                return type;
        }
    }

    /// <summary>Builds a <c>TRUNCATE TABLE</c> for the staging table.</summary>
    /// <param name="tempTableName">The staging table name.</param>
    /// <returns>The <c>TRUNCATE TABLE</c> statement.</returns>
    public static string BuildTruncate(string tempTableName)
        => $"TRUNCATE TABLE {QuoteIdentifier(tempTableName)};";

    /// <summary>Builds a conditional <c>DROP TABLE</c> for the staging table.</summary>
    /// <param name="tempTableName">The staging table name.</param>
    /// <returns>The guarded <c>DROP TABLE</c> statement.</returns>
    public static string BuildDropTempTableIfExists(string tempTableName)
    {
        // tempdb.sys.objects rather than OBJECT_ID(...) so the check is session-correct
        // for local temporary tables, whose names are suffixed per session.
        string literal = tempTableName.Replace("'", "''");
        return $"IF OBJECT_ID(N'tempdb..{literal}') IS NOT NULL DROP TABLE {QuoteIdentifier(tempTableName)};";
    }
}
