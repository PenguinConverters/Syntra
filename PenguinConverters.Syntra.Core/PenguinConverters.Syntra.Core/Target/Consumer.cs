using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    /// Gets the discloser function for decrypting protected strings via Keyra.
    /// </summary>
    protected Func<string, char[]>? Discloser { get; private set; }

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
    /// Sets the discloser function for credential decryption.
    /// </summary>
    /// <param name="discloser">The discloser function.</param>
    public void SetDiscloser(Func<string, char[]> discloser)
    {
        Discloser = discloser;
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
