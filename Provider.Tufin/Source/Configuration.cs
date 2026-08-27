using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.Tufin.Source;

/// <summary>
/// Configuration for the Tufin source provider.
/// </summary>
/// <remarks>
/// Tufin answers with a JSON object that nests its collection two levels deep under names that
/// vary per endpoint - <c>devices.device</c>, <c>policies.policy</c>, <c>rules.rule</c> - so
/// <see cref="RESTful.Source.Configuration.ResultPath"/> is set per endpoint rather than defaulted
/// here.
/// <para>
/// Most of what a Tufin read needs is nesting: a device is only the key to its policies, and a
/// policy only the key to its rules. That is
/// <see cref="RESTful.Source.Configuration.Children"/>, addressed through
/// <c>&lt;%property%&gt;</c> placeholders, and needs no code.
/// </para>
/// </remarks>
public class Configuration : RESTful.Source.Configuration
{
    #region Constants

    /// <summary>
    /// Property holding the identity of a Tufin object.
    /// </summary>
    public const string DefaultIdentityProperty = "id";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Configuration"/> class with the defaults a
    /// Tufin appliance needs. Every one of them may be overridden by the configuration file.
    /// </summary>
    public Configuration()
    {
        ApplyTufinDefaults();
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public override void ApplyDefaults()
    {
        base.ApplyDefaults();

        ApplyTufinDefaults();
    }

    /// <summary>
    /// Fills in whatever the configuration file left unset. Every assignment is conditional, so
    /// running this after deserialization restores the defaults a mentioned section discarded
    /// without overwriting anything the file actually stated.
    /// </summary>
    private void ApplyTufinDefaults()
    {
        IdentityProperty ??= DefaultIdentityProperty;

        Authentication ??= new AuthenticationSettings();

        if (Authentication.Mode == AuthenticationMode.None)
        {
            Authentication.Mode = AuthenticationMode.Basic;
        }
    }

    #endregion
}
