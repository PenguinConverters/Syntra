using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Syntra.Core.Extensions;
using PenguinConverters.Syntra.Core.Settings;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Core.Target;

namespace PenguinConverters.Syntra.Core;

/// <summary>
/// Orchestrates the synchronization pipeline between a source provider and a target consumer.
/// Use <see cref="HandlerBuilder"/> to create an instance.
/// </summary>
public class Handler
{
    #region Fields

    private readonly Configuration _configuration;
    private readonly byte[]? _sourceMetadata;
    private readonly byte[]? _targetMetadata;
    private readonly Func<byte[], Type, object>? _deserializer;
    private readonly Func<string, char[]>? _discloser;
    private readonly ILogger _logger;
    private readonly byte[]? _publicKey;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Handler"/> class.
    /// Use <see cref="HandlerBuilder"/> instead of calling this constructor directly.
    /// </summary>
    internal Handler(
        Configuration configuration,
        byte[]? sourceMetadata,
        byte[]? targetMetadata,
        Func<byte[], Type, object>? deserializer,
        Func<string, char[]>? discloser,
        ILogger logger,
        byte[]? publicKey)
    {
        _configuration = configuration;
        _sourceMetadata = sourceMetadata;
        _targetMetadata = targetMetadata;
        _deserializer = deserializer;
        _discloser = discloser;
        _logger = logger ?? NullLogger.Instance;
        _publicKey = publicKey;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the source provider metadata after synchronization completes.
    /// Used to persist delta tokens for subsequent runs.
    /// </summary>
    public byte[]? SourceMetadata { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the consumer reported errors during synchronization.
    /// </summary>
    public bool HadErrors { get; private set; }

    #endregion

    #region Methods

    /// <summary>
    /// Asynchronously executes the full synchronization pipeline:
    /// builds the provider and consumer, runs synchronization, and finalizes.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation of the pipeline.</param>
    /// <returns>A task that completes when the pipeline has finished.</returns>
    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting synchronization. Source: {Source}, Target: {Target}, Delta: {Delta}",
            _configuration.Source.Type, _configuration.Target.Type, _configuration.Delta);

        IProvider provider = BuildProvider();
        IConsumer consumer = BuildConsumer();

        try
        {
            _logger.LogInformation("Running synchronization pipeline.");
            await consumer.SynchronizeAsync(provider, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during synchronization pipeline.");
            HadErrors = true;
            throw;
        }
        finally
        {
            try
            {
                _logger.LogInformation("Finalizing synchronization pipeline.");
                await consumer.FinalizeAsync(provider, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during finalization.");
                HadErrors = true;
            }

            SourceMetadata = provider.Metadata;
            HadErrors = HadErrors || consumer.HadErrors;
        }

        _logger.LogInformation("Synchronization completed. HadErrors: {HadErrors}", HadErrors);
    }

    private IProvider BuildProvider()
    {
        _logger.LogDebug("Building provider from assembly '{Assembly}'.", _configuration.Source.Type);

        byte[]? sourceConfigBytes = SerializeSourceConfiguration();

        InstanceBuilder<IProviderBuilder> builder = new InstanceBuilder<IProviderBuilder>(_configuration.Source.Type)
            .WithLogger(_logger)
            .WithMetadata(_configuration.Delta ? _sourceMetadata : null);

        if (sourceConfigBytes is not null)
            builder.WithConfiguration(sourceConfigBytes);

        if (_deserializer is not null)
            builder.WithDeserializer(_deserializer);

        if (_discloser is not null)
            builder.WithDiscloser(_discloser);

        if (_publicKey is not null)
            builder.WithPublicKey(_publicKey);

        return (IProvider)builder.Build();
    }

    private IConsumer BuildConsumer()
    {
        _logger.LogDebug("Building consumer from assembly '{Assembly}'.", _configuration.Target.Type);

        byte[]? targetConfigBytes = SerializeTargetConfiguration();

        InstanceBuilder<IConsumerBuilder> builder = new InstanceBuilder<IConsumerBuilder>(_configuration.Target.Type)
            .WithLogger(_logger)
            .WithMetadata(_configuration.Delta ? _targetMetadata : null);

        if (targetConfigBytes is not null)
            builder.WithConfiguration(targetConfigBytes);

        if (_deserializer is not null)
            builder.WithDeserializer(_deserializer);

        if (_discloser is not null)
            builder.WithDiscloser(_discloser);

        if (_publicKey is not null)
            builder.WithPublicKey(_publicKey);

        return (IConsumer)builder.Build();
    }

    private byte[]? SerializeSourceConfiguration()
    {
        try
        {
            return JsonSerializerExtensions.Serialize(_configuration.Source);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to serialize source configuration.");
            return null;
        }
    }

    private byte[]? SerializeTargetConfiguration()
    {
        try
        {
            return JsonSerializerExtensions.Serialize(_configuration.Target);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to serialize target configuration.");
            return null;
        }
    }

    #endregion
}
