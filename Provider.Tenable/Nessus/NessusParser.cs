using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Syntra.Core.Types;

namespace PenguinConverters.Syntra.Provider.Tenable.Nessus;

/// <summary>
/// Expands one row of a Nessus export into the observations its plugin output describes.
/// </summary>
/// <remarks>
/// A scan row is not a record: a single "SSL Cipher Suites Supported" row carries every suite a
/// host offers across every protocol version, printed as text. Storing the row leaves that text
/// unqueryable, so it is read here into one record per suite - and likewise per certificate, per
/// SSH algorithm and per SSH version.
/// <para>
/// A plugin this does not recognise yields nothing. The export is filtered to the plugins a scan
/// policy selected, so an unrecognised one is a row that carries no observation this connector
/// models rather than an error.
/// </para>
/// </remarks>
public static class NessusParser
{
    #region Constants

    /// <summary>
    /// Plugin reporting the IKE version 1 responders a host runs.
    /// </summary>
    public const string PluginInternetKeyExchangeVersion1 =
        "IPSEC Internet Key Exchange (IKE) Version 1 Detection";

    /// <summary>
    /// Plugin reporting the IKE version 2 responders a host runs.
    /// </summary>
    public const string PluginInternetKeyExchangeVersion2 =
        "IPSEC Internet Key Exchange (IKE) Version 2 Detection";

    /// <summary>
    /// Plugin reporting what a Kerberos service discloses.
    /// </summary>
    public const string PluginKerberos = "Kerberos Information Disclosure";

    /// <summary>
    /// Plugin reporting the SSH protocol versions a host answers on.
    /// </summary>
    public const string PluginSecureShellVersions = "SSH Protocol Versions Supported";

    /// <summary>
    /// Plugin reporting the SSH algorithms a host offers.
    /// </summary>
    public const string PluginSecureShellAlgorithms = "SSH Algorithms and Languages Supported";

    /// <summary>
    /// Plugin reporting the certificates a host presents.
    /// </summary>
    public const string PluginCertificate = "SSL Certificate Information";

    /// <summary>
    /// Plugin reporting the TLS cipher suites a host offers.
    /// </summary>
    public const string PluginCipherSuites = "SSL Cipher Suites Supported";

    /// <summary>
    /// Protocol an IKE responder is reached over.
    /// </summary>
    private const string ProtocolUdp = "UDP";

    /// <summary>
    /// Protocol a Kerberos service is reached over.
    /// </summary>
    private const string ProtocolTcp = "TCP";

    #endregion

    #region Methods

    /// <summary>
    /// Expands one row of an export into the records its plugin output describes.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="logger">The logger to report an unreadable plugin output to.</param>
    /// <returns>One property bag per observation.</returns>
    public static IEnumerable<QuickDictionary> Expand(QuickDictionary row, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        foreach (NessusRecord record in Parse(NessusPlugin.FromRow(row), logger))
        {
            yield return NessusProjector.Project(record);
        }
    }

    /// <summary>
    /// Reads the records a plugin output describes.
    /// </summary>
    /// <param name="plugin">The row, mapped onto its plugin fields.</param>
    /// <param name="logger">The logger to report an unreadable plugin output to.</param>
    /// <returns>The records.</returns>
    public static IEnumerable<NessusRecord> Parse(NessusPlugin plugin, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        ILogger log = logger ?? NullLogger.Instance;

        switch (plugin.PluginName)
        {
            case PluginInternetKeyExchangeVersion1:
            case PluginInternetKeyExchangeVersion2:
                yield return Service(plugin, ProtocolUdp);
                break;

            case PluginKerberos:
                yield return Service(plugin, ProtocolTcp);
                break;

            case PluginSecureShellVersions:
                foreach (SecureShellVersion version in SecureShellParser.ParseVersions(plugin.PluginOutput, plugin))
                {
                    yield return version;
                }

                break;

            case PluginSecureShellAlgorithms:
                foreach (SecureShellAlgorithm algorithm in SecureShellParser.ParseAlgorithms(plugin.PluginOutput, plugin))
                {
                    yield return algorithm;
                }

                break;

            case PluginCertificate:
                foreach (NessusRecord certificate in CertificateParser.ParseAll(plugin.PluginOutput, plugin, log))
                {
                    yield return certificate;
                }

                break;

            case PluginCipherSuites:
                foreach (CipherSuite suite in TransportLayerSecurityParser.Parse(plugin.PluginOutput, plugin))
                {
                    yield return suite;
                }

                break;
        }
    }

    /// <summary>
    /// Builds the record for a plugin that reports only that a service is listening.
    /// </summary>
    /// <param name="plugin">The row, mapped onto its plugin fields.</param>
    /// <param name="protocol">The protocol the service is reached over.</param>
    /// <returns>The record.</returns>
    private static NessusRecord Service(NessusPlugin plugin, string protocol)
    {
        return new NessusRecord
        {
            IPAddress = plugin.IPAddress,
            DNSName = plugin.DNSName,
            ShortName = plugin.ShortName,
            Protocol = protocol,
            Port = plugin.Port,
            FirstDiscovered = plugin.FirstDiscovered,
            LastObserved = plugin.LastObserved,
            PluginName = plugin.PluginName,
            Plugin = plugin.Plugin
        };
    }

    #endregion
}
