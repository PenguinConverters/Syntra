namespace PenguinConverters.Syntra.Provider.RESTful.Source;

/// <summary>
/// Selects how the next page of a collection is requested.
/// </summary>
public enum PaginationMode
{
    /// <summary>
    /// The response is the whole collection and no further request is made.
    /// </summary>
    None = 0,

    /// <summary>
    /// The response carries the absolute or relative URL of the next page, as an OData
    /// <c>@odata.nextLink</c> or a HAL <c>_links.next</c> does.
    /// </summary>
    NextLink = 1,

    /// <summary>
    /// The response carries an opaque token that is sent back as a query parameter to request
    /// the next page.
    /// </summary>
    Token = 2,

    /// <summary>
    /// Pages are addressed by a record offset that advances by the page size until a short page
    /// arrives.
    /// </summary>
    Offset = 3,

    /// <summary>
    /// Pages are addressed by an incrementing page number until a short page arrives.
    /// </summary>
    Page = 4
}
