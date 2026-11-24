using System.Text;
using PenguinConverters.Syntra.Core.Settings;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PenguinConverters.Syntra.Core.Tests.Settings;

/// <summary>
/// Verifies that <see cref="ConnectorSection"/> hands a connector its complete configuration section.
/// </summary>
[TestFixture]
public class ConnectorSectionTests
{
    #region Constants

    /// <summary>
    /// A job configuration whose target section carries settings no host knows about.
    /// </summary>
    private const string Job = """
        ObjectNamespace: "ActiveDirectory.Users"
        Schedule: "0 2 * * *"
        Source:
          Type: "Example.Provider"
          BaseDN: "DC=corp,DC=example,DC=com"
          Port: 636
        Target:
          Type: "Example.Consumer"
          TableName: "dbo.ADUser"
          BatchSize: 500
          HasDeletedColumn: true
          ConnectionString:
            Value: "Server=.;Database=x"
            Protected: false
          PrimaryKeys:
            - objectGUID
          Columns:
            objectGUID:
              SourceProperty: "objectGuid"
            displayName: {}
        """;

    #endregion

    #region Methods

    /// <summary>
    /// Deserializes section content the way a host does for a connector.
    /// </summary>
    /// <typeparam name="T">The connector configuration type.</typeparam>
    /// <param name="section">The section to deserialize.</param>
    /// <returns>The connector configuration.</returns>
    private static T Deserialize<T>(ConnectorSection section)
    {
        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithCaseInsensitivePropertyMatching()
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<T>(Encoding.UTF8.GetString(section.Content));
    }

    /// <summary>
    /// Every setting of a section reaches the connector, including nested ones.
    /// </summary>
    [Test]
    public void Read_TargetSection_CarriesEverySetting()
    {
        //Arrange

        //Act
        ConnectorSection? section = ConnectorSection.Read(Job, "target");
        SampleTarget target = Deserialize<SampleTarget>(section!);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(section!.Type, Is.EqualTo("Example.Consumer"));
            Assert.That(target.TableName, Is.EqualTo("dbo.ADUser"));
            Assert.That(target.BatchSize, Is.EqualTo(500));
            Assert.That(target.HasDeletedColumn, Is.True);
            Assert.That(target.ConnectionString!["Value"], Is.EqualTo("Server=.;Database=x"));
            Assert.That(target.PrimaryKeys, Is.EqualTo(new[] { "objectGUID" }));
            Assert.That(target.Columns!.Keys, Is.EquivalentTo(new[] { "objectGUID", "displayName" }));
            Assert.That(target.Columns["objectGUID"].SourceProperty, Is.EqualTo("objectGuid"));
        });
    }

    /// <summary>
    /// The name of the synchronization is added to a section that does not set it, and an
    /// explicit value in the section is kept.
    /// </summary>
    /// <param name="sectionValue">The value the section sets itself, or <c>null</c> for none.</param>
    /// <param name="expected">The value the connector is expected to receive.</param>
    [TestCase(null, "Job.Name")]
    [TestCase("Own.Name", "Own.Name")]
    public void Read_ObjectNamespace_IsAddedUnlessTheSectionSetsIt(string? sectionValue, string expected)
    {
        //Arrange
        string job = sectionValue is null
            ? "target:\n  type: X\n"
            : $"target:\n  type: X\n  OBJECTNAMESPACE: {sectionValue}\n";

        //Act
        ConnectorSection? section = ConnectorSection.Read(job, "Target", "Job.Name");
        SampleTarget target = Deserialize<SampleTarget>(section!);

        //Assert
        Assert.That(target.ObjectNamespace, Is.EqualTo(expected));
    }

    /// <summary>
    /// A job file written as JSON is read like one written as YAML.
    /// </summary>
    [Test]
    public void Read_JsonDocument_IsRead()
    {
        //Arrange
        string job = "{ \"Source\": { \"Type\": \"Example.Provider\", \"BatchSize\": 7 } }";

        //Act
        ConnectorSection? section = ConnectorSection.Read(job, "source");
        SampleTarget settings = Deserialize<SampleTarget>(section!);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(section!.Type, Is.EqualTo("Example.Provider"));
            Assert.That(settings.BatchSize, Is.EqualTo(7));
        });
    }

    /// <summary>
    /// A document without the section yields nothing.
    /// </summary>
    /// <param name="document">The job configuration text.</param>
    [TestCase("")]
    [TestCase("Schedule: \"0 2 * * *\"")]
    public void Read_MissingSection_ReturnsNull(string document)
    {
        //Arrange

        //Act
        ConnectorSection? section = ConnectorSection.Read(document, "target");

        //Assert
        Assert.That(section, Is.Null);
    }

    /// <summary>
    /// A section that is not a mapping, or names no connector, is rejected.
    /// </summary>
    /// <param name="document">The job configuration text.</param>
    [TestCase("target: just-text")]
    [TestCase("target:\n  TableName: T\n")]
    [TestCase("target:\n  Type: \"\"\n")]
    public void Read_UnusableSection_Throws(string document)
    {
        //Arrange

        //Act
        Action action = () => ConnectorSection.Read(document, "target");

        //Assert
        Assert.Throws<InvalidOperationException>(action);
    }

    /// <summary>
    /// A section created for a connector type carries that type and nothing else.
    /// </summary>
    [Test]
    public void ForType_CarriesOnlyTheType()
    {
        //Arrange

        //Act
        ConnectorSection section = ConnectorSection.ForType("Example.Consumer");
        SampleTarget target = Deserialize<SampleTarget>(section);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(section.Type, Is.EqualTo("Example.Consumer"));
            Assert.That(target.TableName, Is.Null);
        });
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Connector configuration with settings the host does not know.
    /// </summary>
    private sealed class SampleTarget
    {
        #region Properties

        /// <summary>
        /// Gets or sets the target table name.
        /// </summary>
        public string? TableName { get; set; }

        /// <summary>
        /// Gets or sets the name of the synchronization.
        /// </summary>
        public string? ObjectNamespace { get; set; }

        /// <summary>
        /// Gets or sets the batch size.
        /// </summary>
        public int BatchSize { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether deletions are recorded in a column.
        /// </summary>
        public bool HasDeletedColumn { get; set; }

        /// <summary>
        /// Gets or sets the connection string settings.
        /// </summary>
        public Dictionary<string, string>? ConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the primary key column names.
        /// </summary>
        public List<string>? PrimaryKeys { get; set; }

        /// <summary>
        /// Gets or sets the column definitions by column name.
        /// </summary>
        public Dictionary<string, SampleColumn>? Columns { get; set; }

        #endregion
    }

    /// <summary>
    /// Column definition of the sample connector configuration.
    /// </summary>
    private sealed class SampleColumn
    {
        #region Properties

        /// <summary>
        /// Gets or sets the source property the column is read from.
        /// </summary>
        public string? SourceProperty { get; set; }

        #endregion
    }

    #endregion
}
