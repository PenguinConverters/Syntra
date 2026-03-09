using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Core;

/// <summary>
/// Builds a <see cref="Handler"/> instance with all required dependencies.
/// </summary>
public class HandlerBuilder
{
    private Configuration? _configuration;
    private byte[]? _sourceMetadata;
    private byte[]? _targetMetadata;
    private Func<byte[], Type, object>? _deserializer;
    private Func<string, char[]>? _discloser;
    private ILogger _logger = NullLogger.Instance;
    private byte[]? _publicKey;

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
    /// Sets the discloser function for credential decryption via Keyra.
    /// </summary>
    /// <param name="discloser">The discloser function.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public HandlerBuilder WithDiscloser(Func<string, char[]> discloser)
    {
        _discloser = discloser;
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
            _discloser,
            _logger,
            _publicKey);
    }
}
