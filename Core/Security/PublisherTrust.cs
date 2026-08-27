namespace PenguinConverters.Syntra.Core.Security;

/// <summary>
/// What could be established about who produced a file.
/// </summary>
public enum PublisherTrust
{
    /// <summary>
    /// The file carries no Authenticode signature.
    /// </summary>
    Unsigned = 0,

    /// <summary>
    /// The file is signed, and the signature is valid and chains to a trusted root, but the
    /// publisher is not the one expected.
    /// </summary>
    Untrusted = 1,

    /// <summary>
    /// The file is signed by the expected publisher, and the signature is valid.
    /// </summary>
    Trusted = 2,

    /// <summary>
    /// The file is signed, but the signature does not verify - it is invalid, expired without a
    /// timestamp, revoked, or the file has been altered since it was signed.
    /// </summary>
    Invalid = 3,

    /// <summary>
    /// Nothing could be established. Authenticode is a Windows facility, so this is the answer
    /// everywhere else, and it is not a statement about the file.
    /// </summary>
    NotVerifiable = 4
}
