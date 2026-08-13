using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.ActiveDirectory;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Provider.ActiveDirectory.Source;

namespace PenguinConverters.Syntra.Provider.ActiveDirectory;

/// <summary>
/// Builds an Active Directory <see cref="IProvider"/> instance with LDAP connection
/// configuration, credentials, and synchronization state.
/// </summary>
public class ProviderBuilder : IProviderBuilder
{
    #region Fields

    private readonly Provider _provider = new();
    private Func<byte[], Type, object>? _deserializer;
    private Func<string, char[]>? _discloser;
    private ILogger? _logger;
    private byte[]? _configuration;
    private byte[]? _metadata;

    #endregion

    #region Methods

    /// <inheritdoc />
    public void AddConfiguration(byte[] configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public void AddMetadata(byte[]? metadata)
    {
        _metadata = metadata;
    }

    /// <inheritdoc />
    public void AddDeserializer(Func<byte[], Type, object> deserializer)
    {
        _deserializer = deserializer;
    }

    /// <inheritdoc />
    public void AddLogger(ILogger logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void AddDiscloser(Func<string, char[]> discloser)
    {
        _discloser = discloser;
    }

    /// <inheritdoc />
    public IProvider Build()
    {
        if (_deserializer is not null)
            _provider.SetDeserializer(_deserializer);

        if (_discloser is not null)
            _provider.SetDiscloser(_discloser);

        if (_logger is not null)
            _provider.SetLogger(_logger);

        if (_configuration is not null)
        {
            _provider.SetConfiguration(_configuration);
            _provider.DeserializeAndApplyConfiguration();
        }

        if (_metadata is not null)
            _provider.SetMetadata(_metadata);

        _provider.InitializeState();

        // Establish LDAP connection using configuration and credentials
        BuildConnection();

        return _provider;
    }

    /// <summary>
    /// Builds the LDAP connection using the provider configuration and credentials.
    /// Uses <see cref="PenguinConverters.Syntra.ActiveDirectory"/> for connection management.
    /// </summary>
    private void BuildConnection()
    {
        Configuration? config = _provider.Configuration;
        if (config is null) return;

        if (string.IsNullOrWhiteSpace(config.ServerName) || string.IsNullOrWhiteSpace(config.BaseDN))
        {
            _logger?.LogError("An LDAP connection requires both ServerName and BaseDN to be configured.");
            return;
        }

        _logger?.LogTrace(
            "Building LDAP connection to {Server}:{Port} with BaseDN '{BaseDN}'.",
            config.ServerName, config.Port, config.BaseDN);

        ConnectionBuilder builder = _logger is null
            ? new ConnectionBuilder()
            : new ConnectionBuilder(_logger);

        // The configured name is usually the domain itself, which resolves to every DC behind it.
        // Expanding it here gives the provider the candidate list it needs to honour DC affinity.
        foreach (string domainController in ResolveDomainControllers(config.ServerName))
        {
            builder.AddDomainController(domainController);
        }

        builder
            .AddBaseDN(config.BaseDN)
            .AddPort(config.Port)
            .SetSecureSocketLayer(config.SecureSocketLayer)
            .SetPageSize(config.PageSize)
            .AddAuthType(ParseOrDefault(config.AuthType, AuthType.Negotiate))
            .SetSearchScope(ParseOrDefault(config.SearchScope, SearchScope.Subtree));

        if (config.Username is not null && config.Password is not null)
        {
            builder.AddCredentials(config.Username, config.Password);
        }

        _provider.Connection = builder.Build();

        // Resolve DC affinity via state
        _provider.TryGetPreferredLdapServer(out _);
    }

    /// <summary>
    /// Expands a DNS name into the host names of every server behind it, so that a domain name
    /// yields its domain controllers rather than a single endpoint.
    /// </summary>
    /// <param name="serverName">The configured server or domain DNS name.</param>
    /// <returns>
    /// The resolved host names, or the configured name itself when it cannot be expanded.
    /// </returns>
    private List<string> ResolveDomainControllers(string serverName)
    {
        List<string> domainControllers = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (IPAddress address in Dns.GetHostAddresses(serverName))
            {
                string? hostName = Dns.GetHostEntry(address).HostName;

                if (!string.IsNullOrWhiteSpace(hostName) && seen.Add(hostName))
                {
                    domainControllers.Add(hostName);
                }
            }
        }
        catch (SocketException ex)
        {
            _logger?.LogWarning(
                ex, "Could not expand '{Server}' into domain controllers; using it as configured.", serverName);
        }
        catch (ArgumentException ex)
        {
            _logger?.LogWarning(
                ex, "'{Server}' is not a resolvable host name; using it as configured.", serverName);
        }

        if (domainControllers.Count == 0)
        {
            domainControllers.Add(serverName);
        }

        _logger?.LogTrace(
            "Resolved '{Server}' to {Count} domain controller(s): {DomainControllers}",
            serverName, domainControllers.Count, string.Join(", ", domainControllers));

        return domainControllers;
    }

    /// <summary>
    /// Parses a configured enum name, falling back to the supplied default when the value is
    /// absent or not recognised.
    /// </summary>
    /// <typeparam name="T">The enum type to parse into.</typeparam>
    /// <param name="value">The configured value.</param>
    /// <param name="fallback">The value to use when parsing fails.</param>
    /// <returns>The parsed enum value, or <paramref name="fallback"/>.</returns>
    private static T ParseOrDefault<T>(string? value, T fallback) where T : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out T parsed) ? parsed : fallback;
    }

    #endregion
}
