using System.Net;
using System.Text;

namespace PenguinConverters.Syntra.Provider.Ciphersuite.Tests;

/// <summary>
/// A transport that answers from a canned script instead of the network, and records what it was
/// asked for.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    #region Fields

    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responder;
    private int _count;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="StubHttpMessageHandler"/> class answering
    /// from a delegate that sees the request and how many have been made before it.
    /// </summary>
    /// <param name="responder">The responder.</param>
    public StubHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StubHttpMessageHandler"/> class answering
    /// each request with the next of a sequence of JSON bodies.
    /// </summary>
    /// <param name="bodies">The bodies, in order.</param>
    public StubHttpMessageHandler(params string[] bodies)
        : this((_, index) => Json(index < bodies.Length ? bodies[index] : "{}"))
    {
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the requests this handler was asked for, in order.
    /// </summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>
    /// Gets the URLs this handler was asked for, in order, as they went onto the wire.
    /// <see cref="Uri.AbsoluteUri"/> rather than <see cref="Uri.ToString()"/>, which unescapes
    /// for display and would hide whether a space in a path was escaped at all.
    /// </summary>
    public List<string> RequestUris => Requests.Select(request => request.RequestUri!.AbsoluteUri).ToList();

    /// <summary>
    /// Gets the request bodies this handler was asked with, in order. They are read as the
    /// request arrives, because the caller disposes the request once it has been answered.
    /// </summary>
    public List<string?> RequestBodies { get; } = [];

    #endregion

    #region Methods

    /// <summary>
    /// Builds a successful JSON response.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <param name="statusCode">The status code. Defaults to <see cref="HttpStatusCode.OK"/>.</param>
    /// <returns>The response.</returns>
    public static HttpResponseMessage Json(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// Builds a plain text response.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <param name="statusCode">The status code. Defaults to <see cref="HttpStatusCode.OK"/>.</param>
    /// <returns>The response.</returns>
    public static HttpResponseMessage Text(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        RequestBodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));

        return _responder(request, _count++);
    }

    #endregion
}
