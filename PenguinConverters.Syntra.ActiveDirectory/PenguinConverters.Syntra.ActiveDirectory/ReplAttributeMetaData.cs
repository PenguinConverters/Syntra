using System.Xml.Serialization;

namespace PenguinConverters.Syntra.ActiveDirectory;

/// <summary>
/// Represents replication metadata for an Active Directory attribute.
/// Deserialized from the <c>msDS-ReplAttributeMetaData</c> attribute XML format.
/// </summary>
[XmlRoot("DS_REPL_ATTR_META_DATA")]
public class ReplAttributeMetaData
{
    /// <summary>
    /// Gets or sets the attribute name.
    /// </summary>
    [XmlElement("pszAttributeName")]
    public string? AttributeName { get; set; }

    /// <summary>
    /// Gets or sets the version number of the attribute, incremented with each change.
    /// </summary>
    [XmlElement("dwVersion")]
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the time of the last originating change in file time format.
    /// </summary>
    [XmlElement("ftimeLastOriginatingChange")]
    public string? LastOriginatingChangeTime { get; set; }

    /// <summary>
    /// Gets or sets the invocation ID of the DSA at which the last originating change was made.
    /// </summary>
    [XmlElement("uuidLastOriginatingDsaInvocationID")]
    public string? LastOriginatingDsaInvocationId { get; set; }

    /// <summary>
    /// Gets or sets the USN assigned at the originating DSA for the last change.
    /// </summary>
    [XmlElement("usnOriginatingChange")]
    public long OriginatingChangeUsn { get; set; }

    /// <summary>
    /// Gets or sets the local USN at which the last change to this attribute was applied.
    /// </summary>
    [XmlElement("usnLocalChange")]
    public long LocalChangeUsn { get; set; }

    /// <summary>
    /// Gets or sets the distinguished name of the server where the last originating change was made.
    /// </summary>
    [XmlElement("pszLastOriginatingDsaDN")]
    public string? LastOriginatingDsaDn { get; set; }

    /// <summary>
    /// Parses the <see cref="LastOriginatingChangeTime"/> to a <see cref="DateTime"/> value.
    /// </summary>
    /// <returns>The parsed <see cref="DateTime"/>, or <c>null</c> if the value cannot be parsed.</returns>
    public DateTime? GetLastOriginatingChangeDateTime()
    {
        if (string.IsNullOrWhiteSpace(LastOriginatingChangeTime))
        {
            return null;
        }

        if (long.TryParse(LastOriginatingChangeTime, out long fileTime) && fileTime > 0)
        {
            try
            {
                return DateTime.FromFileTimeUtc(fileTime);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
}

/// <summary>
/// Container for deserializing a collection of <see cref="ReplAttributeMetaData"/> entries.
/// </summary>
[XmlRoot("DS_REPL_ATTR_META_DATA_BLOB")]
public class ReplAttributeMetaDataCollection
{
    /// <summary>
    /// Gets or sets the list of replication attribute metadata entries.
    /// </summary>
    [XmlElement("DS_REPL_ATTR_META_DATA")]
    public List<ReplAttributeMetaData> Entries { get; set; } = [];
}
