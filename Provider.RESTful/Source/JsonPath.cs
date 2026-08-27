using System.Text.Json;

namespace PenguinConverters.Syntra.Provider.RESTful.Source;

/// <summary>
/// Resolves a dotted path against a parsed JSON document, so that the shape of a response is
/// configuration rather than code.
/// </summary>
/// <remarks>
/// A segment that is an integer indexes an array, which makes <c>_links.next.0.href</c> reach
/// into a HAL response. A property name may itself contain dots - <c>@odata.nextLink</c> is one -
/// so a whole path that matches a property outright wins before the path is split.
/// </remarks>
public static class JsonPath
{
    #region Constants

    /// <summary>
    /// Separator between the segments of a path.
    /// </summary>
    public const char Separator = '.';

    #endregion

    #region Methods

    /// <summary>
    /// Resolves a path against an element.
    /// </summary>
    /// <param name="element">The element to resolve against.</param>
    /// <param name="path">
    /// The path to resolve. An empty path resolves to <paramref name="element"/> itself.
    /// </param>
    /// <param name="value">When this method returns <c>true</c>, the element the path names.</param>
    /// <returns><c>true</c> when the path resolves; otherwise, <c>false</c>.</returns>
    public static bool TryResolve(JsonElement element, string? path, out JsonElement value)
    {
        value = element;

        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        // A property whose own name carries the separator, such as an OData annotation, is
        // matched whole before the path is treated as a sequence of segments.
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(path, out value))
        {
            return true;
        }

        JsonElement current = element;

        foreach (string segment in path.Split(Separator))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(segment, out current))
                {
                    value = default;
                    return false;
                }

                continue;
            }

            if (current.ValueKind == JsonValueKind.Array
                && int.TryParse(segment, out int index)
                && index >= 0
                && index < current.GetArrayLength())
            {
                current = current[index];
                continue;
            }

            value = default;
            return false;
        }

        value = current;
        return true;
    }

    /// <summary>
    /// Resolves a path to a non-empty string.
    /// </summary>
    /// <param name="element">The element to resolve against.</param>
    /// <param name="path">The path to resolve, or <c>null</c>.</param>
    /// <returns>
    /// The string the path names, or <c>null</c> when the path does not resolve, resolves to a
    /// value that is not a string or a number, or resolves to an empty string.
    /// </returns>
    public static string? ResolveString(JsonElement element, string? path)
    {
        if (path is null || !TryResolve(element, path, out JsonElement value))
        {
            return null;
        }

        string? text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    #endregion
}
