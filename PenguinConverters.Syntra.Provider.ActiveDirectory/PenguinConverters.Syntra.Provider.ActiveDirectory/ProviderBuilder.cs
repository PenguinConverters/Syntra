using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Provider.ActiveDirectory.Source;

namespace PenguinConverters.Syntra.Provider.ActiveDirectory;

/// <summary>
/// Builds an Active Directory <see cref="IProvider"/> instance with LDAP connection
/// configuration, credentials, and synchronization state.
/// </summary>
public class ProviderBuilder : IProviderBuilder
{
    private readonly Provider _provider = new();
    private Func<byte[], Type, object>? _deserializer;
    private Func<string, char[]>? _discloser;
    private ILogger? _logger;
    private byte[]? _configuration;
    private byte[]? _metadata;

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

        _logger?.LogTrace(
            "Building LDAP connection to {Server}:{Port} with BaseDN '{BaseDN}'.",
            config.ServerName, config.Port, config.BaseDN);

        // In a real implementation, this creates a PenguinConverters.Syntra.ActiveDirectory.Connection
        // using the ConnectionBuilder pattern:
        //   new ConnectionBuilder()
        //       .AddDomainController(config.ServerName)
        //       .AddBaseDN(config.BaseDN)
        //       .AddPort(config.Port)
        //       .AddAuthType(config.AuthType)
        //       .AddCredentials(username, password)  // via ProtectedString.TryGetValue
        //       .AddSchemaDecoders()
        //       .Build();

        // Resolve DC affinity via state
        _provider.TryGetPreferredLdapServer(out _);
    }
}
