using System.Text.RegularExpressions;

namespace PenguinConverters.Syntra.Provider.Tenable.Nessus;

/// <summary>
/// Reads the SSH plugin outputs: the protocol versions a host answers on, and the algorithms it
/// offers in each negotiation category.
/// </summary>
public static partial class SecureShellParser
{
    #region Constants

    /// <summary>
    /// Preamble each algorithm block of the plugin output begins with.
    /// </summary>
    public const string AlgorithmSectionMarker = "The server supports the following options for ";

    /// <summary>
    /// Protocol the SSH plugins report against.
    /// </summary>
    public const string Protocol = "TCP";

    #endregion

    #region Methods

    /// <summary>
    /// Reads the protocol versions a host reported as supported.
    /// </summary>
    /// <param name="output">The plugin output.</param>
    /// <param name="plugin">The row the output came from.</param>
    /// <returns>One record per version.</returns>
    public static List<SecureShellVersion> ParseVersions(string? output, NessusPlugin? plugin = null)
    {
        List<SecureShellVersion> versions = [];

        if (string.IsNullOrEmpty(output))
        {
            return versions;
        }

        foreach (Match match in VersionExpression().Matches(output))
        {
            SecureShellVersion version = new SecureShellVersion
            {
                Version = match.Groups[1].Value
            };

            Stamp(version, plugin);

            versions.Add(version);
        }

        return versions;
    }

    /// <summary>
    /// Reads the algorithms a host reported as supported, in each negotiation category.
    /// </summary>
    /// <param name="output">The plugin output.</param>
    /// <param name="plugin">The row the output came from.</param>
    /// <returns>One record per algorithm per category.</returns>
    public static List<SecureShellAlgorithm> ParseAlgorithms(string? output, NessusPlugin? plugin = null)
    {
        List<SecureShellAlgorithm> algorithms = [];

        if (string.IsNullOrEmpty(output))
        {
            return algorithms;
        }

        string[] sections = output.Split(AlgorithmSectionMarker, StringSplitOptions.None);

        // The text before the first marker is the plugin's preamble, not a category.
        foreach (string section in sections.Skip(1))
        {
            int separator = section.IndexOf(':');

            if (separator < 0)
            {
                continue;
            }

            string type = section[..separator].Trim();
            string group = GetTypeGroup(type);

            // A category may advertise the same algorithm twice; it is one capability either way.
            HashSet<string> names = new HashSet<string>(
                section[(separator + 1)..]
                    .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            foreach (string name in names)
            {
                SecureShellAlgorithm algorithm = new SecureShellAlgorithm
                {
                    Type = type.ToLowerInvariant(),
                    TypeGroup = group,
                    Algorithm = name.ToLowerInvariant(),
                    AlgorithmClean = Clean(name)
                };

                Stamp(algorithm, plugin);

                algorithms.Add(algorithm);
            }
        }

        return algorithms;
    }

    /// <summary>
    /// Returns the category a negotiation type falls under.
    /// </summary>
    /// <param name="type">The negotiation type as the plugin named it.</param>
    /// <returns>The category.</returns>
    public static string GetTypeGroup(string? type)
    {
        if (string.IsNullOrEmpty(type))
        {
            return "Other";
        }

        return type.ToLowerInvariant().Split('_')[0] switch
        {
            "server" => "Host Key",
            "encryption" => "Encryption",
            "mac" => "MAC",
            "kex" => "KEX",
            "compression" => "Compression",
            _ => "Other"
        };
    }

    /// <summary>
    /// Reduces an advertised algorithm name to the primitive it names.
    /// </summary>
    /// <remarks>
    /// A vendor extension is advertised with an <c>@domain</c> suffix and an encrypt-then-MAC
    /// variant with a trailing <c>-etm</c>. Neither changes which primitive is in use, so a
    /// policy written against the primitive matches however the server advertised it.
    /// </remarks>
    /// <param name="name">The advertised name.</param>
    /// <returns>The cleaned name.</returns>
    public static string Clean(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        return EncryptThenMacSuffix()
            .Replace(name.Split('@')[0], string.Empty)
            .ToLowerInvariant();
    }

    /// <summary>
    /// Stamps a record with the asset the plugin ran against.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="plugin">The row the output came from.</param>
    private static void Stamp(NessusRecord record, NessusPlugin? plugin)
    {
        record.IPAddress = plugin?.IPAddress ?? string.Empty;
        record.DNSName = plugin?.DNSName ?? string.Empty;
        record.ShortName = plugin?.ShortName;
        record.Protocol = Protocol;
        record.Port = plugin?.Port ?? -1;
        record.FirstDiscovered = plugin?.FirstDiscovered;
        record.LastObserved = plugin?.LastObserved;
        record.PluginName = plugin?.PluginName;
        record.Plugin = plugin?.Plugin;
    }

    /// <summary>
    /// Matches a version the plugin lists on its own bulleted line.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^\s*-\s*(\d+(\.\d+)*)\s*$", RegexOptions.Multiline)]
    private static partial Regex VersionExpression();

    /// <summary>
    /// Matches the encrypt-then-MAC marker at the end of an algorithm name.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex("-etm$")]
    private static partial Regex EncryptThenMacSuffix();

    #endregion
}
