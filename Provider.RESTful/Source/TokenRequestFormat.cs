namespace PenguinConverters.Syntra.Provider.RESTful.Source;

/// <summary>
/// Selects how the credentials of a token or login request are encoded.
/// </summary>
public enum TokenRequestFormat
{
    /// <summary>
    /// <c>application/x-www-form-urlencoded</c>, which is what an OAuth 2.0 token endpoint expects.
    /// </summary>
    Form = 0,

    /// <summary>
    /// <c>application/json</c>, for APIs that take their login payload as a JSON object.
    /// </summary>
    Json = 1
}
