using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Keyra;
using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Core;

/// <summary>
/// Builds a <see cref="Handler"/> instance with all required dependencies.
/// </summary>
public class HandlerBuilder
{
    #region Fields

    private Configuration? _configuration;
    private byte[]? _sourceMetadata;
    private byte[]? _targetMetadata;
    private Func<byte[], Type, object>? _deserializer;
    private Decryptor? _decryptor;
    private ILogger _logger = NullLogger.Instance;
    private byte[]? _publicKey;

    #endregion

    #region Methods

    /// <summary>
    /// Sets the synchronization configuration.
    /// </summary>
    /// <param name="configuration">The root configuration.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public HandlerBuilder WithConfiguration(Configuration configuration)
    {
        _configuration = configuration;
        return this;
    }

    /// <summary>
    /// Sets the source metadata for delta synchronization.
    /// </summary>
    /// <param name="metadata">The source metadata bytes, or <c>null</c> for full sync.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public HandlerBuilder WithSourceMetadata(byte[]? metadata)
    {
        _sourceMetadata = metadata;
        return this;
    }

    /// <summary>
    /// Sets the target metadata for delta synchronization.
    /// </summary>
    /// <param name="metadata">The target metadata bytes, or <c>null</c> for full sync.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public HandlerBuilder WithTargetMetadata(byte[]? metadata)
    {
        _targetMetadata = metadata;
        return this;
    }

    /// <summary>
    /// Sets the deserializer function for configuration and metadata objects.
    /// </summary>
    /// <param name="deserializer">The deserializer function.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public HandlerBuilder WithDeserializer(Func<byte[], Type, object> deserializer)
    {
        _deserializer = deserializer;
        return this;
    }

    /// <summary>
    /// Sets the Keyra decryptor used to disclose protected configuration values.
    /// </summary>
    /// <remarks>
    /// Supply a decryptor to share one vault key across several handlers; the caller keeps ownership
    /// and disposes it. When none is supplied, the handler builds one for the run from
    /// <see cref="Configuration.Keyra"/> and disposes it when the run ends.
    /// </remarks>
    /// <param name="decryptor">The decryptor holding the vault key.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public HandlerBuilder WithDecryptor(Decryptor decryptor)
    {
        _decryptor = decryptor;
        return this;
    }

    /// <summary>
    /// Sets the logger instance.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public HandlerBuilder WithLogger(ILogger logger)
    {
        _logger = logger ?? NullLogger.Instance;
        return this;
    }

    /// <summary>
    /// Sets the expected public key for assembly signature validation.
    /// </summary>
    /// <param name="publicKey">The expected public key bytes.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public HandlerBuilder WithPublicKey(byte[] publicKey)
    {
        _publicKey = publicKey;
        return this;
    }

    /// <summary>
    /// Builds and returns a fully configured <see cref="Handler"/> instance.
    /// </summary>
    /// <returns>A handler ready to execute synchronization.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required configuration is missing.</exception>
    public Handler Build()
    {
        if (_configuration is null)
            throw new InvalidOperationException("Configuration is required. Call WithConfiguration() before Build().");

        return new Handler(
            _configuration,
            _sourceMetadata,
            _targetMetadata,
            _deserializer,
            _decryptor,
            _logger,
            _publicKey);
    }

    #endregion
}
