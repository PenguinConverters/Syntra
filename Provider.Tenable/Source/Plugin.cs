namespace PenguinConverters.Syntra.Provider.Tenable.Source;

/// <summary>
/// Selects what is made of each row of an export.
/// </summary>
public enum Plugin
{
    /// <summary>
    /// Each row becomes one record, with its columns as properties.
    /// </summary>
    None = 0,

    /// <summary>
    /// Each row is expanded into the observations its plugin output describes - one per
    /// certificate, cipher suite, SSH algorithm or SSH version - so that what a scan printed as
    /// text becomes queryable.
    /// </summary>
    Nessus = 1
}
