namespace PenguinConverters.Syntra.Core.Settings;

/// <summary>
/// Wraps a string value that may be encrypted via Keyra.
/// When <see cref="Protected"/> is <c>true</c>, the <see cref="Value"/> is treated as a cipher reference
/// and must be decrypted using a discloser function.
/// </summary>
public class ProtectedString
{
    #region Properties

    /// <summary>
    /// Gets or sets the raw string value. When <see cref="Protected"/> is <c>true</c>,
    /// this contains the Keyra cipher reference rather than plaintext.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the <see cref="Value"/> is encrypted
    /// and requires decryption via Keyra.
    /// </summary>
    public bool Protected { get; set; }

    #endregion

    #region Methods

    /// <summary>
    /// Attempts to retrieve the plaintext value, decrypting if necessary.
    /// </summary>
    /// <param name="disclose">The Keyra discloser function, or <c>null</c> if no decryption is available.</param>
    /// <param name="plaintext">When this method returns, contains the plaintext characters.</param>
    /// <returns><c>true</c> if the plaintext was successfully retrieved; otherwise, <c>false</c>.</returns>
    public bool TryGetValue(Func<string, char[]>? disclose, out char[] plaintext)
    {
        if (Protected && disclose != null)
        {
            plaintext = disclose(Value);
            return true;
        }

        plaintext = Value.ToCharArray();
        return true;
    }

    /// <summary>
    /// Returns the raw value string. Does not decrypt protected values.
    /// </summary>
    public override string ToString() => Protected ? "****" : Value;

    #endregion
}
