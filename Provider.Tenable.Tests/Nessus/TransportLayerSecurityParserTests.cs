using PenguinConverters.Syntra.Provider.Tenable.Nessus;

namespace PenguinConverters.Syntra.Provider.Tenable.Tests.Nessus;

[TestFixture]
public class TransportLayerSecurityParserTests
{
    #region Constants

    private const string CipherOutput = """
        Here is the list of SSL ciphers supported by the remote server :
        Each group is reported per SSL Version.

        SSL Version : TLSv12
          High Strength Ciphers (>= 112-bit key)

            ECDHE-RSA-AES256-GCM-SHA384   0xC0, 0x30   ECDHE-RSA  AES-GCM(256)
            ECDHE-RSA-AES128-GCM-SHA256   0xC0, 0x2F   ECDHE-RSA  AES-GCM(128)

        SSL Version : TLSv13
          High Strength Ciphers (>= 112-bit key)

            TLS13-AES-256-GCM-SHA384      0x13, 0x02   ECDHE      AES-GCM(256)
        """;

    #endregion

    #region Methods

    [Test]
    public void Parse_ReadsEverySuiteUnderItsProtocolVersion()
    {
        //Arrange
        //Act
        List<CipherSuite> suites = TransportLayerSecurityParser.Parse(CipherOutput, Plugin());

        //Assert
        Assert.That(suites, Has.Count.EqualTo(3));

        Assert.That(
            suites.Where(suite => suite.TLSVersion == "TLSv12").Select(suite => suite.Name),
            Is.EqualTo(new[] { "ECDHE-RSA-AES256-GCM-SHA384", "ECDHE-RSA-AES128-GCM-SHA256" }));

        Assert.That(suites[2].TLSVersion, Is.EqualTo("TLSv13"));
        Assert.That(suites[2].Name, Is.EqualTo("TLS13-AES-256-GCM-SHA384"));
    }

    [Test]
    public void Parse_ReadsTheWireCodeOfEachSuite()
    {
        //Arrange
        //Act
        List<CipherSuite> suites = TransportLayerSecurityParser.Parse(CipherOutput, Plugin());

        //Assert
        Assert.That(suites[0].Code, Is.EqualTo(new[] { "0xc0", "0x30" }));
        Assert.That(suites[2].Code, Is.EqualTo(new[] { "0x13", "0x02" }));
    }

    [Test]
    public void Parse_StampsEverySuiteWithTheAssetItWasObservedOn()
    {
        //Arrange
        //Act
        List<CipherSuite> suites = TransportLayerSecurityParser.Parse(CipherOutput, Plugin());

        //Assert
        Assert.That(suites[0].IPAddress, Is.EqualTo("10.0.0.9"));
        Assert.That(suites[0].ShortName, Is.EqualTo("api01"));
        Assert.That(suites[0].Protocol, Is.EqualTo("TCP"));
        Assert.That(suites[0].Port, Is.EqualTo(443));
        Assert.That(suites[0].LastObserved, Is.EqualTo(new DateOnly(2026, 3, 4)));
    }

    [Test]
    public void Parse_IgnoresTheHeadingsBeforeTheFirstProtocolVersion()
    {
        //Arrange
        //Act
        List<CipherSuite> suites = TransportLayerSecurityParser.Parse(CipherOutput, Plugin());

        //Assert
        // Only a line carrying a wire code is a suite; the prose above the first version is not.
        Assert.That(suites.Any(suite => suite.Name.Contains("SUPPORTED", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void Parse_WithNoOutput_ReturnsNothing()
    {
        //Arrange
        //Act
        List<CipherSuite> suites = TransportLayerSecurityParser.Parse(null);

        //Assert
        Assert.That(suites, Is.Empty);
    }

    [Test]
    public void Parse_WithNoVersionLine_ReturnsNothing()
    {
        //Arrange
        // A suite is only meaningful under the version that offered it.
        string output = "  ECDHE-RSA-AES256-GCM-SHA384   0xC0, 0x30   ECDHE-RSA  AES-GCM(256)";

        //Act
        List<CipherSuite> suites = TransportLayerSecurityParser.Parse(output, Plugin());

        //Assert
        Assert.That(suites, Is.Empty);
    }

    /// <summary>
    /// Returns the export row the parser stamps its records from.
    /// </summary>
    /// <returns>The row.</returns>
    private static NessusPlugin Plugin()
    {
        return new NessusPlugin
        {
            IPAddress = "10.0.0.9",
            DNSName = "api01.example.com",
            PluginName = "SSL Cipher Suites Supported",
            Port = 443,
            Plugin = 21643,
            FirstDiscovered = new DateOnly(2026, 1, 2),
            LastObserved = new DateOnly(2026, 3, 4)
        };
    }

    #endregion
}
