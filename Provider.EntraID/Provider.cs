using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Provider.EntraID.Settings;
using PenguinConverters.Syntra.Provider.EntraID.Source;

namespace PenguinConverters.Syntra.Provider.EntraID;

/// <summary>
/// Entra ID (Azure AD) source provider that retrieves entities via the Microsoft Graph API.
/// Supports full synchronization by fetching all objects from the configured endpoint, and
/// delta synchronization using Graph delta tokens.
/// When a relationship is configured, the provider streams the objects behind that relationship
/// - group members, application owners - instead of the objects the endpoint returns, stamping
/// each with the identity of the object it hangs off.
/// </summary>
public class Provider : Core.Source.Provider
{
    #region Constants

    /// <summary>
    /// The Graph property holding the unique identity of an object.
    /// </summary>
    public const string PropertyIdentity = "id";

    /// <summary>
    /// The property stamped onto a relationship object with the identity of the object it
    /// hangs off, so that a consumer can key the link on both ends.
    /// </summary>
    public const string PropertyObjectId = "objectId";

    /// <summary>
    /// The property stamped onto every object with the configured tenant, so that a table fed
    /// from several tenants stays keyed.
    /// </summary>
    public const string PropertyTenantId = "tenantId";

    /// <summary>
    /// The OData annotation Graph sets on a delta response entry that has been removed.
    /// </summary>
    public const string PropertyRemoved = "@removed";

    /// <summary>
    /// The endpoint segment that turns a Graph query into a delta query.
    /// </summary>
    public const string DeltaSegment = "delta";

    /// <summary>
    /// The OData query parameter carrying the delta token of the previous run.
    /// </summary>
    public const string DeltaTokenParameter = "$deltatoken";

    /// <summary>
    /// The OData query parameter carrying the property projection.
    /// </summary>
    public const string SelectParameter = "$select";

    /// <summary>
    /// Number of entities buffered between the retrieval and the consumer reading them.
    /// </summary>
    private const int ChannelCapacity = 1024;

    #endregion

    #region Fields

    private Configuration? _configuration;
    private State _state = new();
    private GraphClient? _graphClient;
    private string? _deltaLink;
    private bool _hadErrors;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the provider configuration.
    /// </summary>
    internal Configuration? Configuration
    {
        get => _configuration;
        set => _configuration = value;
    }

    /// <summary>
    /// Gets or sets the Graph reader used for retrieval.
    /// Assigned by <see cref="ProviderBuilder"/> during construction.
    /// </summary>
    internal GraphClient? GraphClient
    {
        get => _graphClient;
        set => _graphClient = value;
    }

    /// <summary>
    /// Gets or sets the synchronization state.
    /// </summary>
    internal State State
    {
        get => _state;
        set => _state = value;
    }

    /// <summary>
    /// Gets a value indicating whether errors occurred during retrieval.
    /// </summary>
    public bool HadErrors => _hadErrors;

    #endregion

    #region Methods

    /// <inheritdoc />
    public override async IAsyncEnumerable<IEntity> RetrieveAsync(
        IEnumerable<string> properties,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_configuration is null)
        {
            Logger.LogError("Entra ID provider configuration is not set.");
            yield break;
        }

        if (_graphClient is null)
        {
            _hadErrors = true;
            Logger.LogError("Entra ID provider Graph client is not established.");
            yield break;
        }

        string endPoint = ResolveEndPoint(_configuration);
        string? deltaToken = ResolveDeltaToken(endPoint);

        PrepareParameters(properties, deltaToken);

        Logger.LogInformation(
            "Starting {SyncType} retrieval of {Shape} from endpoint '{EndPoint}' for tenant '{TenantId}'.",
            _configuration.Delta ? "delta" : "full",
            _configuration.Relationship is null ? "objects" : "relationships",
            endPoint,
            _configuration.TenantId);

        // Retrieval runs on its own task so that relationship endpoints can be read concurrently
        // while the consumer drains what has already been resolved. The channel is bounded, so a
        // slow consumer throttles the retrieval instead of accumulating the whole result set.
        Channel<IEntity> channel = Channel.CreateBounded<IEntity>(
            new BoundedChannelOptions(ChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

        using CancellationTokenSource retrieval = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

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
            Logger.LogWarning("Errors occurred during retrieval; metadata not updated.");
            yield break;
        }

        // The delta link is issued on the last page of a delta response and carries the token
        // that scopes the next run. A full pass produces none, which resets the watermark.
        _state = new State
        {
            DeltaToken = ExtractDeltaToken(_deltaLink),
            EndPoint = endPoint
        };

        RawMetadata = SerializeState();

        Logger.LogInformation(
            "Entra ID retrieval completed. Delta token {State}.",
            _state.DeltaToken is null ? "not issued" : "stored");
    }

    /// <summary>
    /// Deserializes the raw configuration bytes and applies them to this provider.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when deserialization fails.</exception>
    internal void DeserializeAndApplyConfiguration()
    {
        _configuration = DeserializeConfiguration<Configuration>()
            ?? throw new InvalidOperationException("Failed to deserialize Entra ID provider configuration.");
    }

    /// <summary>
    /// Initializes the synchronization state from the raw metadata bytes.
    /// </summary>
    internal void InitializeState()
    {
        _state = RawMetadata is not null && Deserializer is not null
            ? DeserializeMetadata<State>() ?? new State()
            : new State();
    }

    /// <summary>
    /// Discloses a configured secret on behalf of <see cref="ProviderBuilder"/>, which builds the
    /// Graph credential but has no access to the protected disclosure helper this provider
    /// inherits.
    /// </summary>
    /// <param name="secret">The configured secret, or <c>null</c> if the setting was omitted.</param>
    /// <param name="plaintext">
    /// When this method returns <c>true</c>, the disclosed characters. The caller owns the array
    /// and should clear it once the credential has been used.
    /// </param>
    /// <returns><c>true</c> if the value was disclosed; otherwise, <c>false</c>.</returns>
    internal bool TryDiscloseSecret(Secret? secret, out char[] plaintext)
    {
        return TryDisclose(secret, out plaintext);
    }

    /// <summary>
    /// Runs the retrieval, writing every entity it resolves into the channel.
    /// A failure aborts the retrieval but leaves the entities already produced valid: the error
    /// is recorded on <see cref="HadErrors"/> so the caller withholds the metadata update.
    /// </summary>
    /// <param name="writer">The channel the entities are written to.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the retrieval.</param>
    /// <returns>A task that completes when the channel has been closed.</returns>
    private async Task ProduceAsync(ChannelWriter<IEntity> writer, CancellationToken cancellationToken)
    {
        try
        {
            await EnumerateAsync(writer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _hadErrors = true;
            Logger.LogError(ex, "Entra ID retrieval failed.");
        }
        finally
        {
            writer.Complete();
        }
    }

    /// <summary>
    /// Enumerates the configured endpoint and resolves each object into the entities the
    /// consumer receives.
    /// </summary>
    /// <param name="writer">The channel the entities are written to.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the retrieval.</param>
    /// <returns>A task that completes when the endpoint has been exhausted.</returns>
    private async Task EnumerateAsync(ChannelWriter<IEntity> writer, CancellationToken cancellationToken)
    {
        Configuration configuration = _configuration!;
        Configuration? relationship = configuration.Relationship;

        using SemaphoreSlim throttle = new SemaphoreSlim(Math.Max(1, configuration.MaxDegreeOfParallelism));

        List<Task> pending = [];

        await foreach (GraphPage page in _graphClient!
            .ReadAsync(BuildRequestUri(configuration), configuration.HttpHeaders, cancellationToken)
            .ConfigureAwait(false))
        {
            if (page.DeltaLink is not null)
            {
                _deltaLink = page.DeltaLink;
            }

            foreach (IDictionary<string, object?> attributes in page.Entries)
            {
                Entity entity = CreateEntity(attributes);

                if (relationship is null)
                {
                    await writer.WriteAsync(StampEntry(entity, null), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // The link changes ride along on the object itself, as "members@delta" does on a
                // delta response, so there is nothing further to request.
                if (relationship.PropertyEndpoint)
                {
                    foreach (Entity nested in ReadNestedProperty(entity, relationship.EndPoint))
                    {
                        await writer.WriteAsync(StampEntry(nested, entity), cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                if (configuration.Delta)
                {
                    // A delta pass over a relationship that is not carried as a property observes
                    // only the disappearance of the object holding the links: Graph reports the
                    // object as removed, and every link hanging off it goes with it. Additions and
                    // removals of individual links need the property form above.
                    if (entity.State == EntityState.Deleted)
                    {
                        await writer.WriteAsync(StampEntry(entity, entity), cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                if (entity.Identifier is null)
                {
                    continue;
                }

                // A full pass has to read the relationship endpoint of every object. Those reads
                // are independent, so they overlap up to MaxDegreeOfParallelism rather than
                // stalling the enumeration one object at a time. The gate is taken here rather
                // than inside the read, so that the enumeration itself pauses once the reads are
                // saturated instead of queueing a task per object ahead of them.
                await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);

                pending.Add(ResolveRelationshipAsync(entity, relationship, throttle, writer, cancellationToken));
            }

            // ResolveRelationshipAsync reports its own failures, so a completed task holds nothing
            // left to observe and dropping it keeps the list from growing with the result set.
            pending.RemoveAll(task => task.IsCompleted);
        }

        await Task.WhenAll(pending).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the relationship endpoint of a single object and writes every object behind it to
    /// the channel, stamped with the identity of the object it hangs off.
    /// </summary>
    /// <param name="entity">The object holding the relationship.</param>
    /// <param name="relationship">The relationship configuration.</param>
    /// <param name="throttle">
    /// The gate bounding how many of these reads run at once. It is entered by the caller and
    /// released here, so that the enumeration blocks rather than queueing reads ahead of it.
    /// </param>
    /// <param name="writer">The channel the entities are written to.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the read.</param>
    /// <returns>A task that completes when the relationship has been resolved.</returns>
    private async Task ResolveRelationshipAsync(
        Entity entity,
        Configuration relationship,
        SemaphoreSlim throttle,
        ChannelWriter<IEntity> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            string requestUri = BuildRelationshipUri(_configuration!, relationship, entity.Identifier!);

            int resolved = 0;

            await foreach (GraphPage page in _graphClient!
                .ReadAsync(requestUri, relationship.HttpHeaders ?? _configuration!.HttpHeaders, cancellationToken)
                .ConfigureAwait(false))
            {
                foreach (IDictionary<string, object?> attributes in page.Entries)
                {
                    await writer
                        .WriteAsync(StampEntry(CreateEntity(attributes), entity), cancellationToken)
                        .ConfigureAwait(false);

                    resolved++;
                }
            }

            Logger.LogTrace(
                "Resolved {Count} '{EndPoint}' link(s) for '{Identifier}'.",
                resolved, relationship.EndPoint, entity.Identifier);
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
                "Failed to resolve relationship '{EndPoint}' for '{Identifier}'.",
                relationship.EndPoint, entity.Identifier);
        }
        finally
        {
            throttle.Release();
        }
    }

    /// <summary>
    /// Projects a Graph object onto an <see cref="Entity"/> and classifies it.
    /// </summary>
    /// <param name="attributes">The properties of a single Graph object.</param>
    /// <returns>The entity representing the object.</returns>
    private static Entity CreateEntity(IDictionary<string, object?> attributes)
    {
        attributes.TryGetValue(PropertyIdentity, out object? identity);

        // Consumers act on the state, so every entity leaves here classified. Graph annotates a
        // delta entry it has removed; everything else is an object that is present, which the
        // consumer reconciles against what it already holds.
        return new Entity(identity?.ToString(), attributes)
        {
            State = attributes.ContainsKey(PropertyRemoved) ? EntityState.Deleted : EntityState.Updated
        };
    }

    /// <summary>
    /// Stamps the properties a consumer keys on but Graph does not return: the identity of the
    /// parent object for a relationship, and the tenant the object was read from.
    /// </summary>
    /// <param name="entity">The entity to stamp.</param>
    /// <param name="parent">The object the entity hangs off, or <c>null</c> for a plain object.</param>
    /// <returns>The stamped entity.</returns>
    private Entity StampEntry(Entity entity, Entity? parent)
    {
        if (parent is not null)
        {
            entity[PropertyObjectId] = parent.Identifier;
        }

        if (_configuration!.TenantId is not null)
        {
            entity[PropertyTenantId] = _configuration.TenantId;
        }

        return entity;
    }

    /// <summary>
    /// Reads the objects carried by a multi-valued property of an object, which is how a delta
    /// response reports link changes.
    /// </summary>
    /// <param name="entity">The object holding the property.</param>
    /// <param name="propertyName">The name of the property.</param>
    /// <returns>The objects the property carries.</returns>
    private IEnumerable<Entity> ReadNestedProperty(Entity entity, string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            yield break;
        }

        object? value = entity[propertyName];

        if (value is null)
        {
            yield break;
        }

        foreach (IDictionary<string, object?> attributes in GraphClient.ParseEntries(value.ToString()))
        {
            yield return CreateEntity(attributes);
        }
    }

    /// <summary>
    /// Applies the property projection and the delta token to the configured query parameters.
    /// </summary>
    /// <param name="properties">The property names the consumer asked for.</param>
    /// <param name="deltaToken">The delta token of the previous run, or <c>null</c>.</param>
    private void PrepareParameters(IEnumerable<string> properties, string? deltaToken)
    {
        Configuration configuration = _configuration!;

        string[] requested = properties as string[] ?? properties.ToArray();

        // The projection a delta query was opened with is encoded inside the delta token, so a
        // recurring delta query must carry the token alone: repeating $select is rejected.
        if (!string.IsNullOrEmpty(deltaToken))
        {
            configuration.AddParameter(DeltaTokenParameter, deltaToken);
            configuration.Parameters?.Remove(SelectParameter);
        }
        else if (configuration.Delta || configuration.PropertiesToLoad is not null)
        {
            string[] selected = SelectProperties(
                requested, configuration.PropertiesToLoad, configuration.PropertiesToIgnore);

            if (selected.Length > 0)
            {
                configuration.AddParameter(SelectParameter, string.Join(",", selected));
            }
        }

        if (configuration.Delta && configuration.Relationship is not null)
        {
            string[] selected = SelectProperties(
                requested,
                configuration.Relationship.PropertiesToLoad,
                configuration.Relationship.PropertiesToIgnore);

            if (selected.Length > 0)
            {
                configuration.Relationship.AddParameter(SelectParameter, string.Join(",", selected));
            }
        }

        Logger.LogTrace(
            "Retrieval parameters: {Parameters}",
            JsonSerializer.Serialize(configuration.Parameters));
    }

    /// <summary>
    /// Determines the property projection: the configured list when there is one, otherwise the
    /// properties the consumer asked for less those the configuration withholds.
    /// </summary>
    /// <param name="properties">The property names the consumer asked for.</param>
    /// <param name="propertiesToLoad">The configured projection, or <c>null</c>.</param>
    /// <param name="propertiesToIgnore">The properties to withhold, or <c>null</c>.</param>
    /// <returns>The properties to project.</returns>
    private static string[] SelectProperties(
        string[] properties,
        string[]? propertiesToLoad,
        string[]? propertiesToIgnore)
    {
        if (propertiesToLoad is not null)
        {
            return propertiesToLoad;
        }

        if (propertiesToIgnore is null)
        {
            return properties;
        }

        return properties
            .Where(property => !propertiesToIgnore.Contains(property, StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>
    /// Builds the URL of the configured endpoint, appending the delta segment for a delta pass.
    /// </summary>
    /// <param name="configuration">The provider configuration.</param>
    /// <returns>The absolute request URL.</returns>
    private static string BuildRequestUri(Configuration configuration)
    {
        StringBuilder builder = new StringBuilder()
            .Append(TrimSlashes(configuration.BaseUrl))
            .Append('/')
            .Append(TrimSlashes(ResolveEndPoint(configuration)));

        if (configuration.Delta)
        {
            builder.Append('/').Append(DeltaSegment);
        }

        return AppendQuery(builder, configuration.Parameters).ToString();
    }

    /// <summary>
    /// Builds the URL of a relationship endpoint hanging off a single object.
    /// </summary>
    /// <param name="configuration">The provider configuration.</param>
    /// <param name="relationship">The relationship configuration.</param>
    /// <param name="identity">The identity of the object holding the relationship.</param>
    /// <returns>The absolute request URL.</returns>
    private static string BuildRelationshipUri(
        Configuration configuration,
        Configuration relationship,
        string identity)
    {
        StringBuilder builder = new StringBuilder()
            .Append(TrimSlashes(configuration.BaseUrl))
            .Append('/')
            .Append(TrimSlashes(ResolveEndPoint(configuration)))
            .Append('/')
            .Append(Uri.EscapeDataString(identity));

        if (!string.IsNullOrEmpty(relationship.EndPoint))
        {
            builder.Append('/').Append(TrimSlashes(relationship.EndPoint));
        }

        return AppendQuery(builder, relationship.Parameters).ToString();
    }

    /// <summary>
    /// Appends the OData query parameters to a URL under construction.
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
    /// Returns the configured endpoint, falling back to the directory object collection.
    /// </summary>
    /// <param name="configuration">The provider configuration.</param>
    /// <returns>The endpoint path.</returns>
    private static string ResolveEndPoint(Configuration configuration)
    {
        return string.IsNullOrWhiteSpace(configuration.EndPoint)
            ? Source.Configuration.DefaultEndPoint
            : configuration.EndPoint;
    }

    /// <summary>
    /// Returns the delta token that scopes this run, or <c>null</c> when the run is a full pass.
    /// </summary>
    /// <remarks>
    /// A token belongs to the endpoint that issued it, so one recorded against a different
    /// endpoint is discarded rather than replayed: Graph would answer it with an error, whereas
    /// discarding it produces a full pass that re-establishes the watermark.
    /// </remarks>
    /// <param name="endPoint">The endpoint this run reads.</param>
    /// <returns>The delta token, or <c>null</c>.</returns>
    private string? ResolveDeltaToken(string endPoint)
    {
        if (!_configuration!.Delta || string.IsNullOrEmpty(_state.DeltaToken))
        {
            return null;
        }

        if (_state.EndPoint is not null
            && !string.Equals(_state.EndPoint, endPoint, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation(
                "The stored delta token was issued for endpoint '{Previous}', not '{Current}'. "
                + "It does not carry over, so this retrieval performs a full pass.",
                _state.EndPoint, endPoint);

            return null;
        }

        return _state.DeltaToken;
    }

    /// <summary>
    /// Extracts the delta token from the <c>@odata.deltaLink</c> of a completed delta response.
    /// </summary>
    /// <param name="deltaLink">The delta link, or <c>null</c>.</param>
    /// <returns>The delta token, or <c>null</c> when the link carries none.</returns>
    private static string? ExtractDeltaToken(string? deltaLink)
    {
        if (string.IsNullOrEmpty(deltaLink))
        {
            return null;
        }

        int query = deltaLink.IndexOf('?');

        if (query < 0)
        {
            return null;
        }

        foreach (string parameter in deltaLink[(query + 1)..].Split('&'))
        {
            int assignment = parameter.IndexOf('=');

            if (assignment < 0)
            {
                continue;
            }

            if (string.Equals(
                Uri.UnescapeDataString(parameter[..assignment]),
                DeltaTokenParameter,
                StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parameter[(assignment + 1)..]);
            }
        }

        return null;
    }

    /// <summary>
    /// Removes leading and trailing slashes so that URL segments join without doubling them.
    /// </summary>
    /// <param name="segment">The segment to trim.</param>
    /// <returns>The trimmed segment.</returns>
    private static string TrimSlashes(string segment)
    {
        return segment.Trim('/');
    }

    /// <summary>
    /// Serializes the current synchronization state to a byte array.
    /// </summary>
    /// <returns>The serialized state.</returns>
    private byte[] SerializeState()
    {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_state));
    }

    #endregion
}
