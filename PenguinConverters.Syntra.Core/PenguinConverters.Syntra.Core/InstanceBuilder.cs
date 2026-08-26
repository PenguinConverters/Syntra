using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Keyra;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Core.Target;

namespace PenguinConverters.Syntra.Core;

/// <summary>
/// Dynamically loads connector assemblies and creates provider/consumer instances via reflection.
/// Uses the Builder pattern to configure dependencies before building.
/// </summary>
/// <typeparam name="T">The builder interface type: <see cref="IProviderBuilder"/> or <see cref="IConsumerBuilder"/>.</typeparam>
public class InstanceBuilder<T> where T : class
{
    #region Fields

    private readonly string _assemblyName;
    private byte[]? _configuration;
    private byte[]? _metadata;
    private Func<byte[], Type, object>? _deserializer;
    private Decryptor? _decryptor;
    private ILogger _logger = NullLogger.Instance;
    private byte[]? _expectedPublicKey;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceBuilder{T}"/> class.
    /// </summary>
    /// <param name="assemblyName">The name of the assembly containing the connector implementation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assemblyName"/> is null or empty.</exception>
    public InstanceBuilder(string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            throw new ArgumentNullException(nameof(assemblyName));

        _assemblyName = assemblyName;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Sets the serialized configuration bytes.
    /// </summary>
    /// <param name="configuration">The configuration bytes.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public InstanceBuilder<T> WithConfiguration(byte[] configuration)
    {
        _configuration = configuration;
        return this;
    }

    /// <summary>
    /// Sets the serialized metadata bytes for delta synchronization.
    /// </summary>
    /// <param name="metadata">The metadata bytes, or <c>null</c> for full sync.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public InstanceBuilder<T> WithMetadata(byte[]? metadata)
    {
        _metadata = metadata;
        return this;
    }

    /// <summary>
    /// Sets the deserializer function.
    /// </summary>
    /// <param name="deserializer">The deserializer function.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public InstanceBuilder<T> WithDeserializer(Func<byte[], Type, object> deserializer)
    {
        _deserializer = deserializer;
        return this;
    }

    /// <summary>
    /// Sets the Keyra decryptor used to disclose protected configuration values.
    /// </summary>
    /// <param name="decryptor">The decryptor holding the vault key.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public InstanceBuilder<T> WithDecryptor(Decryptor decryptor)
    {
        _decryptor = decryptor;
        return this;
    }

    /// <summary>
    /// Sets the logger instance.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public InstanceBuilder<T> WithLogger(ILogger logger)
    {
        _logger = logger ?? NullLogger.Instance;
        return this;
    }

    /// <summary>
    /// Sets the expected public key for assembly signature validation.
    /// </summary>
    /// <param name="publicKey">The expected public key bytes.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public InstanceBuilder<T> WithPublicKey(byte[] publicKey)
    {
        _expectedPublicKey = publicKey;
        return this;
    }

    /// <summary>
    /// Loads the assembly, discovers the builder implementation, configures it,
    /// and builds the provider or consumer instance.
    /// </summary>
    /// <returns>The built provider or consumer instance.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the assembly cannot be loaded, no implementation is found,
    /// or signature validation fails.
    /// </exception>
    public object Build()
    {
        Assembly assembly = LoadAssembly(_assemblyName);

        ValidateSignature(assembly);

        Type builderType = assembly.GetExportedTypes()
            .FirstOrDefault(t => typeof(T).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            ?? throw new InvalidOperationException(
                $"Assembly '{_assemblyName}' does not contain an implementation of {typeof(T).Name}.");

        T builder = (T)(Activator.CreateInstance(builderType)
            ?? throw new InvalidOperationException(
                $"Failed to create instance of '{builderType.FullName}'."));

        ConfigureBuilder(builder);

        return builder switch
        {
            IProviderBuilder providerBuilder => providerBuilder.Build(),
            IConsumerBuilder consumerBuilder => consumerBuilder.Build(),
            _ => throw new InvalidOperationException(
                $"Builder type '{typeof(T).Name}' is not a supported builder interface.")
        };
    }

    private static Assembly LoadAssembly(string assemblyName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to load assembly '{assemblyName}'. Ensure it is available in the application directory.", ex);
        }
    }

    private void ValidateSignature(Assembly assembly)
    {
        if (_expectedPublicKey is null)
            return;

        byte[]? assemblyPublicKey = assembly.GetName().GetPublicKey();

        if (assemblyPublicKey is null || !assemblyPublicKey.SequenceEqual(_expectedPublicKey))
        {
            throw new InvalidOperationException(
                $"Assembly '{_assemblyName}' public key does not match the expected signature. " +
                "The assembly may have been tampered with.");
        }
    }

    private void ConfigureBuilder(T builder)
    {
        if (builder is IProviderBuilder providerBuilder)
        {
            if (_configuration is not null)
                providerBuilder.AddConfiguration(_configuration);

            providerBuilder.AddMetadata(_metadata);

            if (_deserializer is not null)
                providerBuilder.AddDeserializer(_deserializer);

            providerBuilder.AddLogger(_logger);

            if (_decryptor is not null)
                providerBuilder.AddDecryptor(_decryptor);
        }
        else if (builder is IConsumerBuilder consumerBuilder)
        {
            if (_configuration is not null)
                consumerBuilder.AddConfiguration(_configuration);

            consumerBuilder.AddMetadata(_metadata);

            if (_deserializer is not null)
                consumerBuilder.AddDeserializer(_deserializer);

            consumerBuilder.AddLogger(_logger);

            if (_decryptor is not null)
                consumerBuilder.AddDecryptor(_decryptor);
        }
    }

    #endregion
}
