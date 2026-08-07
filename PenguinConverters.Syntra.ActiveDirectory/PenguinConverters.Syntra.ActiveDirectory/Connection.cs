using System.DirectoryServices.Protocols;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Syntra.Core.Settings;
using PenguinConverters.Syntra.Core.Types;

namespace PenguinConverters.Syntra.ActiveDirectory;

/// <summary>
/// Provides LDAP connectivity to Active Directory using <see cref="System.DirectoryServices.Protocols"/>.
/// Supports paged search, attribute modification, range retrieval, and credential validation.
/// </summary>
public class Connection : IDisposable
{
    private LdapConnection? _ldapConnection;
    private bool _disposed;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Connection"/> class.
    /// </summary>
    public Connection()
    {
        _logger = NullLogger.Instance;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Connection"/> class with a logger.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public Connection(ILogger logger)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets or sets the base distinguished name for LDAP searches.
    /// </summary>
    public string BaseSearchDn { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of domain controllers to connect to.
    /// </summary>
    public List<string> DomainControllers { get; set; } = [];

    /// <summary>
    /// Gets or sets the LDAP port number. Defaults to 636 (LDAPS).
    /// </summary>
    public int Port { get; set; } = 636;

    /// <summary>
    /// Gets or sets the page size for paged LDAP search results.
    /// </summary>
    public int PageSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets a value indicating whether to use SSL for the LDAP connection.
    /// </summary>
    public bool SecureSocketLayer { get; set; } = true;

    /// <summary>
    /// Gets or sets the authentication type for the LDAP connection.
    /// </summary>
    public AuthType AuthenticationType { get; set; } = AuthType.Negotiate;

    /// <summary>
    /// Gets or sets the search scope for LDAP queries.
    /// </summary>
    public SearchScope SearchScope { get; set; } = SearchScope.Subtree;

    /// <summary>
    /// Gets or sets the referral chasing behavior.
    /// </summary>
    public ReferralChasingOptions ReferralChasingOptions { get; set; } = ReferralChasingOptions.None;

    /// <summary>
    /// Gets or sets the username credential as a <see cref="ProtectedString"/>.
    /// </summary>
    public ProtectedString? Username { get; set; }

    /// <summary>
    /// Gets or sets the password credential as a <see cref="ProtectedString"/>.
    /// </summary>
    public ProtectedString? Password { get; set; }

    /// <summary>
    /// Gets the dictionary of attribute encoders keyed by attribute name.
    /// Used to decode LDAP attribute values to .NET types.
    /// </summary>
    public Dictionary<string, Func<byte[], object?>> EncodersByName { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the dictionary of attribute encoders keyed by OID or syntax type.
    /// Used as a fallback when no name-specific encoder is found.
    /// </summary>
    public Dictionary<string, Func<byte[], object?>> EncodersByType { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Asynchronously streams directory entries matching the specified LDAP filter using paged search.
    /// Each page is fetched as the previous one is consumed, so callers never wait for the
    /// full result set to materialize.
    /// </summary>
    /// <param name="ldapFilter">The LDAP search filter expression.</param>
    /// <param name="attributesToLoad">The attributes to retrieve for each entry.</param>
    /// <param name="baseDn">
    /// The base DN for the search. If <c>null</c>, <see cref="BaseSearchDn"/> is used.
    /// </param>
    /// <param name="showDeleted">
    /// If <c>true</c>, includes tombstone (deleted) objects in the results.
    /// </param>
    /// <param name="cancellationToken">A token to signal cancellation of the search.</param>
    /// <returns>
    /// An asynchronous stream of dictionaries, each representing a directory entry with its attributes.
    /// </returns>
    public async IAsyncEnumerable<IDictionary<string, object?>> RetrieveAsync(
        string ldapFilter,
        string[] attributesToLoad,
        string? baseDn = null,
        bool showDeleted = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LdapConnection connection = OpenLdapConnection();
        string searchBase = baseDn ?? BaseSearchDn;

        SearchRequest request = new SearchRequest(searchBase, ldapFilter, SearchScope, attributesToLoad);

        PageResultRequestControl pageControl = new PageResultRequestControl(PageSize);
        request.Controls.Add(pageControl);

        if (showDeleted)
        {
            request.Controls.Add(new ShowDeletedControl());
        }

        while (true)
        {
            SearchResponse response = await connection
                .SendRequestAsync<SearchResponse>(request, cancellationToken)
                .ConfigureAwait(false);

            if (response is null)
            {
                yield break;
            }

            foreach (SearchResultEntry entry in response.Entries)
            {
                QuickDictionary result = new QuickDictionary(
                    entry.Attributes.Count,
                    StringComparer.OrdinalIgnoreCase);

                foreach (string attributeName in entry.Attributes.AttributeNames)
                {
                    DirectoryAttribute attribute = entry.Attributes[attributeName];
                    if (attribute.Count == 0)
                    {
                        result[attributeName] = null;
                        continue;
                    }

                    if (attribute.Count == 1)
                    {
                        result[attributeName] = DecodeAttributeValue(attributeName, attribute[0]);
                    }
                    else
                    {
                        object?[] values = new object?[attribute.Count];
                        for (int i = 0; i < attribute.Count; i++)
                        {
                            values[i] = DecodeAttributeValue(attributeName, attribute[i]);
                        }
                        result[attributeName] = values;
                    }
                }

                yield return result;
            }

            // Check for page continuation
            PageResultResponseControl? pageResponse = response.Controls
                .OfType<PageResultResponseControl>()
                .FirstOrDefault();

            if (pageResponse is null || pageResponse.Cookie.Length == 0)
            {
                break;
            }

            pageControl.Cookie = pageResponse.Cookie;
        }
    }

    /// <summary>
    /// Asynchronously retrieves a multi-valued attribute using range retrieval to handle
    /// large value sets.
    /// </summary>
    /// <param name="baseDn">The distinguished name of the object.</param>
    /// <param name="attribute">The attribute name to retrieve.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the retrieval.</param>
    /// <returns>A list of decoded attribute values.</returns>
    public async Task<List<object?>> RetrieveCollectionAttributeAsync(
        string baseDn,
        string attribute,
        CancellationToken cancellationToken = default)
    {
        LdapConnection connection = OpenLdapConnection();
        List<object?> results = new List<object?>();

        int rangeStart = 0;
        const int rangeStep = 1500;
        bool moreData = true;

        while (moreData)
        {
            string rangeAttribute = $"{attribute};range={rangeStart}-{rangeStart + rangeStep - 1}";
            SearchRequest request = new SearchRequest(baseDn, "(objectClass=*)", SearchScope.Base, rangeAttribute);

            SearchResponse response = await connection
                .SendRequestAsync<SearchResponse>(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.Entries.Count == 0)
            {
                break;
            }

            SearchResultEntry entry = response.Entries[0];
            moreData = false;

            foreach (string attrName in entry.Attributes.AttributeNames)
            {
                DirectoryAttribute attr = entry.Attributes[attrName];

                for (int i = 0; i < attr.Count; i++)
                {
                    results.Add(DecodeAttributeValue(attribute, attr[i]));
                }

                // Check if the attribute name indicates more data (range=X-*)
                if (attrName.Contains('*'))
                {
                    break;
                }

                if (attr.Count == rangeStep)
                {
                    rangeStart += rangeStep;
                    moreData = true;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Asynchronously attempts to replace an attribute value on a directory object.
    /// </summary>
    /// <param name="distinguishedName">The DN of the object to modify.</param>
    /// <param name="attributeName">The name of the attribute to replace.</param>
    /// <param name="value">The new value for the attribute.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the modification.</param>
    /// <returns><c>true</c> if the modification succeeded; otherwise, <c>false</c>.</returns>
    public Task<bool> TryAttributeModificationReplaceAsync(
        string distinguishedName,
        string attributeName,
        object value,
        CancellationToken cancellationToken = default)
    {
        return TryModifyAttributeAsync(
            distinguishedName, attributeName, value, DirectoryAttributeOperation.Replace, cancellationToken);
    }

    /// <summary>
    /// Asynchronously attempts to add a value to an attribute on a directory object.
    /// </summary>
    /// <param name="distinguishedName">The DN of the object to modify.</param>
    /// <param name="attributeName">The name of the attribute to add to.</param>
    /// <param name="value">The value to add.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the modification.</param>
    /// <returns><c>true</c> if the modification succeeded; otherwise, <c>false</c>.</returns>
    public Task<bool> TryAttributeModificationAddAsync(
        string distinguishedName,
        string attributeName,
        object value,
        CancellationToken cancellationToken = default)
    {
        return TryModifyAttributeAsync(
            distinguishedName, attributeName, value, DirectoryAttributeOperation.Add, cancellationToken);
    }

    /// <summary>
    /// Asynchronously attempts to delete a value from an attribute on a directory object.
    /// </summary>
    /// <param name="distinguishedName">The DN of the object to modify.</param>
    /// <param name="attributeName">The name of the attribute to delete from.</param>
    /// <param name="value">The value to remove.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the modification.</param>
    /// <returns><c>true</c> if the modification succeeded; otherwise, <c>false</c>.</returns>
    public Task<bool> TryAttributeModificationDeleteAsync(
        string distinguishedName,
        string attributeName,
        object value,
        CancellationToken cancellationToken = default)
    {
        return TryModifyAttributeAsync(
            distinguishedName, attributeName, value, DirectoryAttributeOperation.Delete, cancellationToken);
    }

    /// <summary>
    /// Asynchronously attempts to create a new directory object with the specified attributes.
    /// </summary>
    /// <param name="distinguishedName">The DN of the new object.</param>
    /// <param name="attributes">The attributes to set on the new object.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the creation.</param>
    /// <returns><c>true</c> if the object was created successfully; otherwise, <c>false</c>.</returns>
    public async Task<bool> TryAddRequestAsync(
        string distinguishedName,
        IDictionary<string, object> attributes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LdapConnection connection = OpenLdapConnection();
            AddRequest request = new AddRequest(distinguishedName);

            foreach (KeyValuePair<string, object> kvp in attributes)
            {
                request.Attributes.Add(new DirectoryAttribute(kvp.Key, kvp.Value.ToString()));
            }

            await connection.SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Created object: {DN}", distinguishedName);
            return true;
        }
        catch (DirectoryOperationException ex)
        {
            _logger.LogError(ex, "Failed to create object: {DN}", distinguishedName);
            return false;
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "LDAP error creating object: {DN}", distinguishedName);
            return false;
        }
    }

    /// <summary>
    /// Validates whether the supplied credentials are valid against the directory.
    /// </summary>
    /// <remarks>
    /// Synchronous for the same reason as <see cref="OpenLdapConnection"/>: credential validation
    /// is a bind, and no asynchronous bind exists in <c>System.DirectoryServices.Protocols</c>.
    /// </remarks>
    /// <param name="username">The username to validate.</param>
    /// <param name="password">The password to validate.</param>
    /// <returns><c>true</c> if the credentials are valid; otherwise, <c>false</c>.</returns>
    public bool IsValidCredential(string username, string password)
    {
        try
        {
            string server = DomainControllers.FirstOrDefault()
                ?? throw new InvalidOperationException("No domain controllers configured.");

            LdapDirectoryIdentifier identifier = new LdapDirectoryIdentifier(server, Port);
            NetworkCredential credential = new NetworkCredential(username, password);

            using LdapConnection testConnection = new LdapConnection(identifier)
            {
                AuthType = AuthenticationType
            };

            testConnection.SessionOptions.SecureSocketLayer = SecureSocketLayer;
            testConnection.SessionOptions.ReferralChasing = ReferralChasingOptions;
            testConnection.Bind(credential);

            return true;
        }
        catch (LdapException)
        {
            return false;
        }
    }

    /// <summary>
    /// Opens or returns the existing LDAP connection.
    /// </summary>
    /// <remarks>
    /// This method is synchronous by design. <c>System.DirectoryServices.Protocols</c> exposes no
    /// asynchronous bind — <see cref="LdapConnection.Bind()"/> has no APM or TAP counterpart — so
    /// wrapping it would only move the block onto a thread-pool thread without gaining any
    /// scalability. The bind happens once per connection; every subsequent request is genuinely
    /// asynchronous via <see cref="LdapConnectionExtensions.SendRequestAsync"/>.
    /// </remarks>
    /// <returns>An open <see cref="LdapConnection"/>.</returns>
    public LdapConnection OpenLdapConnection()
    {
        if (_ldapConnection is not null)
        {
            return _ldapConnection;
        }

        string server = DomainControllers.FirstOrDefault()
            ?? throw new InvalidOperationException("No domain controllers configured.");

        LdapDirectoryIdentifier identifier = new LdapDirectoryIdentifier(server, Port);

        _ldapConnection = new LdapConnection(identifier)
        {
            AuthType = AuthenticationType
        };

        _ldapConnection.SessionOptions.SecureSocketLayer = SecureSocketLayer;
        _ldapConnection.SessionOptions.ReferralChasing = ReferralChasingOptions;
        _ldapConnection.SessionOptions.ProtocolVersion = 3;

        if (Username is not null && Password is not null)
        {
            Username.TryGetValue(null, out char[]? usernameChars);
            Password.TryGetValue(null, out char[]? passwordChars);

            NetworkCredential credential = new NetworkCredential(
                new string(usernameChars),
                new string(passwordChars));

            _ldapConnection.Bind(credential);
        }
        else
        {
            _ldapConnection.Bind();
        }

        _logger.LogInformation("LDAP connection established to {Server}:{Port}", server, Port);

        return _ldapConnection;
    }

    /// <summary>
    /// Creates a deep copy of this <see cref="Connection"/> with the same configuration.
    /// </summary>
    /// <returns>A new <see cref="Connection"/> instance with identical settings.</returns>
    public Connection Clone()
    {
        Connection clone = new Connection(_logger)
        {
            BaseSearchDn = BaseSearchDn,
            Port = Port,
            PageSize = PageSize,
            SecureSocketLayer = SecureSocketLayer,
            AuthenticationType = AuthenticationType,
            SearchScope = SearchScope,
            ReferralChasingOptions = ReferralChasingOptions,
            Username = Username,
            Password = Password
        };

        clone.DomainControllers.AddRange(DomainControllers);

        foreach (KeyValuePair<string, Func<byte[], object?>> kvp in EncodersByName)
        {
            clone.EncodersByName[kvp.Key] = kvp.Value;
        }

        foreach (KeyValuePair<string, Func<byte[], object?>> kvp in EncodersByType)
        {
            clone.EncodersByType[kvp.Key] = kvp.Value;
        }

        return clone;
    }

    /// <summary>
    /// Decodes a raw attribute value using the registered encoders.
    /// </summary>
    private object? DecodeAttributeValue(string attributeName, object rawValue)
    {
        if (EncodersByName.TryGetValue(attributeName, out Func<byte[], object?>? encoder))
        {
            return rawValue is byte[] bytes ? encoder(bytes) : rawValue;
        }

        // Fall back to string decoding
        return rawValue switch
        {
            byte[] byteArray => Encoding.UTF8.GetString(byteArray),
            _ => rawValue
        };
    }

    /// <summary>
    /// Asynchronously attempts to modify an attribute on a directory object.
    /// </summary>
    private async Task<bool> TryModifyAttributeAsync(
        string distinguishedName,
        string attributeName,
        object value,
        DirectoryAttributeOperation operation,
        CancellationToken cancellationToken)
    {
        try
        {
            LdapConnection connection = OpenLdapConnection();
            DirectoryAttributeModification modification = new DirectoryAttributeModification
            {
                Name = attributeName,
                Operation = operation
            };

            if (value is byte[] bytes)
            {
                modification.Add(bytes);
            }
            else
            {
                modification.Add(value.ToString());
            }

            ModifyRequest request = new ModifyRequest(distinguishedName, modification);
            await connection.SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Modified attribute '{Attribute}' on {DN} ({Operation})",
                attributeName, distinguishedName, operation);

            return true;
        }
        catch (DirectoryOperationException ex)
        {
            _logger.LogError(ex, "Failed to modify attribute '{Attribute}' on {DN} ({Operation})",
                attributeName, distinguishedName, operation);
            return false;
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "LDAP error modifying attribute '{Attribute}' on {DN} ({Operation})",
                attributeName, distinguishedName, operation);
            return false;
        }
    }

    /// <summary>
    /// Releases the LDAP connection.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _ldapConnection?.Dispose();
            _ldapConnection = null;
            _disposed = true;
        }
    }
}
