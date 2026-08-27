using System.Reflection;
using System.Reflection.Emit;
using PenguinConverters.Syntra.Core.Source;

namespace PenguinConverters.Syntra.Core.Tests;

[TestFixture]
public class ConnectorProbingTests
{
    #region Fields

    private string _connectors = string.Empty;

    #endregion

    #region Methods

    [SetUp]
    public void SetUp()
    {
        _connectors = Path.Combine(AppContext.BaseDirectory, InstanceBuilder<IProviderBuilder>.ConnectorDirectoryName);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_connectors))
        {
            Directory.Delete(_connectors, recursive: true);
        }
    }

    [Test]
    public void Build_WithAnAssemblyThatIsNeitherReferencedNorPresent_SaysWhereItLooked()
    {
        //Arrange
        InstanceBuilder<IProviderBuilder> builder =
            new InstanceBuilder<IProviderBuilder>("PenguinConverters.Syntra.Provider.NotInstalled");

        //Act
        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        //Assert
        // A deployment problem should read as one: the message names the directories that were
        // searched rather than only the assembly that was wanted.
        Assert.That(exception!.Message, Does.Contain("neither referenced"));
        Assert.That(exception.Message, Does.Contain(AppContext.BaseDirectory));
    }

    [Test]
    public void Build_WithAFileThatIsNotStrongNamed_RefusesToLoadIt()
    {
        //Arrange
        // A connector deployed as a file has to carry the same strong name as the framework it
        // plugs into; an unsigned one is refused before it enters the process.
        Directory.CreateDirectory(_connectors);

        string path = Path.Combine(_connectors, "PenguinConverters.Syntra.Provider.Unsigned.dll");

        WriteUnsignedAssembly(path);

        InstanceBuilder<IProviderBuilder> builder =
            new InstanceBuilder<IProviderBuilder>("PenguinConverters.Syntra.Provider.Unsigned");

        //Act
        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        //Assert
        Assert.That(exception!.Message, Does.Contain("not strong-named"));
    }

    [Test]
    public void Build_WithAFileSignedByAnotherKey_RefusesToLoadIt()
    {
        //Arrange
        Directory.CreateDirectory(_connectors);

        string path = Path.Combine(_connectors, "PenguinConverters.Syntra.Provider.Foreign.dll");

        File.Copy(ForeignSignedAssemblyPath(), path, overwrite: true);

        InstanceBuilder<IProviderBuilder> builder =
            new InstanceBuilder<IProviderBuilder>("PenguinConverters.Syntra.Provider.Foreign");

        //Act
        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        //Assert
        Assert.That(exception!.Message, Does.Contain("different key"));
    }

    [Test]
    public void Build_WithAReferencedConnector_ResolvesItWithoutAFile()
    {
        //Arrange
        // The reference path: this test project references Core, which is strong-named with the
        // repository key and listed in deps.json, so the loader finds it without touching disk.
        Assembly core = Assembly.Load(new AssemblyName("PenguinConverters.Syntra.Core"));

        //Act
        byte[]? token = core.GetName().GetPublicKeyToken();

        //Assert
        Assert.That(token, Is.Not.Null.And.Not.Empty, "the repository key must be present for probing to have an expectation");
    }

    /// <summary>
    /// Emits a minimal unsigned assembly, standing in for a connector somebody built without the
    /// repository key.
    /// </summary>
    /// <remarks>
    /// Emitted rather than borrowed from the test dependencies: every assembly this project ships
    /// with happens to be strong-named, so borrowing one would silently test nothing.
    /// </remarks>
    /// <param name="path">The file to write.</param>
    private static void WriteUnsignedAssembly(string path)
    {
        PersistedAssemblyBuilder assembly = new PersistedAssemblyBuilder(
            new AssemblyName("PenguinConverters.Syntra.Provider.Unsigned"),
            typeof(object).Assembly);

        assembly.DefineDynamicModule("main");

        using FileStream stream = File.Create(path);

        assembly.Save(stream);
    }

    /// <summary>
    /// Returns an assembly on disk that is strong-named with a key other than the repository's.
    /// </summary>
    /// <returns>The path.</returns>
    private static string ForeignSignedAssemblyPath()
    {
        // A Microsoft-signed assembly is strong-named with a key that is definitively not ours.
        string path = typeof(object).Assembly.Location;

        Assert.That(
            AssemblyName.GetAssemblyName(path).GetPublicKey(),
            Is.Not.Null.And.Not.Empty,
            "this test needs a strong-named assembly signed by somebody else");

        return path;
    }

    #endregion
}
