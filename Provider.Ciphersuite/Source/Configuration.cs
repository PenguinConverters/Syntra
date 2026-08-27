using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.Ciphersuite.Source;

/// <summary>
/// Configuration for the cipher suite source provider.
/// </summary>
/// <remarks>
/// The catalogue is a public reference API: it takes no credentials, returns its whole collection
/// in one response, and carries no notion of a record being modified or removed. What it does do
/// is key each cipher suite by its IANA name rather than carrying that name as a property, which
/// <see cref="Provider"/> unwraps.
/// </remarks>
public class Configuration : RESTful.Source.Configuration
{
    #region Constants

    /// <summary>
    /// Path to the collection within the response.
    /// </summary>
    public const string DefaultResultPath = "ciphersuites";

    /// <summary>
    /// Property the IANA name is stamped onto once it has been lifted out of the wrapper.
    /// </summary>
    public const string DefaultIdentityProperty = "iana_name";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Configuration"/> class with the defaults the
    /// cipher suite catalogue needs. Every one of them may be overridden by the configuration file.
    /// </summary>
    public Configuration()
    {
        ApplyCiphersuiteDefaults();
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public override void ApplyDefaults()
    {
        base.ApplyDefaults();

        ApplyCiphersuiteDefaults();
    }

    /// <summary>
    /// Fills in whatever the configuration file left unset. The catalogue takes no credentials,
    /// so no authentication section is established: leaving it unset is what sends an anonymous
    /// request.
    /// </summary>
    private void ApplyCiphersuiteDefaults()
    {
        ResultPath ??= DefaultResultPath;
        IdentityProperty ??= DefaultIdentityProperty;
    }

    #endregion
}
