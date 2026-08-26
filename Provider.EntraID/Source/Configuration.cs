using PenguinConverters.Keyra.Settings;

namespace PenguinConverters.Syntra.Provider.EntraID.Source;

/// <summary>
/// Configuration settings for the Entra ID (Azure AD) source provider.
/// Defines Microsoft Graph API connection parameters, the OData query to issue, and the
/// relationship endpoint whose nested objects are streamed instead of the objects themselves.
/// </summary>
/// <remarks>
/// The type is self-referential: <see cref="Relationship"/> is another
/// <see cref="Configuration"/>, so a nested endpoint carries its own property projection,
/// query parameters and headers. Only the fields that make sense on a nested read are
/// consulted there; credentials and <see cref="BaseUrl"/> always come from the outer instance.
/// </remarks>
public class Configuration
{
    #region Constants

    /// <summary>
    /// Microsoft Graph service root used when <see cref="BaseUrl"/> is not configured.
    /// </summary>
    public const string DefaultBaseUrl = "https://graph.microsoft.com/v1.0";

    /// <summary>
    /// Graph endpoint used when <see cref="EndPoint"/> is not configured.
    /// </summary>
    public const string DefaultEndPoint = "directoryObjects";

    /// <summary>
    /// Number of retries a throttled or failed read is given. Default: <c>1</c>.
    /// </summary>
    public const int DefaultReadRetryMaxCount = 1;

    /// <summary>
    /// Base delay in seconds between retries of a throttled or failed read. Default: <c>5</c>.
    /// </summary>
    public const int DefaultReadRetryDelaySeconds = 5;

    /// <summary>
    /// Number of relationship endpoints read concurrently. Default: <c>5</c>.
    /// </summary>
    public const int DefaultMaxDegreeOfParallelism = 5;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the Microsoft Graph service root, which selects the API version.
    /// Defaults to <see cref="DefaultBaseUrl"/>; set it to the <c>/beta</c> root to reach
    /// endpoints that have not reached general availability.
    /// </summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>
    /// Gets or sets the Azure AD tenant identifier.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the application (client) identifier for the Graph API service principal.
    /// Leaving it unset falls back to the ambient identity of the host, which is the managed
    /// identity when running in Azure.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret credential for the Graph API service principal.
    /// Uses <see cref="Secret"/> for optional Keyra encryption support.
    /// </summary>
    public Secret? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the Microsoft Graph API endpoint path relative to <see cref="BaseUrl"/>
    /// (for example <c>users</c>, <c>groups</c>, <c>auditLogs/directoryAudits</c>).
    /// Defaults to <see cref="DefaultEndPoint"/>.
    /// </summary>
    public string? EndPoint { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether delta synchronization is enabled.
    /// When <c>true</c> the <c>delta</c> segment is appended to <see cref="EndPoint"/> and the
    /// delta token of the previous run, if any, scopes the query to what has changed since.
    /// </summary>
    public bool Delta { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="EndPoint"/> names a multi-valued
    /// property of the parent object rather than a nested endpoint to read.
    /// Meaningful on <see cref="Relationship"/>: a delta response carries its link changes in a
    /// property such as <c>members@delta</c>, which needs no second request.
    /// </summary>
    public bool PropertyEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the nested relationship whose objects are streamed in place of the objects
    /// returned by <see cref="EndPoint"/>.
    /// </summary>
    public Configuration? Relationship { get; set; }

    /// <summary>
    /// Gets or sets the properties to project through <c>$select</c>.
    /// When unset, the properties requested by the consumer are used instead, less
    /// <see cref="PropertiesToIgnore"/>.
    /// </summary>
    public string[]? PropertiesToLoad { get; set; }

    /// <summary>
    /// Gets or sets the properties to withhold from <c>$select</c> when the projection is
    /// derived from the properties the consumer requested. Ignored when
    /// <see cref="PropertiesToLoad"/> is set.
    /// </summary>
    public string[]? PropertiesToIgnore { get; set; }

    /// <summary>
    /// Gets or sets the OData query parameters sent with the request, such as <c>$filter</c>
    /// or <c>$count</c>. <c>$select</c> and <c>$deltatoken</c> are added by the provider.
    /// </summary>
    public SortedList<string, object>? Parameters { get; set; }

    /// <summary>
    /// Gets or sets additional HTTP request headers. Advanced queries need
    /// <c>ConsistencyLevel: eventual</c> here.
    /// </summary>
    public SortedList<string, string>? HttpHeaders { get; set; }

    /// <summary>
    /// Gets or sets the number of retries a throttled or failed read is given.
    /// Projected onto the Kiota retry handler, which honours the <c>Retry-After</c> header
    /// Graph sends with an HTTP 429. Defaults to <see cref="DefaultReadRetryMaxCount"/>.
    /// </summary>
    public int ReadRetryMaxCount { get; set; } = DefaultReadRetryMaxCount;

    /// <summary>
    /// Gets or sets the base delay in seconds between retries. The retry handler backs off
    /// exponentially from it, and a <c>Retry-After</c> header overrides it.
    /// Defaults to <see cref="DefaultReadRetryDelaySeconds"/>.
    /// </summary>
    public int ReadRetryDelaySeconds { get; set; } = DefaultReadRetryDelaySeconds;

    /// <summary>
    /// Gets or sets the number of relationship endpoints read concurrently during a full pass.
    /// Defaults to <see cref="DefaultMaxDegreeOfParallelism"/>.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = DefaultMaxDegreeOfParallelism;

    #endregion

    #region Methods

    /// <summary>
    /// Adds an OData query parameter.
    /// </summary>
    /// <param name="key">The parameter name, including its leading <c>$</c> where applicable.</param>
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

    #endregion
}
