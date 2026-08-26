using System.Text;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using PenguinConverters.Keyra;
using PenguinConverters.Syntra.Core.Settings;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Core.Target;

namespace PenguinConverters.Syntra.Host.Console;

/// <summary>
/// Loads YAML configuration, creates provider and consumer instances, and runs synchronization.
/// Supports an optional SchemaDesigner mode for schema inference.
/// </summary>
public class Worker
{
    #region Fields

    private readonly string _configurationPath;
    private readonly bool _schemaMode;
    private readonly ILogger _logger;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Worker"/> class.
    /// </summary>
    /// <param name="configurationPath">The path to the YAML configuration file.</param>
    /// <param name="schemaMode">Whether to run in SchemaDesigner mode.</param>
    /// <param name="logger">The logger instance.</param>
    public Worker(string configurationPath, bool schemaMode, ILogger logger)
    {
        _configurationPath = configurationPath ?? throw new ArgumentNullException(nameof(configurationPath));
        _schemaMode = schemaMode;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #endregion

    #region Methods

    /// <summary>
    /// Asynchronously executes the synchronization workflow: loads configuration,
    /// builds provider and consumer, and runs sync.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation of the workflow.</param>
    /// <returns>A task that completes when the workflow has finished.</returns>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading configuration from {Path}", _configurationPath);

        if (!File.Exists(_configurationPath))
        {
            throw new FileNotFoundException($"Configuration file not found: {_configurationPath}", _configurationPath);
        }

        string yamlContent = await File.ReadAllTextAsync(_configurationPath, cancellationToken).ConfigureAwait(false);
        // The naming convention establishes camelCase as the form the documentation uses, and
        // case-insensitive matching lets a configuration written in any other casing bind to the
        // same properties - PascalCase being what a JSON job file carries, YAML being a superset
        // of JSON.
        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithCaseInsensitivePropertyMatching()
            .Build();

        SyncConfiguration configuration = deserializer.Deserialize<SyncConfiguration>(yamlContent);

        if (configuration?.Source is null)
        {
            throw new InvalidOperationException("Configuration must contain a 'source' section.");
        }

        if (configuration.Target is null && !_schemaMode)
        {
            throw new InvalidOperationException("Configuration must contain a 'target' section unless running in --schema mode.");
        }

        _logger.LogInformation("Source type: {SourceType}", configuration.Source.Type);

        // Build YAML deserializer function for connector configuration
        Func<byte[], Type, object> yamlDeserializerFunc = (bytes, type) =>
        {
            string yaml = Encoding.UTF8.GetString(bytes);
            IDeserializer yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithCaseInsensitivePropertyMatching()
                .Build();
            return yamlDeserializer.Deserialize(yaml, type)!;
        };

        // The key opens every protected value in this configuration, so it is held for the whole run
        // and disposed with it.
        using Decryptor? decryptor = configuration.Keyra?.CreateDecryptor();

        if (decryptor is not null)
        {
            _logger.LogInformation("Opened Keyra key {KeyId} for credential disclosure", decryptor.KeyId);
        }

        // Build provider
        IProvider provider = BuildProvider(configuration.Source, yamlDeserializerFunc, decryptor);

        // Build consumer
        IConsumer consumer;
        if (_schemaMode)
        {
            _logger.LogInformation("Running in SchemaDesigner mode");
            consumer = BuildConsumer(
                new TargetConfiguration { Type = "PenguinConverters.Syntra.Consumer.SchemaDesigner" },
                yamlDeserializerFunc,
                decryptor);
        }
        else
        {
            _logger.LogInformation("Target type: {TargetType}", configuration.Target!.Type);
            consumer = BuildConsumer(configuration.Target, yamlDeserializerFunc, decryptor);
        }

        // Run synchronization
        _logger.LogInformation("Starting synchronization");
        await consumer.SynchronizeAsync(provider, cancellationToken).ConfigureAwait(false);
        await consumer.FinalizeAsync(provider, cancellationToken).ConfigureAwait(false);

        if (consumer.HadErrors)
        {
            _logger.LogWarning("Synchronization completed with errors");
        }
        else
        {
            _logger.LogInformation("Synchronization completed successfully");
        }
    }

    /// <summary>
    /// Builds a provider instance by dynamically loading the connector assembly.
    /// </summary>
    private IProvider BuildProvider(
        SourceConfiguration sourceConfig, Func<byte[], Type, object> deserializer, Decryptor? decryptor)
    {
        string assemblyName = sourceConfig.Type;
        System.Reflection.Assembly assembly = System.Reflection.Assembly.Load(assemblyName);

        Type builderType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IProviderBuilder).IsAssignableFrom(t) && !t.IsAbstract)
            ?? throw new InvalidOperationException($"No IProviderBuilder found in assembly '{assemblyName}'.");

        IProviderBuilder providerBuilder = (IProviderBuilder)Activator.CreateInstance(builderType)!;

        byte[] configBytes = Encoding.UTF8.GetBytes(
            new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()
                .Serialize(sourceConfig));

        providerBuilder.AddConfiguration(configBytes);
        providerBuilder.AddMetadata(null);
        providerBuilder.AddDeserializer(deserializer);
        providerBuilder.AddLogger(_logger);

        if (decryptor is not null)
            providerBuilder.AddDecryptor(decryptor);

        return providerBuilder.Build();
    }

    /// <summary>
    /// Builds a consumer instance by dynamically loading the connector assembly.
    /// </summary>
    private IConsumer BuildConsumer(
        TargetConfiguration targetConfig, Func<byte[], Type, object> deserializer, Decryptor? decryptor)
    {
        string assemblyName = targetConfig.Type;
        System.Reflection.Assembly assembly = System.Reflection.Assembly.Load(assemblyName);

        Type builderType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IConsumerBuilder).IsAssignableFrom(t) && !t.IsAbstract)
            ?? throw new InvalidOperationException($"No IConsumerBuilder found in assembly '{assemblyName}'.");

        IConsumerBuilder consumerBuilder = (IConsumerBuilder)Activator.CreateInstance(builderType)!;

        byte[] configBytes = Encoding.UTF8.GetBytes(
            new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()
                .Serialize(targetConfig));

        consumerBuilder.AddConfiguration(configBytes);
        consumerBuilder.AddMetadata(null);
        consumerBuilder.AddDeserializer(deserializer);
        consumerBuilder.AddLogger(_logger);

        if (decryptor is not null)
            consumerBuilder.AddDecryptor(decryptor);

        return consumerBuilder.Build();
    }

    #endregion
}

/// <summary>
/// Top-level synchronization configuration containing source and target sections.
/// </summary>
internal class SyncConfiguration
{
    #region Properties

    /// <summary>
    /// Gets or sets the source provider configuration.
    /// </summary>
    public SourceConfiguration? Source { get; set; }

    /// <summary>
    /// Gets or sets the target consumer configuration.
    /// </summary>
    public TargetConfiguration? Target { get; set; }

    /// <summary>
    /// Gets or sets the Keyra vault key location used to disclose protected configuration values.
    /// </summary>
    public KeyraSettings? Keyra { get; set; }

    #endregion
}
