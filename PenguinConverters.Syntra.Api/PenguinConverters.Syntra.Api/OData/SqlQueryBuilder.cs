using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace PenguinConverters.Syntra.Api.OData;

/// <summary>
/// Translates OData query parameters into SQL clauses for dynamic query construction.
/// </summary>
/// <remarks>
/// Supports the following OData system query options:
/// <list type="bullet">
///   <item><c>$filter</c> - Translated to a parameterized WHERE clause via <see cref="FilterParser"/>.</item>
///   <item><c>$orderby</c> - Translated to an ORDER BY clause.</item>
///   <item><c>$top</c> - Translated to TOP N or FETCH NEXT N ROWS.</item>
///   <item><c>$skip</c> / <c>$skiptoken</c> - Translated to OFFSET/FETCH pagination.</item>
///   <item><c>$select</c> - Translated to a column projection list.</item>
///   <item><c>$apply</c> - Supports <c>distinct</c> aggregate transformation.</item>
///   <item><c>$count</c> - When true, generates a COUNT query.</item>
/// </list>
/// All identifiers are bracket-escaped and values are parameterized to prevent SQL injection.
/// </remarks>
public sealed partial class SqlQueryBuilder
{
    #region Fields

    private readonly FilterParser _filterParser = new();
    private readonly List<SqlParameter> _parameters = [];

    #endregion

    #region Properties

    /// <summary>
    /// Gets the SQL parameters generated during query building.
    /// </summary>
    public IReadOnlyList<SqlParameter> Parameters => _parameters;

    #endregion

    #region Methods

    /// <summary>
    /// Builds a SQL SELECT statement from OData query parameters targeting a view or stored procedure.
    /// </summary>
    /// <param name="source">The SQL view or table name to query.</param>
    /// <param name="queryParams">Dictionary of OData query parameters.</param>
    /// <param name="defaultMaxRows">Default maximum rows when no $top is specified.</param>
    /// <returns>A parameterized SQL query string.</returns>
    public string BuildQuery(string source, IDictionary<string, string> queryParams, int defaultMaxRows = 10000)
    {
        _parameters.Clear();
        string sanitizedSource = SanitizeObjectName(source);

        string select = BuildSelectClause(queryParams);
        string distinct = GetDistinct(queryParams);
        string? where = BuildWhereClause(queryParams);
        string? orderBy = BuildOrderByClause(queryParams);
        (string? offset, string? fetch) = BuildPagination(queryParams, defaultMaxRows);

        StringBuilder sb = new StringBuilder();

        // When using OFFSET/FETCH, ORDER BY is required
        bool usePagination = offset is not null;

        if (usePagination)
        {
            sb.Append($"SELECT {distinct}{select} FROM [{sanitizedSource}]");

            if (where is not null)
                sb.Append($" WHERE {where}");

            // ORDER BY is required for OFFSET/FETCH
            sb.Append($" ORDER BY {orderBy ?? "(SELECT NULL)"}");
            sb.Append($" OFFSET {offset} ROWS");

            if (fetch is not null)
                sb.Append($" FETCH NEXT {fetch} ROWS ONLY");
        }
        else
        {
            string top = BuildTopClause(queryParams, defaultMaxRows);
            sb.Append($"SELECT {distinct}{top}{select} FROM [{sanitizedSource}]");

            if (where is not null)
                sb.Append($" WHERE {where}");

            if (orderBy is not null)
                sb.Append($" ORDER BY {orderBy}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a SQL COUNT query from OData query parameters.
    /// </summary>
    /// <param name="source">The SQL view or table name to query.</param>
    /// <param name="queryParams">Dictionary of OData query parameters.</param>
    /// <returns>A parameterized SQL COUNT query string.</returns>
    public string BuildCountQuery(string source, IDictionary<string, string> queryParams)
    {
        _parameters.Clear();
        string sanitizedSource = SanitizeObjectName(source);
        string? where = BuildWhereClause(queryParams);

        StringBuilder sb = new StringBuilder();
        sb.Append($"SELECT COUNT(*) FROM [{sanitizedSource}]");

        if (where is not null)
            sb.Append($" WHERE {where}");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a stored procedure execution command with named parameters.
    /// </summary>
    /// <param name="procedureName">The stored procedure name (e.g., SP_S1FE_Users_CREATE).</param>
    /// <param name="parameters">Dictionary of parameter names and values.</param>
    /// <returns>A parameterized SQL EXEC statement.</returns>
    public string BuildStoredProcedureCall(string procedureName, IDictionary<string, object?> parameters)
    {
        _parameters.Clear();
        string sanitizedProc = SanitizeObjectName(procedureName);

        if (parameters.Count == 0)
            return $"EXEC [{sanitizedProc}]";

        StringBuilder sb = new StringBuilder();
        sb.Append($"EXEC [{sanitizedProc}]");

        List<string> paramClauses = new List<string>();
        int idx = 0;
        foreach ((string key, object? value) in parameters)
        {
            string sanitizedKey = SanitizeIdentifier(key);
            string paramName = $"@sp{idx++}";
            paramClauses.Add($"@{sanitizedKey} = {paramName}");
            _parameters.Add(new SqlParameter(paramName, value ?? DBNull.Value));
        }

        sb.Append(' ');
        sb.Append(string.Join(", ", paramClauses));

        return sb.ToString();
    }

    private string BuildSelectClause(IDictionary<string, string> queryParams)
    {
        if (!queryParams.TryGetValue("$select", out string? select) || string.IsNullOrWhiteSpace(select))
            return "*";

        string[] columns = select.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IEnumerable<string> sanitized = columns.Select(c => $"[{SanitizeIdentifier(c)}]");
        return string.Join(", ", sanitized);
    }

    private static string GetDistinct(IDictionary<string, string> queryParams)
    {
        if (queryParams.TryGetValue("$apply", out string? apply) &&
            apply.Contains("distinct", StringComparison.OrdinalIgnoreCase))
        {
            return "DISTINCT ";
        }
        return string.Empty;
    }

    private string? BuildWhereClause(IDictionary<string, string> queryParams)
    {
        if (!queryParams.TryGetValue("$filter", out string? filter) || string.IsNullOrWhiteSpace(filter))
            return null;

        string clause = _filterParser.Parse(filter);
        _parameters.AddRange(_filterParser.Parameters);
        return clause;
    }

    private static string? BuildOrderByClause(IDictionary<string, string> queryParams)
    {
        if (!queryParams.TryGetValue("$orderby", out string? orderBy) || string.IsNullOrWhiteSpace(orderBy))
            return null;

        string[] clauses = orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<string> parts = new List<string>();

        foreach (string clause in clauses)
        {
            string[] tokens = clause.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string column = SanitizeIdentifier(tokens[0]);
            string direction = tokens.Length > 1 && tokens[1].Equals("desc", StringComparison.OrdinalIgnoreCase)
                ? "DESC"
                : "ASC";
            parts.Add($"[{column}] {direction}");
        }

        return string.Join(", ", parts);
    }

    private static string BuildTopClause(IDictionary<string, string> queryParams, int defaultMaxRows)
    {
        if (queryParams.TryGetValue("$top", out string? topStr) && int.TryParse(topStr, out int top) && top > 0)
            return $"TOP {top} ";

        return defaultMaxRows > 0 ? $"TOP {defaultMaxRows} " : string.Empty;
    }

    private static (string? Offset, string? Fetch) BuildPagination(
        IDictionary<string, string> queryParams, int defaultMaxRows)
    {
        string? offsetValue = null;
        string? fetchValue = null;

        // $skip → OFFSET
        if (queryParams.TryGetValue("$skip", out string? skipStr) && int.TryParse(skipStr, out int skip) && skip >= 0)
        {
            offsetValue = skip.ToString();
        }
        // $skiptoken → OFFSET (alternative pagination mechanism)
        else if (queryParams.TryGetValue("$skiptoken", out string? tokenStr) && int.TryParse(tokenStr, out int token) && token >= 0)
        {
            offsetValue = token.ToString();
        }

        if (offsetValue is null)
            return (null, null);

        // When using OFFSET, $top controls FETCH NEXT
        if (queryParams.TryGetValue("$top", out string? topStr) && int.TryParse(topStr, out int top) && top > 0)
        {
            fetchValue = top.ToString();
        }
        else if (defaultMaxRows > 0)
        {
            fetchValue = defaultMaxRows.ToString();
        }

        return (offsetValue, fetchValue);
    }

    /// <summary>
    /// Sanitizes a column or parameter identifier, allowing only alphanumeric characters and underscores.
    /// </summary>
    private static string SanitizeIdentifier(string identifier)
    {
        string sanitized = IdentifierCleanupPattern().Replace(identifier.Trim(), string.Empty);
        if (string.IsNullOrEmpty(sanitized))
            throw new ArgumentException($"Invalid identifier: '{identifier}'");
        return sanitized;
    }

    /// <summary>
    /// Sanitizes a database object name (table, view, stored procedure), allowing dots for schema qualification.
    /// </summary>
    private static string SanitizeObjectName(string name)
    {
        string sanitized = ObjectNameCleanupPattern().Replace(name.Trim(), string.Empty);
        if (string.IsNullOrEmpty(sanitized))
            throw new ArgumentException($"Invalid object name: '{name}'");
        return sanitized;
    }

    [GeneratedRegex(@"[^A-Za-z0-9_]")]
    private static partial Regex IdentifierCleanupPattern();

    [GeneratedRegex(@"[^A-Za-z0-9_.]")]
    private static partial Regex ObjectNameCleanupPattern();

    #endregion
}
