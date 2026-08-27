using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Syntra.Core.Types;
using PenguinConverters.Syntra.Provider.Tenable.Source;

namespace PenguinConverters.Syntra.Provider.Tenable.Nessus;

/// <summary>
/// Builds the content-reader delegate that reads a Nessus export and expands each of its rows
/// into the observations its plugin output describes.
/// </summary>
/// <remarks>
/// This is the seam <c>RESTful.Provider.ContentReader</c> exposes: the retrieval loop, the
/// paging, the credentials and the transport are the base provider's, and everything Nessus-
/// specific is this one delegate. A host that wants a different expansion assigns its own
/// delegate in place of this one; nothing else about the connector changes.
/// </remarks>
public static class NessusContentReader
{
    #region Methods

    /// <summary>
    /// Builds a delegate that reads a delimited export and expands every row.
    /// </summary>
    /// <param name="delimiter">The character separating fields.</param>
    /// <param name="encoding">The encoding the export is written in.</param>
    /// <param name="logger">The logger to report an unreadable plugin output to.</param>
    /// <returns>The delegate.</returns>
    public static Func<Stream, RESTful.Source.Configuration, CancellationToken, IAsyncEnumerable<QuickDictionary>> Create(
        char delimiter,
        Encoding encoding,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        return (content, _, cancellationToken) =>
            ReadAsync(content, delimiter, encoding, logger ?? NullLogger.Instance, cancellationToken);
    }

    /// <summary>
    /// Builds a delegate that reads a delimited export and expands every row, taking the
    /// delimiter and the encoding from a configuration.
    /// </summary>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <param name="logger">The logger to report an unreadable plugin output to.</param>
    /// <returns>The delegate.</returns>
    public static Func<Stream, RESTful.Source.Configuration, CancellationToken, IAsyncEnumerable<QuickDictionary>> Create(
        Configuration configuration,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return Create(configuration.Delimiter, configuration.GetEncoding(), logger);
    }

    /// <summary>
    /// Reads a delimited export and expands every row.
    /// </summary>
    /// <param name="content">The response body.</param>
    /// <param name="delimiter">The character separating fields.</param>
    /// <param name="encoding">The encoding the export is written in.</param>
    /// <param name="logger">The logger to report an unreadable plugin output to.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the read.</param>
    /// <returns>One property bag per observation.</returns>
    public static async IAsyncEnumerable<QuickDictionary> ReadAsync(
        Stream content,
        char delimiter,
        Encoding encoding,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (QuickDictionary row in DelimitedReader
            .ReadAsync(content, delimiter, encoding, cancellationToken)
            .ConfigureAwait(false))
        {
            // A row expands into as many records as its plugin output describes, or into none
            // when the plugin is not one this connector reads.
            foreach (QuickDictionary record in NessusParser.Expand(row, logger))
            {
                yield return record;
            }
        }
    }

    #endregion
}
