using System.Text.RegularExpressions;

namespace PenguinConverters.Syntra.Provider.Tenable.Nessus;

/// <summary>
/// Reads the TLS cipher suite plugin output: the suites a host offers, grouped under the protocol
/// version each was offered on.
/// </summary>
public static partial class TransportLayerSecurityParser
{
    #region Constants

    /// <summary>
    /// Prefix of the line that opens a protocol version's block of suites.
    /// </summary>
    public const string VersionMarker = "SSL Version : ";

    /// <summary>
    /// Protocol the TLS plugin reports against.
    /// </summary>
    public const string Protocol = "TCP";

    #endregion

    #region Methods

    /// <summary>
    /// Reads the cipher suites a host reported as supported.
    /// </summary>
    /// <param name="output">The plugin output.</param>
    /// <param name="plugin">The row the output came from.</param>
    /// <returns>One record per suite per protocol version.</returns>
    public static List<CipherSuite> Parse(string? output, NessusPlugin? plugin = null)
    {
        List<CipherSuite> suites = [];

        if (string.IsNullOrEmpty(output))
        {
            return suites;
        }

        string? version = null;

        foreach (string raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();

            if (line.StartsWith(VersionMarker, StringComparison.Ordinal))
            {
                version = line[VersionMarker.Length..].Trim();
                continue;
            }

            // A suite is only meaningful under the version that offered it, so anything before
            // the first version line is preamble.
            if (version is null)
            {
                continue;
            }

            Match match = CipherLineExpression().Match(line);

            if (!match.Success)
            {
                continue;
            }

            CipherSuite suite = new CipherSuite
            {
                TLSVersion = version,
                Name = match.Groups["name"].Value.Trim().ToUpperInvariant(),
                Code = match.Groups["code"].Value
                    .Split(',')
                    .Select(part => part.Trim().ToLowerInvariant())
                    .ToList()
            };

            Stamp(suite, plugin);

            suites.Add(suite);
        }

        return suites;
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
    /// Matches a suite line, which names the suite and then its two-byte wire code.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^(?<name>\S[\S\s]*?)\s+(?<code>0x[0-9A-Fa-f]+,\s*0x[0-9A-Fa-f]+)")]
    private static partial Regex CipherLineExpression();

    #endregion
}
