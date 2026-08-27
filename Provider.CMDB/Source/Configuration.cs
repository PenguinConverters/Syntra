using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.CMDB.Source;

/// <summary>
/// Configuration for the CMDB source provider.
/// </summary>
/// <remarks>
/// Everything a CMDB read needs beyond the installation's own details - which host, which form,
/// which credentials - is settled here as a default, so a configuration file carries only what
/// actually varies. The rest of the connector is
/// <see cref="PenguinConverters.Syntra.Provider.RESTful.Provider"/> unchanged.
/// <para>
/// The shape these defaults describe is the CMDB REST API: a JSON envelope carrying its records
/// under <c>entries</c>, each wrapping its fields in <c>values</c> alongside its links, with the
/// next page addressed by a HAL link and the session established by a JWT login.
/// </para>
/// </remarks>
public class Configuration : RESTful.Source.Configuration
{
    #region Constants

    /// <summary>
    /// Endpoint a session is established at.
    /// </summary>
    public const string DefaultLoginEndPoint = "/api/jwt/login";

    /// <summary>
    /// Endpoint a session is released at.
    /// </summary>
    public const string DefaultLogoutEndPoint = "/api/jwt/logout";

    /// <summary>
    /// Scheme the session token is presented under.
    /// </summary>
    public const string DefaultTokenScheme = "AR-JWT";

    /// <summary>
    /// Header identifying the kind of client making the request.
    /// </summary>
    public const string DefaultClientTypeHeader = "X-AR-Client-Type";

    /// <summary>
    /// Client type an integration identifies itself as.
    /// </summary>
    public const string DefaultClientType = "34";

    /// <summary>
    /// Query parameter the property projection is sent under.
    /// </summary>
    public const string DefaultPropertiesParameter = "fields";

    /// <summary>
    /// Format the property projection is wrapped in, which addresses the field values of a record
    /// rather than the record envelope.
    /// </summary>
    public const string DefaultProjectionFormat = "values({0})";

    /// <summary>
    /// Path to the collection within the response.
    /// </summary>
    public const string DefaultResultPath = "entries";

    /// <summary>
    /// Path within each element of the collection to the object carrying the field values.
    /// </summary>
    public const string DefaultEntryPath = "values";

    /// <summary>
    /// Path to the URL of the next page within the response.
    /// </summary>
    public const string DefaultNextLinkPath = "_links.next.0.href";

    /// <summary>
    /// Query parameter the record filter is sent under.
    /// </summary>
    public const string DefaultFilterParameter = "q";

    /// <summary>
    /// Format of the delta filter, in the quoted query language the API expects.
    /// </summary>
    public const string DefaultFilterFormat = "'{0}' > \"{1}\"";

    /// <summary>
    /// Property carrying the modification timestamp a delta run watermarks on.
    /// </summary>
    public const string DefaultOffsetProperty = "Modified Date";

    /// <summary>
    /// Property that marks a record as deleted.
    /// </summary>
    public const string DefaultDeletedProperty = "Mark As Deleted";

    /// <summary>
    /// Value of <see cref="DefaultDeletedProperty"/> that marks a record as deleted.
    /// </summary>
    public const string DefaultDeletedMarker = "Yes";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Configuration"/> class with the defaults the
    /// CMDB REST API needs. Every one of them may be overridden by the configuration file.
    /// </summary>
    public Configuration()
    {
        ApplyCmdbDefaults();
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public override void ApplyDefaults()
    {
        base.ApplyDefaults();

        ApplyCmdbDefaults();
    }

    /// <summary>
    /// Fills in whatever the configuration file left unset. Every assignment is conditional, so
    /// running this after deserialization restores the defaults a mentioned section discarded -
    /// a file naming only a username and a password would otherwise take the JWT login, the
    /// logout and the scheme down with it.
    /// </summary>
    private void ApplyCmdbDefaults()
    {
        PropertiesParameter ??= DefaultPropertiesParameter;

        if (PropertiesFormat == DefaultPropertiesFormat)
        {
            PropertiesFormat = DefaultProjectionFormat;
        }

        ResultPath ??= DefaultResultPath;
        EntryPath ??= DefaultEntryPath;

        OffsetProperty ??= DefaultOffsetProperty;
        FilterParameter ??= DefaultFilterParameter;
        FilterFormat ??= DefaultFilterFormat;

        DeletedProperty ??= DefaultDeletedProperty;

        if (DeletedValue == RESTful.Source.Configuration.DefaultDeletedValue)
        {
            DeletedValue = DefaultDeletedMarker;
        }

        Pagination ??= new PaginationSettings();

        if (Pagination.Mode == PaginationMode.None)
        {
            Pagination.Mode = PaginationMode.NextLink;
        }

        Pagination.NextLinkPath ??= DefaultNextLinkPath;

        Authentication ??= new AuthenticationSettings();

        if (Authentication.Mode == AuthenticationMode.None)
        {
            Authentication.Mode = AuthenticationMode.Session;
        }

        Authentication.TokenEndPoint ??= DefaultLoginEndPoint;
        Authentication.LogoutEndPoint ??= DefaultLogoutEndPoint;

        if (Authentication.Scheme == AuthenticationSettings.DefaultScheme)
        {
            Authentication.Scheme = DefaultTokenScheme;
        }

        AddHttpHeader(DefaultClientTypeHeader, DefaultClientType, false);
    }

    #endregion
}
