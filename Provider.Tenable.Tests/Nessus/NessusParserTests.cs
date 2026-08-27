using PenguinConverters.Syntra.Core.Types;
using PenguinConverters.Syntra.Provider.Tenable.Nessus;

namespace PenguinConverters.Syntra.Provider.Tenable.Tests.Nessus;

[TestFixture]
public class NessusParserTests
{
    #region Methods

    [Test]
    public void FromRow_MapsTheProseColumnNamesOfAnExport()
    {
        //Arrange
        QuickDictionary row = Row(
            ("IP Address", "10.0.0.7"),
            ("DNS Name", "web01.example.com"),
            ("Plugin Name", "SSH Protocol Versions Supported"),
            ("Plugin Output", "  - 2.0"),
            ("Port", "22"),
            ("Plugin", "10881"),
            ("First Discovered", "Jan 2, 2026 08:15:00 UTC"),
            ("Last Observed", "Mar 4, 2026 21:00:00 UTC"));

        //Act
        NessusPlugin plugin = NessusPlugin.FromRow(row);

        //Assert
        Assert.That(plugin.IPAddress, Is.EqualTo("10.0.0.7"));
        Assert.That(plugin.ShortName, Is.EqualTo("web01"));
        Assert.That(plugin.Port, Is.EqualTo(22));
        Assert.That(plugin.Plugin, Is.EqualTo(10881L));
        Assert.That(plugin.FirstDiscovered, Is.EqualTo(new DateOnly(2026, 1, 2)));
        Assert.That(plugin.LastObserved, Is.EqualTo(new DateOnly(2026, 3, 4)));
    }

    [Test]
    public void FromRow_WithColumnsMissing_LeavesThemAtTheirDefaults()
    {
        //Arrange
        QuickDictionary row = Row(("Plugin Name", "SSH Protocol Versions Supported"));

        //Act
        NessusPlugin plugin = NessusPlugin.FromRow(row);

        //Assert
        Assert.That(plugin.IPAddress, Is.Empty);
        Assert.That(plugin.Port, Is.Zero);
        Assert.That(plugin.Plugin, Is.Null);
        Assert.That(plugin.FirstDiscovered, Is.Null);
        Assert.That(plugin.ShortName, Is.Null);
    }

    [Test]
    public void FromRow_WithAnUnparseableDate_LeavesItUnset()
    {
        //Arrange
        QuickDictionary row = Row(("First Discovered", "whenever"));

        //Act
        NessusPlugin plugin = NessusPlugin.FromRow(row);

        //Assert
        Assert.That(plugin.FirstDiscovered, Is.Null);
    }

    [Test]
    public void Expand_WithACipherSuiteRow_YieldsOneRecordPerSuite()
    {
        //Arrange
        QuickDictionary row = Row(
            ("IP Address", "10.0.0.9"),
            ("DNS Name", "api01.example.com"),
            ("Plugin Name", "SSL Cipher Suites Supported"),
            ("Port", "443"),
            ("Plugin Output",
                "SSL Version : TLSv12\n"
                + "  ECDHE-RSA-AES256-GCM-SHA384   0xC0, 0x30   ECDHE-RSA  AES-GCM(256)\n"
                + "  ECDHE-RSA-AES128-GCM-SHA256   0xC0, 0x2F   ECDHE-RSA  AES-GCM(128)\n"));

        //Act
        List<QuickDictionary> records = NessusParser.Expand(row).ToList();

        //Assert
        // One scan row carried two observations; storing the row would have left both as text.
        Assert.That(records, Has.Count.EqualTo(2));
        Assert.That(records[0]["Name"], Is.EqualTo("ECDHE-RSA-AES256-GCM-SHA384"));
        Assert.That(records[0]["TLSVersion"], Is.EqualTo("TLSv12"));
        Assert.That(records[0]["Port"], Is.EqualTo(443));
    }

    [Test]
    public void Expand_WithAnIkeRow_YieldsOneServiceRecordOverUdp()
    {
        //Arrange
        QuickDictionary row = Row(
            ("IP Address", "10.0.0.4"),
            ("DNS Name", "vpn01.example.com"),
            ("Plugin Name", "IPSEC Internet Key Exchange (IKE) Version 2 Detection"),
            ("Port", "500"));

        //Act
        List<QuickDictionary> records = NessusParser.Expand(row).ToList();

        //Assert
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0]["Protocol"], Is.EqualTo("UDP"));
        Assert.That(records[0]["ShortName"], Is.EqualTo("vpn01"));
    }

    [Test]
    public void Expand_WithAKerberosRow_YieldsOneServiceRecordOverTcp()
    {
        //Arrange
        QuickDictionary row = Row(
            ("IP Address", "10.0.0.5"),
            ("Plugin Name", "Kerberos Information Disclosure"),
            ("Port", "88"));

        //Act
        List<QuickDictionary> records = NessusParser.Expand(row).ToList();

        //Assert
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0]["Protocol"], Is.EqualTo("TCP"));
    }

    [Test]
    public void Expand_WithAnUnrecognisedPlugin_YieldsNothing()
    {
        //Arrange
        // The export is filtered to the plugins a scan policy selected, so a row this connector
        // models no observation for is not an error.
        QuickDictionary row = Row(("Plugin Name", "Nessus Scan Information"), ("Port", "0"));

        //Act
        List<QuickDictionary> records = NessusParser.Expand(row).ToList();

        //Assert
        Assert.That(records, Is.Empty);
    }

    [Test]
    public void Expand_StampsEveryRecordWithAContentFingerprint()
    {
        //Arrange
        QuickDictionary row = Row(
            ("IP Address", "10.0.0.7"),
            ("Plugin Name", "SSH Protocol Versions Supported"),
            ("Port", "22"),
            ("Plugin Output", "  - 1.99\n  - 2.0\n"));

        //Act
        List<QuickDictionary> records = NessusParser.Expand(row).ToList();

        //Assert
        Assert.That(records, Has.Count.EqualTo(2));
        Assert.That(records[0][NessusProjector.FingerprintProperty], Is.Not.Null);
        Assert.That(
            records[0][NessusProjector.FingerprintProperty],
            Is.Not.EqualTo(records[1][NessusProjector.FingerprintProperty]),
            "two different observations must not share a fingerprint");
    }

    [Test]
    public void Project_FlattensAMultiValuedPropertyIntoOneColumn()
    {
        //Arrange
        CipherSuite suite = new CipherSuite
        {
            Name = "ECDHE-RSA-AES256-GCM-SHA384",
            TLSVersion = "TLSv12",
            Code = ["0xc0", "0x30"]
        };

        //Act
        QuickDictionary properties = NessusProjector.Project(suite);

        //Assert
        // Left as a list, a consumer would persist the collection's type name.
        Assert.That(properties["Code"], Is.EqualTo("0xc0, 0x30"));
    }

    [Test]
    public void Fingerprint_IsStableAcrossPropertyOrderAndChangesWithContent()
    {
        //Arrange
        QuickDictionary first = Row(("Name", "a"), ("Version", "1"));
        QuickDictionary second = Row(("Version", "1"), ("Name", "a"));
        QuickDictionary third = Row(("Name", "a"), ("Version", "2"));

        //Act
        string one = NessusProjector.Fingerprint(first);
        string two = NessusProjector.Fingerprint(second);
        string three = NessusProjector.Fingerprint(third);

        //Assert
        Assert.That(one, Is.EqualTo(two), "the same content fingerprints alike whatever order it was written in");
        Assert.That(one, Is.Not.EqualTo(three));
    }

    [Test]
    public void GetKey_JoinsThePartsSoTwoServicesCannotCollide()
    {
        //Arrange
        // Concatenated, "10.0.0.11" on port 443 and "10.0.0.1" on port 1443 would read alike.
        NessusRecord first = new NessusRecord { IPAddress = "10.0.0.11", Protocol = "TCP", Port = 443 };
        NessusRecord second = new NessusRecord { IPAddress = "10.0.0.1", Protocol = "TCP", Port = 1443 };

        //Act
        //Assert
        Assert.That(first.GetKey(), Is.Not.EqualTo(second.GetKey()));
        Assert.That(first.GetKey(), Does.Contain("10.0.0.11"));
        Assert.That(first.GetKey(), Does.Contain("443"));
    }

    [Test]
    public void LastModified_TakesTheLaterOfTheTwoDates()
    {
        //Arrange
        NessusRecord record = new NessusRecord
        {
            FirstDiscovered = new DateOnly(2026, 1, 2),
            LastObserved = new DateOnly(2026, 3, 4)
        };

        //Act
        //Assert
        Assert.That(record.LastModified, Is.EqualTo(new DateOnly(2026, 3, 4)));
    }

    /// <summary>
    /// Builds an export row.
    /// </summary>
    /// <param name="cells">The columns and their values.</param>
    /// <returns>The row.</returns>
    private static QuickDictionary Row(params (string Column, string Value)[] cells)
    {
        QuickDictionary row = new QuickDictionary(StringComparer.OrdinalIgnoreCase);

        foreach ((string column, string value) in cells)
        {
            row[column] = value;
        }

        return row;
    }

    #endregion
}
