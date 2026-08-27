using System.Globalization;
using PenguinConverters.Syntra.Core.Types;

namespace PenguinConverters.Syntra.Provider.Tenable.Nessus;

/// <summary>
/// One row of a Nessus export: the host that was scanned, the plugin that ran against it, and the
/// text that plugin produced.
/// </summary>
/// <remarks>
/// The export names its columns in prose - <c>IP Address</c>, <c>Plugin Output</c> - so a row is
/// mapped onto this type by <see cref="FromRow"/> rather than by a serializer, which keeps the
/// column names in one visible place and avoids a round trip through JSON to reach them.
/// </remarks>
public class NessusPlugin
{
    #region Constants

    /// <summary>
    /// Column carrying the IP address of the scanned host.
    /// </summary>
    public const string ColumnIPAddress = "IP Address";

    /// <summary>
    /// Column carrying the DNS name of the scanned host.
    /// </summary>
    public const string ColumnDNSName = "DNS Name";

    /// <summary>
    /// Column carrying the name of the plugin that ran.
    /// </summary>
    public const string ColumnPluginName = "Plugin Name";

    /// <summary>
    /// Column carrying the text the plugin produced.
    /// </summary>
    public const string ColumnPluginOutput = "Plugin Output";

    /// <summary>
    /// Column carrying the port the service was reached on.
    /// </summary>
    public const string ColumnPort = "Port";

    /// <summary>
    /// Column carrying the identifier of the plugin that ran.
    /// </summary>
    public const string ColumnPlugin = "Plugin";

    /// <summary>
    /// Column carrying the date the asset was first discovered.
    /// </summary>
    public const string ColumnFirstDiscovered = "First Discovered";

    /// <summary>
    /// Column carrying the date the asset was last observed.
    /// </summary>
    public const string ColumnLastObserved = "Last Observed";

    /// <summary>
    /// Format the export renders a date in, such as <c>Jan 1, 2026 00:00:00 UTC</c>.
    /// </summary>
    public const string DateFormat = "MMM d, yyyy HH:mm:ss 'UTC'";

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the IP address of the scanned host.
    /// </summary>
    public string IPAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the DNS name of the scanned host.
    /// </summary>
    public string DNSName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the plugin that ran, which selects the parser.
    /// </summary>
    public string PluginName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the text the plugin produced, which the parsers read.
    /// </summary>
    public string PluginOutput { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the port the service was reached on.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the plugin that ran.
    /// </summary>
    public long? Plugin { get; set; }

    /// <summary>
    /// Gets or sets the date the asset was first discovered.
    /// </summary>
    public DateOnly? FirstDiscovered { get; set; }

    /// <summary>
    /// Gets or sets the date the asset was last observed.
    /// </summary>
    public DateOnly? LastObserved { get; set; }

    /// <summary>
    /// Gets the leading label of <see cref="DNSName"/>, which is the host name without its domain.
    /// </summary>
    public string? ShortName => string.IsNullOrEmpty(DNSName) ? null : DNSName.Split('.')[0];

    #endregion

    #region Methods

    /// <summary>
    /// Maps one row of an export onto a plugin record.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The plugin record.</returns>
    public static NessusPlugin FromRow(QuickDictionary row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new NessusPlugin
        {
            IPAddress = ReadText(row, ColumnIPAddress) ?? string.Empty,
            DNSName = ReadText(row, ColumnDNSName) ?? string.Empty,
            PluginName = ReadText(row, ColumnPluginName) ?? string.Empty,
            PluginOutput = ReadText(row, ColumnPluginOutput) ?? string.Empty,
            Port = ReadInt32(row, ColumnPort) ?? 0,
            Plugin = ReadInt64(row, ColumnPlugin),
            FirstDiscovered = ReadDate(row, ColumnFirstDiscovered),
            LastObserved = ReadDate(row, ColumnLastObserved)
        };
    }

    /// <summary>
    /// Reads a column as text.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The value, or <c>null</c> when the column is absent or empty.</returns>
    private static string? ReadText(QuickDictionary row, string column)
    {
        if (!row.TryGetValue(column, out object? value))
        {
            return null;
        }

        string? text = value?.ToString();

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Reads a column as a 32-bit whole number.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The value, or <c>null</c> when the column is absent or is not a number.</returns>
    private static int? ReadInt32(QuickDictionary row, string column)
    {
        return int.TryParse(
            ReadText(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    /// <summary>
    /// Reads a column as a 64-bit whole number.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The value, or <c>null</c> when the column is absent or is not a number.</returns>
    private static long? ReadInt64(QuickDictionary row, string column)
    {
        return long.TryParse(
            ReadText(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : null;
    }

    /// <summary>
    /// Reads a column as a date.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The date, or <c>null</c> when the column is absent or is not one.</returns>
    private static DateOnly? ReadDate(QuickDictionary row, string column)
    {
        string? text = ReadText(row, column);

        if (text is null)
        {
            return null;
        }

        if (DateTime.TryParseExact(
            text,
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTime exact))
        {
            return DateOnly.FromDateTime(exact);
        }

        // An export configured for another locale or a newer format still carries a date; falling
        // back to a general parse keeps it rather than discarding the record's timeline.
        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTime parsed)
            ? DateOnly.FromDateTime(parsed)
            : null;
    }

    #endregion
}
