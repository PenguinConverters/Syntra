using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Keyra;
using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Core.Source;

namespace PenguinConverters.Syntra.Core.Target;

/// <summary>
/// Abstract base implementation of <see cref="IConsumer"/> providing common functionality
/// for configuration deserialization, logging, and credential disclosure.
/// </summary>
public abstract class Consumer : IConsumer
{
    #region Properties

    /// <summary>
    /// Gets the logger instance for diagnostic output.
    /// </summary>
    protected ILogger Logger { get; private set; } = NullLogger.Instance;

    /// <summary>
    /// Gets the deserializer function for converting byte arrays to typed objects.
    /// </summary>
    protected Func<byte[], Type, object>? Deserializer { get; private set; }

    /// <summary>
    /// Gets the Keyra decryptor used to disclose protected configuration values.
    /// Owned by the synchronization pipeline; never disposed here.
    /// </summary>
    protected Decryptor? Decryptor { get; private set; }

    /// <summary>
    /// Gets or sets the raw configuration bytes.
    /// </summary>
    protected byte[]? RawConfiguration { get; set; }

    /// <summary>
    /// Gets or sets the raw metadata bytes for delta synchronization.
    /// </summary>
    protected byte[]? RawMetadata { get; set; }

    /// <inheritdoc />
    public virtual bool HadErrors { get; protected set; }

    #endregion

    #region Methods

    /// <inheritdoc />
    public abstract Task SynchronizeAsync(IProvider provider, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task FinalizeAsync(IProvider provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the logger instance.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    public void SetLogger(ILogger logger)
    {
        Logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Sets the deserializer function.
    /// </summary>
    /// <param name="deserializer">The deserializer function.</param>
    public void SetDeserializer(Func<byte[], Type, object> deserializer)
    {
        Deserializer = deserializer;
    }

    /// <summary>
    /// Sets the Keyra decryptor used to disclose protected configuration values.
    /// </summary>
    /// <param name="decryptor">The decryptor holding the vault key.</param>
    public void SetDecryptor(Decryptor decryptor)
    {
        Decryptor = decryptor;
    }

    /// <summary>
    /// Discloses a configuration secret, whether it is stored as plaintext or Keyra ciphertext.
    /// </summary>
    /// <param name="secret">The configured secret, or <c>null</c> if the setting was omitted.</param>
    /// <param name="plaintext">
    /// When this method returns <c>true</c>, the disclosed characters. The caller owns the array and
    /// should clear it once the credential has been used.
    /// </param>
    /// <returns><c>true</c> if the value was disclosed; otherwise, <c>false</c>.</returns>
    protected bool TryDisclose(Secret? secret, out char[] plaintext)
    {
        plaintext = [];

        if (secret is null)
            return false;

        // A protected value with no key available fails rather than yielding its ciphertext, so a
        // misconfigured vault surfaces here instead of as an authentication failure further on.
        Func<string, char[]>? decrypt = Decryptor is null ? null : Decryptor.Decrypt;

        if (!secret.TryGetValue(decrypt!, out plaintext) || plaintext is null)
        {
            Logger.LogError(
                "Failed to disclose a protected configuration value. Check that the Keyra key is " +
                "configured and that it is the one the value was protected with.");
            plaintext = [];
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sets the raw configuration bytes.
    /// </summary>
    /// <param name="configuration">The configuration bytes.</param>
    public void SetConfiguration(byte[] configuration)
    {
        RawConfiguration = configuration;
    }

    /// <summary>
    /// Sets the raw metadata bytes.
    /// </summary>
    /// <param name="metadata">The metadata bytes.</param>
    public void SetMetadata(byte[]? metadata)
    {
        RawMetadata = metadata;
    }

    /// <summary>
    /// Deserializes the raw configuration bytes to a typed configuration object.
    /// </summary>
    /// <typeparam name="T">The configuration type.</typeparam>
    /// <returns>The deserialized configuration, or the default value if no configuration or deserializer is set.</returns>
    protected T? DeserializeConfiguration<T>() where T : class
    {
        if (RawConfiguration is null || Deserializer is null)
            return default;

        return (T)Deserializer(RawConfiguration, typeof(T));
    }

    /// <summary>
    /// Deserializes the raw metadata bytes to a typed metadata object.
    /// </summary>
    /// <typeparam name="T">The metadata type.</typeparam>
    /// <returns>The deserialized metadata, or the default value if no metadata or deserializer is set.</returns>
    protected T? DeserializeMetadata<T>() where T : class
    {
        if (RawMetadata is null || Deserializer is null)
            return default;

        return (T)Deserializer(RawMetadata, typeof(T));
    }

    #endregion
}
