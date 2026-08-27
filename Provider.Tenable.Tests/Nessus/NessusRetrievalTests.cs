using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Types;
using PenguinConverters.Syntra.Provider.RESTful.Source;
using PenguinConverters.Syntra.Provider.Tenable.Nessus;

namespace PenguinConverters.Syntra.Provider.Tenable.Tests.Nessus;

[TestFixture]
public class NessusRetrievalTests
{
    #region Constants

    /// <summary>
    /// The text one export row carries in its plugin output cell, listing every suite a host
    /// offers under the protocol version it was offered on.
    /// </summary>
    private const string CipherOutput =
        "SSL Version : TLSv12\n"
        + "  ECDHE-RSA-AES256-GCM-SHA384   0xC0, 0x30   ECDHE-RSA  AES-GCM(256)\n"
        + "  ECDHE-RSA-AES128-GCM-SHA256   0xC0, 0x2F   ECDHE-RSA  AES-GCM(128)\n";

    #endregion

    #region Methods

    [Test]
    public async Task RetrieveAsync_WithTheNessusPlugin_ExpandsEachRowIntoItsObservations()
    {
        //Arrange
        string export =
            "IP Address,DNS Name,Plugin Name,Port,Plugin,Plugin Output\n"
            + "10.0.0.9,api01.example.com,SSL Cipher Suites Supported,443,21643,\""
            + CipherOutput
            + "\"";

        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Text(export));

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "tenable.example.com",
            EndPoint = "rest/report/4711/download",
            Plugin = Source.Plugin.Nessus,
            IdentityProperty = NessusProjector.FingerprintProperty
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        // The single scan row carried two observations; storing the row would have left both as
        // unqueryable text in one column.
        Assert.That(entities, Has.Count.EqualTo(2));
        Assert.That(entities[0]["Name"], Is.EqualTo("ECDHE-RSA-AES256-GCM-SHA384"));
        Assert.That(entities[0]["TLSVersion"], Is.EqualTo("TLSv12"));
        Assert.That(entities[0]["Code"], Is.EqualTo("0xc0, 0x30"));
        Assert.That(entities[0]["DNSName"], Is.EqualTo("api01.example.com"));
        Assert.That(entities[0]["ShortName"], Is.EqualTo("api01"));
        Assert.That(entities[0]["Port"], Is.EqualTo(443));
        Assert.That(entities[0]["Plugin"], Is.EqualTo(21643L));
        Assert.That(entities[0].Identifier, Is.Not.Null, "the fingerprint keys the record");
        Assert.That(entities[1]["Name"], Is.EqualTo("ECDHE-RSA-AES128-GCM-SHA256"));
    }

    [Test]
    public async Task RetrieveAsync_WithoutTheNessusPlugin_StoresTheRowAsItStands()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Text(
                "Plugin Name,Plugin Output\nSSL Cipher Suites Supported,\"SSL Version : TLSv12\""));

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "tenable.example.com",
            EndPoint = "rest/report/4711/download"
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities, Has.Count.EqualTo(1));
        Assert.That(entities[0]["Plugin Output"], Is.EqualTo("SSL Version : TLSv12"));
    }

    [Test]
    public async Task RetrieveAsync_WithAnAssignedContentReader_PrefersItOverTheConfiguredPlugin()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Text("anything"));

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "tenable.example.com",
            EndPoint = "rest/report/4711/download",
            Plugin = Source.Plugin.Nessus
        });

        provider.ContentReader = (_, _, _) => Custom();

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        // The delegate is the seam: a host assigning its own expansion replaces the built-in one
        // without touching the retrieval, the paging or the credentials.
        Assert.That(entities, Has.Count.EqualTo(1));
        Assert.That(entities[0]["custom"], Is.EqualTo("yes"));
    }

    [Test]
    public async Task RetrieveAsync_WithARowNoPluginModels_YieldsNothingForIt()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Text(
                "Plugin Name,Port,Plugin Output\nNessus Scan Information,0,\"Scan duration : 42 sec\""));

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "tenable.example.com",
            EndPoint = "rest/report/4711/download",
            Plugin = Source.Plugin.Nessus
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities, Is.Empty);
        Assert.That(provider.HadErrors, Is.False, "an unmodelled plugin is not a failure");
    }

    [Test]
    public void NessusContentReader_Create_BuildsTheDelegateFromAConfiguration()
    {
        //Arrange
        Source.Configuration configuration = new Source.Configuration { Delimiter = ';' };

        //Act
        Func<Stream, RESTful.Source.Configuration, CancellationToken, IAsyncEnumerable<QuickDictionary>> reader =
            NessusContentReader.Create(configuration, NullLogger.Instance);

        //Assert
        Assert.That(reader, Is.Not.Null);
    }

    /// <summary>
    /// Yields one record, standing in for a host's own expansion.
    /// </summary>
    /// <returns>The record.</returns>
    private static async IAsyncEnumerable<QuickDictionary> Custom()
    {
        await Task.CompletedTask.ConfigureAwait(false);

        QuickDictionary record = new QuickDictionary(StringComparer.OrdinalIgnoreCase)
        {
            ["custom"] = "yes"
        };

        yield return record;
    }

    /// <summary>
    /// Builds a provider reading through a stub transport.
    /// </summary>
    /// <param name="transport">The transport answering the requests.</param>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <returns>The provider.</returns>
    private static Provider Build(StubHttpMessageHandler transport, Source.Configuration configuration)
    {
        Provider provider = new Provider
        {
            Configuration = configuration,
            RestClient = new RestClient(new HttpClient(transport), NullLogger.Instance)
        };

        provider.SetLogger(NullLogger.Instance);

        return provider;
    }

    /// <summary>
    /// Drains a retrieval.
    /// </summary>
    /// <param name="provider">The provider to read.</param>
    /// <returns>Every entity the retrieval produced.</returns>
    private static async Task<List<IEntity>> CollectAsync(Provider provider)
    {
        List<IEntity> entities = [];

        await foreach (IEntity entity in provider.RetrieveAsync([]))
        {
            entities.Add(entity);
        }

        return entities;
    }

    #endregion
}
