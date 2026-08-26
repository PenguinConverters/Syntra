using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Types;

namespace PenguinConverters.Syntra.Provider.EntraID.Source;

/// <summary>
/// Reads Microsoft Graph collections page by page over an <see cref="HttpClient"/> built by
/// <c>KiotaClientFactory</c>.
/// </summary>
/// <remarks>
/// Nothing here retries, backs off, or inspects a status code for throttling: HTTP 429, 503 and
/// 504 are answered by the Kiota retry handler in the pipeline, which honours the
/// <c>Retry-After</c> header Graph sends with a throttling response, and redirects are followed
/// by the redirect handler. A response that still arrives here unsuccessful has exhausted those
/// handlers, so it is surfaced as an exception carrying the Graph error body.
/// </remarks>
internal sealed class GraphClient : IDisposable
{
    #region Constants

    /// <summary>
    /// The property holding the entries of a Graph collection response.
    /// </summary>
    public const string PropertyValue = "value";

    /// <summary>
    /// The property holding the URL of the next page of a Graph collection response.
    /// </summary>
    public const string PropertyNextLink = "@odata.nextLink";

    /// <summary>
    /// The property holding the URL that resumes a delta query, carrying the delta token.
    /// </summary>
    public const string PropertyDeltaLink = "@odata.deltaLink";

    /// <summary>
    /// Number of characters of an error response body carried into the exception message.
    /// </summary>
    private const int ErrorBodyLimit = 2048;

    #endregion

    #region Fields

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private bool _disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphClient"/> class.
    /// </summary>
    /// <param name="httpClient">
    /// The client carrying the Kiota middleware pipeline. It is owned by this instance and
    /// disposed with it.
    /// </param>
    /// <param name="logger">The logger to use for diagnostic output.</param>
    internal GraphClient(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Streams a Graph collection, following <c>@odata.nextLink</c> until the collection is
    /// exhausted. Each page is yielded as it arrives, so a large result set is never held whole.
    /// </summary>
    /// <param name="requestUri">The absolute URL of the first page.</param>
    /// <param name="headers">Additional request headers, or <c>null</c>.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the read.</param>
    /// <returns>An asynchronous stream of pages.</returns>
    public async IAsyncEnumerable<GraphPage> ReadAsync(
        string requestUri,
        IReadOnlyDictionary<string, string>? headers,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? next = requestUri;

        while (next is not null)
        {
            GraphPage page = await ReadPageAsync(next, headers, cancellationToken).ConfigureAwait(false);

            yield return page;

            next = page.NextLink;
        }
    }

    /// <summary>
    /// Parses a JSON array of Graph objects held as raw text, which is how a multi-valued
    /// property such as <c>members@delta</c> is carried on its parent object.
    /// </summary>
    /// <param name="json">The raw JSON text of the array, or of a single object.</param>
    /// <returns>One property bag per element.</returns>
    public static List<IDictionary<string, object?>> ParseEntries(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(json);

        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => ReadEntries(document.RootElement),
            JsonValueKind.Object => [ReadProperties(document.RootElement)],
            _ => []
        };
    }

    /// <summary>
    /// Reads a single page of a Graph collection.
    /// </summary>
    /// <param name="requestUri">The absolute URL of the page.</param>
    /// <param name="headers">Additional request headers, or <c>null</c>.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the read.</param>
    /// <returns>The page.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when Graph answers unsuccessfully after the middleware pipeline has done what it can.
    /// </exception>
    private async Task<GraphPage> ReadPageAsync(
        string requestUri,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        _logger.LogTrace("Requesting Graph page {RequestUri}.", requestUri);

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (headers is not null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            throw new HttpRequestException(
                $"Microsoft Graph answered {(int)response.StatusCode} {response.ReasonPhrase} for "
                + $"{requestUri}: {Truncate(body)}",
                null,
                response.StatusCode);
        }

        await using Stream content = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using JsonDocument document = await JsonDocument
            .ParseAsync(content, default, cancellationToken)
            .ConfigureAwait(false);

        return ReadPage(document.RootElement);
    }

    /// <summary>
    /// Projects a parsed Graph response onto a <see cref="GraphPage"/>.
    /// </summary>
    /// <param name="root">The root element of the response.</param>
    /// <returns>The page.</returns>
    private static GraphPage ReadPage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new GraphPage([], null, null);
        }

        List<IDictionary<string, object?>> entries;

        // A collection endpoint wraps its entries in "value"; addressing a single resource
        // returns the object itself.
        if (root.TryGetProperty(PropertyValue, out JsonElement value) && value.ValueKind == JsonValueKind.Array)
        {
            entries = ReadEntries(value);
        }
        else
        {
            entries = [ReadProperties(root)];
        }

        return new GraphPage(entries, ReadLink(root, PropertyNextLink), ReadLink(root, PropertyDeltaLink));
    }

    /// <summary>
    /// Reads a JSON array of Graph objects into property bags.
    /// </summary>
    /// <param name="array">The array to read.</param>
    /// <returns>One property bag per element.</returns>
    private static List<IDictionary<string, object?>> ReadEntries(JsonElement array)
    {
        List<IDictionary<string, object?>> entries = new List<IDictionary<string, object?>>(array.GetArrayLength());

        foreach (JsonElement element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                entries.Add(ReadProperties(element));
            }
        }

        return entries;
    }

    /// <summary>
    /// Projects a Graph object onto a case-insensitive property bag.
    /// </summary>
    /// <param name="element">The object to project.</param>
    /// <returns>The property bag.</returns>
    private static IDictionary<string, object?> ReadProperties(JsonElement element)
    {
        QuickDictionary properties = new QuickDictionary(StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty property in element.EnumerateObject())
        {
            properties[property.Name] = ReadValue(property.Value);
        }

        return properties;
    }

    /// <summary>
    /// Reads a JSON value as the CLR value a consumer can persist.
    /// </summary>
    /// <remarks>
    /// Objects and arrays keep their raw JSON text rather than being flattened. A relational
    /// consumer stores that text in a single column, and the provider re-parses it in place when
    /// a relationship is configured against a multi-valued property.
    /// </remarks>
    /// <param name="element">The value to read.</param>
    /// <returns>The CLR value.</returns>
    private static object? ReadValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            // Cast both arms: a conditional over long and double unifies on double, which would
            // surface every whole number as a floating point value.
            JsonValueKind.Number => element.TryGetInt64(out long number)
                ? number
                : (object)element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText()
        };
    }

    /// <summary>
    /// Reads an OData annotation holding a URL.
    /// </summary>
    /// <param name="root">The root element of the response.</param>
    /// <param name="name">The annotation to read.</param>
    /// <returns>The URL, or <c>null</c> when the annotation is absent or empty.</returns>
    private static string? ReadLink(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement link) || link.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? value = link.GetString();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Shortens an error response body to what is useful in an exception message.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <returns>The shortened body.</returns>
    private static string Truncate(string body)
    {
        return body.Length <= ErrorBodyLimit ? body : string.Concat(body.AsSpan(0, ErrorBodyLimit), "...");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    #endregion
}

/// <summary>
/// A single page of a Microsoft Graph collection response.
/// </summary>
/// <param name="Entries">The objects the page carries.</param>
/// <param name="NextLink">
/// The URL of the following page, or <c>null</c> when this is the last one.
/// </param>
/// <param name="DeltaLink">
/// The URL that resumes the delta query, carried on the last page of a delta response only.
/// </param>
internal sealed record GraphPage(
    IReadOnlyList<IDictionary<string, object?>> Entries,
    string? NextLink,
    string? DeltaLink);
