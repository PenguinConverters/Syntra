using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Target;
using PenguinConverters.Syntra.Consumer.ActiveDirectory.Target;

namespace PenguinConverters.Syntra.Consumer.ActiveDirectory;

/// <summary>
/// Builds an Active Directory <see cref="IConsumer"/> instance with LDAP connection
/// configuration, credentials, and property mapping for write-back operations.
/// </summary>
public class ConsumerBuilder : IConsumerBuilder
{
    #region Fields

    private readonly Consumer _consumer = new();
    private Func<byte[], Type, object>? _deserializer;
    private Func<string, char[]>? _discloser;
    private ILogger? _logger;
    private byte[]? _configuration;
    private byte[]? _metadata;

    #endregion

    #region Methods

    /// <inheritdoc />
    public void AddConfiguration(byte[] configuration) => _configuration = configuration;

    /// <inheritdoc />
    public void AddMetadata(byte[]? metadata) => _metadata = metadata;

    /// <inheritdoc />
    public void AddDeserializer(Func<byte[], Type, object> deserializer) => _deserializer = deserializer;

    /// <inheritdoc />
    public void AddLogger(ILogger logger) => _logger = logger;

    /// <inheritdoc />
    public void AddDiscloser(Func<string, char[]> discloser) => _discloser = discloser;

    /// <inheritdoc />
    public IConsumer Build()
    {
        if (_deserializer is not null) _consumer.SetDeserializer(_deserializer);
        if (_discloser is not null) _consumer.SetDiscloser(_discloser);
        if (_logger is not null) _consumer.SetLogger(_logger);

        if (_configuration is not null)
        {
            _consumer.SetConfiguration(_configuration);
            _consumer.DeserializeAndApplyConfiguration();
        }

        if (_metadata is not null)
            _consumer.SetMetadata(_metadata);

        // Build LDAP connection using configuration and credentials
        BuildConnection();

        return _consumer;
    }

    /// <summary>
    /// Builds the LDAP connection using the consumer configuration and credentials.
    /// Uses <see cref="PenguinConverters.Syntra.ActiveDirectory"/> for connection management.
    /// </summary>
    private void BuildConnection()
    {
        Configuration? config = _consumer.Configuration;
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
    }

    #endregion
}
