using System.DirectoryServices.Protocols;
using Microsoft.Extensions.Logging;
using PenguinConverters.Keyra;
using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.ActiveDirectory;

/// <summary>
/// Fluent builder for constructing <see cref="Connection"/> instances with a chainable API.
/// </summary>
public class ConnectionBuilder
{
    #region Fields

    private readonly Connection _connection;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionBuilder"/> class.
    /// </summary>
    public ConnectionBuilder()
    {
        _connection = new Connection();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionBuilder"/> class with a logger.
    /// </summary>
    /// <param name="logger">The logger instance for the connection.</param>
    public ConnectionBuilder(ILogger logger)
    {
        _connection = new Connection(logger);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Adds a domain controller hostname or IP address.
    /// </summary>
    /// <param name="domainController">The domain controller address.</param>
    /// <returns>This builder for chaining.</returns>
    public ConnectionBuilder AddDomainController(string domainController)
    {
        _connection.DomainControllers.Add(domainController);
        return this;
    }

    /// <summary>
    /// Sets the base distinguished name for LDAP searches.
    /// </summary>
    /// <param name="baseDn">The base DN (e.g., <c>DC=contoso,DC=com</c>).</param>
    /// <returns>This builder for chaining.</returns>
    public ConnectionBuilder AddBaseDN(string baseDn)
    {
        _connection.BaseSearchDn = baseDn;
        return this;
    }

    /// <summary>
    /// Sets the LDAP port number.
    /// </summary>
    /// <param name="port">The port number (e.g., 636 for LDAPS, 389 for LDAP).</param>
    /// <returns>This builder for chaining.</returns>
    public ConnectionBuilder AddPort(int port)
    {
        _connection.Port = port;
        return this;
    }

    /// <summary>
    /// Enables or disables SSL for the LDAP connection.
    /// </summary>
    /// <param name="useSsl">
    /// <c>true</c> to enable SSL (LDAPS); <c>false</c> for plain LDAP.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    public ConnectionBuilder SetSecureSocketLayer(bool useSsl)
    {
        _connection.SecureSocketLayer = useSsl;
        return this;
    }

    /// <summary>
    /// Sets the LDAP authentication type.
    /// </summary>
    /// <param name="authType">The authentication type to use.</param>
    /// <returns>This builder for chaining.</returns>
    public ConnectionBuilder AddAuthType(AuthType authType)
    {
        _connection.AuthenticationType = authType;
        return this;
    }

    /// <summary>
    /// Sets the credentials for LDAP authentication.
    /// </summary>
    /// <param name="username">The username as a <see cref="Secret"/>.</param>
    /// <param name="password">The password as a <see cref="Secret"/>.</param>
    /// <returns>This builder for chaining.</returns>
    public ConnectionBuilder AddCredentials(Secret username, Secret password)
    {
        _connection.Username = username;
        _connection.Password = password;
        return this;
    }

    /// <summary>
    /// Sets the Keyra decryptor used to disclose protected credentials. Required whenever the
    /// credentials passed to <see cref="AddCredentials"/> carry protected values.
    /// </summary>
    /// <param name="decryptor">The decryptor holding the vault key.</param>
    /// <returns>This builder for chaining.</returns>
    public ConnectionBuilder AddDecryptor(Decryptor decryptor)
    {
        _connection.Decryptor = decryptor;
        return this;
    }

    /// <summary>
    /// Adds attribute decoders from a schema provider to the connection.
    /// </summary>
    /// <param name="decoders">
    /// A dictionary mapping attribute names to decoder functions.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    public ConnectionBuilder AddSchemaDecoders(Dictionary<string, Func<byte[], object?>> decoders)
    {
        foreach (KeyValuePair<string, Func<byte[], object?>> kvp in decoders)
        {
            _connection.EncodersByName[kvp.Key] = kvp.Value;
        }
        return this;
    }

    /// <summary>
    /// Sets the search scope for LDAP queries.
    /// </summary>
    /// <param name="searchScope">The search scope.</param>
    /// <returns>This builder for chaining.</returns>
    public ConnectionBuilder SetSearchScope(SearchScope searchScope)
    {
        _connection.SearchScope = searchScope;
        return this;
    }

    /// <summary>
    /// Sets the page size for paged LDAP search results.
    /// </summary>
    /// <param name="pageSize">The number of results per page.</param>
    /// <returns>This builder for chaining.</returns>
    public ConnectionBuilder SetPageSize(int pageSize)
    {
        _connection.PageSize = pageSize;
        return this;
    }

    /// <summary>
    /// Sets the referral chasing options for the LDAP connection.
    /// </summary>
    /// <param name="options">The referral chasing behavior.</param>
    /// <returns>This builder for chaining.</returns>
    public ConnectionBuilder SetReferralChasing(ReferralChasingOptions options)
    {
        _connection.ReferralChasingOptions = options;
        return this;
    }

    /// <summary>
    /// Builds and returns the configured <see cref="Connection"/> instance.
    /// </summary>
    /// <returns>A fully configured <see cref="Connection"/>.</returns>
    public Connection Build()
    {
        if (_connection.DomainControllers.Count == 0)
        {
            throw new InvalidOperationException("At least one domain controller must be configured.");
        }

        if (string.IsNullOrWhiteSpace(_connection.BaseSearchDn))
        {
            throw new InvalidOperationException("Base search DN must be configured.");
        }

        return _connection;
    }

    #endregion
}
