namespace PenguinConverters.Syntra.Core.Security;

/// <summary>
/// What was established about who produced a file.
/// </summary>
/// <param name="Trust">What could be established.</param>
/// <param name="Subject">
/// The common name of the signing certificate, or <c>null</c> when the file carries no usable
/// signature.
/// </param>
/// <param name="Thumbprint">
/// The SHA-1 thumbprint of the signing certificate, which is what a certificate store and a
/// signature dialog show, or <c>null</c> when there is no signature to take one from.
/// </param>
/// <param name="Detail">
/// The reason behind <see cref="Trust"/> when it is worth reporting, or <c>null</c>.
/// </param>
public sealed record PublisherVerification(
    PublisherTrust Trust,
    string? Subject,
    string? Thumbprint,
    string? Detail = null);
