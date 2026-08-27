using System.Text;
using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.Tenable.Source;

/// <summary>
/// Configuration for the Tenable source provider.
/// </summary>
/// <remarks>
/// Tenable identifies a caller by an access key beside a secret key in one header, and answers a
/// report download with a delimited export rather than JSON. Both are settled here as defaults.
/// </remarks>
public class Configuration : RESTful.Source.Configuration
{
    #region Constants

    /// <summary>
    /// Header the API key is presented in.
    /// </summary>
    public const string DefaultHeaderName = "x-apikey";

    /// <summary>
    /// Format composing the access key and the secret key into the header value.
    /// </summary>
    public const string DefaultKeyFormat = "accesskey={0};secretkey={1};";

    /// <summary>
    /// Media type a report download is requested as.
    /// </summary>
    public const string DefaultAccept = "text/csv";

    /// <summary>
    /// Character separating the fields of an export.
    /// </summary>
    public const char DefaultDelimiter = ',';

    /// <summary>
    /// Web name of the encoding an export is read with.
    /// </summary>
    public const string DefaultEncoding = "utf-8";

    /// <summary>
    /// Endpoint the reports available to the caller are listed at.
    /// </summary>
    public const string DefaultReportEndPoint = "rest/report";

    /// <summary>
    /// Path to the report list within the listing response.
    /// </summary>
    public const string DefaultReportResultPath = "response.usable";

    /// <summary>
    /// Property carrying a report's name in the listing.
    /// </summary>
    public const string DefaultReportNameProperty = "name";

    /// <summary>
    /// Property carrying a report's identifier in the listing.
    /// </summary>
    public const string DefaultReportIdentityProperty = "id";

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets what is made of each row of an export. Defaults to
    /// <see cref="Source.Plugin.None"/>, which stores the row as it stands;
    /// <see cref="Source.Plugin.Nessus"/> expands it into the observations its plugin output
    /// describes.
    /// </summary>
    public Plugin Plugin { get; set; } = Plugin.None;

    /// <summary>
    /// Gets or sets the character separating the fields of an export.
    /// Defaults to <see cref="DefaultDelimiter"/>.
    /// </summary>
    public char Delimiter { get; set; } = DefaultDelimiter;

    /// <summary>
    /// Gets or sets the web name of the encoding an export is read with, such as <c>utf-8</c>,
    /// <c>windows-1252</c> or <c>iso-8859-1</c>. Defaults to <see cref="DefaultEncoding"/>.
    /// </summary>
    public string Encoding { get; set; } = DefaultEncoding;

    /// <summary>
    /// Gets or sets the endpoint the reports available to the caller are listed at, which is read
    /// to resolve a <c>&lt;%ReportId(name)%&gt;</c> written into
    /// <see cref="RESTful.Source.Configuration.EndPoint"/>.
    /// Defaults to <see cref="DefaultReportEndPoint"/>.
    /// </summary>
    public string ReportEndPoint { get; set; } = DefaultReportEndPoint;

    /// <summary>
    /// Gets or sets the path to the report list within the listing response.
    /// Defaults to <see cref="DefaultReportResultPath"/>.
    /// </summary>
    public string ReportResultPath { get; set; } = DefaultReportResultPath;

    /// <summary>
    /// Gets or sets the property carrying a report's name in the listing.
    /// Defaults to <see cref="DefaultReportNameProperty"/>.
    /// </summary>
    public string ReportNameProperty { get; set; } = DefaultReportNameProperty;

    /// <summary>
    /// Gets or sets the property carrying a report's identifier in the listing.
    /// Defaults to <see cref="DefaultReportIdentityProperty"/>.
    /// </summary>
    public string ReportIdentityProperty { get; set; } = DefaultReportIdentityProperty;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Configuration"/> class with the defaults the
    /// Tenable API needs. Every one of them may be overridden by the configuration file.
    /// </summary>
    public Configuration()
    {
        ApplyTenableDefaults();
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public override void ApplyDefaults()
    {
        base.ApplyDefaults();

        ApplyTenableDefaults();
    }

    /// <summary>
    /// Fills in whatever the configuration file left unset. Every assignment is conditional, so
    /// running this after deserialization restores the defaults a mentioned section discarded -
    /// a file naming only the two keys would otherwise take the header name and the format that
    /// compose them down with it.
    /// </summary>
    private void ApplyTenableDefaults()
    {
        if (Accept == DefaultMediaType)
        {
            Accept = DefaultAccept;
        }

        Authentication ??= new AuthenticationSettings();

        if (Authentication.Mode == AuthenticationMode.None)
        {
            Authentication.Mode = AuthenticationMode.ApiKey;
        }

        if (Authentication.HeaderName == AuthenticationSettings.DefaultHeaderName)
        {
            Authentication.HeaderName = DefaultHeaderName;
        }

        if (Authentication.ValueFormat == AuthenticationSettings.DefaultValueFormat)
        {
            Authentication.ValueFormat = DefaultKeyFormat;
        }
    }

    /// <summary>
    /// Returns the encoding named by <see cref="Encoding"/>.
    /// </summary>
    /// <returns>
    /// The encoding, falling back to UTF-8 when the configured name does not name one.
    /// </returns>
    public Encoding GetEncoding()
    {
        try
        {
            return System.Text.Encoding.GetEncoding(Encoding);
        }
        catch (ArgumentException)
        {
            return System.Text.Encoding.UTF8;
        }
    }

    /// <summary>
    /// Returns the configuration the report listing is read with.
    /// </summary>
    /// <remarks>
    /// The listing answers with JSON while the export it points at answers with a delimited body,
    /// so the listing carries its own media type and result path rather than the report's.
    /// </remarks>
    /// <returns>The listing configuration.</returns>
    public RESTful.Source.Configuration GetReportConfiguration()
    {
        return new RESTful.Source.Configuration
        {
            BaseUrl = BaseUrl,
            Host = Host,
            Scheme = Scheme,
            Port = Port,
            EndPoint = ReportEndPoint,
            Accept = DefaultMediaType,
            ResultPath = ReportResultPath,
            IdentityProperty = ReportIdentityProperty
        };
    }

    #endregion
}
