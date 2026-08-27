using PenguinConverters.Syntra.Provider.Tenable.Nessus;

namespace PenguinConverters.Syntra.Provider.Tenable.Tests.Nessus;

[TestFixture]
public class SecureShellParserTests
{
    #region Constants

    private const string VersionOutput = """
        The remote SSH daemon supports the following versions of the
        SSH protocol :

          - 1.99
          - 2.0
        """;

    private const string AlgorithmOutput = """
        Nessus negotiated the following encryption algorithm with the server :

        The server supports the following options for kex_algorithms :

          curve25519-sha256
          curve25519-sha256@libssh.org
          diffie-hellman-group14-sha1

        The server supports the following options for encryption_algorithms_client_to_server :

          aes128-ctr
          aes256-gcm@openssh.com

        The server supports the following options for mac_algorithms_client_to_server :

          hmac-sha2-256-etm@openssh.com
          hmac-sha1

        The server supports the following options for compression_algorithms_client_to_server :

          none

        The server supports the following options for server_host_key_algorithms :

          ssh-ed25519
        """;

    #endregion

    #region Methods

    [Test]
    public void ParseVersions_ReadsEveryBulletedVersion()
    {
        //Arrange
        NessusPlugin plugin = Plugin();

        //Act
        List<SecureShellVersion> versions = SecureShellParser.ParseVersions(VersionOutput, plugin);

        //Assert
        Assert.That(versions.Select(version => version.Version), Is.EqualTo(new[] { "1.99", "2.0" }));
        Assert.That(versions[0].IPAddress, Is.EqualTo("10.0.0.7"));
        Assert.That(versions[0].DNSName, Is.EqualTo("web01.example.com"));
        Assert.That(versions[0].ShortName, Is.EqualTo("web01"), "the domain is dropped from the short name");
        Assert.That(versions[0].Protocol, Is.EqualTo("TCP"));
        Assert.That(versions[0].Port, Is.EqualTo(22));
        Assert.That(versions[0].Plugin, Is.EqualTo(10881L));
    }

    [Test]
    public void ParseVersions_WithNoOutput_ReturnsNothing()
    {
        //Arrange
        //Act
        List<SecureShellVersion> versions = SecureShellParser.ParseVersions(null);

        //Assert
        Assert.That(versions, Is.Empty);
    }

    [Test]
    public void ParseAlgorithms_ReadsEveryAlgorithmOfEveryCategory()
    {
        //Arrange
        //Act
        List<SecureShellAlgorithm> algorithms = SecureShellParser.ParseAlgorithms(AlgorithmOutput, Plugin());

        //Assert
        Assert.That(algorithms, Has.Count.EqualTo(9));

        Assert.That(
            algorithms.Where(algorithm => algorithm.TypeGroup == "KEX").Select(algorithm => algorithm.Algorithm),
            Is.EquivalentTo(new[]
            {
                "curve25519-sha256",
                "curve25519-sha256@libssh.org",
                "diffie-hellman-group14-sha1"
            }));

        Assert.That(
            algorithms.Single(algorithm => algorithm.Algorithm == "ssh-ed25519").TypeGroup,
            Is.EqualTo("Host Key"));

        Assert.That(
            algorithms.Single(algorithm => algorithm.Algorithm == "none").TypeGroup,
            Is.EqualTo("Compression"));
    }

    [Test]
    public void ParseAlgorithms_ReducesAnAdvertisedNameToThePrimitive()
    {
        //Arrange
        //Act
        List<SecureShellAlgorithm> algorithms = SecureShellParser.ParseAlgorithms(AlgorithmOutput, Plugin());

        //Assert
        // A vendor suffix and the encrypt-then-MAC marker do not change which primitive is in use.
        Assert.That(
            algorithms.Single(algorithm => algorithm.Algorithm == "hmac-sha2-256-etm@openssh.com").AlgorithmClean,
            Is.EqualTo("hmac-sha2-256"));

        Assert.That(
            algorithms.Single(algorithm => algorithm.Algorithm == "aes256-gcm@openssh.com").AlgorithmClean,
            Is.EqualTo("aes256-gcm"));
    }

    [Test]
    public void ParseAlgorithms_WithAnAlgorithmListedTwice_KeepsItOnce()
    {
        //Arrange
        string output = """
            The server supports the following options for encryption_algorithms_client_to_server :

              aes128-ctr
              aes128-ctr
            """;

        //Act
        List<SecureShellAlgorithm> algorithms = SecureShellParser.ParseAlgorithms(output, Plugin());

        //Assert
        Assert.That(algorithms, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseAlgorithms_IgnoresThePreambleBeforeTheFirstCategory()
    {
        //Arrange
        //Act
        List<SecureShellAlgorithm> algorithms = SecureShellParser.ParseAlgorithms(AlgorithmOutput, Plugin());

        //Assert
        Assert.That(
            algorithms.Any(algorithm => algorithm.Algorithm.Contains("nessus", StringComparison.OrdinalIgnoreCase)),
            Is.False);
    }

    [TestCase("server_host_key_algorithms", "Host Key")]
    [TestCase("encryption_algorithms_client_to_server", "Encryption")]
    [TestCase("mac_algorithms_server_to_client", "MAC")]
    [TestCase("kex_algorithms", "KEX")]
    [TestCase("compression_algorithms_client_to_server", "Compression")]
    [TestCase("languages_client_to_server", "Other")]
    [TestCase("", "Other")]
    [TestCase(null, "Other")]
    public void GetTypeGroup_ClassifiesTheNegotiationType(string? type, string expected)
    {
        //Arrange
        //Act
        string group = SecureShellParser.GetTypeGroup(type);

        //Assert
        Assert.That(group, Is.EqualTo(expected));
    }

    /// <summary>
    /// Returns the export row the parsers stamp their records from.
    /// </summary>
    /// <returns>The row.</returns>
    private static NessusPlugin Plugin()
    {
        return new NessusPlugin
        {
            IPAddress = "10.0.0.7",
            DNSName = "web01.example.com",
            PluginName = "SSH Algorithms and Languages Supported",
            Port = 22,
            Plugin = 10881,
            FirstDiscovered = new DateOnly(2026, 1, 2),
            LastObserved = new DateOnly(2026, 3, 4)
        };
    }

    #endregion
}
