using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Extensions;
using PenguinConverters.Syntra.Core.Types;

namespace PenguinConverters.Syntra.Provider.RESTful.Source;

/// <summary>
/// Reads a REST collection page by page over an <see cref="HttpClient"/> carrying the Kiota
/// middleware pipeline.
/// </summary>
/// <remarks>
/// Nothing here retries, backs off or inspects a status code for throttling: that is the retry
/// handler in the pipeline, which honours a <c>Retry-After</c> header, and redirects are followed
/// by the redirect handler. A response that still arrives here unsuccessful has exhausted those
/// handlers, so it is surfaced as an exception carrying the error body.
/// </remarks>
public sealed class RestClient : IDisposable
{
    #region Constants

    /// <summary>
    /// Characters of an error response body carried into the exception message.
    /// </summary>
    private const int ErrorBodyLimit = 2048;

    #endregion

    #region Fields

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly IDisposable? _owned;
    private bool _disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="RestClient"/> class.
    /// </summary>
    /// <param name="httpClient">
    /// The client carrying the Kiota middleware pipeline. It is owned by this instance and
    /// disposed with it.
    /// </param>
    /// <param name="logger">The logger to use for diagnostic output.</param>
    /// <param name="owned">
    /// A further resource whose lifetime follows this client, such as the session the
    /// authentication provider holds open. Disposed after the client.
    /// </param>
    public RestClient(HttpClient httpClient, ILogger logger, IDisposable? owned = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _owned = owned;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Streams a REST collection, following the configured pagination until it is exhausted.
    /// Each page is yielded as it arrives, so a large result set is never held whole.
    /// </summary>
    /// <param name="requestUri">The absolute URL of the first page.</param>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <param name="contentReader">
    /// Reads a response body that is not JSON, or <c>null</c> to parse the body as JSON.
    /// </param>
    /// <param name="cancellationToken">A token to signal cancellation of the read.</param>
    /// <returns>An asynchronous stream of pages.</returns>
    public async IAsyncEnumerable<RestPage> ReadAsync(
        string requestUri,
        Configuration configuration,
        Func<Stream, Configuration, CancellationToken, IAsyncEnumerable<QuickDictionary>>? contentReader,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        PaginationSettings pagination = configuration.Pagination ?? new PaginationSettings();

        string? next = requestUri;
        int pages = 0;
        int pageNumber = pagination.FirstPage;
        int offset = 0;

        while (next is not null && pages < Math.Max(1, pagination.MaximumPages))
        {
            RestPage page = await ReadPageAsync(next, configuration, contentReader, cancellationToken)
                .ConfigureAwait(false);

            yield return page;

            pages++;

            switch (pagination.Mode)
            {
                case PaginationMode.NextLink:
                    next = page.NextLink is null ? null : Resolve(next, page.NextLink);
                    break;

                case PaginationMode.Token:
                    next = page.NextToken is null || string.IsNullOrEmpty(pagination.TokenParameter)
                        ? null
                        : SetQueryParameter(
                            requestUri,
                            pagination.TokenParameter,
                            page.NextToken,
                            pagination.TokenReplacesQuery);
                    break;

                case PaginationMode.Offset:
                    // A page shorter than the requested size is the last one. Without a page size
                    // there is no short page to recognise, so a single page is all that is read.
                    if (pagination.PageSize <= 0
                        || page.Entries.Count < pagination.PageSize
                        || string.IsNullOrEmpty(pagination.OffsetParameter))
                    {
                        next = null;
                        break;
                    }

                    offset += pagination.PageSize;
                    next = SetQueryParameter(
                        next, pagination.OffsetParameter, offset.ToString(), false);
                    break;

                case PaginationMode.Page:
                    if (pagination.PageSize <= 0
                        || page.Entries.Count < pagination.PageSize
                        || string.IsNullOrEmpty(pagination.PageParameter))
                    {
                        next = null;
                        break;
                    }

                    pageNumber++;
                    next = SetQueryParameter(
                        next, pagination.PageParameter, pageNumber.ToString(), false);
                    break;

                default:
                    next = null;
                    break;
            }
        }

        if (next is not null)
        {
            _logger.LogWarning(
                "Stopped reading '{RequestUri}' after {Pages} page(s): the configured maximum was "
                + "reached while the API was still offering another page.",
                requestUri, pages);
        }
    }

    /// <summary>
    /// Parses a JSON array of objects held as raw text, which is how a nested collection is
    /// carried once its parent object has been projected onto a property bag.
    /// </summary>
    /// <param name="json">The raw JSON text of the array, or of a single object.</param>
    /// <returns>One property bag per element.</returns>
    public static List<QuickDictionary> ParseEntries(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(json);

        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => ReadEntries(document.RootElement, null),
            JsonValueKind.Object => [ReadProperties(document.RootElement)],
            _ => []
        };
    }

    /// <summary>
    /// Projects a JSON object onto a case-insensitive property bag.
    /// </summary>
    /// <remarks>
    /// A nested object or array keeps its raw JSON text rather than being flattened. A relational
    /// consumer stores that text in a single column, and a provider re-parses it in place when a
    /// nested collection is configured against it.
    /// </remarks>
    /// <param name="element">The object to project.</param>
    /// <returns>The property bag.</returns>
    public static QuickDictionary ReadProperties(JsonElement element)
    {
        QuickDictionary properties = new QuickDictionary(StringComparer.OrdinalIgnoreCase);

        if (element.ValueKind != JsonValueKind.Object)
        {
            return properties;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            properties[property.Name] = ReadValue(property.Value);
        }

        return properties;
    }

    /// <summary>
    /// Reads a single page.
    /// </summary>
    /// <param name="requestUri">The absolute URL of the page.</param>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <param name="contentReader">The reader for a body that is not JSON, or <c>null</c>.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the read.</param>
    /// <returns>The page.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the API answers unsuccessfully after the middleware pipeline has done what it
    /// can.
    /// </exception>
    private async Task<RestPage> ReadPageAsync(
        string requestUri,
        Configuration configuration,
        Func<Stream, Configuration, CancellationToken, IAsyncEnumerable<QuickDictionary>>? contentReader,
        CancellationToken cancellationToken)
    {
        _logger.LogTrace("Requesting {RequestUri}.", requestUri);

        using HttpRequestMessage request = new HttpRequestMessage(
            configuration.GetHttpMethod(), requestUri);

        if (!string.IsNullOrWhiteSpace(configuration.Accept))
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(configuration.Accept));
        }

        if (configuration.HttpHeaders is not null)
        {
            foreach (KeyValuePair<string, string> header in configuration.HttpHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (configuration.Body is not null)
        {
            request.Content = new StringContent(
                configuration.Body, Encoding.UTF8, configuration.ContentType);
        }

        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            throw new HttpRequestException(
                $"The API answered {(int)response.StatusCode} {response.ReasonPhrase} for "
                + $"{requestUri}: {Truncate(body)}",
                null,
                response.StatusCode);
        }

        await using Stream content = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        if (contentReader is not null)
        {
            List<QuickDictionary> read = [];

            await foreach (QuickDictionary entry in contentReader(content, configuration, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                read.Add(entry);
            }

            return new RestPage(read, null, null);
        }

        using JsonDocument document = await JsonDocument
            .ParseAsync(content, default, cancellationToken)
            .ConfigureAwait(false);

        return ReadPage(document.RootElement, configuration);
    }

    /// <summary>
    /// Projects a parsed response onto a page.
    /// </summary>
    /// <param name="root">The root element of the response.</param>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <returns>The page.</returns>
    private static RestPage ReadPage(JsonElement root, Configuration configuration)
    {
        PaginationSettings? pagination = configuration.Pagination;

        if (!JsonPath.TryResolve(root, configuration.ResultPath, out JsonElement collection))
        {
            return new RestPage(
                [],
                JsonPath.ResolveString(root, pagination?.NextLinkPath),
                JsonPath.ResolveString(root, pagination?.TokenPath));
        }

        List<QuickDictionary> entries = collection.ValueKind switch
        {
            JsonValueKind.Array => ReadEntries(collection, configuration.EntryPath),
            JsonValueKind.Object => [ReadEntry(collection, configuration.EntryPath)],
            _ => []
        };

        return new RestPage(
            entries,
            JsonPath.ResolveString(root, pagination?.NextLinkPath),
            JsonPath.ResolveString(root, pagination?.TokenPath));
    }

    /// <summary>
    /// Reads a JSON array of objects into property bags.
    /// </summary>
    /// <param name="array">The array to read.</param>
    /// <param name="entryPath">The path within each element to the object carrying the properties.</param>
    /// <returns>One property bag per element.</returns>
    private static List<QuickDictionary> ReadEntries(JsonElement array, string? entryPath)
    {
        List<QuickDictionary> entries = new List<QuickDictionary>(array.GetArrayLength());

        foreach (JsonElement element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                entries.Add(ReadEntry(element, entryPath));
            }
        }

        return entries;
    }

    /// <summary>
    /// Reads one element of a collection, descending to the object that carries the properties.
    /// </summary>
    /// <param name="element">The element to read.</param>
    /// <param name="entryPath">The path to the object carrying the properties.</param>
    /// <returns>The property bag.</returns>
    private static QuickDictionary ReadEntry(JsonElement element, string? entryPath)
    {
        // An element that does not carry the configured path is read whole rather than dropped:
        // an API is free to omit the wrapper on some of its records, and losing them silently
        // would be worse than carrying the wrapper through.
        return JsonPath.TryResolve(element, entryPath, out JsonElement entry)
            ? ReadProperties(entry)
            : ReadProperties(element);
    }

    /// <summary>
    /// Reads a JSON value as the CLR value a consumer can persist.
    /// </summary>
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
    /// Resolves the URL of the next page, which an API is free to state relative to the request
    /// it answered.
    /// </summary>
    /// <param name="requestUri">The URL of the request that carried the link.</param>
    /// <param name="link">The link as the response stated it.</param>
    /// <returns>The absolute URL.</returns>
    private static string Resolve(string requestUri, string link)
    {
        return UrlResolver.Resolve(requestUri, link);
    }

    /// <summary>
    /// Returns a URL with one query parameter set to a value.
    /// </summary>
    /// <param name="requestUri">The URL to rewrite.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The parameter value.</param>
    /// <param name="replaceQuery">
    /// <c>true</c> to discard the rest of the query, which is what an API expects when it has
    /// bound the original query to a continuation token; <c>false</c> to keep it.
    /// </param>
    /// <returns>The rewritten URL.</returns>
    private static string SetQueryParameter(
        string requestUri,
        string name,
        string value,
        bool replaceQuery)
    {
        UriBuilder builder = new UriBuilder(requestUri);

        string parameter = $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";

        if (replaceQuery || string.IsNullOrEmpty(builder.Query))
        {
            builder.Query = parameter;
            return builder.Uri.ToString();
        }

        IEnumerable<string> kept = builder.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(existing => !IsParameter(existing, name));

        builder.Query = string.Join('&', kept.Append(parameter));

        return builder.Uri.ToString();
    }

    /// <summary>
    /// Determines whether a query fragment assigns the named parameter.
    /// </summary>
    /// <param name="fragment">The <c>name=value</c> fragment.</param>
    /// <param name="name">The parameter name.</param>
    /// <returns><c>true</c> when the fragment assigns that parameter; otherwise, <c>false</c>.</returns>
    private static bool IsParameter(string fragment, string name)
    {
        int assignment = fragment.IndexOf('=');
        string key = assignment < 0 ? fragment : fragment[..assignment];

        return string.Equals(Uri.UnescapeDataString(key), name, StringComparison.OrdinalIgnoreCase);
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
        _owned?.Dispose();
    }

    #endregion
}
