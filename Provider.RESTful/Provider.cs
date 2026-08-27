using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Types;
using PenguinConverters.Syntra.Provider.RESTful.Settings;
using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.RESTful;

/// <summary>
/// A source provider for a RESTful HTTP API, driven entirely by configuration.
/// </summary>
/// <remarks>
/// This is a working connector on its own: a configuration naming a service root, an endpoint,
/// credentials and the shape of the response is enough, and the synchronization pipeline can load
/// this assembly directly. <see cref="RetrieveAsync"/> is implemented here and is not meant to be
/// replaced - an API that does something unusual is accommodated through the hooks below, each of
/// which is a <c>protected virtual</c> method whose default implementation invokes an optional
/// delegate. A derived connector overrides the method; a host wiring one up assigns the delegate;
/// neither has to restate the retrieval loop.
/// <list type="bullet">
///   <item><description>
///     <see cref="ValueHandlers"/> and <see cref="ValueHandler"/> - coerce a raw property value
///     to the type a consumer should store, per property name or across the board.
///   </description></item>
///   <item><description>
///     <see cref="EntryTransform"/> - reshape, tag or drop a whole record before it becomes an
///     entity.
///   </description></item>
///   <item><description>
///     <see cref="StateSelector"/> and <see cref="IdentitySelector"/> - decide what an object's
///     synchronization state and identity are when configuration cannot express it.
///   </description></item>
///   <item><description>
///     <see cref="ContentReader"/> - read a response body that is not JSON.
///   </description></item>
///   <item><description>
///     <see cref="EndPointResolver"/> - resolve an endpoint that has to be looked up before it
///     can be read.
///   </description></item>
/// </list>
/// Authentication is the seam of <see cref="ProviderBuilder"/> rather than of this type, because
/// it is settled before the first request is made.
/// </remarks>
public class Provider : Core.Source.Provider, IDisposable
{
    #region Constants

    /// <summary>
    /// Entities buffered between the retrieval and the consumer reading them.
    /// </summary>
    private const int ChannelCapacity = 1024;

    /// <summary>
    /// Projection token that asks for every property, which is passed through rather than
    /// expanded into a list of names.
    /// </summary>
    private const string AllProperties = "*";

    #endregion

    #region Fields

    private readonly Dictionary<string, Func<object?, object?>> _valueHandlers =
        new Dictionary<string, Func<object?, object?>>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<Configuration, object?> _configuredFilters = [];

    private readonly Lock _offsetLock = new Lock();

    private Configuration? _configuration;
    private State _state = new State();
    private RestClient? _restClient;
    private DateTime? _offset;
    private bool _hadErrors;
    private bool _disposed;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the endpoint configuration. Assigned by <see cref="ProviderBuilder"/>.
    /// </summary>
    public Configuration? Configuration
    {
        get => _configuration;
        set => _configuration = value;
    }

    /// <summary>
    /// Gets or sets the reader the endpoints are read through.
    /// Assigned by <see cref="ProviderBuilder"/>, which owns its construction.
    /// </summary>
    public RestClient? RestClient
    {
        get => _restClient;
        set => _restClient = value;
    }

    /// <summary>
    /// Gets or sets the watermark this run resumes from and records.
    /// </summary>
    public State State
    {
        get => _state;
        set => _state = value;
    }

    /// <summary>
    /// Gets a value indicating whether anything failed during retrieval. When it is <c>true</c>
    /// the watermark is withheld, so the next run repeats the range rather than skipping past
    /// records that were never read.
    /// </summary>
    public bool HadErrors => _hadErrors;

    /// <summary>
    /// Gets the per-property value handlers, keyed by property name and compared
    /// case-insensitively. A handler receives the value as the response carried it - a string, a
    /// number, a boolean, or the raw JSON text of a nested object - and returns what the consumer
    /// should store.
    /// </summary>
    public IDictionary<string, Func<object?, object?>> ValueHandlers => _valueHandlers;

    /// <summary>
    /// Gets or sets the handler applied to every property that has no entry in
    /// <see cref="ValueHandlers"/>. It receives the property name and the value.
    /// </summary>
    public Func<string, object?, object?>? ValueHandler { get; set; }

    /// <summary>
    /// Gets or sets the transform applied to a whole record once its values have been handled.
    /// Returning <c>null</c> drops the record; returning a different property bag replaces it.
    /// </summary>
    public Func<QuickDictionary, Configuration, QuickDictionary?>? EntryTransform { get; set; }

    /// <summary>
    /// Gets or sets the delegate deciding the synchronization state of a record, for an API whose
    /// deletion marker is more than a property equal to a value.
    /// </summary>
    public Func<QuickDictionary, Configuration, EntityState>? StateSelector { get; set; }

    /// <summary>
    /// Gets or sets the delegate deciding the identity of a record, for an API whose key is
    /// composite or derived.
    /// </summary>
    public Func<QuickDictionary, Configuration, string?>? IdentitySelector { get; set; }

    /// <summary>
    /// Gets or sets the reader for a response body that is not JSON, such as a CSV export.
    /// It streams one property bag per record.
    /// </summary>
    public Func<Stream, Configuration, CancellationToken, IAsyncEnumerable<QuickDictionary>>? ContentReader { get; set; }

    /// <summary>
    /// Gets or sets the delegate resolving an endpoint that cannot be written down in full - one
    /// naming a report whose identifier has to be looked up first, for instance. It runs after
    /// the parent placeholders have been substituted.
    /// </summary>
    public Func<Configuration, string, CancellationToken, ValueTask<string>>? EndPointResolver { get; set; }

    /// <summary>
    /// Gets a value indicating whether response bodies are read by <see cref="ReadContent"/>
    /// rather than parsed as JSON. A connector for an API that never answers with JSON overrides
    /// this to <c>true</c> so that its reader applies without a delegate being assigned.
    /// </summary>
    protected virtual bool ReadsContent => ContentReader is not null;

    #endregion

    #region Methods

    /// <inheritdoc />
    public override async IAsyncEnumerable<IEntity> RetrieveAsync(
        IEnumerable<string> properties,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_configuration is null)
        {
            Logger.LogError("The RESTful provider configuration is not set.");
            yield break;
        }

        if (_restClient is null)
        {
            _hadErrors = true;
            Logger.LogError("The RESTful provider HTTP client is not established.");
            yield break;
        }

        string endPoint = _configuration.EndPoint ?? string.Empty;
        string[] requested = properties as string[] ?? properties?.ToArray() ?? [];

        // The running watermark starts at the previous one, so a run that reads nothing leaves it
        // where it was instead of resetting it to the beginning of time.
        _offset = ResolveOffset(endPoint);
        _hadErrors = false;

        PrepareConfiguration(_configuration, requested, _offset);

        Logger.LogInformation(
            "Starting {SyncType} retrieval of '{EndPoint}' from {BaseUrl}.",
            _configuration.Delta ? "delta" : "full",
            endPoint,
            _configuration.GetBaseUrl());

        // Retrieval runs on its own task so that child endpoints are read concurrently while the
        // consumer drains what has already been resolved. The channel is bounded, so a slow
        // consumer throttles the retrieval instead of accumulating the whole result set.
        Channel<IEntity> channel = Channel.CreateBounded<IEntity>(
            new BoundedChannelOptions(ChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

        using CancellationTokenSource retrieval =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task producer = ProduceAsync(channel.Writer, retrieval.Token);

        try
        {
            await foreach (IEntity entity in channel.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return entity;
            }
        }
        finally
        {
            // A consumer that stops early leaves the retrieval blocked on a full channel;
            // cancelling releases it so the task can be awaited rather than abandoned.
            await retrieval.CancelAsync().ConfigureAwait(false);

            try
            {
                await producer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_hadErrors)
        {
            RawMetadata = null;
            Logger.LogWarning("Errors occurred during retrieval; the watermark was not updated.");
            yield break;
        }

        _state = new State
        {
            Offset = _offset,
            Token = _state.Token,
            EndPoint = endPoint,
            Recorded = DateTime.UtcNow
        };

        RawMetadata = SerializeState();

        Logger.LogInformation(
            "Retrieval completed. Watermark {State}.",
            _offset is null ? "not recorded" : $"at {_offset:O}");
    }

    /// <summary>
    /// Deserializes the raw configuration bytes and applies them to this provider.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when deserialization fails.</exception>
    public void ApplyConfiguration()
    {
        _configuration = ReadConfiguration()
            ?? throw new InvalidOperationException(
                "Failed to deserialize the RESTful provider configuration.");

        // Deserialization replaces a nested settings section wholesale rather than merging into
        // it, so a connector's defaults for one are restored here rather than lost to a file that
        // mentioned the section at all.
        _configuration.ApplyDefaults();
    }

    /// <summary>
    /// Initializes the watermark from the raw metadata bytes.
    /// </summary>
    public void InitializeState()
    {
        _state = RawMetadata is not null && Deserializer is not null
            ? DeserializeMetadata<State>() ?? new State()
            : new State();
    }

    /// <summary>
    /// Discloses a configured secret on behalf of <see cref="ProviderBuilder"/>, which assembles
    /// the credentials but has no access to the protected disclosure helper this provider
    /// inherits.
    /// </summary>
    /// <param name="secret">The configured secret, or <c>null</c> if the setting was omitted.</param>
    /// <param name="plaintext">
    /// When this method returns <c>true</c>, the disclosed characters. The caller owns the array
    /// and should clear it once the credential has been used.
    /// </param>
    /// <returns><c>true</c> if the value was disclosed; otherwise, <c>false</c>.</returns>
    public bool TryDiscloseSecret(Secret? secret, out char[] plaintext)
    {
        return TryDisclose(secret, out plaintext);
    }

    /// <summary>
    /// Registers a value handler for one property.
    /// </summary>
    /// <param name="propertyName">The property the handler applies to.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>This provider, so that registrations chain.</returns>
    public Provider AddValueHandler(string propertyName, Func<object?, object?> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(handler);

        _valueHandlers[propertyName] = handler;

        return this;
    }

    /// <summary>
    /// Deserializes the raw configuration bytes to the configuration type this connector uses.
    /// A connector deriving its own configuration type overrides this to name it.
    /// </summary>
    /// <returns>The configuration, or <c>null</c> when none could be deserialized.</returns>
    protected virtual Configuration? ReadConfiguration()
    {
        return DeserializeConfiguration<Configuration>();
    }

    /// <summary>
    /// Prepares an endpoint and its children for retrieval by adding the property projection,
    /// the delta filter and the paging parameters to what was configured.
    /// </summary>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <param name="properties">The property names the consumer asked for.</param>
    /// <param name="offset">The watermark this run resumes from, or <c>null</c> for a full pass.</param>
    protected virtual void PrepareConfiguration(
        Configuration configuration,
        string[] properties,
        DateTime? offset)
    {
        ApplyProjection(configuration, properties);
        ApplyFilter(configuration, offset);
        ApplyPaging(configuration);

        foreach (Configuration child in configuration.Children ?? [])
        {
            PrepareConfiguration(child, properties, offset);
        }

        Logger.LogTrace(
            "Prepared '{EndPoint}' with parameters {Parameters}.",
            configuration.EndPoint,
            JsonSerializer.Serialize(configuration.Parameters));
    }

    /// <summary>
    /// Coerces one property value to what the consumer should store.
    /// </summary>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The value as the response carried it.</param>
    /// <returns>The value to store.</returns>
    protected virtual object? ResolveValue(string propertyName, object? value)
    {
        if (_valueHandlers.TryGetValue(propertyName, out Func<object?, object?>? handler))
        {
            return handler(value);
        }

        return ValueHandler is null ? value : ValueHandler(propertyName, value);
    }

    /// <summary>
    /// Reshapes a record once its values have been handled.
    /// </summary>
    /// <param name="properties">The record.</param>
    /// <param name="configuration">The endpoint it came from.</param>
    /// <returns>
    /// The record to carry forward, or <c>null</c> to drop it.
    /// </returns>
    protected virtual QuickDictionary? TransformEntry(QuickDictionary properties, Configuration configuration)
    {
        return EntryTransform is null ? properties : EntryTransform(properties, configuration);
    }

    /// <summary>
    /// Decides the synchronization state of a record.
    /// </summary>
    /// <param name="properties">The record.</param>
    /// <param name="configuration">The endpoint it came from.</param>
    /// <returns>The state.</returns>
    protected virtual EntityState ResolveState(QuickDictionary properties, Configuration configuration)
    {
        return StateSelector is not null
            ? StateSelector(properties, configuration)
            : ResolveConfiguredState(properties, configuration);
    }

    /// <summary>
    /// Decides the synchronization state of a record from the configured deletion marker, which
    /// is what <see cref="ResolveState"/> does when no delegate is assigned. It stays available
    /// to a derived connector that wants to fall back to it.
    /// </summary>
    /// <param name="properties">The record.</param>
    /// <param name="configuration">The endpoint it came from.</param>
    /// <returns>The state.</returns>
    protected EntityState ResolveConfiguredState(QuickDictionary properties, Configuration configuration)
    {
        string? deletedProperty = configuration.DeletedProperty ?? _configuration?.DeletedProperty;

        if (deletedProperty is not null
            && properties.TryGetValue(deletedProperty, out object? value)
            && string.Equals(value?.ToString(), configuration.DeletedValue, StringComparison.OrdinalIgnoreCase))
        {
            return EntityState.Deleted;
        }

        // A delta run reports what changed, so what it returns has changed. A full pass makes no
        // such claim and leaves the record for the consumer to reconcile against what it holds.
        return _configuration?.Delta == true || configuration.Delta
            ? EntityState.Updated
            : EntityState.Unclassified;
    }

    /// <summary>
    /// Decides the identity of a record.
    /// </summary>
    /// <param name="properties">The record.</param>
    /// <param name="configuration">The endpoint it came from.</param>
    /// <returns>The identity, or <c>null</c> when the record carries none.</returns>
    protected virtual string? ResolveIdentity(QuickDictionary properties, Configuration configuration)
    {
        if (IdentitySelector is not null)
        {
            return IdentitySelector(properties, configuration);
        }

        string? identityProperty = configuration.IdentityProperty ?? _configuration?.IdentityProperty;

        return identityProperty is not null && properties.TryGetValue(identityProperty, out object? value)
            ? value?.ToString()
            : null;
    }

    /// <summary>
    /// Resolves an endpoint whose address is not known until the run starts. The parent
    /// placeholders have already been substituted by the time it is called.
    /// </summary>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <param name="endPoint">The endpoint as configuration and placeholders left it.</param>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <returns>The endpoint to read.</returns>
    protected virtual ValueTask<string> ResolveEndPointAsync(
        Configuration configuration,
        string endPoint,
        CancellationToken cancellationToken)
    {
        return EndPointResolver is null
            ? ValueTask.FromResult(endPoint)
            : EndPointResolver(configuration, endPoint, cancellationToken);
    }

    /// <summary>
    /// Reads a response body that is not JSON.
    /// </summary>
    /// <param name="content">The response body.</param>
    /// <param name="configuration">The endpoint it came from.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the read.</param>
    /// <returns>One property bag per record.</returns>
    protected virtual IAsyncEnumerable<QuickDictionary> ReadContent(
        Stream content,
        Configuration configuration,
        CancellationToken cancellationToken)
    {
        return ContentReader?.Invoke(content, configuration, cancellationToken)
            ?? AsyncEnumerable.Empty<QuickDictionary>();
    }

    /// <summary>
    /// Runs the retrieval, writing every entity it resolves into the channel.
    /// A failure aborts the retrieval but leaves the entities already produced valid: the error
    /// is recorded on <see cref="HadErrors"/> so that the watermark is withheld.
    /// </summary>
    /// <param name="writer">The channel the entities are written to.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the retrieval.</param>
    /// <returns>A task that completes when the channel has been closed.</returns>
    private async Task ProduceAsync(ChannelWriter<IEntity> writer, CancellationToken cancellationToken)
    {
        try
        {
            await EnumerateAsync(_configuration!, null, writer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _hadErrors = true;
            Logger.LogError(ex, "RESTful retrieval failed.");
        }
        finally
        {
            writer.Complete();
        }
    }

    /// <summary>
    /// Reads one endpoint and either writes its objects to the channel or, when it has children,
    /// reads those for each object instead.
    /// </summary>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <param name="parent">The object this endpoint hangs off, or <c>null</c> at the root.</param>
    /// <param name="writer">The channel the entities are written to.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the read.</param>
    /// <returns>A task that completes when the endpoint has been exhausted.</returns>
    private async Task EnumerateAsync(
        Configuration configuration,
        Entity? parent,
        ChannelWriter<IEntity> writer,
        CancellationToken cancellationToken)
    {
        string requestUri = await BuildRequestUriAsync(configuration, parent, cancellationToken)
            .ConfigureAwait(false);

        Configuration[] children = configuration.Children ?? [];

        // The gate is local to this level. A shared one would deadlock a nested read: a child
        // holding the only permit would wait forever for a grandchild to be let through.
        using SemaphoreSlim throttle = new SemaphoreSlim(
            Math.Max(1, configuration.MaxDegreeOfParallelism));

        List<Task> pending = [];

        await foreach (RestPage page in _restClient!
            .ReadAsync(requestUri, configuration, ReadsContent ? ReadContent : null, cancellationToken)
            .ConfigureAwait(false))
        {
            foreach (QuickDictionary raw in page.Entries)
            {
                QuickDictionary? shaped = TransformEntry(ApplyValueHandlers(raw), configuration);

                if (shaped is null)
                {
                    continue;
                }

                MergeConfiguredProperties(shaped, configuration);
                TrackOffset(shaped, configuration);

                Entity entity = CreateEntity(shaped, configuration);

                if (parent is not null)
                {
                    MergeParent(entity, parent, configuration);
                }

                if (children.Length == 0)
                {
                    await writer.WriteAsync(entity, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                foreach (Configuration child in children)
                {
                    // The gate is taken here rather than inside the read, so that the enumeration
                    // itself pauses once the child reads are saturated instead of queueing a task
                    // per object ahead of them.
                    await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);

                    pending.Add(ResolveChildAsync(child, entity, throttle, writer, cancellationToken));
                }
            }

            // A child read reports its own failures, so a completed task holds nothing left to
            // observe and dropping it keeps the list from growing with the result set.
            pending.RemoveAll(task => task.IsCompleted);
        }

        await Task.WhenAll(pending).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one child endpoint for a single parent object.
    /// </summary>
    /// <param name="configuration">The child endpoint configuration.</param>
    /// <param name="parent">The object the child endpoint hangs off.</param>
    /// <param name="throttle">
    /// The gate bounding how many of these reads run at once. It is entered by the caller and
    /// released here, so that the enumeration blocks rather than queueing reads ahead of it.
    /// </param>
    /// <param name="writer">The channel the entities are written to.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the read.</param>
    /// <returns>A task that completes when the child endpoint has been exhausted.</returns>
    private async Task ResolveChildAsync(
        Configuration configuration,
        Entity parent,
        SemaphoreSlim throttle,
        ChannelWriter<IEntity> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnumerateAsync(configuration, parent, writer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // The retry handler has already exhausted its attempts by the time a failure reaches
            // here, so this object is given up on and the rest of the pass continues.
            _hadErrors = true;
            Logger.LogError(
                ex,
                "Failed to read child endpoint '{EndPoint}' for '{Identifier}'.",
                configuration.EndPoint,
                parent.Identifier);
        }
        finally
        {
            throttle.Release();
        }
    }

    /// <summary>
    /// Applies the value handlers to every property of a record.
    /// </summary>
    /// <param name="properties">The record.</param>
    /// <returns>The record, with each value replaced by what its handler returned.</returns>
    private QuickDictionary ApplyValueHandlers(QuickDictionary properties)
    {
        if (_valueHandlers.Count == 0 && ValueHandler is null)
        {
            return properties;
        }

        // The keys are snapshotted because assigning through the indexer invalidates an
        // enumerator over the same dictionary.
        string[] keys = properties.Keys.ToArray();

        foreach (string key in keys)
        {
            properties[key] = ResolveValue(key, properties[key]);
        }

        return properties;
    }

    /// <summary>
    /// Projects a record onto an entity.
    /// </summary>
    /// <param name="properties">The record.</param>
    /// <param name="configuration">The endpoint it came from.</param>
    /// <returns>The entity.</returns>
    private Entity CreateEntity(QuickDictionary properties, Configuration configuration)
    {
        return new Entity(ResolveIdentity(properties, configuration), properties)
        {
            State = ResolveState(properties, configuration)
        };
    }

    /// <summary>
    /// Adds the properties an endpoint stamps onto everything it produces: a value written as a
    /// placeholder copies another property of the same record, which renames it, and any other
    /// value is a constant that tags the record with where it came from.
    /// </summary>
    /// <param name="properties">The record.</param>
    /// <param name="configuration">The endpoint it came from.</param>
    private static void MergeConfiguredProperties(QuickDictionary properties, Configuration configuration)
    {
        if (configuration.Properties is null)
        {
            return;
        }

        foreach (KeyValuePair<string, object> property in configuration.Properties)
        {
            if (properties.ContainsKey(property.Key))
            {
                continue;
            }

            string? source = ReadPlaceholder(property.Value?.ToString());

            properties[property.Key] = source is not null
                ? properties.TryGetValue(source, out object? value) ? value : null
                : property.Value;
        }
    }

    /// <summary>
    /// Stamps a child record with what it inherits from the object it hangs off.
    /// </summary>
    /// <param name="entity">The child entity.</param>
    /// <param name="parent">The parent entity.</param>
    /// <param name="configuration">The child endpoint configuration.</param>
    private static void MergeParent(Entity entity, Entity parent, Configuration configuration)
    {
        if (configuration.ParentIdentityProperty is not null)
        {
            entity[configuration.ParentIdentityProperty] = parent.Identifier;
        }

        if (!configuration.InheritParentProperties)
        {
            return;
        }

        // The child's own properties win: an inherited value is context, not a correction.
        foreach (KeyValuePair<string, object?> property in parent.Properties)
        {
            if (!entity.Properties.ContainsKey(property.Key))
            {
                entity[property.Key] = property.Value;
            }
        }
    }

    /// <summary>
    /// Builds the absolute URL of an endpoint.
    /// </summary>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <param name="parent">The object this endpoint hangs off, or <c>null</c> at the root.</param>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <returns>The absolute URL.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the configuration names no service root.
    /// </exception>
    private async ValueTask<string> BuildRequestUriAsync(
        Configuration configuration,
        Entity? parent,
        CancellationToken cancellationToken)
    {
        string baseUrl = _configuration!.GetBaseUrl()
            ?? throw new InvalidOperationException(
                "The configuration names neither BaseUrl nor Host, so no service root can be resolved.");

        string endPoint = ApplyPlaceholders(configuration.EndPoint ?? string.Empty, parent);

        endPoint = await ResolveEndPointAsync(configuration, endPoint, cancellationToken)
            .ConfigureAwait(false);

        StringBuilder builder = new StringBuilder(baseUrl.TrimEnd('/'));

        if (!string.IsNullOrEmpty(endPoint))
        {
            builder.Append('/').Append(endPoint.Trim('/'));
        }

        return AppendQuery(builder, configuration.Parameters).ToString();
    }

    /// <summary>
    /// Substitutes the placeholders of a template with properties of the parent object, which is
    /// how a child endpoint is addressed per parent.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <param name="parent">The parent object, or <c>null</c>.</param>
    /// <returns>The substituted template.</returns>
    private static string ApplyPlaceholders(string template, Entity? parent)
    {
        if (parent is null || !template.Contains(Configuration.PlaceholderPrefix, StringComparison.Ordinal))
        {
            return template;
        }

        StringBuilder builder = new StringBuilder(template);

        foreach (KeyValuePair<string, object?> property in parent.Properties)
        {
            builder.Replace(
                $"{Configuration.PlaceholderPrefix}{property.Key}{Configuration.PlaceholderSuffix}",
                Uri.EscapeDataString(property.Value?.ToString() ?? string.Empty));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns the property a placeholder names, or <c>null</c> when the value is not one.
    /// </summary>
    /// <param name="value">The configured value.</param>
    /// <returns>The property name, or <c>null</c>.</returns>
    private static string? ReadPlaceholder(string? value)
    {
        if (value is null
            || !value.StartsWith(Configuration.PlaceholderPrefix, StringComparison.Ordinal)
            || !value.EndsWith(Configuration.PlaceholderSuffix, StringComparison.Ordinal))
        {
            return null;
        }

        return value[
            Configuration.PlaceholderPrefix.Length..^Configuration.PlaceholderSuffix.Length];
    }

    /// <summary>
    /// Appends the query parameters to a URL under construction.
    /// </summary>
    /// <remarks>
    /// Names are escaped along with values, which turns a leading <c>$</c> into <c>%24</c>. The
    /// Kiota parameter-name decoding handler restores it on the way out, so a configuration is
    /// free to carry any parameter name without the escaping corrupting an OData system query
    /// option.
    /// </remarks>
    /// <param name="builder">The URL under construction.</param>
    /// <param name="parameters">The parameters to append, or <c>null</c>.</param>
    /// <returns>The URL under construction.</returns>
    private static StringBuilder AppendQuery(StringBuilder builder, SortedList<string, object>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return builder;
        }

        char separator = '?';

        foreach (KeyValuePair<string, object> parameter in parameters)
        {
            string? value = parameter.Value?.ToString();

            if (value is null)
            {
                continue;
            }

            builder
                .Append(separator)
                .Append(Uri.EscapeDataString(parameter.Key))
                .Append('=')
                .Append(Uri.EscapeDataString(value));

            separator = '&';
        }

        return builder;
    }

    /// <summary>
    /// Adds the property projection to the query parameters of an endpoint.
    /// </summary>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <param name="properties">The property names the consumer asked for.</param>
    private void ApplyProjection(Configuration configuration, string[] properties)
    {
        if (string.IsNullOrEmpty(configuration.PropertiesParameter))
        {
            return;
        }

        // A projection written into the configuration is what the operator meant; it is not
        // second-guessed from what the consumer happens to ask for.
        if (configuration.Parameters?.ContainsKey(configuration.PropertiesParameter) == true)
        {
            return;
        }

        string[] selected = configuration.PropertiesToLoad
            ?? (configuration.PropertiesToIgnore is null
                ? properties
                : properties
                    .Where(property => !configuration.PropertiesToIgnore.Contains(
                        property, StringComparer.OrdinalIgnoreCase))
                    .ToArray());

        if (selected.Length == 0 || selected.Contains(AllProperties, StringComparer.Ordinal))
        {
            return;
        }

        HashSet<string> projection = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);

        // The properties the retrieval itself reads have to come back whether or not the consumer
        // asked for them: without the watermark property a delta run cannot advance, and without
        // the deletion marker a removed object arrives looking like a live one.
        Add(projection, configuration.IdentityProperty ?? _configuration?.IdentityProperty);
        Add(projection, configuration.DeletedProperty ?? _configuration?.DeletedProperty);

        if (configuration.Delta || _configuration?.Delta == true)
        {
            Add(projection, configuration.OffsetProperty ?? _configuration?.OffsetProperty);
        }

        // Withholding a property is the last word, including over the three added just above. An
        // API that always returns its object reference and rejects a request asking for it needs
        // that reference named as the identity and withheld from the projection at the same time.
        if (configuration.PropertiesToIgnore is not null)
        {
            projection.ExceptWith(configuration.PropertiesToIgnore);
        }

        if (projection.Count == 0)
        {
            return;
        }

        configuration.AddParameter(
            configuration.PropertiesParameter,
            string.Format(
                CultureInfo.InvariantCulture,
                configuration.PropertiesFormat,
                string.Join(configuration.PropertiesSeparator, projection)),
            false);
    }

    /// <summary>
    /// Adds the delta filter to the query parameters of an endpoint.
    /// </summary>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <param name="offset">The watermark this run resumes from, or <c>null</c> for a full pass.</param>
    private void ApplyFilter(Configuration configuration, DateTime? offset)
    {
        if (string.IsNullOrEmpty(configuration.FilterParameter))
        {
            return;
        }

        // The filter this endpoint was configured with is captured before the first run writes
        // over it. Every subsequent run combines from that rather than from what the previous run
        // left behind, which would otherwise nest one delta filter inside the next.
        if (!_configuredFilters.TryGetValue(configuration, out object? configured))
        {
            configuration.Parameters?.TryGetValue(configuration.FilterParameter, out configured);
            _configuredFilters[configuration] = configured;
        }

        if (offset is null
            || !configuration.Delta
            || string.IsNullOrEmpty(configuration.FilterFormat)
            || string.IsNullOrEmpty(configuration.OffsetProperty))
        {
            return;
        }

        string filter = string.Format(
            CultureInfo.InvariantCulture,
            configuration.FilterFormat,
            configuration.OffsetProperty,
            offset.Value.ToString(configuration.OffsetFormat, CultureInfo.InvariantCulture));

        // A filter already in the configuration scopes what this connector reads at all, so the
        // delta filter narrows it rather than replacing it.
        if (configured is not null)
        {
            filter = string.Format(
                CultureInfo.InvariantCulture,
                configuration.FilterCombineFormat,
                configured,
                filter);
        }

        configuration.AddParameter(configuration.FilterParameter, filter);
    }

    /// <summary>
    /// Adds the paging parameters to the query parameters of an endpoint.
    /// </summary>
    /// <param name="configuration">The endpoint configuration.</param>
    private static void ApplyPaging(Configuration configuration)
    {
        PaginationSettings? pagination = configuration.Pagination;

        if (pagination is null)
        {
            return;
        }

        if (pagination.PageSize > 0 && !string.IsNullOrEmpty(pagination.PageSizeParameter))
        {
            configuration.AddParameter(pagination.PageSizeParameter, pagination.PageSize, false);
        }

        switch (pagination.Mode)
        {
            case PaginationMode.Offset when !string.IsNullOrEmpty(pagination.OffsetParameter):
                configuration.AddParameter(pagination.OffsetParameter, 0, false);
                break;

            case PaginationMode.Page when !string.IsNullOrEmpty(pagination.PageParameter):
                configuration.AddParameter(pagination.PageParameter, pagination.FirstPage, false);
                break;
        }
    }

    /// <summary>
    /// Advances the running watermark past a record.
    /// </summary>
    /// <param name="properties">The record.</param>
    /// <param name="configuration">The endpoint it came from.</param>
    private void TrackOffset(QuickDictionary properties, Configuration configuration)
    {
        if (_configuration?.Delta != true && !configuration.Delta)
        {
            return;
        }

        string? offsetProperty = configuration.OffsetProperty ?? _configuration?.OffsetProperty;

        if (offsetProperty is null
            || !properties.TryGetValue(offsetProperty, out object? value)
            || !TryReadDateTime(value, configuration.OffsetFormat, out DateTime modified))
        {
            return;
        }

        // Child endpoints advance the watermark concurrently, so the comparison and the
        // assignment have to happen together.
        lock (_offsetLock)
        {
            if (_offset is null || modified > _offset)
            {
                _offset = modified;
            }
        }
    }

    /// <summary>
    /// Reads a modification timestamp out of whatever the API returned for it.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="format">The format the configuration renders the watermark with.</param>
    /// <param name="modified">When this method returns <c>true</c>, the timestamp in UTC.</param>
    /// <returns><c>true</c> when a timestamp was read; otherwise, <c>false</c>.</returns>
    private static bool TryReadDateTime(object? value, string format, out DateTime modified)
    {
        modified = default;

        switch (value)
        {
            case null:
                return false;

            case DateTime dateTime:
                modified = dateTime.ToUniversalTime();
                return true;

            case DateTimeOffset dateTimeOffset:
                modified = dateTimeOffset.UtcDateTime;
                return true;

            // A JSON number in a timestamp property is Unix time. Values large enough to be
            // milliseconds are read as such, which is the only way to tell the two apart without
            // being told.
            case long epoch:
                modified = epoch > 99999999999L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
                return true;
        }

        string? text = value.ToString();

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (DateTime.TryParseExact(
            text,
            format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out modified))
        {
            return true;
        }

        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out modified);
    }

    /// <summary>
    /// Returns the watermark this run resumes from.
    /// </summary>
    /// <param name="endPoint">The endpoint this run reads.</param>
    /// <returns>The watermark, or <c>null</c> when the run is a full pass.</returns>
    private DateTime? ResolveOffset(string endPoint)
    {
        if (_configuration?.Delta != true || _state.Offset is null)
        {
            return null;
        }

        if (_state.EndPoint is not null
            && !string.Equals(_state.EndPoint, endPoint, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation(
                "The stored watermark was recorded for endpoint '{Previous}', not '{Current}'. "
                + "It does not carry over, so this retrieval performs a full pass.",
                _state.EndPoint,
                endPoint);

            return null;
        }

        return _state.Offset;
    }

    /// <summary>
    /// Adds a property name to a projection when it is configured.
    /// </summary>
    /// <param name="projection">The projection under construction.</param>
    /// <param name="propertyName">The property name, or <c>null</c>.</param>
    private static void Add(HashSet<string> projection, string? propertyName)
    {
        if (!string.IsNullOrEmpty(propertyName))
        {
            projection.Add(propertyName);
        }
    }

    /// <summary>
    /// Serializes the watermark to a byte array.
    /// </summary>
    /// <returns>The serialized watermark.</returns>
    private byte[] SerializeState()
    {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_state));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the resources held by this provider.
    /// </summary>
    /// <param name="disposing">
    /// <c>true</c> when called from <see cref="Dispose()"/>; <c>false</c> from a finalizer.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (disposing)
        {
            // The decryptor belongs to the synchronization pipeline and outlives this provider,
            // so it is deliberately not disposed here.
            _restClient?.Dispose();
            _restClient = null;
        }
    }

    #endregion
}
