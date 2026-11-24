using System.Text;

namespace PenguinConverters.Syntra.Consumer.AzureSQL.Target;

/// <summary>
/// Builds the T-SQL of the bulk synchronization path: the staging table, the statement that
/// merges a staged batch into the target, and the catalog queries describing the target.
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

    /// <summary>
    /// Suffix of the temporary table collecting every key a run has seen.
    /// </summary>
    public const string KeyTableSuffix = "_K";

    /// <summary>
    /// Suffix of the temporary table listing the target rows a reconciliation deletes.
    /// </summary>
    public const string CandidateTableSuffix = "_X";

    /// <summary>
    /// Largest total byte length of the key columns of a clustered index.
    /// </summary>
    public const int MaxIndexKeyBytes = 900;

    /// <summary>
    /// Separator between the entries of a list that takes one statement line per entry at the
    /// second indentation level.
    /// </summary>
    private const string ListSeparator = ",\n        ";

    /// <summary>
    /// Query reading the writable columns of the table named by <c>@TableName</c>.
    /// </summary>
    private const string TargetSchemaQuery = """
        SELECT
            OBJECT_SCHEMA_NAME(COL.[object_id]) As [SchemaName],
            OBJECT_NAME(COL.[object_id]) As [ObjectName],
            COL.[name] As [ColumnName],
            TYPE_NAME(COL.[system_type_id]) As [TypeName],
            COL.[max_length] As [MaxLength],
            COL.[precision] As [Precision],
            COL.[scale] As [Scale],
            COL.[collation_name] As [CollationName]
        FROM
            [sys].[columns] COL
        WHERE
            COL.[object_id] = OBJECT_ID(@TableName)
            AND COL.[is_computed] = 0
            AND COL.[is_identity] = 0
            AND COL.[generated_always_type] = 0
            AND TYPE_NAME(COL.[system_type_id]) <> 'timestamp'
        ORDER BY
            COL.[column_id];
        """;

    /// <summary>
    /// Query reading the key columns of every usable unique index of the table named by
    /// <c>@TableName</c>.
    /// </summary>
    private const string UniqueIndexQuery = """
        SELECT
            IDX.[index_id] As [IndexId],
            COL.[name] As [ColumnName]
        FROM
            [sys].[indexes] IDX
        JOIN
            [sys].[index_columns] IXC
        ON
            (IXC.[object_id] = IDX.[object_id] AND IXC.[index_id] = IDX.[index_id])
        JOIN
            [sys].[columns] COL
        ON
            (COL.[object_id] = IXC.[object_id] AND COL.[column_id] = IXC.[column_id])
        WHERE
            IDX.[object_id] = OBJECT_ID(@TableName)
            AND IDX.[is_unique] = 1
            AND IDX.[is_disabled] = 0
            AND IDX.[has_filter] = 0
            AND IXC.[is_included_column] = 0
        ORDER BY
            IDX.[index_id],
            IXC.[key_ordinal];
        """;

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

        return $"[{identifier.Replace("]", "]]")}]";
    }

    /// <summary>
    /// Builds the two-part quoted name of a database object.
    /// </summary>
    /// <param name="schemaName">The schema the object belongs to.</param>
    /// <param name="objectName">The object name.</param>
    /// <returns>A name such as <c>[dbo].[ADUser]</c>.</returns>
    public static string QuoteObjectName(string schemaName, string objectName)
    {
        return $"{QuoteIdentifier(schemaName)}.{QuoteIdentifier(objectName)}";
    }

    /// <summary>
    /// Derives a local temporary table name from the name of a synchronization.
    /// </summary>
    /// <param name="objectNamespace">The name identifying the synchronization.</param>
    /// <param name="suffix">
    /// The text distinguishing the temporary tables of one run: empty for the staging table,
    /// <see cref="KeyTableSuffix"/> or <see cref="CandidateTableSuffix"/> for the others.
    /// </param>
    /// <returns>
    /// A name such as <c>#S1_ActiveDirectory_Users</c>: the prefix, the given name with every
    /// character other than a letter or digit replaced by an underscore, and the suffix. The
    /// name part is cut so that the whole name stays within
    /// <see cref="MaxTempTableNameLength"/> characters. An empty name yields <c>#S1_Sync</c>.
    /// </returns>
    public static string BuildTempTableName(string? objectNamespace, string suffix = "")
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

        if (builder.Length > MaxTempTableNameLength - suffix.Length)
            builder.Length = MaxTempTableNameLength - suffix.Length;

        return builder.Append(suffix).ToString();
    }

    /// <summary>
    /// Determines whether a set of key columns can carry a clustered index.
    /// </summary>
    /// <param name="keys">The key columns.</param>
    /// <returns>
    /// <c>true</c> when no column is a MAX, XML or large-object type and the columns together
    /// stay within <see cref="MaxIndexKeyBytes"/>; otherwise <c>false</c>.
    /// </returns>
    public static bool CanIndex(IReadOnlyList<TargetColumn> keys)
    {
        int bytes = 0;

        foreach (TargetColumn key in keys)
        {
            if (key.MaxLength <= 0 || key.BaseType is "XML" or "TEXT" or "NTEXT" or "IMAGE")
                return false;

            bytes += key.MaxLength;
        }

        return bytes <= MaxIndexKeyBytes;
    }

    /// <summary>
    /// Builds the <c>CREATE TABLE</c> statement of the table collecting every key a run has seen.
    /// </summary>
    /// <param name="keyTableName">The key table name.</param>
    /// <param name="keys">The primary key columns of the target.</param>
    /// <returns>
    /// A statement declaring the key columns with the type and collation of the target. When the
    /// columns can be indexed the table is clustered on them and silently drops a key it already
    /// holds; otherwise it is a heap.
    /// </returns>
    public static string BuildCreateKeyTable(string keyTableName, IReadOnlyList<TargetColumn> keys)
    {
        string declarations = string.Join(",\n    ", keys.Select(BuildKeyDeclaration));
        string keyList = string.Join(", ", keys.Select(key => QuoteIdentifier(key.Name)));
        string index = CanIndex(keys)
            ? $",\n    PRIMARY KEY CLUSTERED ({keyList}) WITH (IGNORE_DUP_KEY = ON)"
            : string.Empty;

        return $"""
            CREATE TABLE
                {QuoteIdentifier(keyTableName)}
            (
                {declarations}{index}
            );
            """;
    }

    /// <summary>
    /// Builds the <c>CREATE TABLE</c> statement of the table listing the target rows to delete.
    /// </summary>
    /// <param name="candidateTableName">The candidate table name.</param>
    /// <param name="keys">The primary key columns of the target.</param>
    /// <returns>
    /// A statement declaring the key columns with the type and collation of the target, and a
    /// clustered <see cref="SequenceColumn"/> numbering the rows from one.
    /// </returns>
    public static string BuildCreateCandidateTable(string candidateTableName, IReadOnlyList<TargetColumn> keys)
    {
        string declarations = string.Join(",\n    ", keys.Select(BuildKeyDeclaration));

        return $"""
            CREATE TABLE
                {QuoteIdentifier(candidateTableName)}
            (
                {QuoteIdentifier(SequenceColumn)} BIGINT IDENTITY(1, 1) NOT NULL PRIMARY KEY CLUSTERED,
                {declarations}
            );
            """;
    }

    /// <summary>
    /// Builds the statement that records the keys of the staged batch in the key table.
    /// </summary>
    /// <param name="keyTableName">The key table name.</param>
    /// <param name="tempTableName">The staging table name.</param>
    /// <param name="primaryKeys">The primary key column names.</param>
    /// <returns>
    /// An insert of every staged key whose latest staged row is not a deletion.
    /// </returns>
    public static string BuildInsertKeys(string keyTableName, string tempTableName, IReadOnlyList<string> primaryKeys)
    {
        string keyList = string.Join(", ", primaryKeys.Select(QuoteIdentifier));
        string deleted = QuoteIdentifier(DeletedFlagColumn);

        return $"""
            INSERT INTO
                {QuoteIdentifier(keyTableName)} ({keyList})
            SELECT
                {keyList}
            FROM
                (
                    SELECT
                        {keyList}, {deleted},
                        ROW_NUMBER() OVER (PARTITION BY {keyList} ORDER BY {QuoteIdentifier(SequenceColumn)} DESC) As [__S1Rank]
                    FROM
                        {QuoteIdentifier(tempTableName)}
                ) As STG
            WHERE
                STG.[__S1Rank] = 1
                AND STG.{deleted} = 0;
            """;
    }

    /// <summary>
    /// Builds the predicate restricting reconciliation to target rows with a given column value.
    /// </summary>
    /// <param name="column">The constrained target column.</param>
    /// <param name="parameterName">
    /// The name of the parameter carrying the value, or <c>null</c> to select rows where the
    /// column is null.
    /// </param>
    /// <returns>
    /// A comparison of the bare column with the parameter converted to the column's own type.
    /// </returns>
    public static string BuildConstraint(TargetColumn column, string? parameterName)
    {
        return parameterName is null
            ? $"DST.{QuoteIdentifier(column.Name)} IS NULL"
            : $"DST.{QuoteIdentifier(column.Name)} = CAST({parameterName} AS {column.Declaration})";
    }

    /// <summary>
    /// Builds the query counting the target rows in scope and those among them whose key the run
    /// has not seen.
    /// </summary>
    /// <param name="targetObject">The quoted, schema-qualified target table name.</param>
    /// <param name="keyTableName">The key table name.</param>
    /// <param name="primaryKeys">The primary key column names.</param>
    /// <param name="scope">The predicates every row in scope satisfies; may be empty.</param>
    /// <returns>A query returning one row with the columns <c>Total</c> and <c>Missing</c>.</returns>
    public static string BuildReconciliationCount(
        string targetObject,
        string keyTableName,
        IReadOnlyList<string> primaryKeys,
        IReadOnlyList<string> scope)
    {
        string keyList = string.Join(", ", primaryKeys.Select(QuoteIdentifier));

        return $"""
            SELECT
                COUNT_BIG(*) As [Total],
                COUNT_BIG(*) - COUNT_BIG(SRC.[__S1Seen]) As [Missing]
            FROM
                {targetObject} DST
            LEFT JOIN
                (
                    SELECT DISTINCT
                        {keyList}, 1 As [__S1Seen]
                    FROM
                        {QuoteIdentifier(keyTableName)}
                ) As SRC
            ON
                ({BuildKeyMatch(primaryKeys)})
            WHERE
                {BuildScope(scope)};
            """;
    }

    /// <summary>
    /// Builds the statement that lists the target rows in scope whose key the run has not seen.
    /// </summary>
    /// <param name="targetObject">The quoted, schema-qualified target table name.</param>
    /// <param name="keyTableName">The key table name.</param>
    /// <param name="candidateTableName">The candidate table name.</param>
    /// <param name="primaryKeys">The primary key column names.</param>
    /// <param name="scope">The predicates every row in scope satisfies; may be empty.</param>
    /// <returns>An insert of the keys of those rows into the candidate table.</returns>
    public static string BuildCollectCandidates(
        string targetObject,
        string keyTableName,
        string candidateTableName,
        IReadOnlyList<string> primaryKeys,
        IReadOnlyList<string> scope)
    {
        string keyList = string.Join(", ", primaryKeys.Select(QuoteIdentifier));
        string targetKeys = string.Join(", ", primaryKeys.Select(key => $"DST.{QuoteIdentifier(key)}"));

        return $"""
            INSERT INTO
                {QuoteIdentifier(candidateTableName)} ({keyList})
            SELECT
                {targetKeys}
            FROM
                {targetObject} DST
            WHERE
                {BuildScope(scope)}
                AND NOT EXISTS
                (
                    SELECT
                        1
                    FROM
                        {QuoteIdentifier(keyTableName)} SRC
                    WHERE
                        {BuildKeyMatch(primaryKeys)}
                );
            """;
    }

    /// <summary>
    /// Builds the statement that deletes one range of the listed candidate rows from the target.
    /// </summary>
    /// <param name="targetObject">The quoted, schema-qualified target table name.</param>
    /// <param name="candidateTableName">The candidate table name.</param>
    /// <param name="primaryKeys">The primary key column names.</param>
    /// <param name="softDeleteColumn">
    /// The target column recording a deletion time, or <c>null</c> to delete rows physically.
    /// </param>
    /// <returns>
    /// A statement acting on the candidates numbered <c>@From</c> to <c>@To</c>: a delete, or an
    /// update stamping the soft-delete column of rows not yet marked.
    /// </returns>
    public static string BuildApplyCandidates(
        string targetObject,
        string candidateTableName,
        IReadOnlyList<string> primaryKeys,
        string? softDeleteColumn)
    {
        string range = $"SRC.{QuoteIdentifier(SequenceColumn)} BETWEEN @From AND @To";

        if (softDeleteColumn is null)
        {
            return $"""
                DELETE
                    DST
                FROM
                    {targetObject} DST
                JOIN
                    {QuoteIdentifier(candidateTableName)} SRC
                ON
                    ({BuildKeyMatch(primaryKeys)})
                WHERE
                    {range};
                """;
        }

        return $"""
            UPDATE
                DST
            SET
                DST.{QuoteIdentifier(softDeleteColumn)} = SYSUTCDATETIME()
            FROM
                {targetObject} DST
            JOIN
                {QuoteIdentifier(candidateTableName)} SRC
            ON
                ({BuildKeyMatch(primaryKeys)})
            WHERE
                {range}
                AND DST.{QuoteIdentifier(softDeleteColumn)} IS NULL;
            """;
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

        string declarations = string.Join(",\n    ", columns.Select(column =>
            string.IsNullOrWhiteSpace(column.Collation)
                ? $"{QuoteIdentifier(column.Name)} {column.Declaration} NULL"
                : $"{QuoteIdentifier(column.Name)} {column.Declaration} COLLATE {column.Collation} NULL"));

        return $"""
            CREATE TABLE
                {QuoteIdentifier(tempTableName)}
            (
                {declarations},
                {QuoteIdentifier(DeletedFlagColumn)} BIT NOT NULL,
                {QuoteIdentifier(SequenceColumn)} BIGINT NOT NULL
            );
            """;
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
    /// The match condition compares the primary key columns of target and source directly. A
    /// matched row is updated only when at least one non-key column differs; that comparison is
    /// exact, character data being compared by its bytes, independent of collation.
    /// </para>
    /// <para>
    /// A staged deletion removes the matched row when <paramref name="softDeleteColumn"/> is
    /// <c>null</c>. Otherwise it stamps that column with the current UTC time, leaves every other
    /// column untouched and does not stamp a row that is already marked. A staged non-deletion
    /// clears the column again. A staged deletion with no matching row writes nothing.
    /// </para>
    /// <para>
    /// The statement carries no table hint. It runs under the isolation level of the session and
    /// locks only the rows it writes, so statements writing other keys of the same table are not
    /// blocked.
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

        string deleted = QuoteIdentifier(DeletedFlagColumn);
        string sequence = QuoteIdentifier(SequenceColumn);
        string columnList = string.Join(", ", columns.Select(column => QuoteIdentifier(column.Name)));
        string sourceList = string.Join(", ", columns.Select(column => $"SRC.{QuoteIdentifier(column.Name)}"));
        string keyList = string.Join(", ", primaryKeys.Select(QuoteIdentifier));
        string keyMatch = string.Join(" AND ", primaryKeys.Select(key =>
            $"DST.{QuoteIdentifier(key)} = SRC.{QuoteIdentifier(key)}"));

        string? changed = updatable.Count == 0
            ? null
            : $"EXISTS (SELECT {string.Join(", ", updatable.Select(column => BuildComparable("SRC", column)))} " +
              $"EXCEPT SELECT {string.Join(", ", updatable.Select(column => BuildComparable("DST", column)))})";

        string matched = softDeleteColumn is null
            ? BuildMatchedForHardDelete(updatable, deleted, changed)
            : BuildMatchedForSoftDelete(updatable, deleted, changed, QuoteIdentifier(softDeleteColumn));

        return $"""
            MERGE INTO
                {targetObject} DST
            USING
                (
                    SELECT
                        {columnList}, {deleted}
                    FROM
                        (
                            SELECT
                                {columnList}, {deleted},
                                ROW_NUMBER() OVER (PARTITION BY {keyList} ORDER BY {sequence} DESC) As [__S1Rank]
                            FROM
                                {QuoteIdentifier(tempTableName)}
                        ) As STG
                    WHERE
                        STG.[__S1Rank] = 1
                ) As SRC
            ON
                ({keyMatch})
            {matched}
            WHEN NOT MATCHED BY TARGET AND (SRC.{deleted} = 0)
                THEN INSERT
                    ({columnList})
                VALUES
                    ({sourceList});
            """;
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
        return TargetSchemaQuery;
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
        return UniqueIndexQuery;
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
        return $"""
            TRUNCATE TABLE
                {QuoteIdentifier(tempTableName)};
            """;
    }

    /// <summary>
    /// Builds the statement that drops the staging table when the current session has one.
    /// </summary>
    /// <param name="tempTableName">The staging table name.</param>
    /// <returns>The guarded <c>DROP TABLE</c> statement.</returns>
    public static string BuildDropTempTableIfExists(string tempTableName)
    {
        return $"""
            IF OBJECT_ID(N'tempdb..{tempTableName.Replace("'", "''")}') IS NOT NULL
                DROP TABLE
                    {QuoteIdentifier(tempTableName)};
            """;
    }

    /// <summary>
    /// Builds the <c>WHEN MATCHED</c> clauses for a target whose rows are deleted physically.
    /// </summary>
    /// <param name="updatable">The staged non-key columns.</param>
    /// <param name="deleted">The quoted deletion flag column.</param>
    /// <param name="changed">The change-detection predicate, or <c>null</c> when there is nothing to update.</param>
    /// <returns>A delete clause, followed by an update clause when there are columns to update.</returns>
    private static string BuildMatchedForHardDelete(List<TargetColumn> updatable, string deleted, string? changed)
    {
        string delete = $"""
            WHEN MATCHED AND (SRC.{deleted} = 1) THEN
                DELETE
            """;

        if (changed is null)
            return delete;

        string assignments = string.Join(ListSeparator, updatable.Select(column =>
            $"DST.{QuoteIdentifier(column.Name)} = SRC.{QuoteIdentifier(column.Name)}"));

        return $"""
            {delete}
            WHEN MATCHED AND ({changed}) THEN
                UPDATE SET
                    {assignments}
            """;
    }

    /// <summary>
    /// Builds the <c>WHEN MATCHED</c> clause for a target that records deletions in a column.
    /// </summary>
    /// <param name="updatable">The staged non-key columns.</param>
    /// <param name="deleted">The quoted deletion flag column.</param>
    /// <param name="changed">The change-detection predicate, or <c>null</c> when there is nothing to update.</param>
    /// <param name="softDelete">The quoted target column recording the deletion time.</param>
    /// <returns>
    /// One update clause that stamps an unmarked row for a staged deletion, and writes the staged
    /// values and clears the mark for a staged non-deletion that differs or is marked.
    /// </returns>
    private static string BuildMatchedForSoftDelete(
        List<TargetColumn> updatable,
        string deleted,
        string? changed,
        string softDelete)
    {
        string reinstated = changed is null
            ? $"DST.{softDelete} IS NOT NULL"
            : $"(DST.{softDelete} IS NOT NULL OR {changed})";

        List<string> assignments = updatable
            .Select(column =>
                $"DST.{QuoteIdentifier(column.Name)} = CASE WHEN SRC.{deleted} = 1 " +
                $"THEN DST.{QuoteIdentifier(column.Name)} ELSE SRC.{QuoteIdentifier(column.Name)} END")
            .ToList();

        assignments.Add($"DST.{softDelete} = CASE WHEN SRC.{deleted} = 1 THEN SYSUTCDATETIME() ELSE NULL END");

        return $"""
            WHEN MATCHED AND ((SRC.{deleted} = 1 AND DST.{softDelete} IS NULL) OR (SRC.{deleted} = 0 AND {reinstated})) THEN
                UPDATE SET
                    {string.Join(ListSeparator, assignments)}
            """;
    }

    /// <summary>
    /// Builds the declaration of a key column in a key or candidate table.
    /// </summary>
    /// <param name="key">The primary key column of the target.</param>
    /// <returns>The column with the target's type and collation, declared not nullable.</returns>
    private static string BuildKeyDeclaration(TargetColumn key)
    {
        return string.IsNullOrWhiteSpace(key.Collation)
            ? $"{QuoteIdentifier(key.Name)} {key.Declaration} NOT NULL"
            : $"{QuoteIdentifier(key.Name)} {key.Declaration} COLLATE {key.Collation} NOT NULL";
    }

    /// <summary>
    /// Builds the condition matching target and source rows on the bare primary key columns.
    /// </summary>
    /// <param name="primaryKeys">The primary key column names.</param>
    /// <returns>One equality per key column, joined with <c>AND</c>.</returns>
    private static string BuildKeyMatch(IReadOnlyList<string> primaryKeys)
    {
        return string.Join(" AND ", primaryKeys.Select(key =>
            $"DST.{QuoteIdentifier(key)} = SRC.{QuoteIdentifier(key)}"));
    }

    /// <summary>
    /// Joins scope predicates into one condition.
    /// </summary>
    /// <param name="scope">The predicates; may be empty.</param>
    /// <returns>The predicates joined with <c>AND</c>, or a condition that is always true.</returns>
    private static string BuildScope(IReadOnlyList<string> scope)
    {
        return scope.Count == 0 ? "1 = 1" : string.Join("\n    AND ", scope);
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
