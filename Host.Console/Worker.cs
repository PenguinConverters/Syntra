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
            .IgnoreUnmatchedProperties()
            .Build();

        SyncConfiguration configuration = deserializer.Deserialize<SyncConfiguration>(yamlContent);

        string objectNamespace = string.IsNullOrWhiteSpace(configuration?.ObjectNamespace)
            ? Path.GetFileNameWithoutExtension(_configurationPath)
            : configuration.ObjectNamespace;

        ConnectorSection? source = ConnectorSection.Read(yamlContent, "source");
        ConnectorSection? target = _schemaMode
            ? ConnectorSection.ForType("PenguinConverters.Syntra.Consumer.SchemaDesigner")
            : ConnectorSection.Read(yamlContent, "target", objectNamespace);

        if (source is null)
        {
            throw new InvalidOperationException("Configuration must contain a 'source' section.");
        }

        if (target is null)
        {
            throw new InvalidOperationException("Configuration must contain a 'target' section unless running in --schema mode.");
        }

        _logger.LogInformation("Source type: {SourceType}", source.Type);
        _logger.LogInformation("Target type: {TargetType}", target.Type);

        Func<byte[], Type, object> yamlDeserializerFunc = (bytes, type) =>
        {
            string yaml = Encoding.UTF8.GetString(bytes);
            IDeserializer yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithCaseInsensitivePropertyMatching()
                .IgnoreUnmatchedProperties()
                .Build();
            return yamlDeserializer.Deserialize(yaml, type)!;
        };

        // The key opens every protected value in this configuration, so it is held for the whole run
        // and disposed with it.
        using Decryptor? decryptor = configuration?.Keyra?.CreateDecryptor();

        if (decryptor is not null)
        {
            _logger.LogInformation("Opened Keyra key {KeyId} for credential disclosure", decryptor.KeyId);
        }

        if (_schemaMode)
        {
            _logger.LogInformation("Running in SchemaDesigner mode");
        }

        IProvider provider = BuildProvider(source, yamlDeserializerFunc, decryptor);
        IConsumer consumer = BuildConsumer(target, yamlDeserializerFunc, decryptor);

        _logger.LogInformation("Starting synchronization");
        try
        {
            await consumer.SynchronizeAsync(provider, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await consumer.FinalizeAsync(provider, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await ReleaseAsync(provider).ConfigureAwait(false);
                await ReleaseAsync(consumer).ConfigureAwait(false);
            }
        }

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
    /// Releases a connector that holds resources, whichever disposal contract it implements.
    /// </summary>
    /// <param name="connector">The connector to release.</param>
    /// <returns>A task that completes when the connector has been released.</returns>
    private async ValueTask ReleaseAsync(object connector)
    {
        try
        {
            switch (connector)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;

                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release {Connector}.", connector.GetType().Name);
        }
    }

    /// <summary>
    /// Builds a provider instance by dynamically loading the connector assembly.
    /// </summary>
    /// <param name="section">The source section of the job configuration.</param>
    /// <param name="deserializer">The function the connector deserializes its configuration with.</param>
    /// <param name="decryptor">The decryptor for protected configuration values, if a key is configured.</param>
    /// <returns>The provider, configured from the complete section.</returns>
    private IProvider BuildProvider(
        ConnectorSection section, Func<byte[], Type, object> deserializer, Decryptor? decryptor)
    {
        string assemblyName = section.Type;
        System.Reflection.Assembly assembly = System.Reflection.Assembly.Load(assemblyName);

        Type builderType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IProviderBuilder).IsAssignableFrom(t) && !t.IsAbstract)
            ?? throw new InvalidOperationException($"No IProviderBuilder found in assembly '{assemblyName}'.");

        IProviderBuilder providerBuilder = (IProviderBuilder)Activator.CreateInstance(builderType)!;

        providerBuilder.AddConfiguration(section.Content);
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
    /// <param name="section">The target section of the job configuration.</param>
    /// <param name="deserializer">The function the connector deserializes its configuration with.</param>
    /// <param name="decryptor">The decryptor for protected configuration values, if a key is configured.</param>
    /// <returns>The consumer, configured from the complete section.</returns>
    private IConsumer BuildConsumer(
        ConnectorSection section, Func<byte[], Type, object> deserializer, Decryptor? decryptor)
    {
        string assemblyName = section.Type;
        System.Reflection.Assembly assembly = System.Reflection.Assembly.Load(assemblyName);

        Type builderType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IConsumerBuilder).IsAssignableFrom(t) && !t.IsAbstract)
            ?? throw new InvalidOperationException($"No IConsumerBuilder found in assembly '{assemblyName}'.");

        IConsumerBuilder consumerBuilder = (IConsumerBuilder)Activator.CreateInstance(builderType)!;

        consumerBuilder.AddConfiguration(section.Content);
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
    /// Gets or sets the name identifying this synchronization. The configuration file name
    /// stands in when it is not set.
    /// </summary>
    public string? ObjectNamespace { get; set; }

    /// <summary>
    /// Gets or sets the Keyra vault key location used to disclose protected configuration values.
    /// </summary>
    public KeyraSettings? Keyra { get; set; }

    #endregion
}
