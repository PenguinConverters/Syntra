namespace PenguinConverters.Syntra.Provider.Tenable.Source;

/// <summary>
/// Reads a call written into an endpoint, such as
/// <c>rest/report/&lt;%ReportId(Weekly Scan)%&gt;/download</c>.
/// </summary>
/// <remarks>
/// An endpoint that names a report cannot be written down in full, because the identifier changes
/// every time the report runs. Writing the lookup into the endpoint keeps that a configuration
/// concern rather than a code one.
/// </remarks>
public static class FunctionPlaceholder
{
    #region Constants

    /// <summary>
    /// Opening delimiter of a call.
    /// </summary>
    public const string Prefix = "<%";

    /// <summary>
    /// Closing delimiter of a call.
    /// </summary>
    public const string Suffix = "%>";

    #endregion

    #region Methods

    /// <summary>
    /// Reads the first call an endpoint carries.
    /// </summary>
    /// <param name="endPoint">The endpoint.</param>
    /// <param name="placeholder">
    /// When this method returns <c>true</c>, the whole placeholder including its delimiters, so
    /// that the caller can substitute the result for it.
    /// </param>
    /// <param name="name">When this method returns <c>true</c>, the name of the call.</param>
    /// <param name="arguments">When this method returns <c>true</c>, its arguments.</param>
    /// <returns><c>true</c> when the endpoint carries a call; otherwise, <c>false</c>.</returns>
    public static bool TryParse(
        string? endPoint,
        out string placeholder,
        out string name,
        out string[] arguments)
    {
        placeholder = string.Empty;
        name = string.Empty;
        arguments = [];

        if (string.IsNullOrEmpty(endPoint))
        {
            return false;
        }

        int start = endPoint.IndexOf(Prefix, StringComparison.Ordinal);

        if (start < 0)
        {
            return false;
        }

        int end = endPoint.IndexOf(Suffix, start + Prefix.Length, StringComparison.Ordinal);

        if (end < 0)
        {
            return false;
        }

        string body = endPoint[(start + Prefix.Length)..end];

        placeholder = string.Concat(Prefix, body, Suffix);

        int open = body.IndexOf('(');
        int close = body.LastIndexOf(')');

        // Without brackets this is a property placeholder, which the base provider substitutes
        // from the parent object. It is not a call and is left alone.
        if (open <= 0 || close <= open)
        {
            placeholder = string.Empty;
            return false;
        }

        name = body[..open].Trim();

        string parameters = body[(open + 1)..close].Trim();

        arguments = parameters.Length == 0
            ? []
            : parameters.Split(',').Select(argument => argument.Trim()).ToArray();

        return name.Length > 0;
    }

    #endregion
}
