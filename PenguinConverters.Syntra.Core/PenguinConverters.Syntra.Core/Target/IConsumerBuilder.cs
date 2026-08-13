using Microsoft.Extensions.Logging;

namespace PenguinConverters.Syntra.Core.Target;

/// <summary>
/// Builds an <see cref="IConsumer"/> instance with the required dependencies.
/// Each connector assembly implements this interface to provide its own builder.
/// </summary>
public interface IConsumerBuilder
{
    #region Methods

    /// <summary>
    /// Adds the serialized configuration for the consumer.
    /// </summary>
    /// <param name="configuration">The serialized configuration bytes (JSON or YAML).</param>
    void AddConfiguration(byte[] configuration);

    /// <summary>
    /// Adds the serialized metadata for delta synchronization.
    /// </summary>
    /// <param name="metadata">The metadata bytes from the previous sync, or <c>null</c> for full sync.</param>
    void AddMetadata(byte[]? metadata);

    /// <summary>
    /// Adds a deserializer function for converting byte arrays to typed configuration objects.
    /// </summary>
    /// <param name="deserializer">A function that deserializes a byte array to an object of the specified type.</param>
    void AddDeserializer(Func<byte[], Type, object> deserializer);

    /// <summary>
    /// Adds a logger instance for diagnostic output.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    void AddLogger(ILogger logger);

    /// <summary>
    /// Adds a discloser function for decrypting protected strings via Keyra.
    /// </summary>
    /// <param name="discloser">A function that takes a cipher reference and returns the plaintext characters.</param>
    void AddDiscloser(Func<string, char[]> discloser);

    /// <summary>
    /// Builds and returns the configured <see cref="IConsumer"/> instance.
    /// </summary>
    /// <returns>A fully configured consumer ready for use.</returns>
    IConsumer Build();

    #endregion
}
