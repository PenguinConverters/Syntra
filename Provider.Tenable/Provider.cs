using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Types;
using PenguinConverters.Syntra.Provider.RESTful.Source;
using PenguinConverters.Syntra.Provider.Tenable.Nessus;
using PenguinConverters.Syntra.Provider.Tenable.Source;

namespace PenguinConverters.Syntra.Provider.Tenable;

/// <summary>
/// Tenable source provider, reading a scan report export.
/// </summary>
/// <remarks>
/// Two things set a Tenable read apart from a plain JSON API, and each is one of the seams
/// <see cref="RESTful.Provider"/> offers rather than a reimplementation of the retrieval:
/// <list type="bullet">
///   <item><description>
///     A report downloads as a delimited export, so the response body is read by
///     <see cref="DelimitedReader"/> instead of being parsed as JSON.
///   </description></item>
///   <item><description>
///     A report's identifier changes every time it runs, so an endpoint may name the report
///     instead - <c>rest/report/&lt;%ReportId(Weekly Scan)%&gt;/download</c> - and the identifier
///     is looked up when the retrieval starts.
///   </description></item>
/// </list>
/// The composite API key header, the transport and the retry pipeline all come from the base.
/// <para>
/// Setting <see cref="Source.Configuration.Plugin"/> to <see cref="Source.Plugin.Nessus"/>
/// additionally expands each exported row into the observations its plugin output describes -
/// one record per certificate, cipher suite, SSH algorithm or SSH version - so that what the
/// scan printed as text becomes queryable. That expansion is
/// <see cref="NessusContentReader"/>, built as the content-reader delegate the base provider
/// calls; assigning <see cref="RESTful.Provider.ContentReader"/> replaces it with another.
/// </para>
/// </remarks>
public class Provider : RESTful.Provider
{
    #region Constants

    /// <summary>
    /// Name of the call that resolves a report's identifier from its name.
    /// </summary>
    public const string ReportIdFunction = "ReportId";

    /// <summary>
    /// Name the legacy connector used for the same call, accepted so that an endpoint carried
    /// over from it keeps working.
    /// </summary>
    public const string LegacyReportIdFunction = "GETReportMaxId";

    #endregion

    #region Fields

    private readonly ConcurrentDictionary<string, string> _resolvedEndPoints =
        new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

    #endregion

    #region Methods

    /// <inheritdoc />
    protected override RESTful.Source.Configuration? ReadConfiguration()
    {
        return DeserializeConfiguration<Source.Configuration>();
    }

    /// <inheritdoc />
    /// <remarks>
    /// A Tenable report answers with a delimited export, never with JSON, so the body is always
    /// read rather than parsed. The report listing is read directly and does not come through
    /// here.
    /// </remarks>
    protected override bool ReadsContent => true;

    /// <inheritdoc />
    protected override IAsyncEnumerable<QuickDictionary> ReadContent(
        Stream content,
        RESTful.Source.Configuration configuration,
        CancellationToken cancellationToken)
    {
        if (ContentReader is not null)
        {
            return ContentReader(content, configuration, cancellationToken);
        }

        Source.Configuration settings = Configuration as Source.Configuration ?? new Source.Configuration();

        // The Nessus expansion is the content-reader delegate, built here rather than assigned
        // so that selecting it stays a configuration choice. A host is free to assign its own to
        // ContentReader instead, which the branch above hands back to.
        if (settings.Plugin == Source.Plugin.Nessus)
        {
            return NessusContentReader.ReadAsync(
                content, settings.Delimiter, settings.GetEncoding(), Logger, cancellationToken);
        }

        return DelimitedReader.ReadAsync(
            content, settings.Delimiter, settings.GetEncoding(), cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask<string> ResolveEndPointAsync(
        RESTful.Source.Configuration configuration,
        string endPoint,
        CancellationToken cancellationToken)
    {
        if (EndPointResolver is not null)
        {
            return await EndPointResolver(configuration, endPoint, cancellationToken).ConfigureAwait(false);
        }

        if (!FunctionPlaceholder.TryParse(endPoint, out string placeholder, out string name, out string[] arguments))
        {
            return endPoint;
        }

        if (!IsReportIdFunction(name))
        {
            Logger.LogWarning(
                "Endpoint '{EndPoint}' calls '{Function}', which this connector does not provide. "
                + "It is left as written.",
                endPoint,
                name);

            return endPoint;
        }

        if (arguments.Length == 0)
        {
            Logger.LogError("'{Placeholder}' names no report, so no identifier can be resolved.", placeholder);
            return endPoint;
        }

        string reportName = string.Join(',', arguments);

        // Resolving once per run keeps a nested endpoint from re-reading the listing per parent.
        string identity = await GetOrAddAsync(
            reportName,
            () => ResolveReportIdAsync(reportName, cancellationToken)).ConfigureAwait(false);

        return identity.Length == 0
            ? endPoint
            : endPoint.Replace(placeholder, identity, StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether a call names the report identifier lookup.
    /// </summary>
    /// <param name="name">The name of the call.</param>
    /// <returns><c>true</c> when it does; otherwise, <c>false</c>.</returns>
    private static bool IsReportIdFunction(string name)
    {
        return string.Equals(name, ReportIdFunction, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, LegacyReportIdFunction, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a resolved endpoint from the cache, resolving it once when it is not there.
    /// </summary>
    /// <param name="key">The report name.</param>
    /// <param name="resolve">The lookup.</param>
    /// <returns>The resolved identifier, or an empty value when it could not be resolved.</returns>
    private async Task<string> GetOrAddAsync(string key, Func<Task<string>> resolve)
    {
        if (_resolvedEndPoints.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        string identity = await resolve().ConfigureAwait(false);

        // A failed lookup is not cached: the report may simply not have run yet, and the next
        // retrieval should ask again rather than inherit this run's answer.
        if (identity.Length > 0)
        {
            _resolvedEndPoints[key] = identity;
        }

        return identity;
    }

    /// <summary>
    /// Reads the report listing and returns the highest identifier carried by a report of the
    /// given name.
    /// </summary>
    /// <remarks>
    /// A report keeps its name across runs and takes a new identifier each time, so the highest
    /// one is the most recent run.
    /// </remarks>
    /// <param name="reportName">The report name.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the read.</param>
    /// <returns>The identifier, or an empty value when no report of that name was found.</returns>
    private async Task<string> ResolveReportIdAsync(string reportName, CancellationToken cancellationToken)
    {
        if (Configuration is not Source.Configuration settings || RestClient is null)
        {
            return string.Empty;
        }

        RESTful.Source.Configuration listing = settings.GetReportConfiguration();

        string requestUri = $"{settings.GetBaseUrl()?.TrimEnd('/')}/{listing.EndPoint?.Trim('/')}";

        long highest = -1;

        Logger.LogTrace("Resolving the identifier of report '{Report}' from {RequestUri}.", reportName, requestUri);

        // The listing is JSON while the report itself is a delimited export, so it is read
        // without the content reader this provider otherwise applies to every body.
        await foreach (RestPage page in RestClient
            .ReadAsync(requestUri, listing, null, cancellationToken)
            .ConfigureAwait(false))
        {
            foreach (QuickDictionary report in page.Entries)
            {
                if (!report.TryGetValue(settings.ReportNameProperty, out object? name)
                    || !string.Equals(name?.ToString(), reportName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (report.TryGetValue(settings.ReportIdentityProperty, out object? identity)
                    && long.TryParse(
                        identity?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                    && value > highest)
                {
                    highest = value;
                }
            }
        }

        if (highest < 0)
        {
            Logger.LogError("No report named '{Report}' was found at {RequestUri}.", reportName, requestUri);
            return string.Empty;
        }

        Logger.LogInformation("Report '{Report}' resolved to {Identity}.", reportName, highest);

        return highest.ToString(CultureInfo.InvariantCulture);
    }

    #endregion
}
