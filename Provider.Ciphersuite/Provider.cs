using PenguinConverters.Syntra.Core.Types;
using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.Ciphersuite;

/// <summary>
/// Cipher suite source provider, reading the TLS cipher suite catalogue.
/// </summary>
/// <remarks>
/// The catalogue keys each cipher suite by its IANA name instead of carrying that name as a
/// property, so a record arrives wrapped:
/// <code>
/// { "ciphersuites": [ { "TLS_AES_256_GCM_SHA384": { "security": "recommended", ... } } ] }
/// </code>
/// An entry transform unwraps it and stamps the key back on as a property, which is the one thing
/// configuration cannot express. Everything else - retrieval, the anonymous request, the response
/// path - comes from <see cref="RESTful.Provider"/>.
/// </remarks>
public class Provider : RESTful.Provider
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Provider"/> class.
    /// </summary>
    public Provider()
    {
        EntryTransform = Unwrap;
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    protected override RESTful.Source.Configuration? ReadConfiguration()
    {
        return DeserializeConfiguration<Source.Configuration>();
    }

    /// <summary>
    /// Lifts a cipher suite out of the single-property wrapper it arrives in and stamps the
    /// wrapper's key onto it as the IANA name.
    /// </summary>
    /// <param name="properties">The wrapper, whose one property is named for the cipher suite.</param>
    /// <param name="configuration">The endpoint the wrapper came from.</param>
    /// <returns>
    /// The cipher suite, or the record unchanged when it is not wrapped - a catalogue that starts
    /// returning its records flat should keep working rather than silently yield nothing.
    /// </returns>
    private static QuickDictionary? Unwrap(QuickDictionary properties, RESTful.Source.Configuration configuration)
    {
        if (properties.Count != 1)
        {
            return properties;
        }

        string name = properties.Keys.First();

        // The wrapped object is held as raw JSON text, which is how a nested structure is carried
        // once a record has been projected onto a property bag.
        List<QuickDictionary> unwrapped = RestClient.ParseEntries(properties[name]?.ToString());

        if (unwrapped.Count != 1)
        {
            return properties;
        }

        string identityProperty = configuration.IdentityProperty ?? Source.Configuration.DefaultIdentityProperty;

        unwrapped[0][identityProperty] = name;

        return unwrapped[0];
    }

    #endregion
}
