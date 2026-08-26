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
    #region Constants

    /// <summary>Suffix identifying the staging table carrying inserts and updates.</summary>
    public const string UpsertSuffix = "_U";

    /// <summary>Suffix identifying the staging table carrying source-reported deletions.</summary>
    public const string DeleteSuffix = "_D";

    #endregion

    #region Methods

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
    /// <param name="suffix">
    /// Distinguishes the two staging tables of a run: <see cref="UpsertSuffix"/> or
    /// <see cref="DeleteSuffix"/>.
    /// </param>
    /// <returns>A local temporary table name such as <c>#S1_Contoso_Users_U</c>.</returns>
    public static string BuildTempTableName(string? synchronizationName, string suffix = "")
    {
        suffix ??= string.Empty;

        StringBuilder builder = new StringBuilder();
        builder.Append("#S1_");

        if (string.IsNullOrWhiteSpace(synchronizationName))
        {
            builder.Append("Sync");
        }
        else
        {
            foreach (char character in synchronizationName)
                builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        // SQL Server truncates #temp names at 116 characters. Trim the derived part so the
        // suffix always survives: it is what keeps the two staging tables distinct.
        int maxLength = 100 - suffix.Length;
        if (builder.Length > maxLength)
            builder.Length = maxLength;

        builder.Append(suffix);
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
    /// Emits <c>WHEN MATCHED</c> and <c>WHEN NOT MATCHED BY TARGET</c> only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never add a <c>WHEN NOT MATCHED BY SOURCE</c> clause to this statement.</b>
    /// </para>
    /// <para>
    /// The MERGE target is the whole destination table, but the source is a single <em>batch</em>
    /// of at most <c>BatchSize</c> rows, not the full result of the synchronization.
    /// <c>WHEN NOT MATCHED BY SOURCE</c> matches every target row absent from the source, so in a
    /// per-batch MERGE it matches every row not in the batch currently being flushed. Adding
    /// <c>THEN DELETE</c> would therefore delete the entire table on the first flush and leave
    /// only the last batch behind; adding <c>THEN UPDATE</c> would mark every one of those rows
    /// deleted. The statement is safe today precisely because that clause is absent and the two
    /// clauses present can only touch rows the batch actually carries.
    /// </para>
    /// <para>
    /// The temptation is real, because it looks like the natural way to make the MERGE perform
    /// full-sync deletion reconciliation, and reconciliation is still unimplemented. It is not.
    /// Deletion has two separate paths that are already correct:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// Entities the source reports as deleted travel through the staging tables. With a
    /// soft-delete column that is an ordinary UPDATE applied by this MERGE; without one it is
    /// <see cref="BuildDeleteFromStaging"/>, whose <c>INNER JOIN</c> against the key-only delete
    /// staging table can by construction only reach keys the source supplied.
    /// </item>
    /// <item>
    /// Entities that merely stopped being returned are found by full-sync reconciliation in
    /// <c>Consumer.DeletionTrivialAsync</c>, which compares the target against the composite keys
    /// observed across the <em>whole</em> run and is threshold-guarded. That comparison needs the
    /// complete key set, which no single batch has, so it cannot be expressed here.
    /// </item>
    /// </list>
    /// <para>
    /// If a per-batch MERGE ever does need to reach beyond the staged keys, restrict the target
    /// first, for example by merging into a common table expression filtered to keys present in
    /// staging, so no clause can touch a row the batch does not carry.
    /// </para>
    /// </remarks>
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

        // "BY TARGET" is written out rather than the equivalent bare "WHEN NOT MATCHED" so the
        // contrast with BY SOURCE is visible at the point someone would edit this list.
        builder.AppendLine("WHEN NOT MATCHED BY TARGET THEN");
        builder.Append("    INSERT (").Append(columnList).AppendLine(")");
        builder.Append("    VALUES (").Append(sourceList).AppendLine(")");

        // Do not add WHEN NOT MATCHED BY SOURCE here. The source is one batch, not the whole
        // synchronization, so that clause matches every row outside the current batch: THEN
        // DELETE empties the table down to the last batch flushed. See the remarks on this
        // method for the two deletion paths that already handle this correctly.

        builder.Append(';');
        return builder.ToString();
    }

    /// <summary>
    /// Builds the statement that removes rows the source reported as deleted, matching the target
    /// against the key-only delete staging table.
    /// </summary>
    /// <remarks>
    /// Only used when the target has no soft-delete column. A soft delete is an ordinary UPDATE
    /// of the timestamp column, so those rows travel through the upsert staging table and the
    /// MERGE handles them; no separate statement and no second staging table are involved.
    /// </remarks>
    /// <param name="targetTable">The destination table name.</param>
    /// <param name="deleteTableName">The staging table holding the keys to remove.</param>
    /// <param name="primaryKeys">The columns forming the match condition.</param>
    /// <returns>The delete statement.</returns>
    /// <exception cref="ArgumentException">Thrown when no primary keys are supplied.</exception>
    public static string BuildDeleteFromStaging(
        string targetTable,
        string deleteTableName,
        IReadOnlyList<string> primaryKeys)
    {
        if (primaryKeys is null || primaryKeys.Count == 0)
            throw new ArgumentException("At least one primary key is required.", nameof(primaryKeys));

        string correlation = string.Join(
            Environment.NewLine + "          AND ",
            primaryKeys.Select(k =>
                $"target.{QuoteIdentifier(k)} = staged.{QuoteIdentifier(k)}"));

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("DELETE target");
        builder.Append("  FROM ").Append(QuoteIdentifier(targetTable)).AppendLine(" AS target");
        builder.Append(" INNER JOIN ").Append(QuoteIdentifier(deleteTableName)).AppendLine(" AS staged");
        builder.Append("    ON ").Append(correlation).Append(';');

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

    #endregion
}
