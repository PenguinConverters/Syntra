using System.Globalization;
using PenguinConverters.Syntra.Provider.CMDB.Source;

namespace PenguinConverters.Syntra.Provider.CMDB;

/// <summary>
/// CMDB source provider, reading records from the CMDB REST API.
/// </summary>
/// <remarks>
/// Retrieval, paging, the JWT session, delta filtering and deletion marking all come from
/// <see cref="RESTful.Provider"/> and are described by <see cref="Configuration"/>. What is left
/// here is the one thing configuration cannot state: the API returns its timestamps as strings,
/// and a value handler turns them into the type a consumer should store.
/// </remarks>
public class Provider : RESTful.Provider
{
    #region Constants

    /// <summary>
    /// Format the API renders a timestamp in.
    /// </summary>
    public const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Provider"/> class.
    /// </summary>
    public Provider()
    {
        // The watermark property arrives as a quoted string. Coercing it here rather than at the
        // consumer means the delta watermark and the stored column agree on what the value is,
        // and a record whose timestamp the API renders oddly is left as text rather than dropped.
        AddValueHandler(Source.Configuration.DefaultOffsetProperty, ReadTimestamp);
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    protected override RESTful.Source.Configuration? ReadConfiguration()
    {
        return DeserializeConfiguration<Configuration>();
    }

    /// <summary>
    /// Reads a timestamp the API rendered as a string.
    /// </summary>
    /// <param name="value">The value as the response carried it.</param>
    /// <returns>
    /// The timestamp in UTC, or the value unchanged when it is not one the API's format describes.
    /// </returns>
    private static object? ReadTimestamp(object? value)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return value;
        }

        return DateTime.TryParseExact(
            text,
            TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out DateTime timestamp)
            ? timestamp
            : value;
    }

    #endregion
}
