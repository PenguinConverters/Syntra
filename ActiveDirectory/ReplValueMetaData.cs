using System.Xml.Serialization;

namespace PenguinConverters.Syntra.ActiveDirectory;

/// <summary>
/// Represents value-level replication metadata for an Active Directory attribute.
/// Deserialized from the <c>msDS-ReplValueMetaData</c> attribute XML format.
/// </summary>
[XmlRoot("DS_REPL_VALUE_META_DATA")]
public class ReplValueMetaData
{
    #region Constants

    /// <summary>
    /// The constructed attribute that exposes value-level replication metadata on a directory object.
    /// It is only returned when requested explicitly, and its values must be read through range
    /// retrieval because an object may carry more link values than a single response can hold.
    /// </summary>
    public const string Attribute = "msDS-ReplValueMetaData";

    #endregion

    #region Fields

    private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(ReplValueMetaData));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the attribute name.
    /// </summary>
    [XmlElement("pszAttributeName")]
    public string? AttributeName { get; set; }

    /// <summary>
    /// Gets or sets the distinguished name of the object this value is associated with.
    /// </summary>
    [XmlElement("pszObjectDn")]
    public string? ObjectDn { get; set; }

    /// <summary>
    /// Gets or sets the value size in bytes.
    /// </summary>
    [XmlElement("cbData")]
    public int DataSize { get; set; }

    /// <summary>
    /// Gets or sets the version number of the value, incremented with each change.
    /// </summary>
    [XmlElement("dwVersion")]
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the time of the last originating change in file time format.
    /// </summary>
    [XmlElement("ftimeLastOriginatingChange")]
    public string? LastOriginatingChangeTime { get; set; }

    /// <summary>
    /// Gets or sets the time this value was created in file time format.
    /// </summary>
    [XmlElement("ftimeCreated")]
    public string? CreatedTime { get; set; }

    /// <summary>
    /// Gets or sets the time this value was deleted in file time format.
    /// A value of <c>0</c> indicates the value has not been deleted.
    /// </summary>
    [XmlElement("ftimeDeleted")]
    public string? DeletedTime { get; set; }

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
    /// Gets or sets the local USN at which the last change was applied.
    /// </summary>
    [XmlElement("usnLocalChange")]
    public long LocalChangeUsn { get; set; }

    /// <summary>
    /// Gets or sets the distinguished name of the server where the last originating change was made.
    /// </summary>
    [XmlElement("pszLastOriginatingDsaDN")]
    public string? LastOriginatingDsaDn { get; set; }

    /// <summary>
    /// Gets a value indicating whether this value has been deleted.
    /// </summary>
    public bool IsDeleted
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DeletedTime))
            {
                return false;
            }

            return long.TryParse(DeletedTime, out long deletedFileTime) && deletedFileTime > 0;
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// Attempts to parse a single <c>msDS-ReplValueMetaData</c> value.
    /// </summary>
    /// <param name="value">The XML fragment of one metadata entry.</param>
    /// <param name="metaData">
    /// When this method returns, contains the parsed metadata, or <c>null</c> when parsing failed.
    /// </param>
    /// <returns><c>true</c> if the value was parsed; otherwise, <c>false</c>.</returns>
    public static bool TryParse(string? value, out ReplValueMetaData? metaData)
    {
        metaData = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using StringReader reader = new StringReader(value);
            metaData = (ReplValueMetaData?)Serializer.Deserialize(reader);

            return metaData is not null;
        }
        catch (InvalidOperationException)
        {
            // Every deserialization failure surfaces wrapped in InvalidOperationException.
            return false;
        }
    }

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

    #endregion
}

/// <summary>
/// Container for deserializing a collection of <see cref="ReplValueMetaData"/> entries.
/// </summary>
[XmlRoot("DS_REPL_VALUE_META_DATA_BLOB")]
public class ReplValueMetaDataCollection
{
    #region Properties

    /// <summary>
    /// Gets or sets the list of replication value metadata entries.
    /// </summary>
    [XmlElement("DS_REPL_VALUE_META_DATA")]
    public List<ReplValueMetaData> Entries { get; set; } = [];

    #endregion
}
