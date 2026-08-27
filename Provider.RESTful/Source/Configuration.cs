using System.Reflection;

namespace PenguinConverters.Syntra.Provider.RESTful.Source;

/// <summary>
/// Describes one REST endpoint: where it is, how it is authenticated, how its response is shaped
/// and how it is continued past the first page.
/// </summary>
/// <remarks>
/// The type is self-referential through <see cref="Children"/>, so an endpoint whose objects are
/// only the keys to a second endpoint - policies behind devices, members behind groups - is
/// expressed without code. A child carries its own endpoint, parameters and result shape; the
/// connection settings, credentials and base URL always come from the root.
/// <para>
/// A connector for a specific API derives from this type and sets its defaults in the
/// constructor, so that a configuration file carries only what actually varies per installation.
/// </para>
/// </remarks>
public class Configuration
{
    #region Constants

    /// <summary>
    /// HTTP method used when none is configured.
    /// </summary>
    public const string DefaultHttpMethod = "GET";

    /// <summary>
    /// Media type requested and sent when none is configured.
    /// </summary>
    public const string DefaultMediaType = "application/json";

    /// <summary>
    /// Separator joining the projected property names when none is configured.
    /// </summary>
    public const string DefaultPropertiesSeparator = ",";

    /// <summary>
    /// Format the projected property names are wrapped in when none is configured, which passes
    /// the joined list through unchanged.
    /// </summary>
    public const string DefaultPropertiesFormat = "{0}";

    /// <summary>
    /// Format a delta filter is combined with an already configured filter by, where <c>{0}</c>
    /// is the configured filter and <c>{1}</c> is the delta filter.
    /// </summary>
    public const string DefaultFilterCombineFormat = "({0}) AND {1}";

    /// <summary>
    /// Format a delta watermark is rendered with when none is configured.
    /// </summary>
    public const string DefaultOffsetFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    /// <summary>
    /// Value that marks an object as deleted when none is configured.
    /// </summary>
    public const string DefaultDeletedValue = "true";

    /// <summary>
    /// Retries a failed read is given when none is configured.
    /// </summary>
    public const int DefaultReadRetryMaxCount = 3;

    /// <summary>
    /// Base delay in seconds between retries when none is configured.
    /// </summary>
    public const int DefaultReadRetryDelaySeconds = 5;

    /// <summary>
    /// Child endpoints read concurrently when no degree of parallelism is configured.
    /// </summary>
    public const int DefaultMaxDegreeOfParallelism = 5;

    /// <summary>
    /// Seconds a connection attempt is given when none is configured.
    /// </summary>
    public const int DefaultConnectTimeoutSeconds = 120;

    /// <summary>
    /// Seconds a whole request is given when none is configured. It bounds the retries the
    /// pipeline performs as well as the request itself.
    /// </summary>
    public const int DefaultRequestTimeoutSeconds = 600;

    /// <summary>
    /// Connections opened per server when none is configured.
    /// </summary>
    public const int DefaultMaxConnectionsPerServer = 10;

    /// <summary>
    /// Seconds a pooled connection is kept when none is configured.
    /// </summary>
    public const int DefaultPooledConnectionLifetimeSeconds = 120;

    /// <summary>
    /// Opening delimiter of a placeholder resolved against the parent object.
    /// </summary>
    public const string PlaceholderPrefix = "<%";

    /// <summary>
    /// Closing delimiter of a placeholder resolved against the parent object.
    /// </summary>
    public const string PlaceholderSuffix = "%>";

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the absolute service root, such as <c>https://api.example.com/v2</c>.
    /// It takes precedence over <see cref="Host"/>. Consulted on the root configuration only.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the host name the service root is composed from when <see cref="BaseUrl"/>
    /// is not configured. Consulted on the root configuration only.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// Gets or sets the scheme the service root is composed with.
    /// Defaults to <see cref="Uri.UriSchemeHttps"/>.
    /// </summary>
    public string Scheme { get; set; } = Uri.UriSchemeHttps;

    /// <summary>
    /// Gets or sets the port the service root is composed with. <c>-1</c> uses the default port
    /// of <see cref="Scheme"/>.
    /// </summary>
    public int Port { get; set; } = -1;

    /// <summary>
    /// Gets or sets the endpoint path relative to the service root.
    /// A placeholder written as <c>&lt;%property%&gt;</c> is replaced with that property of the
    /// parent object, which is how a child endpoint is addressed per parent.
    /// </summary>
    public string? EndPoint { get; set; }

    /// <summary>
    /// Gets or sets the HTTP method the endpoint is read with.
    /// Defaults to <see cref="DefaultHttpMethod"/>.
    /// </summary>
    public string HttpMethod { get; set; } = DefaultHttpMethod;

    /// <summary>
    /// Gets or sets the request body, for an API that takes its query as a posted document.
    /// It accepts the same <c>&lt;%property%&gt;</c> placeholders as <see cref="EndPoint"/>.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Gets or sets the media type of <see cref="Body"/>.
    /// Defaults to <see cref="DefaultMediaType"/>.
    /// </summary>
    public string ContentType { get; set; } = DefaultMediaType;

    /// <summary>
    /// Gets or sets the media type requested. Defaults to <see cref="DefaultMediaType"/>.
    /// </summary>
    public string Accept { get; set; } = DefaultMediaType;

    /// <summary>
    /// Gets or sets the query parameters sent with the request. The provider adds the property
    /// projection, the delta filter and the paging parameters to whatever is configured here.
    /// </summary>
    public SortedList<string, object>? Parameters { get; set; }

    /// <summary>
    /// Gets or sets additional request headers.
    /// </summary>
    public SortedList<string, string>? HttpHeaders { get; set; }

    /// <summary>
    /// Gets or sets the properties to project. When unset, the properties the consumer asked for
    /// are projected instead, less <see cref="PropertiesToIgnore"/>.
    /// </summary>
    public string[]? PropertiesToLoad { get; set; }

    /// <summary>
    /// Gets or sets the properties to withhold from a projection derived from what the consumer
    /// asked for. Ignored when <see cref="PropertiesToLoad"/> is set.
    /// </summary>
    public string[]? PropertiesToIgnore { get; set; }

    /// <summary>
    /// Gets or sets the query parameter the projection is sent under, such as <c>fields</c>,
    /// <c>_return_fields</c> or <c>$select</c>. Leaving it unset sends no projection, so the API
    /// returns whatever it returns by default.
    /// </summary>
    public string? PropertiesParameter { get; set; }

    /// <summary>
    /// Gets or sets the format the joined projection is wrapped in, where <c>{0}</c> is the
    /// joined list - <c>values({0})</c> for an API that expects a function call.
    /// Defaults to <see cref="DefaultPropertiesFormat"/>.
    /// </summary>
    public string PropertiesFormat { get; set; } = DefaultPropertiesFormat;

    /// <summary>
    /// Gets or sets the separator the projected property names are joined with.
    /// Defaults to <see cref="DefaultPropertiesSeparator"/>.
    /// </summary>
    public string PropertiesSeparator { get; set; } = DefaultPropertiesSeparator;

    /// <summary>
    /// Gets or sets the path to the collection within the response, such as <c>entries</c>,
    /// <c>result</c> or <c>response.usable</c>. Leaving it unset takes the response itself,
    /// which covers an endpoint answering with a bare array and one answering with a single
    /// object.
    /// </summary>
    public string? ResultPath { get; set; }

    /// <summary>
    /// Gets or sets the path within each element of the collection to the object carrying the
    /// properties, such as <c>values</c> for an API that wraps every record alongside its links.
    /// </summary>
    public string? EntryPath { get; set; }

    /// <summary>
    /// Gets or sets the property holding the unique identity of an object. It becomes the entity
    /// identifier and addresses the object when a child endpoint is read.
    /// </summary>
    public string? IdentityProperty { get; set; }

    /// <summary>
    /// Gets or sets the property the parent identity is stamped onto, so that a consumer can key
    /// a link on both ends. Consulted on a child configuration.
    /// </summary>
    public string? ParentIdentityProperty { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the properties of the parent object are merged
    /// into each object a child endpoint returns. Defaults to <c>false</c>; the properties named
    /// by <see cref="Properties"/> are merged either way.
    /// </summary>
    public bool InheritParentProperties { get; set; }

    /// <summary>
    /// Gets or sets properties added to every object this endpoint produces. A value written as
    /// <c>&lt;%property%&gt;</c> copies that property of the object itself, which renames it;
    /// any other value is a constant, which tags the object with the endpoint it came from.
    /// </summary>
    public SortedList<string, object>? Properties { get; set; }

    /// <summary>
    /// Gets or sets the endpoints read for each object this one returns. When it is set, the
    /// objects of the child endpoints are streamed and the objects of this one are not.
    /// </summary>
    public Configuration[]? Children { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether only what changed since the previous run is
    /// retrieved. It needs <see cref="OffsetProperty"/> and <see cref="FilterParameter"/> to
    /// have anything to filter on.
    /// </summary>
    public bool Delta { get; set; }

    /// <summary>
    /// Gets or sets the property carrying the modification timestamp a delta run watermarks on.
    /// </summary>
    public string? OffsetProperty { get; set; }

    /// <summary>
    /// Gets or sets the query parameter the delta filter is sent under, such as <c>q</c> or
    /// <c>$filter</c>.
    /// </summary>
    public string? FilterParameter { get; set; }

    /// <summary>
    /// Gets or sets the format of the delta filter, where <c>{0}</c> is
    /// <see cref="OffsetProperty"/> and <c>{1}</c> is the watermark of the previous run.
    /// </summary>
    public string? FilterFormat { get; set; }

    /// <summary>
    /// Gets or sets the format the delta filter is combined with a filter already present in
    /// <see cref="Parameters"/> by, where <c>{0}</c> is the configured filter and <c>{1}</c> is
    /// the delta filter. Defaults to <see cref="DefaultFilterCombineFormat"/>.
    /// </summary>
    public string FilterCombineFormat { get; set; } = DefaultFilterCombineFormat;

    /// <summary>
    /// Gets or sets the format the watermark is rendered with.
    /// Defaults to <see cref="DefaultOffsetFormat"/>.
    /// </summary>
    public string OffsetFormat { get; set; } = DefaultOffsetFormat;

    /// <summary>
    /// Gets or sets the property that marks an object as deleted.
    /// </summary>
    public string? DeletedProperty { get; set; }

    /// <summary>
    /// Gets or sets the value of <see cref="DeletedProperty"/> that marks an object as deleted,
    /// compared case-insensitively. Defaults to <see cref="DefaultDeletedValue"/>.
    /// </summary>
    public string DeletedValue { get; set; } = DefaultDeletedValue;

    /// <summary>
    /// Gets or sets the credentials and the protocol they are presented with.
    /// Consulted on the root configuration only.
    /// </summary>
    public AuthenticationSettings? Authentication { get; set; }

    /// <summary>
    /// Gets or sets how this endpoint is continued past its first page.
    /// </summary>
    public PaginationSettings? Pagination { get; set; }

    /// <summary>
    /// Gets or sets the forward proxy requests are routed through.
    /// Consulted on the root configuration only.
    /// </summary>
    public ProxySettings? Proxy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server certificate is verified. Defaults to
    /// <c>true</c>; turning it off accepts any certificate and is only defensible against an
    /// appliance with a self-signed certificate on a trusted network.
    /// Consulted on the root configuration only.
    /// </summary>
    public bool RemoteCertificateValidation { get; set; } = true;

    /// <summary>
    /// Gets or sets the retries a failed read is given. Projected onto the Kiota retry handler,
    /// which honours a <c>Retry-After</c> header.
    /// Defaults to <see cref="DefaultReadRetryMaxCount"/>.
    /// </summary>
    public int ReadRetryMaxCount { get; set; } = DefaultReadRetryMaxCount;

    /// <summary>
    /// Gets or sets the base delay in seconds between retries, which the handler backs off
    /// exponentially from. Defaults to <see cref="DefaultReadRetryDelaySeconds"/>.
    /// </summary>
    public int ReadRetryDelaySeconds { get; set; } = DefaultReadRetryDelaySeconds;

    /// <summary>
    /// Gets or sets the child endpoints read concurrently.
    /// Defaults to <see cref="DefaultMaxDegreeOfParallelism"/>.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = DefaultMaxDegreeOfParallelism;

    /// <summary>
    /// Gets or sets the seconds a connection attempt is given.
    /// Defaults to <see cref="DefaultConnectTimeoutSeconds"/>.
    /// </summary>
    public int ConnectTimeoutSeconds { get; set; } = DefaultConnectTimeoutSeconds;

    /// <summary>
    /// Gets or sets the seconds a whole request is given, retries included.
    /// Defaults to <see cref="DefaultRequestTimeoutSeconds"/>.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;

    /// <summary>
    /// Gets or sets the connections opened per server.
    /// Defaults to <see cref="DefaultMaxConnectionsPerServer"/>.
    /// </summary>
    public int MaxConnectionsPerServer { get; set; } = DefaultMaxConnectionsPerServer;

    /// <summary>
    /// Gets or sets the seconds a pooled connection is kept before it is re-established, which
    /// bounds how long a stale DNS answer stays in use.
    /// Defaults to <see cref="DefaultPooledConnectionLifetimeSeconds"/>.
    /// </summary>
    public int PooledConnectionLifetimeSeconds { get; set; } = DefaultPooledConnectionLifetimeSeconds;

    #endregion

    #region Methods

    /// <summary>
    /// Re-applies this connector's defaults after a configuration file has been deserialized
    /// over it.
    /// </summary>
    /// <remarks>
    /// A deserializer fills an object it is given rather than merging into one, so a nested
    /// section a configuration file mentions arrives whole and replaces what the constructor put
    /// there. A file naming nothing but a username and a password under
    /// <see cref="Authentication"/> would therefore also silently discard the authentication mode
    /// the connector defaults to, and its requests would go out anonymous.
    /// <para>
    /// A derived configuration overrides this to restore the defaults of any nested section it
    /// sets, filling in only what is still unset so that a configured value always wins. It runs
    /// once, after deserialization, whichever serializer the host uses.
    /// </para>
    /// </remarks>
    public virtual void ApplyDefaults()
    {
    }

    /// <summary>
    /// Adds a query parameter.
    /// </summary>
    /// <param name="key">The parameter name.</param>
    /// <param name="value">The parameter value.</param>
    /// <param name="overwrite">
    /// <c>true</c> to replace a value already configured under <paramref name="key"/>;
    /// <c>false</c> to leave the configured value in place. Defaults to <c>true</c>.
    /// </param>
    public void AddParameter(string key, object value, bool overwrite = true)
    {
        Parameters ??= new SortedList<string, object>(StringComparer.OrdinalIgnoreCase);

        if (!Parameters.ContainsKey(key))
        {
            Parameters.Add(key, value);
            return;
        }

        if (overwrite)
        {
            Parameters[key] = value;
        }
    }

    /// <summary>
    /// Adds a request header.
    /// </summary>
    /// <param name="key">The header name.</param>
    /// <param name="value">The header value.</param>
    /// <param name="overwrite">
    /// <c>true</c> to replace a value already configured under <paramref name="key"/>;
    /// <c>false</c> to leave the configured value in place. Defaults to <c>true</c>.
    /// </param>
    public void AddHttpHeader(string key, string value, bool overwrite = true)
    {
        HttpHeaders ??= new SortedList<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!HttpHeaders.ContainsKey(key))
        {
            HttpHeaders.Add(key, value);
            return;
        }

        if (overwrite)
        {
            HttpHeaders[key] = value;
        }
    }

    /// <summary>
    /// Returns the service root this configuration addresses, composed from
    /// <see cref="BaseUrl"/> or from <see cref="Host"/>, <see cref="Scheme"/> and
    /// <see cref="Port"/>.
    /// </summary>
    /// <returns>
    /// The service root without a trailing slash, or <c>null</c> when neither is configured.
    /// </returns>
    public string? GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Host))
        {
            return null;
        }

        UriBuilder builder = new UriBuilder
        {
            Scheme = string.IsNullOrWhiteSpace(Scheme) ? Uri.UriSchemeHttps : Scheme,
            Host = Host,
            Port = Port
        };

        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// Returns the HTTP method named by <see cref="HttpMethod"/>.
    /// </summary>
    /// <returns>
    /// The method, falling back to <see cref="System.Net.Http.HttpMethod.Get"/> when the
    /// configured name does not name one.
    /// </returns>
    public HttpMethod GetHttpMethod()
    {
        if (string.IsNullOrWhiteSpace(HttpMethod))
        {
            return System.Net.Http.HttpMethod.Get;
        }

        PropertyInfo? property = typeof(HttpMethod).GetProperty(
            HttpMethod,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);

        return property?.GetValue(null) as HttpMethod ?? System.Net.Http.HttpMethod.Get;
    }

    #endregion
}
