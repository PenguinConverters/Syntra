using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace PenguinConverters.Syntra.Api.OData;

/// <summary>
/// Parses OData $filter expressions and converts them to parameterized SQL WHERE clauses.
/// </summary>
/// <remarks>
/// Supports the following OData filter operations:
/// <list type="bullet">
///   <item>Comparison: eq, ne, gt, ge, lt, le</item>
///   <item>Logical: and, or, not</item>
///   <item>String functions: contains(), startswith(), endswith()</item>
///   <item>Null checks: eq null, ne null</item>
/// </list>
/// All values are parameterized to prevent SQL injection.
/// </remarks>
public sealed partial class FilterParser
{
    private int _parameterIndex;
    private readonly List<SqlParameter> _parameters = [];

    /// <summary>
    /// Gets the SQL parameters generated during parsing.
    /// </summary>
    public IReadOnlyList<SqlParameter> Parameters => _parameters;

    /// <summary>
    /// Parses an OData $filter expression into a parameterized SQL WHERE clause.
    /// </summary>
    /// <param name="filter">The OData filter expression.</param>
    /// <returns>A SQL WHERE clause (without the WHERE keyword).</returns>
    /// <exception cref="ArgumentException">Thrown when the filter expression is invalid.</exception>
    public string Parse(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return "1=1";

        _parameterIndex = 0;
        _parameters.Clear();

        return ParseExpression(filter.Trim());
    }

    private string ParseExpression(string expression)
    {
        // Handle parenthesized groups
        expression = expression.Trim();

        // Handle 'not' prefix
        if (expression.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
        {
            string inner = expression[4..].Trim();
            return $"NOT ({ParseExpression(inner)})";
        }

        // Split on top-level 'and' / 'or' (not inside parentheses)
        (string left, string? op, string right) = SplitLogicalOperator(expression);
        if (op is not null)
        {
            string sqlOp = op.Equals("and", StringComparison.OrdinalIgnoreCase) ? "AND" : "OR";
            return $"({ParseExpression(left)}) {sqlOp} ({ParseExpression(right)})";
        }

        // Handle parenthesized expression
        if (expression.StartsWith('(') && FindClosingParen(expression, 0) == expression.Length - 1)
        {
            return ParseExpression(expression[1..^1]);
        }

        // Handle string functions: contains(), startswith(), endswith()
        Match funcMatch = FunctionPattern().Match(expression);
        if (funcMatch.Success)
        {
            return ParseFunction(funcMatch);
        }

        // Handle comparison operators: eq, ne, gt, ge, lt, le
        Match compMatch = ComparisonPattern().Match(expression);
        if (compMatch.Success)
        {
            return ParseComparison(compMatch);
        }

        throw new ArgumentException($"Unable to parse filter expression: '{expression}'");
    }

    private string ParseFunction(Match match)
    {
        string function = match.Groups["func"].Value.ToLowerInvariant();
        string property = SanitizeIdentifier(match.Groups["prop"].Value);
        string value = UnquoteValue(match.Groups["val"].Value);
        string paramName = NextParameterName();

        switch (function)
        {
            case "contains":
                _parameters.Add(new SqlParameter(paramName, $"%{EscapeLikePattern(value)}%"));
                return $"[{property}] LIKE {paramName} ESCAPE '\\'";

            case "startswith":
                _parameters.Add(new SqlParameter(paramName, $"{EscapeLikePattern(value)}%"));
                return $"[{property}] LIKE {paramName} ESCAPE '\\'";

            case "endswith":
                _parameters.Add(new SqlParameter(paramName, $"%{EscapeLikePattern(value)}"));
                return $"[{property}] LIKE {paramName} ESCAPE '\\'";

            default:
                throw new ArgumentException($"Unsupported function: '{function}'");
        }
    }

    private string ParseComparison(Match match)
    {
        string property = SanitizeIdentifier(match.Groups["prop"].Value);
        string op = match.Groups["op"].Value.ToLowerInvariant();
        string rawValue = match.Groups["val"].Value.Trim();

        // Handle null comparisons
        if (rawValue.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return op switch
            {
                "eq" => $"[{property}] IS NULL",
                "ne" => $"[{property}] IS NOT NULL",
                _ => throw new ArgumentException($"Operator '{op}' is not supported with null values.")
            };
        }

        string sqlOp = op switch
        {
            "eq" => "=",
            "ne" => "<>",
            "gt" => ">",
            "ge" => ">=",
            "lt" => "<",
            "le" => "<=",
            _ => throw new ArgumentException($"Unsupported comparison operator: '{op}'")
        };

        string paramName = NextParameterName();
        object value = ParseValue(rawValue);
        _parameters.Add(new SqlParameter(paramName, value));

        return $"[{property}] {sqlOp} {paramName}";
    }

    /// <summary>
    /// Splits an expression on the outermost logical operator (and/or) that is not
    /// nested inside parentheses.
    /// </summary>
    private static (string Left, string? Op, string Right) SplitLogicalOperator(string expression)
    {
        int depth = 0;
        bool inString = false;

        for (int i = 0; i < expression.Length; i++)
        {
            char ch = expression[i];

            if (ch == '\'')
            {
                // Handle escaped single quotes ('')
                if (i + 1 < expression.Length && expression[i + 1] == '\'')
                {
                    i++;
                    continue;
                }
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (ch == '(') depth++;
            else if (ch == ')') depth--;

            if (depth != 0) continue;

            // Check for ' and ' or ' or ' at this position
            foreach (string logicalOp in new[] { "and", "or" })
            {
                string pattern = $" {logicalOp} ";
                if (i + pattern.Length <= expression.Length &&
                    expression.Substring(i, pattern.Length).Equals(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    string left = expression[..i].Trim();
                    string right = expression[(i + pattern.Length)..].Trim();
                    if (left.Length > 0 && right.Length > 0)
                        return (left, logicalOp, right);
                }
            }
        }

        return (expression, null, string.Empty);
    }

    private static int FindClosingParen(string expression, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < expression.Length; i++)
        {
            if (expression[i] == '(') depth++;
            else if (expression[i] == ')') depth--;
            if (depth == 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// Sanitizes a column/property identifier to prevent SQL injection.
    /// Only alphanumeric characters and underscores are allowed.
    /// </summary>
    private static string SanitizeIdentifier(string identifier)
    {
        string sanitized = IdentifierPattern().Replace(identifier.Trim(), string.Empty);
        if (string.IsNullOrEmpty(sanitized))
            throw new ArgumentException($"Invalid identifier: '{identifier}'");
        return sanitized;
    }

    private static object ParseValue(string raw)
    {
        raw = raw.Trim();

        // Quoted string
        if (raw.StartsWith('\'') && raw.EndsWith('\''))
            return UnquoteValue(raw);

        // Boolean
        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;

        // Integer
        if (long.TryParse(raw, out long longVal))
            return longVal;

        // Decimal
        if (decimal.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out decimal decVal))
            return decVal;

        // GUID
        if (Guid.TryParse(raw, out Guid guidVal))
            return guidVal;

        // DateTimeOffset (ISO 8601)
        if (DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset dtoVal))
            return dtoVal;

        return raw;
    }

    private static string UnquoteValue(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith('\'') && raw.EndsWith('\''))
            raw = raw[1..^1];
        // OData uses '' for escaped single quotes
        return raw.Replace("''", "'");
    }

    /// <summary>
    /// Escapes special characters in LIKE patterns (%, _, [, \).
    /// </summary>
    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_")
            .Replace("[", "\\[");
    }

    private string NextParameterName() => $"@p{_parameterIndex++}";

    [GeneratedRegex(@"^(?<func>contains|startswith|endswith)\((?<prop>[^,]+),\s*(?<val>'[^']*(?:''[^']*)*')\)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex FunctionPattern();

    [GeneratedRegex(@"^(?<prop>[A-Za-z_]\w*)\s+(?<op>eq|ne|gt|ge|lt|le)\s+(?<val>.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ComparisonPattern();

    [GeneratedRegex(@"[^A-Za-z0-9_]")]
    private static partial Regex IdentifierPattern();
}
