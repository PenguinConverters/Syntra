namespace PenguinConverters.Syntra.ActiveDirectory;

/// <summary>
/// Flags that define the behavior and state of an Active Directory user account.
/// Corresponds to the <c>userAccountControl</c> attribute in AD.
/// </summary>
[Flags]
public enum UserAccountControl
{
    /// <summary>
    /// No flags set.
    /// </summary>
    None = 0x0000,

    /// <summary>
    /// The logon script will be executed.
    /// </summary>
    Script = 0x0001,

    /// <summary>
    /// The user account is disabled.
    /// </summary>
    AccountDisabled = 0x0002,

    /// <summary>
    /// The home directory is required.
    /// </summary>
    HomeDirectoryRequired = 0x0008,

    /// <summary>
    /// The account is currently locked out.
    /// </summary>
    Lockout = 0x0010,

    /// <summary>
    /// No password is required for the account.
    /// </summary>
    PasswordNotRequired = 0x0020,

    /// <summary>
    /// The user cannot change the password.
    /// </summary>
    PasswordCannotChange = 0x0040,

    /// <summary>
    /// The user can send an encrypted text password.
    /// </summary>
    EncryptedTextPasswordAllowed = 0x0080,

    /// <summary>
    /// An account for users whose primary account is in another domain.
    /// Also known as a local user account.
    /// </summary>
    TempDuplicateAccount = 0x0100,

    /// <summary>
    /// A default account type representing a typical user.
    /// </summary>
    NormalAccount = 0x0200,

    /// <summary>
    /// A trust account for a domain that trusts other domains.
    /// </summary>
    InterdomainTrustAccount = 0x0800,

    /// <summary>
    /// A computer account for a workstation or server in the domain.
    /// </summary>
    WorkstationTrustAccount = 0x1000,

    /// <summary>
    /// A computer account for a backup domain controller.
    /// </summary>
    ServerTrustAccount = 0x2000,

    /// <summary>
    /// The password for this account never expires.
    /// </summary>
    PasswordNeverExpires = 0x10000,

    /// <summary>
    /// An MNS (Majority Node Set) logon account type.
    /// </summary>
    MnsLogonAccount = 0x20000,

    /// <summary>
    /// Forces the user to log on using a smart card.
    /// </summary>
    SmartcardRequired = 0x40000,

    /// <summary>
    /// The service account is trusted for Kerberos delegation.
    /// </summary>
    TrustedForDelegation = 0x80000,

    /// <summary>
    /// The security context of the user is not delegated.
    /// </summary>
    NotDelegated = 0x100000,

    /// <summary>
    /// Requires DES-only Kerberos encryption types.
    /// </summary>
    UseDesKeyOnly = 0x200000,

    /// <summary>
    /// Kerberos pre-authentication is not required.
    /// </summary>
    DontRequirePreauth = 0x400000,

    /// <summary>
    /// The user password has expired.
    /// </summary>
    PasswordExpired = 0x800000,

    /// <summary>
    /// The account is enabled for delegation with protocol transition.
    /// </summary>
    TrustedToAuthenticateForDelegation = 0x1000000,

    /// <summary>
    /// The account is a read-only domain controller (RODC).
    /// </summary>
    PartialSecretsAccount = 0x04000000
}
