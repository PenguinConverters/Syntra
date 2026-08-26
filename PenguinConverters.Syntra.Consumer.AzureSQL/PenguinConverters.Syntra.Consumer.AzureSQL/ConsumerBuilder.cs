using Microsoft.Extensions.Logging;
using PenguinConverters.Keyra;
using PenguinConverters.Syntra.Core.Target;

namespace PenguinConverters.Syntra.Consumer.AzureSQL;

/// <summary>
/// Builds an Azure SQL <see cref="IConsumer"/> instance with SQL connection
/// configuration, credentials, and MERGE operation settings.
/// </summary>
public class ConsumerBuilder : IConsumerBuilder
{
    #region Fields

    private readonly Consumer _consumer = new();
    private Func<byte[], Type, object>? _deserializer;
    private Decryptor? _decryptor;
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
    public void AddDecryptor(Decryptor decryptor) => _decryptor = decryptor;

    /// <inheritdoc />
    public IConsumer Build()
    {
        if (_deserializer is not null) _consumer.SetDeserializer(_deserializer);
        if (_decryptor is not null) _consumer.SetDecryptor(_decryptor);
        if (_logger is not null) _consumer.SetLogger(_logger);

        if (_configuration is not null)
        {
            _consumer.SetConfiguration(_configuration);
            _consumer.DeserializeAndApplyConfiguration();
        }

        if (_metadata is not null)
            _consumer.SetMetadata(_metadata);

        return _consumer;
    }

    #endregion
}
