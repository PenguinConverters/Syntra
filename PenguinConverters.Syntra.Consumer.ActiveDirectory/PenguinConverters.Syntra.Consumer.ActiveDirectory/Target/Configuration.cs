using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Consumer.ActiveDirectory.Target;

/// <summary>
/// Configuration settings for the Active Directory consumer.
/// Defines LDAP connection parameters for writing changes back to Active Directory.
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
    /// Gets or sets the base distinguished name for the target OU.
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
    /// Gets or sets the LDAP search filter used for locating target objects.
    /// </summary>
    public string LdapFilter { get; set; } = DefaultLdapFilter;

    /// <summary>
    /// Gets or sets the username credential for LDAP authentication.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? Username { get; set; }

    /// <summary>
    /// Gets or sets the password credential for LDAP authentication.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? Password { get; set; }

    /// <summary>
    /// Gets or sets the LDAP authentication type (Negotiate, Basic, etc.).
    /// Defaults to <c>Negotiate</c>.
    /// </summary>
    public string AuthType { get; set; } = "Negotiate";

    /// <summary>
    /// Gets or sets the LDAP search scope (Base, OneLevel, Subtree).
    /// Defaults to <c>Subtree</c>.
    /// </summary>
    public string SearchScope { get; set; } = "Subtree";

    /// <summary>
    /// Gets or sets a value indicating whether SSL is enabled.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool SecureSocketLayer { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether delta synchronization is enabled.
    /// </summary>
    public bool Delta { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether composite keys should be tracked
    /// for deletion reconciliation during full sync.
    /// </summary>
    public bool KeepCompositeKeys { get; set; }

    /// <summary>
    /// Gets or sets the property definitions for target AD object attributes.
    /// </summary>
    public Dictionary<string, PropertyDefinition>? Properties { get; set; }

    /// <summary>
    /// Gets or sets the deletion thresholds for preventing mass deletions.
    /// </summary>
    public List<ThresholdDefinition>? Thresholds { get; set; }

    /// <summary>
    /// Gets or sets the LDAP attributes to load when searching for existing objects.
    /// </summary>
    public List<string>? PropertiesToLoad { get; set; }
}

/// <summary>
/// Defines a target AD attribute and its mapping from source entities.
/// </summary>
public class PropertyDefinition
{
    /// <summary>
    /// Gets or sets the AD attribute name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the value template, which may include placeholders
    /// like <c>&lt;%sourceProperty%&gt;</c>.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a primary key attribute.
    /// </summary>
    public bool PrimaryKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a multi-valued attribute.
    /// </summary>
    public bool Array { get; set; }
}

/// <summary>
/// Defines a deletion threshold to guard against mass deletions.
/// </summary>
public class ThresholdDefinition
{
    /// <summary>
    /// Gets or sets the operation type this threshold applies to (e.g., "Delete").
    /// </summary>
    public string? Operation { get; set; }

    /// <summary>
    /// Gets or sets the threshold percentage (0-100).
    /// </summary>
    public int Percentage { get; set; }
}
