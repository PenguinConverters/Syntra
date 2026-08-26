using PenguinConverters.Keyra;

namespace PenguinConverters.Syntra.Core.Settings;

/// <summary>
/// Locates the Keyra vault key that discloses the <see cref="PenguinConverters.Keyra.Settings.Secret"/>
/// values carried by a configuration, and builds the <see cref="Decryptor"/> from it.
/// </summary>
/// <remarks>
/// The key password is never read from the configuration file, because that file is exactly what the
/// key protects. It comes from the environment instead, so a configuration can be committed and shared
/// while the credential that opens it stays with the deployment.
/// </remarks>
public class KeyraSettings
{
    #region Constants

    /// <summary>
    /// Environment variable consulted for the armored vault share when <see cref="ShareVariable"/>
    /// names none.
    /// </summary>
    public const string DefaultShareVariable = "SYNTRA_KEYRA_SHARE";

    /// <summary>
    /// Environment variable consulted for the key password when <see cref="PasswordVariable"/>
    /// names none.
    /// </summary>
    public const string DefaultPasswordVariable = "SYNTRA_KEYRA_PASSWORD";

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the path to the key source: a <c>.keyra</c> package, a file holding an armored
    /// share, a key document, or a vault directory. Keyra decides which it is by content, so the
    /// file name and extension carry no meaning.
    /// </summary>
    public string? KeyFile { get; set; }

    /// <summary>
    /// Gets or sets the name of the environment variable holding an armored vault share
    /// (<c>KEYRA:</c> … <c>:ARYEK</c>). Consulted only when <see cref="KeyFile"/> is unset, which is
    /// the usual arrangement for a container or CI runner with no key file on disk.
    /// Defaults to <see cref="DefaultShareVariable"/>.
    /// </summary>
    public string? ShareVariable { get; set; }

    /// <summary>
    /// Gets or sets the name of the environment variable holding the key password.
    /// Defaults to <see cref="DefaultPasswordVariable"/>. An absent password is not an error:
    /// a Windows-identity key opens without one.
    /// </summary>
    public string? PasswordVariable { get; set; }

    #endregion

    #region Methods

    /// <summary>
    /// Builds the <see cref="Decryptor"/> that discloses this configuration's protected values.
    /// </summary>
    /// <returns>
    /// A decryptor holding the vault key. The caller owns it and must dispose it: it holds key
    /// material for its lifetime.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no key source is configured, or when the key cannot be opened with the supplied
    /// credential.
    /// </exception>
    public Decryptor CreateDecryptor()
    {
        DecryptorBuilder builder = new DecryptorBuilder();

        if (!string.IsNullOrWhiteSpace(KeyFile))
        {
            builder.UseKeyFile(KeyFile);
        }
        else
        {
            string shareVariable = string.IsNullOrWhiteSpace(ShareVariable)
                ? DefaultShareVariable
                : ShareVariable;

            string? share = Environment.GetEnvironmentVariable(shareVariable);

            if (string.IsNullOrWhiteSpace(share))
            {
                throw new InvalidOperationException(
                    "No Keyra key source is configured. Set 'keyFile' to a key package, share file, " +
                    $"key document or vault directory, or place an armored share in the '{shareVariable}' " +
                    "environment variable.");
            }

            builder.UseShare(share);
        }

        string passwordVariable = string.IsNullOrWhiteSpace(PasswordVariable)
            ? DefaultPasswordVariable
            : PasswordVariable;

        string? password = Environment.GetEnvironmentVariable(passwordVariable);

        if (!string.IsNullOrEmpty(password))
        {
            builder.WithPassword(password);
        }

        return builder.Build();
    }

    #endregion
}
