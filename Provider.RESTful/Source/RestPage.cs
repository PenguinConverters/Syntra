using PenguinConverters.Syntra.Core.Types;

namespace PenguinConverters.Syntra.Provider.RESTful.Source;

/// <summary>
/// A single page of a REST collection response.
/// </summary>
/// <param name="Entries">The objects the page carries, one property bag each.</param>
/// <param name="NextLink">
/// The URL of the following page as the response stated it, absolute or relative, or <c>null</c>
/// when the response carried none.
/// </param>
/// <param name="NextToken">
/// The continuation token the response carried, or <c>null</c> when it carried none.
/// </param>
public sealed record RestPage(
    IReadOnlyList<QuickDictionary> Entries,
    string? NextLink,
    string? NextToken);
