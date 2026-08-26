using System.DirectoryServices.Protocols;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.ActiveDirectory;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Types;
using PenguinConverters.Syntra.Provider.ActiveDirectory.Settings;
using PenguinConverters.Syntra.Provider.ActiveDirectory.Source;

namespace PenguinConverters.Syntra.Provider.ActiveDirectory;

/// <summary>
/// Active Directory source provider that retrieves entities via LDAP.
/// Supports full synchronization with configurable search filters and
/// delta synchronization using USN change tracking: a stored watermark, and nothing else,
/// decides whether a run is a delta or a full pass.
/// When relationships are configured, the provider streams parent/child link entities
/// instead of objects, sourcing link changes from value-level replication metadata.
/// </summary>
public class Provider : Core.Source.Provider
{
    #region Constants

    /// <summary>
    /// LDAP filter pattern for delta sync using uSNChanged attribute.
    /// </summary>
    public const string LdapFilterPatternChanged = "(&({0})(uSNChanged>={1}))";

    /// <summary>
    /// LDAP filter pattern for deleted objects in delta sync. It wraps the configured
    /// <see cref="Source.Configuration.LdapFilter"/>, which is what a tombstone search must be
    /// scoped by.
    /// </summary>
    /// <remarks>
    /// Retrieval is not wired up: reading tombstones needs all three of a base DN pointing at the
    /// <c>CN=Deleted Objects</c> container, the ShowDeleted control on the request, and read
    /// permission on that container - the last of which is routinely withheld in hardened
    /// environments. This pattern is kept because any implementation of that search still has to
    /// compose the configured filter this way.
    /// </remarks>
    public const string LdapFilterPatternDeleted = "(&({0})(isDeleted=*))";

    /// <summary>
    /// LDAP filter pattern for memberOf relationship resolution.
    /// </summary>
    public const string LdapFilterPatternMemberOf = "(&({0})(memberOf={1}))";

    /// <summary>
    /// The distinguishedName attribute used as the bind attribute.
    /// </summary>
    public const string LdapBindAttribute = "distinguishedName";

    /// <summary>
    /// The uSNChanged attribute name.
    /// </summary>
    public const string LdapUsnChangedAttribute = "uSNChanged";

    /// <summary>
    /// The uSNCreated attribute name.
    /// </summary>
    public const string LdapUsnCreatedAttribute = "uSNCreated";

    /// <summary>
    /// LDAP filter used for base-scoped lookups of a single object.
    /// </summary>
    public const string LdapFilterPatternObject = "(objectClass=*)";

    /// <summary>
    /// The RootDSE attribute holding the distinguished name of the server object
    /// of the domain controller answering the query.
    /// </summary>
    public const string LdapServerNameAttribute = "serverName";

    /// <summary>
    /// The RootDSE attribute holding the highest USN committed on the domain controller.
    /// </summary>
    public const string LdapHighestCommittedUsnAttribute = "highestCommittedUSN";

    /// <summary>
    /// The dNSHostName attribute of a domain controller.
    /// </summary>
    public const string LdapDnsHostNameAttribute = "dNSHostName";

    /// <summary>
    /// The objectGUID attribute used to identify a domain controller across sync runs.
    /// </summary>
    public const string LdapObjectGuidAttribute = "objectGUID";

    /// <summary>
    /// The property holding the distinguished name of the object a relationship originates from.
    /// </summary>
    public const string RelationshipParentProperty = "parent";

    /// <summary>
    /// The property holding the distinguished name of the object a relationship points at.
    /// </summary>
    public const string RelationshipChildProperty = "child";

    #endregion

    #region Fields

    private Configuration? _configuration;
    private State _state = new();
    private bool _hadErrors;
    private Connection? _connection;
    private IDictionary<string, object>? _server;
    private long _highestObservedUsn;
    private bool _decodersLoaded;

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
    /// Gets or sets the LDAP connection used for directory searches.
    /// Assigned by <see cref="ProviderBuilder"/> during construction.
    /// </summary>
    internal Connection? Connection
    {
        get => _connection;
        set => _connection = value;
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
            Logger.LogError("Active Directory provider configuration is not set.");
            yield break;
        }

        if (_connection is null)
        {
            _hadErrors = true;
            Logger.LogError("Active Directory provider LDAP connection is not established.");
            yield break;
        }

        await EnsureSchemaDecodersAsync(cancellationToken).ConfigureAwait(false);

        HashSet<string> requestedProperties = new HashSet<string>(properties, StringComparer.OrdinalIgnoreCase)
        {
            LdapBindAttribute,
            LdapUsnChangedAttribute,
            LdapUsnCreatedAttribute
        };

        string[] propertyList = requestedProperties.ToArray();

        // The watermark of the previous run drives the delta filter and classifies everything
        // this run produces. A watermark of zero is no watermark: there is nothing to filter
        // against, so the run is a full pass.
        long watermark = _state.HighestCommittedUSN;
        _highestObservedUsn = watermark;

        Logger.LogInformation(
            "Starting {SyncType} retrieval of {Shape} from {BaseDN} with filter '{Filter}'.",
            watermark > 0 ? "delta" : "full",
            HasRelationships() ? "relationships" : "objects",
            _configuration.BaseDN,
            _configuration.LdapFilter);

        // Build LDAP filter: delta uses uSNChanged watermark, full uses configured filter.
        // The configured filter is unwrapped first, because the pattern parenthesises it again.
        string ldapFilter = watermark > 0
            ? string.Format(
                LdapFilterPatternChanged, TrimFilterParentheses(_configuration.LdapFilter), watermark)
            : _configuration.LdapFilter;

        Logger.LogTrace(
            "Executing LDAP search with filter: {Filter} requesting {AttributeCount} attributes: {Attributes}",
            ldapFilter, propertyList.Length, string.Join(", ", propertyList));

        await foreach (IEntity entity in SearchAsync(ldapFilter, propertyList, watermark, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return entity;
        }

        // Update state on success
        if (!_hadErrors)
        {
            // The DC reported its highestCommittedUSN before the search began, so everything
            // committed while the search ran is picked up by the next one. Only when that value
            // is unavailable does the highest uSNChanged observed serve as the watermark.
            long committed = ReadHighestCommittedUsn(_server);

            _state.HighestCommittedUSN = committed > 0 ? committed : _highestObservedUsn;

            if (_server is not null)
            {
                _state.Server = ReadServerValue(_server, LdapDnsHostNameAttribute);
                _state.ServerObjectGuid = ReadServerValue(_server, LdapObjectGuidAttribute);
            }

            RawMetadata = SerializeState();
            Logger.LogInformation(
                "Synchronization state updated successfully. USN watermark {Previous} -> {Current}.",
                watermark, _state.HighestCommittedUSN);
        }
        else
        {
            RawMetadata = null;
            Logger.LogWarning("Errors occurred during retrieval; metadata not updated.");
        }
    }

    /// <summary>
    /// Streams the results of a single paged LDAP search as entities.
    /// A failing page aborts this search only: the error is recorded on
    /// <see cref="HadErrors"/> so the caller withholds the metadata update,
    /// while the entities already produced remain valid.
    /// </summary>
    /// <param name="ldapFilter">The LDAP search filter to execute.</param>
    /// <param name="propertyList">The attributes to request for each entry.</param>
    /// <param name="watermark">The USN watermark of the previous run.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the search.</param>
    /// <returns>An asynchronous stream of entities.</returns>
    private async IAsyncEnumerable<IEntity> SearchAsync(
        string ldapFilter,
        string[] propertyList,
        long watermark,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The enumerator is stepped by hand because C# forbids `yield return` inside a
        // try/catch: trapping the LDAP failure requires the fetch and the yield to sit
        // in separate statements.
        await using IAsyncEnumerator<IDictionary<string, object?>> entries = _connection!
            .RetrieveAsync(ldapFilter, propertyList, _configuration!.BaseDN, false, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            IDictionary<string, object?> attributes;

            try
            {
                if (!await entries.MoveNextAsync().ConfigureAwait(false))
                {
                    break;
                }

                attributes = entries.Current;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _hadErrors = true;
                Logger.LogError(ex, "LDAP search failed for filter '{Filter}'.", ldapFilter);
                break;
            }

            Entity entity = CreateEntity(attributes);

            if (entity.Identifier is not null && HasRelationships())
            {
                List<IEntity> links = [];

                try
                {
                    links = await RetrieveAssignedValuesAsync(entity.Identifier, watermark, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _hadErrors = true;
                    Logger.LogError(
                        ex, "Failed to resolve relationships for '{Identifier}'.", entity.Identifier);
                }

                foreach (IEntity link in links)
                {
                    yield return link;
                }

                continue;
            }

            yield return entity;
        }
    }

    /// <summary>
    /// Projects a raw LDAP entry onto an <see cref="Entity"/>, classifies it against the
    /// watermark of the previous run, and advances the watermark of the current one.
    /// </summary>
    /// <param name="attributes">The decoded attributes of a single directory entry.</param>
    /// <returns>The entity representing the directory entry.</returns>
    private Entity CreateEntity(IDictionary<string, object?> attributes)
    {
        attributes.TryGetValue(LdapBindAttribute, out object? distinguishedName);

        Entity entity = new Entity(distinguishedName?.ToString(), attributes);

        long usnChanged = ReadUsn(attributes, LdapUsnChangedAttribute);
        long usnCreated = ReadUsn(attributes, LdapUsnCreatedAttribute);

        if (usnChanged > _highestObservedUsn)
        {
            _highestObservedUsn = usnChanged;
        }

        // Consumers act on the state, so every entity leaves here classified. An object created
        // at or after the previous watermark is new to the consumer; anything older it has seen
        // before. On a full pass the watermark is zero, which makes the whole result set Created.
        entity.State = usnCreated >= _state.HighestCommittedUSN
            ? EntityState.Created
            : EntityState.Updated;

        return entity;
    }

    /// <summary>
    /// Resolves the configured relationships of a single object into parent/child link entities.
    /// </summary>
    /// <remarks>
    /// A delta pass reads <c>msDS-ReplValueMetaData</c> rather than the link attribute itself.
    /// The attribute only lists the values that are currently present, so a membership removed
    /// since the last run leaves no trace in it; the value metadata keeps one entry per value
    /// ever written, each carrying the local USN of its last change and a version that is odd
    /// while the value is present and even once it has been removed. That is what makes a
    /// removed membership observable without diffing against previously stored state.
    /// </remarks>
    /// <param name="distinguishedName">The distinguished name of the object holding the links.</param>
    /// <param name="watermark">The USN watermark of the previous run.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the retrieval.</param>
    /// <returns>The link entities produced for the object.</returns>
    private async Task<List<IEntity>> RetrieveAssignedValuesAsync(
        string distinguishedName,
        long watermark,
        CancellationToken cancellationToken)
    {
        List<IEntity> links = [];

        if (_configuration?.Relationships is null || _connection is null)
        {
            return links;
        }

        foreach (Relationship relationship in _configuration.Relationships)
        {
            if (string.IsNullOrWhiteSpace(relationship.Attribute))
            {
                continue;
            }

            int resolved = links.Count;

            // Both reads go through range retrieval: a group can hold far more members than a
            // single LDAP response returns.
            if (_configuration.Delta)
            {
                List<object?> values = await _connection
                    .RetrieveCollectionAttributeAsync(
                        distinguishedName, ReplValueMetaData.Attribute, cancellationToken)
                    .ConfigureAwait(false);

                foreach (object? value in values)
                {
                    if (!ReplValueMetaData.TryParse(value?.ToString(), out ReplValueMetaData? metaData))
                    {
                        continue;
                    }

                    if (!string.Equals(
                        metaData!.AttributeName, relationship.Attribute, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Unchanged since the last run: the consumer already has it.
                    if (metaData.LocalChangeUsn <= watermark)
                    {
                        continue;
                    }

                    // The version increments on every write, so it is odd while the value is
                    // present and even once it has been removed.
                    links.Add(CreateLinkEntity(
                        distinguishedName,
                        metaData.ObjectDn,
                        metaData.Version % 2 != 0 ? EntityState.Created : EntityState.Deleted));
                }
            }
            else
            {
                List<object?> values = await _connection
                    .RetrieveCollectionAttributeAsync(
                        distinguishedName, relationship.Attribute, cancellationToken)
                    .ConfigureAwait(false);

                foreach (object? value in values)
                {
                    links.Add(CreateLinkEntity(distinguishedName, value?.ToString(), EntityState.Created));
                }
            }

            Logger.LogTrace(
                "Resolved {Count} '{Attribute}' link(s) for '{Identifier}'.",
                links.Count - resolved, relationship.Attribute, distinguishedName);
        }

        return links;
    }

    /// <summary>
    /// Creates the entity representing a single relationship between two directory objects.
    /// </summary>
    /// <param name="parent">The distinguished name of the object holding the link.</param>
    /// <param name="child">The distinguished name of the object the link points at.</param>
    /// <param name="state">The synchronization state of the link.</param>
    /// <returns>The link entity.</returns>
    private static Entity CreateLinkEntity(string parent, string? child, EntityState state)
    {
        QuickDictionary properties = new QuickDictionary(2, StringComparer.OrdinalIgnoreCase)
        {
            { RelationshipParentProperty, parent },
            { RelationshipChildProperty, child }
        };

        // A slash cannot appear unescaped in a distinguished name, so it separates the two
        // halves of the composite key without ambiguity.
        return new Entity(string.Concat(parent, "/", child), properties)
        {
            State = state
        };
    }

    /// <summary>
    /// Determines whether the configuration declares at least one usable relationship.
    /// </summary>
    /// <returns><c>true</c> when relationships are configured; otherwise, <c>false</c>.</returns>
    private bool HasRelationships()
    {
        return _configuration?.Relationships is not null
            && _configuration.Relationships.Any(
                relationship => !string.IsNullOrWhiteSpace(relationship.Attribute));
    }

    /// <summary>
    /// Loads the attribute decoders from the directory schema once per provider, so that values
    /// are surfaced as the types the schema declares instead of raw strings.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation of the schema query.</param>
    /// <returns>A task that completes when the decoders have been installed.</returns>
    private async Task EnsureSchemaDecodersAsync(CancellationToken cancellationToken)
    {
        if (_decodersLoaded || _connection is null)
        {
            return;
        }

        _decodersLoaded = true;

        SchemaProvider schemaProvider = new SchemaProvider(_connection, Logger);

        Dictionary<string, Func<byte[], object?>> decoders =
            await schemaProvider.GetDecodersAsync(cancellationToken).ConfigureAwait(false);

        foreach (KeyValuePair<string, Func<byte[], object?>> decoder in decoders)
        {
            _connection.EncodersByName[decoder.Key] = decoder.Value;
        }

        // The schema types objectGUID as an octet string, which would surface as raw bytes.
        _connection.EncodersByName[LdapObjectGuidAttribute] = SchemaProvider.DecoderObjectGUID;

        Logger.LogTrace("Installed {Count} attribute decoder(s) from the directory schema.", decoders.Count);
    }

    /// <summary>
    /// Removes one enclosing pair of parentheses from an LDAP filter, so that a filter written
    /// as a complete expression can be embedded into a composing pattern.
    /// </summary>
    /// <param name="ldapFilter">The configured filter.</param>
    /// <returns>The filter without its outermost parentheses.</returns>
    private static string TrimFilterParentheses(string ldapFilter)
    {
        return ldapFilter.Length > 1 && ldapFilter[0] == '(' && ldapFilter[^1] == ')'
            ? ldapFilter[1..^1]
            : ldapFilter;
    }

    /// <summary>
    /// Reads a USN attribute from a directory entry.
    /// </summary>
    /// <param name="attributes">The attributes of the directory entry.</param>
    /// <param name="attributeName">The name of the USN attribute to read.</param>
    /// <returns>The USN value, or <c>0</c> when the attribute is absent or unparsable.</returns>
    private static long ReadUsn(IDictionary<string, object?> attributes, string attributeName)
    {
        if (!attributes.TryGetValue(attributeName, out object? value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            long number => number,
            int number => number,
            _ => long.TryParse(value.ToString(), out long parsed) ? parsed : 0
        };
    }

    /// <summary>
    /// Attempts to find the preferred LDAP server based on the previous synchronization state.
    /// Ensures DC affinity by matching the <see cref="State.ServerObjectGuid"/> from the last sync run.
    /// </summary>
    /// <remarks>
    /// Each configured domain controller is probed in turn for its RootDSE and server object until
    /// one matches the objectGUID recorded by the previous run. The connection is then pinned to the
    /// resolved DC, because a USN watermark is only meaningful on the DC that issued it: falling
    /// through to a replica would silently skip changes that have not replicated yet. When no DC
    /// matches, the newly selected one is adopted and the watermark is reset, so the retrieval that
    /// follows performs a full pass against it.
    ///
    /// This method is synchronous for the same reason as
    /// <see cref="PenguinConverters.Syntra.ActiveDirectory.Connection.OpenLdapConnection"/>: it runs
    /// once while the provider is being built, and each probe is a bind plus a base-scoped lookup of
    /// a single object.
    /// </remarks>
    /// <param name="serverInfo">When this method returns, contains the server metadata if found.</param>
    /// <returns>
    /// <c>true</c> if a domain controller matching the previous state was found;
    /// <c>false</c> if a new DC was selected and the state was reset.
    /// </returns>
    public bool TryGetPreferredLdapServer(out IDictionary<string, object>? serverInfo)
    {
        serverInfo = null;

        try
        {
            Logger.LogTrace("Attempting to locate preferred LDAP server...");

            if (_connection is null)
            {
                Logger.LogWarning("No LDAP connection available to locate the preferred server.");
                return false;
            }

            // A server already resolved during this run is authoritative; re-probing it would
            // only re-read the RootDSE it was built from.
            if (_server is not null)
            {
                serverInfo = _server;
                return MatchesPreviousServer(_server);
            }

            string? previousObjectGuid = _state.ServerObjectGuid;

            if (previousObjectGuid is null)
            {
                Logger.LogInformation("No previous server state found. Selecting first available DC.");
            }
            else
            {
                Logger.LogTrace("Looking for DC with objectGUID '{Guid}'.", previousObjectGuid);
            }

            foreach (string domainController in _connection.DomainControllers.ToArray())
            {
                IDictionary<string, object>? candidate = ReadServerInfo(domainController);

                if (candidate is null)
                {
                    continue;
                }

                _server = candidate;
                serverInfo = candidate;

                if (previousObjectGuid is null)
                {
                    break;
                }

                if (MatchesPreviousServer(candidate))
                {
                    PinConnectionToServer(candidate);
                    Logger.LogInformation(
                        "Preferred domain controller '{Server}' matched the previous state; DC affinity preserved.",
                        ReadServerValue(candidate, LdapDnsHostNameAttribute) ?? domainController);

                    return true;
                }

                Logger.LogTrace(
                    "Domain controller '{Server}' has objectGUID '{Guid}' and does not match the previous state.",
                    domainController,
                    ReadServerValue(candidate, LdapObjectGuidAttribute));
            }

            if (_server is null)
            {
                Logger.LogWarning("None of the configured domain controllers answered the RootDSE query.");
                return false;
            }

            // A different DC means the previous watermark belongs to a USN sequence that does not
            // exist here, so it is discarded rather than reused.
            PinConnectionToServer(_server);

            _state = new State
            {
                HighestCommittedUSN = 0,
                Server = ReadServerValue(_server, LdapDnsHostNameAttribute),
                ServerObjectGuid = ReadServerValue(_server, LdapObjectGuidAttribute)
            };

            serverInfo = _server;

            Logger.LogInformation(
                "Selected domain controller '{Server}'. No watermark carries over to it, "
                + "so this retrieval performs a full pass.",
                _state.Server);

            return false;
        }
        catch (Exception ex)
        {
            _hadErrors = true;
            Logger.LogError(ex, "Failed to locate preferred LDAP server.");
            throw;
        }
    }

    /// <summary>
    /// Reads the RootDSE of a single domain controller and merges in the attributes of the
    /// server object it names, producing the metadata that identifies that DC.
    /// </summary>
    /// <param name="domainController">The host name of the domain controller to probe.</param>
    /// <returns>
    /// The server metadata, or <c>null</c> when the domain controller did not answer.
    /// </returns>
    private IDictionary<string, object>? ReadServerInfo(string domainController)
    {
        using Connection probe = _connection!.Clone();

        probe.DomainControllers.Clear();
        probe.DomainControllers.Add(domainController);

        try
        {
            LdapConnection ldapConnection = probe.OpenLdapConnection();

            SearchRequest rootDseRequest = new SearchRequest(
                null,
                LdapFilterPatternObject,
                SearchScope.Base,
                LdapServerNameAttribute,
                LdapHighestCommittedUsnAttribute,
                LdapDnsHostNameAttribute);

            SearchResponse rootDseResponse = (SearchResponse)ldapConnection.SendRequest(rootDseRequest);

            if (rootDseResponse.Entries.Count == 0)
            {
                Logger.LogWarning("Domain controller '{Server}' returned no RootDSE.", domainController);
                return null;
            }

            SearchResultEntry rootDse = rootDseResponse.Entries[0];

            Dictionary<string, object> server = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string? serverName = ReadAttributeString(rootDse, LdapServerNameAttribute);
            string? dnsHostName = ReadAttributeString(rootDse, LdapDnsHostNameAttribute);

            if (serverName is not null)
            {
                server[LdapServerNameAttribute] = serverName;
            }

            if (long.TryParse(
                ReadAttributeString(rootDse, LdapHighestCommittedUsnAttribute), out long highestCommittedUsn))
            {
                server[LdapHighestCommittedUsnAttribute] = highestCommittedUsn;
            }

            // The RootDSE carries no objectGUID; that lives on the server object in the
            // configuration naming context which the RootDSE points at.
            if (serverName is not null)
            {
                SearchRequest serverRequest = new SearchRequest(
                    serverName,
                    LdapFilterPatternObject,
                    SearchScope.Base,
                    LdapObjectGuidAttribute,
                    LdapDnsHostNameAttribute);

                SearchResponse serverResponse = (SearchResponse)ldapConnection.SendRequest(serverRequest);

                if (serverResponse.Entries.Count > 0)
                {
                    SearchResultEntry serverEntry = serverResponse.Entries[0];

                    Guid? objectGuid = ReadAttributeGuid(serverEntry, LdapObjectGuidAttribute);
                    if (objectGuid.HasValue)
                    {
                        server[LdapObjectGuidAttribute] = objectGuid.Value.ToString();
                    }

                    dnsHostName ??= ReadAttributeString(serverEntry, LdapDnsHostNameAttribute);
                }
            }

            server[LdapDnsHostNameAttribute] = dnsHostName ?? domainController;

            Logger.LogTrace(
                "Probed domain controller '{Server}': objectGUID '{Guid}', highestCommittedUSN {Usn}.",
                server[LdapDnsHostNameAttribute],
                ReadServerValue(server, LdapObjectGuidAttribute),
                ReadHighestCommittedUsn(server));

            return server;
        }
        catch (LdapException ex)
        {
            Logger.LogWarning(ex, "Domain controller '{Server}' could not be probed.", domainController);
            return null;
        }
        catch (DirectoryOperationException ex)
        {
            Logger.LogWarning(ex, "Domain controller '{Server}' rejected the RootDSE query.", domainController);
            return null;
        }
    }

    /// <summary>
    /// Determines whether the supplied server is the one recorded by the previous run.
    /// </summary>
    /// <param name="server">The server metadata to compare.</param>
    /// <returns><c>true</c> when the objectGUID matches the previous state; otherwise <c>false</c>.</returns>
    private bool MatchesPreviousServer(IDictionary<string, object> server)
    {
        string? objectGuid = ReadServerValue(server, LdapObjectGuidAttribute);

        return objectGuid is not null
            && string.Equals(objectGuid, _state.ServerObjectGuid, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Restricts the connection to the resolved domain controller, so that every subsequent
    /// search runs against the DC the USN watermark belongs to.
    /// </summary>
    /// <param name="server">The resolved server metadata.</param>
    private void PinConnectionToServer(IDictionary<string, object> server)
    {
        string? dnsHostName = ReadServerValue(server, LdapDnsHostNameAttribute);

        if (_connection is null || dnsHostName is null)
        {
            return;
        }

        _connection.DomainControllers.Clear();
        _connection.DomainControllers.Add(dnsHostName);
    }

    /// <summary>
    /// Reads a server metadata value as a string.
    /// </summary>
    /// <param name="server">The server metadata.</param>
    /// <param name="key">The metadata key to read.</param>
    /// <returns>The value, or <c>null</c> when absent.</returns>
    private static string? ReadServerValue(IDictionary<string, object> server, string key)
    {
        return server.TryGetValue(key, out object? value) ? value?.ToString() : null;
    }

    /// <summary>
    /// Reads the highest committed USN captured from a domain controller's RootDSE.
    /// </summary>
    /// <param name="server">The server metadata, or <c>null</c>.</param>
    /// <returns>The USN, or <c>0</c> when it was not captured.</returns>
    private static long ReadHighestCommittedUsn(IDictionary<string, object>? server)
    {
        if (server is null || !server.TryGetValue(LdapHighestCommittedUsnAttribute, out object? value))
        {
            return 0;
        }

        return value is long usn ? usn : 0;
    }

    /// <summary>
    /// Reads the first value of a directory attribute as a string.
    /// </summary>
    /// <param name="entry">The directory entry to read from.</param>
    /// <param name="attributeName">The attribute to read.</param>
    /// <returns>The first value, or <c>null</c> when the attribute is absent or empty.</returns>
    private static string? ReadAttributeString(SearchResultEntry entry, string attributeName)
    {
        DirectoryAttribute? attribute = entry.Attributes[attributeName];

        if (attribute is null || attribute.Count == 0)
        {
            return null;
        }

        string[] values = (string[])attribute.GetValues(typeof(string));

        return values.Length > 0 ? values[0] : null;
    }

    /// <summary>
    /// Reads the first value of a directory attribute as a <see cref="Guid"/>.
    /// The value is requested as raw bytes, because the string projection of a binary
    /// attribute is lossy.
    /// </summary>
    /// <param name="entry">The directory entry to read from.</param>
    /// <param name="attributeName">The attribute to read.</param>
    /// <returns>The GUID, or <c>null</c> when the attribute is absent or not a GUID.</returns>
    private static Guid? ReadAttributeGuid(SearchResultEntry entry, string attributeName)
    {
        DirectoryAttribute? attribute = entry.Attributes[attributeName];

        if (attribute is null || attribute.Count == 0)
        {
            return null;
        }

        byte[][] values = (byte[][])attribute.GetValues(typeof(byte[]));

        return values.Length > 0 && values[0].Length == 16 ? new Guid(values[0]) : null;
    }

    /// <summary>
    /// Deserializes the raw configuration bytes and applies them to this provider.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when deserialization fails.</exception>
    internal void DeserializeAndApplyConfiguration()
    {
        _configuration = DeserializeConfiguration<Configuration>()
            ?? throw new InvalidOperationException("Failed to deserialize Active Directory provider configuration.");
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
    /// Serializes the current synchronization state to a byte array.
    /// </summary>
    private byte[] SerializeState()
    {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_state));
    }

    #endregion
}
