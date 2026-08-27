using System.Runtime.CompilerServices;
using System.Text;
using PenguinConverters.Syntra.Core.Types;

namespace PenguinConverters.Syntra.Provider.Tenable.Source;

/// <summary>
/// Reads a delimited export - the format a Tenable report downloads as - into one property bag
/// per row, streaming as the response arrives.
/// </summary>
/// <remarks>
/// The BCL candidate for this is <c>Microsoft.VisualBasic.FileIO.TextFieldParser</c>, which is
/// available on this target with no package reference and handles the same quoting rules. It is
/// not used because it is synchronous only: it has no asynchronous read, so parsing a report
/// through it would block a thread pool thread for as long as the download takes. A scan export
/// runs to hundreds of megabytes, and this connector exists to stream one.
/// <para>
/// The rules implemented are RFC 4180: a field may be quoted, a quoted field may contain the
/// delimiter and line breaks, and a quote inside a quoted field is written twice.
/// </para>
/// </remarks>
public static class DelimitedReader
{
    #region Constants

    /// <summary>
    /// Characters read from the response at a time.
    /// </summary>
    private const int BufferSize = 8192;

    /// <summary>
    /// Format naming a column the header row does not, where <c>{0}</c> is its position.
    /// </summary>
    public const string UnnamedColumnFormat = "Column {0}";

    /// <summary>
    /// Format disambiguating a column name the header row repeats, where <c>{0}</c> is the name
    /// and <c>{1}</c> is how many times it has been seen.
    /// </summary>
    public const string DuplicateColumnFormat = "{0} ({1})";

    #endregion

    #region Methods

    /// <summary>
    /// Streams the rows of a delimited export, taking the first row as the column names.
    /// </summary>
    /// <param name="content">The response body.</param>
    /// <param name="delimiter">The character separating fields.</param>
    /// <param name="encoding">The encoding the body is written in.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the read.</param>
    /// <returns>One property bag per row.</returns>
    public static async IAsyncEnumerable<QuickDictionary> ReadAsync(
        Stream content,
        char delimiter,
        Encoding encoding,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string[]? columns = null;

        await foreach (List<string> row in ReadRowsAsync(content, delimiter, encoding, cancellationToken)
            .ConfigureAwait(false))
        {
            if (columns is null)
            {
                columns = NameColumns(row);
                continue;
            }

            yield return Project(columns, row);
        }
    }

    /// <summary>
    /// Projects a row onto a property bag.
    /// </summary>
    /// <param name="columns">The column names.</param>
    /// <param name="row">The cells of the row.</param>
    /// <returns>The property bag.</returns>
    private static QuickDictionary Project(string[] columns, List<string> row)
    {
        QuickDictionary properties = new QuickDictionary(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < row.Count; index++)
        {
            // A row wider than the header keeps its surplus cells under their position rather
            // than dropping them, so a malformed export loses nothing silently.
            string column = index < columns.Length
                ? columns[index]
                : string.Format(UnnamedColumnFormat, index + 1);

            properties[column] = row[index];
        }

        return properties;
    }

    /// <summary>
    /// Names the columns from the header row, disambiguating any name it repeats.
    /// </summary>
    /// <remarks>
    /// A report is free to carry the same column name twice. Letting the second overwrite the
    /// first would drop a column without saying so, and a property bag cannot hold both under one
    /// name, so the repeat is suffixed with its occurrence.
    /// </remarks>
    /// <param name="row">The header row.</param>
    /// <returns>The column names.</returns>
    private static string[] NameColumns(List<string> row)
    {
        string[] columns = new string[row.Count];
        Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < row.Count; index++)
        {
            string name = string.IsNullOrWhiteSpace(row[index])
                ? string.Format(UnnamedColumnFormat, index + 1)
                : row[index];

            if (seen.TryGetValue(name, out int count))
            {
                seen[name] = ++count;
                name = string.Format(DuplicateColumnFormat, name, count);
            }
            else
            {
                seen[name] = 1;
            }

            columns[index] = name;
        }

        return columns;
    }

    /// <summary>
    /// Streams the rows of a delimited body.
    /// </summary>
    /// <param name="content">The body.</param>
    /// <param name="delimiter">The character separating fields.</param>
    /// <param name="encoding">The encoding the body is written in.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the read.</param>
    /// <returns>The cells of each row.</returns>
    private static async IAsyncEnumerable<List<string>> ReadRowsAsync(
        Stream content,
        char delimiter,
        Encoding encoding,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using StreamReader reader = new StreamReader(content, encoding, detectEncodingFromByteOrderMarks: true);

        char[] buffer = new char[BufferSize];

        StringBuilder field = new StringBuilder();
        List<string> row = [];

        bool quoted = false;
        bool quotePending = false;
        bool skipLineFeed = false;
        bool started = false;

        int read;

        while ((read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            for (int index = 0; index < read; index++)
            {
                char character = buffer[index];

                // A carriage return ends the row; the line feed that usually follows it is part
                // of the same break, not an empty row after it.
                if (skipLineFeed)
                {
                    skipLineFeed = false;

                    if (character == '\n')
                    {
                        continue;
                    }
                }

                if (quotePending)
                {
                    quotePending = false;

                    if (character == '"')
                    {
                        // A doubled quote inside a quoted field is one literal quote.
                        field.Append('"');
                        started = true;
                        continue;
                    }

                    quoted = false;
                }
                else if (quoted)
                {
                    if (character == '"')
                    {
                        quotePending = true;
                        continue;
                    }

                    field.Append(character);
                    started = true;
                    continue;
                }
                else if (character == '"' && field.Length == 0)
                {
                    quoted = true;
                    started = true;
                    continue;
                }

                if (character == delimiter)
                {
                    row.Add(field.ToString());
                    field.Clear();
                    started = true;
                    continue;
                }

                if (character is '\r' or '\n')
                {
                    skipLineFeed = character == '\r';

                    row.Add(field.ToString());
                    field.Clear();

                    if (!IsBlank(row))
                    {
                        yield return row;
                    }

                    row = [];
                    started = false;
                    continue;
                }

                field.Append(character);
                started = true;
            }
        }

        // A body that does not end with a line break still holds a final row.
        if (started || field.Length > 0)
        {
            row.Add(field.ToString());

            if (!IsBlank(row))
            {
                yield return row;
            }
        }
    }

    /// <summary>
    /// Determines whether a row carries nothing, which is what a trailing line break produces.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns><c>true</c> when every cell is empty; otherwise, <c>false</c>.</returns>
    private static bool IsBlank(List<string> row)
    {
        foreach (string cell in row)
        {
            if (cell.Length > 0)
            {
                return false;
            }
        }

        return true;
    }

    #endregion
}
