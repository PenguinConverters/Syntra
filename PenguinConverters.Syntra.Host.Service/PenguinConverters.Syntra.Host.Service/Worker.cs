using System.Text;
using NCrontab;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using PenguinConverters.Syntra.Core.Settings;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Core.Target;

namespace PenguinConverters.Syntra.Host.Service;

/// <summary>
/// Background service that loads synchronization configurations from the Configuration/ directory,
/// schedules them via cron expressions, and runs them on timer-based intervals.
/// Uses lease file locking to prevent concurrent execution of the same configuration.
/// </summary>
public class Worker : IHostedService, IDisposable
{
    #region Fields

    private readonly ILogger<Worker> _logger;
    private readonly List<ScheduledJob> _jobs = [];
    private readonly string _configurationDirectory;
    private readonly CancellationTokenSource _stoppingTokenSource = new();
    private bool _disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Worker"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public Worker(ILogger<Worker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configurationDirectory = Path.Combine(AppContext.BaseDirectory, "Configuration");
    }

    #endregion

    #region Methods

    /// <summary>
    /// Starts the hosted service by loading all configuration files and scheduling their timers.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <returns>A completed task once initialization is finished.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Syntra Service starting");

        InitializeComponent();

        foreach (ScheduledJob job in _jobs)
        {
            ScheduleNextRun(job);
        }

        _logger.LogInformation("Syntra Service started with {Count} scheduled job(s)", _jobs.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the hosted service and disposes all timers.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <returns>A completed task once shutdown is finished.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Syntra Service stopping");

        foreach (ScheduledJob job in _jobs)
        {
            job.Timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        _stoppingTokenSource.Cancel();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Loads all YAML and JSON configuration files from the Configuration/ directory.
    /// </summary>
    private void InitializeComponent()
    {
        if (!Directory.Exists(_configurationDirectory))
        {
            _logger.LogWarning("Configuration directory not found: {Directory}", _configurationDirectory);
            return;
        }

        IEnumerable<string> configFiles = Directory.GetFiles(_configurationDirectory, "*.yaml")
            .Concat(Directory.GetFiles(_configurationDirectory, "*.yml"))
            .Concat(Directory.GetFiles(_configurationDirectory, "*.json"));

        foreach (string configFile in configFiles)
        {
            try
            {
                _logger.LogInformation("Loading configuration: {File}", configFile);

                string content = File.ReadAllText(configFile);
                IDeserializer deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                ServiceSyncConfiguration config = deserializer.Deserialize<ServiceSyncConfiguration>(content);

                if (string.IsNullOrWhiteSpace(config?.Schedule))
                {
                    _logger.LogWarning("Configuration file {File} has no cron schedule, skipping", configFile);
                    continue;
                }

                CrontabSchedule schedule = CrontabSchedule.Parse(config.Schedule);

                _jobs.Add(new ScheduledJob
                {
                    Name = Path.GetFileNameWithoutExtension(configFile),
                    ConfigurationPath = configFile,
                    Configuration = config,
                    CronSchedule = schedule,
                    LeaseFilePath = Path.Combine(_configurationDirectory, $".{Path.GetFileNameWithoutExtension(configFile)}.lease")
                });

                _logger.LogInformation("Loaded job '{Name}' with schedule '{Schedule}'",
                    Path.GetFileNameWithoutExtension(configFile), config.Schedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration file: {File}", configFile);
            }
        }
    }

    /// <summary>
    /// Calculates the next run time for the job and sets the timer accordingly.
    /// </summary>
    /// <param name="job">The scheduled job to configure.</param>
    private void ScheduleNextRun(ScheduledJob job)
    {
        DateTime now = DateTime.UtcNow;
        DateTime nextRun = job.CronSchedule.GetNextOccurrence(now);
        TimeSpan delay = nextRun - now;

        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        _logger.LogInformation("Job '{Name}' next run at {NextRun:O} (in {Delay})", job.Name, nextRun, delay);

        job.Timer?.Dispose();
        job.Timer = new Timer(OnTimerElapsed!, job, delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Handles the timer elapsed event. Disables the timer, acquires a lease, runs synchronization,
    /// and schedules the next execution.
    /// </summary>
    /// <param name="state">The <see cref="ScheduledJob"/> associated with the timer.</param>
    private async void OnTimerElapsed(object state)
    {
        ScheduledJob job = (ScheduledJob)state;

        // Disable the timer to prevent re-entry
        job.Timer?.Change(Timeout.Infinite, Timeout.Infinite);

        bool leaseAcquired = false;

        try
        {
            // Lease file locking to prevent concurrent execution
            if (!TryAcquireLease(job))
            {
                _logger.LogWarning("Job '{Name}' is already running (lease held), skipping execution", job.Name);
                return;
            }

            leaseAcquired = true;

            _logger.LogInformation("Job '{Name}' starting synchronization", job.Name);

            await ExecuteSynchronizationAsync(job, _stoppingTokenSource.Token).ConfigureAwait(false);

            _logger.LogInformation("Job '{Name}' synchronization completed", job.Name);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Job '{Name}' was cancelled during synchronization", job.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job '{Name}' failed during synchronization", job.Name);
        }
        finally
        {
            if (leaseAcquired)
            {
                ReleaseLease(job);
            }

            if (!_stoppingTokenSource.IsCancellationRequested)
            {
                ScheduleNextRun(job);
            }
        }
    }

    /// <summary>
    /// Asynchronously executes the synchronization workflow for a given job.
    /// </summary>
    /// <param name="job">The job to execute.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the workflow.</param>
    /// <returns>A task that completes when the workflow has finished.</returns>
    private async Task ExecuteSynchronizationAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        Func<byte[], Type, object> yamlDeserializerFunc = (bytes, type) =>
        {
            string yaml = Encoding.UTF8.GetString(bytes);
            IDeserializer deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            return deserializer.Deserialize(yaml, type)!;
        };

        ServiceSyncConfiguration config = job.Configuration;

        if (config.Source is null)
        {
            _logger.LogError("Job '{Name}' has no source configuration", job.Name);
            return;
        }

        if (config.Target is null)
        {
            _logger.LogError("Job '{Name}' has no target configuration", job.Name);
            return;
        }

        // Build provider
        IProvider provider = BuildProvider(config.Source, yamlDeserializerFunc);

        // Build consumer
        IConsumer consumer = BuildConsumer(config.Target, yamlDeserializerFunc);

        // Run synchronization
        await consumer.SynchronizeAsync(provider, cancellationToken).ConfigureAwait(false);
        await consumer.FinalizeAsync(provider, cancellationToken).ConfigureAwait(false);

        if (consumer.HadErrors)
        {
            _logger.LogWarning("Job '{Name}' completed with errors", job.Name);
        }
    }

    /// <summary>
    /// Builds a provider instance by dynamically loading the connector assembly.
    /// </summary>
    private IProvider BuildProvider(SourceConfiguration sourceConfig, Func<byte[], Type, object> deserializer)
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

        return providerBuilder.Build();
    }

    /// <summary>
    /// Builds a consumer instance by dynamically loading the connector assembly.
    /// </summary>
    private IConsumer BuildConsumer(TargetConfiguration targetConfig, Func<byte[], Type, object> deserializer)
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

        return consumerBuilder.Build();
    }

    /// <summary>
    /// Attempts to acquire a lease file lock to prevent concurrent execution.
    /// </summary>
    /// <param name="job">The job to acquire the lease for.</param>
    /// <returns><c>true</c> if the lease was acquired; <c>false</c> if it is already held.</returns>
    private bool TryAcquireLease(ScheduledJob job)
    {
        try
        {
            if (File.Exists(job.LeaseFilePath))
            {
                string leaseContent = File.ReadAllText(job.LeaseFilePath);

                // A lease older than 1 hour is considered stale and may be taken over.
                if (DateTime.TryParse(leaseContent, out DateTime leaseTime)
                    && DateTime.UtcNow - leaseTime < TimeSpan.FromHours(1))
                {
                    return false;
                }
            }

            File.WriteAllText(job.LeaseFilePath, DateTime.UtcNow.ToString("O"));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Releases the lease file lock.
    /// </summary>
    /// <param name="job">The job to release the lease for.</param>
    private void ReleaseLease(ScheduledJob job)
    {
        try
        {
            if (File.Exists(job.LeaseFilePath))
            {
                File.Delete(job.LeaseFilePath);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to release lease file for job '{Name}'", job.Name);
        }
    }

    /// <summary>
    /// Releases all resources used by the worker.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (ScheduledJob job in _jobs)
            {
                job.Timer?.Dispose();
            }

            _stoppingTokenSource.Dispose();
            _disposed = true;
        }
    }

    #endregion
}

/// <summary>
/// Represents a scheduled synchronization job loaded from a configuration file.
/// </summary>
internal class ScheduledJob
{
    #region Properties

    /// <summary>
    /// Gets or sets the display name of the job.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full path to the configuration file.
    /// </summary>
    public string ConfigurationPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the parsed synchronization configuration.
    /// </summary>
    public ServiceSyncConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// Gets or sets the parsed cron schedule.
    /// </summary>
    public CrontabSchedule CronSchedule { get; set; } = null!;

    /// <summary>
    /// Gets or sets the timer used to trigger the next execution.
    /// </summary>
    public Timer? Timer { get; set; }

    /// <summary>
    /// Gets or sets the path to the lease file used for concurrency control.
    /// </summary>
    public string LeaseFilePath { get; set; } = string.Empty;

    #endregion
}

/// <summary>
/// Configuration model for the service host, extending the sync configuration with a cron schedule.
/// </summary>
internal class ServiceSyncConfiguration
{
    #region Properties

    /// <summary>
    /// Gets or sets the cron expression defining when the synchronization runs.
    /// </summary>
    public string? Schedule { get; set; }

    /// <summary>
    /// Gets or sets the source provider configuration.
    /// </summary>
    public SourceConfiguration? Source { get; set; }

    /// <summary>
    /// Gets or sets the target consumer configuration.
    /// </summary>
    public TargetConfiguration? Target { get; set; }

    #endregion
}
