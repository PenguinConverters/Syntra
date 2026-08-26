using Microsoft.Extensions.Logging;
using PenguinConverters.Keyra;
using PenguinConverters.Syntra.Core.Source;

namespace PenguinConverters.Syntra.Provider.AzureSQL;

/// <summary>
/// Builds an Azure SQL <see cref="IProvider"/> instance with SQL connection
/// configuration, credentials, and offset-based delta state.
/// </summary>
public class ProviderBuilder : IProviderBuilder
{
    #region Fields

    private readonly Provider _provider = new();
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
    public IProvider Build()
    {
        if (_deserializer is not null) _provider.SetDeserializer(_deserializer);
        if (_decryptor is not null) _provider.SetDecryptor(_decryptor);
        if (_logger is not null) _provider.SetLogger(_logger);

        if (_configuration is not null)
        {
            _provider.SetConfiguration(_configuration);
            _provider.DeserializeAndApplyConfiguration();
        }

        if (_metadata is not null)
            _provider.SetMetadata(_metadata);

        _provider.InitializeOffset();

        return _provider;
    }

    #endregion
}
