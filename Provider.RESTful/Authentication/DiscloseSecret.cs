using PenguinConverters.Keyra.Settings;

namespace PenguinConverters.Syntra.Provider.RESTful.Authentication;

/// <summary>
/// Discloses a configured secret, whether it is stored as plaintext or as Keyra ciphertext.
/// </summary>
/// <remarks>
/// The builder binds this to the disclosure helper the provider inherits, so that anything
/// needing a credential - a token provider, a proxy, a connector's own authentication code -
/// gets one without being handed the vault key itself.
/// </remarks>
/// <param name="secret">The configured secret, or <c>null</c> if the setting was omitted.</param>
/// <param name="plaintext">
/// When this delegate returns <c>true</c>, the disclosed characters. The caller owns the array
/// and should clear it once the credential has been used.
/// </param>
/// <returns><c>true</c> if the value was disclosed; otherwise, <c>false</c>.</returns>
public delegate bool DiscloseSecret(Secret? secret, out char[] plaintext);
