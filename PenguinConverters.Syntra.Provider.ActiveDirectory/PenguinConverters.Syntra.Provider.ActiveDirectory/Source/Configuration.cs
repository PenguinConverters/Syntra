using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Provider.ActiveDirectory.Source;

/// <summary>
/// Configuration settings for the Active Directory source provider.
/// Defines LDAP connection parameters, search filters, and authentication options.
/// </summary>
public class Configuration
{
    /// <summary>
    /// Default LDAP port for SSL connections.
    /// </summary>
    public const int DefaultPort = 636;

    /// <summary>
    /// Default LDAP search filter.
    /// </summary>
    public const string DefaultLdapFilter = "objectClass=*";

    /// <summary>
    /// Default LDAP filter for deleted objects.
    /// </summary>
    public const string DefaultLdapFilterDeleted = "isDeleted=*";

    /// <summary>
    /// Default LDAP page size for paged result queries.
    /// </summary>
    public const int DefaultPageSize = 300;

    /// <summary>
    /// Gets or sets the base distinguished name for LDAP queries.
    /// </summary>
    public string? BaseDN { get; set; }

    /// <summary>
    /// Gets or sets the fully qualified domain name of the LDAP server.
    /// </summary>
    public string? ServerName { get; set; }

    /// <summary>
    /// Gets or sets the TCP port for the LDAP connection.
    /// Defaults to <c>636</c> (LDAPS).
    /// </summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>
    /// Gets or sets the LDAP search filter used for full synchronization.
    /// </summary>
    public string LdapFilter { get; set; } = DefaultLdapFilter;

    /// <summary>
    /// Gets or sets the LDAP search filter for locating deleted objects in delta sync.
    /// </summary>
    public string LdapFilterDeleted { get; set; } = DefaultLdapFilterDeleted;

    /// <summary>
    /// Gets or sets the LDAP username credential.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? Username { get; set; }

    /// <summary>
    /// Gets or sets the LDAP password credential.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? Password { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether delta synchronization is enabled.
    /// When <c>true</c>, only objects changed since the last synchronization are retrieved.
    /// </summary>
    public bool Delta { get; set; }

    /// <summary>
    /// Gets or sets the LDAP search scope (Base, OneLevel, Subtree).
    /// Defaults to <c>Subtree</c>.
    /// </summary>
    public string SearchScope { get; set; } = "Subtree";

    /// <summary>
    /// Gets or sets the LDAP authentication type (Negotiate, Basic, etc.).
    /// Defaults to <c>Negotiate</c>.
    /// </summary>
    public string AuthType { get; set; } = "Negotiate";

    /// <summary>
    /// Gets or sets the LDAP query page size.
    /// Defaults to <c>300</c>.
    /// </summary>
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>
    /// Gets or sets a value indicating whether SSL is enabled on the LDAP connection.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool SecureSocketLayer { get; set; } = true;

    /// <summary>
    /// Gets or sets the relationship definitions for resolving nested group memberships
    /// or other multi-valued attributes.
    /// </summary>
    public List<Relationship>? Relationships { get; set; }
}

/// <summary>
/// Defines a relationship between LDAP objects for nested retrieval.
/// </summary>
public class Relationship
{
    /// <summary>
    /// Gets or sets the LDAP filter template for resolving the relationship.
    /// </summary>
    public string? LdapFilter { get; set; }

    /// <summary>
    /// Gets or sets the attribute name to load from related objects.
    /// </summary>
    public string? Attribute { get; set; }
}
