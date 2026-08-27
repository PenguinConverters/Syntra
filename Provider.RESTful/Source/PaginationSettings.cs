namespace PenguinConverters.Syntra.Provider.RESTful.Source;

/// <summary>
/// How a collection response is continued past its first page.
/// </summary>
public class PaginationSettings
{
    #region Constants

    /// <summary>
    /// Pages a single retrieval is allowed to read before it is abandoned. A server that keeps
    /// answering with a next link it never retires would otherwise loop forever.
    /// </summary>
    public const int DefaultMaximumPages = 100000;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets how the next page is requested. Defaults to <see cref="PaginationMode.None"/>.
    /// </summary>
    public PaginationMode Mode { get; set; } = PaginationMode.None;

    /// <summary>
    /// Gets or sets the path to the next page URL within the response, such as
    /// <c>@odata.nextLink</c> or <c>_links.next.0.href</c>. A relative URL is resolved against
    /// the request it came from.
    /// </summary>
    public string? NextLinkPath { get; set; }

    /// <summary>
    /// Gets or sets the path to the continuation token within the response, such as
    /// <c>next_page_id</c>.
    /// </summary>
    public string? TokenPath { get; set; }

    /// <summary>
    /// Gets or sets the query parameter the continuation token is sent back under.
    /// </summary>
    public string? TokenParameter { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the continuation token replaces the query of the
    /// next request rather than joining it. Defaults to <c>true</c>, because an API that issues
    /// a continuation token has already bound the original query to it and rejects the two
    /// together.
    /// </summary>
    public bool TokenReplacesQuery { get; set; } = true;

    /// <summary>
    /// Gets or sets the query parameter carrying the record offset.
    /// </summary>
    public string? OffsetParameter { get; set; }

    /// <summary>
    /// Gets or sets the query parameter carrying the page number.
    /// </summary>
    public string? PageParameter { get; set; }

    /// <summary>
    /// Gets or sets the query parameter carrying the page size.
    /// </summary>
    public string? PageSizeParameter { get; set; }

    /// <summary>
    /// Gets or sets the number of records requested per page. It also decides when an
    /// offset-paged or number-paged collection is exhausted: a page shorter than this is the
    /// last one.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Gets or sets the number the first page is addressed by, which is <c>0</c> for an API that
    /// counts pages from zero and <c>1</c> for one that counts from one. Defaults to <c>1</c>.
    /// </summary>
    public int FirstPage { get; set; } = 1;

    /// <summary>
    /// Gets or sets the pages a single retrieval is allowed to read.
    /// Defaults to <see cref="DefaultMaximumPages"/>.
    /// </summary>
    public int MaximumPages { get; set; } = DefaultMaximumPages;

    #endregion
}
