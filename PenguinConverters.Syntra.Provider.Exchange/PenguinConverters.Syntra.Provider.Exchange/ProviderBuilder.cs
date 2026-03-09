using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Source;

namespace PenguinConverters.Syntra.Provider.Exchange;

/// <summary>
/// Builds an Exchange Online <see cref="IProvider"/> instance with Graph API
/// client configuration and credentials.
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
    public IProvider Build()
    {
        if (_deserializer is not null) _provider.SetDeserializer(_deserializer);
        if (_discloser is not null) _provider.SetDiscloser(_discloser);
        if (_logger is not null) _provider.SetLogger(_logger);

        if (_configuration is not null)
        {
            _provider.SetConfiguration(_configuration);
            _provider.DeserializeAndApplyConfiguration();
        }

        if (_metadata is not null)
            _provider.SetMetadata(_metadata);

        return _provider;
    }
}
