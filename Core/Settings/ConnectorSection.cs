using System.Text;
using YamlDotNet.RepresentationModel;

namespace PenguinConverters.Syntra.Core.Settings;

/// <summary>
/// One connector section of a job configuration document - <c>source</c> or <c>target</c> - kept
/// as the author wrote it, so that every setting of the section reaches the connector.
/// </summary>
/// <remarks>
/// A host reads only the connector assembly name from a section. Everything else in it belongs to
/// the connector, which deserializes <see cref="Content"/> into its own configuration type.
/// </remarks>
public sealed class ConnectorSection
{
    #region Constants

    /// <summary>
    /// Key naming the connector assembly within a section.
    /// </summary>
    private const string TypeKey = "type";

    /// <summary>
    /// Key under which the name of the synchronization is passed to the connector.
    /// </summary>
    private const string ObjectNamespaceKey = "objectNamespace";

    /// <summary>
    /// Root key stating whether the job is a delta synchronization, passed on to each section.
    /// </summary>
    private const string DeltaKey = "delta";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectorSection"/> class.
    /// </summary>
    /// <param name="type">The assembly name of the connector.</param>
    /// <param name="content">The section as UTF-8 encoded YAML.</param>
    private ConnectorSection(string type, byte[] content)
    {
        Type = type;
        Content = content;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the assembly name of the connector the section configures.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Gets the complete section as UTF-8 encoded YAML.
    /// </summary>
    public byte[] Content { get; }

    #endregion

    #region Methods

    /// <summary>
    /// Reads one connector section from a job configuration document.
    /// </summary>
    /// <param name="document">The job configuration as YAML or JSON text.</param>
    /// <param name="sectionName">The section key, matched without regard to case.</param>
    /// <param name="objectNamespace">
    /// The name of the synchronization to add to the section as <c>objectNamespace</c> when the
    /// section does not set it itself; <c>null</c> to add nothing.
    /// </param>
    /// <returns>
    /// The section, or <c>null</c> when the document has no such section. The <c>delta</c> value
    /// at the root of the document is added to a section that does not set <c>delta</c> itself.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the section is not a mapping or does not name a connector type.
    /// </exception>
    public static ConnectorSection? Read(string document, string sectionName, string? objectNamespace = null)
    {
        YamlStream stream = new YamlStream();

        using (StringReader reader = new StringReader(document))
        {
            stream.Load(reader);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            return null;

        YamlNode? node = Find(root, sectionName);

        if (node is null)
            return null;

        if (node is not YamlMappingNode section)
            throw new InvalidOperationException($"Configuration section '{sectionName}' must be a mapping.");

        string? type = (Find(section, TypeKey) as YamlScalarNode)?.Value;

        if (string.IsNullOrWhiteSpace(type))
        {
            throw new InvalidOperationException(
                $"Configuration section '{sectionName}' must name the connector assembly in '{TypeKey}'.");
        }

        if (!string.IsNullOrWhiteSpace(objectNamespace) && Find(section, ObjectNamespaceKey) is null)
            section.Add(ObjectNamespaceKey, objectNamespace);

        if (Find(section, DeltaKey) is null && Find(root, DeltaKey) is YamlScalarNode delta && delta.Value is not null)
            section.Add(DeltaKey, delta.Value);

        return new ConnectorSection(type.Trim(), Serialize(section));
    }

    /// <summary>
    /// Creates a section that names a connector and carries no further settings.
    /// </summary>
    /// <param name="type">The assembly name of the connector.</param>
    /// <returns>The section.</returns>
    public static ConnectorSection ForType(string type)
    {
        YamlMappingNode section = new YamlMappingNode();
        section.Add(TypeKey, type);

        return new ConnectorSection(type, Serialize(section));
    }

    /// <summary>
    /// Finds the value stored under a key of a mapping, matching the key without regard to case.
    /// </summary>
    /// <param name="mapping">The mapping to search.</param>
    /// <param name="key">The key to find.</param>
    /// <returns>The value node, or <c>null</c> when the mapping has no such key.</returns>
    private static YamlNode? Find(YamlMappingNode mapping, string key)
    {
        foreach (KeyValuePair<YamlNode, YamlNode> entry in mapping.Children)
        {
            if (entry.Key is YamlScalarNode scalar &&
                string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Writes a mapping as a YAML document of its own.
    /// </summary>
    /// <param name="section">The mapping to write.</param>
    /// <returns>The UTF-8 encoded YAML text.</returns>
    private static byte[] Serialize(YamlMappingNode section)
    {
        using StringWriter writer = new StringWriter();

        new YamlStream(new YamlDocument(section)).Save(writer, false);

        return Encoding.UTF8.GetBytes(writer.ToString());
    }

    #endregion
}
