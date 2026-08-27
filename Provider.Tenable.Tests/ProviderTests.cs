using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Provider.RESTful.Source;
using PenguinConverters.Syntra.Provider.Tenable.Source;

namespace PenguinConverters.Syntra.Provider.Tenable.Tests;

[TestFixture]
public class ProviderTests
{
    #region Methods

    [Test]
    public void Configuration_CarriesTheDefaultsTheApiNeeds()
    {
        //Arrange
        //Act
        Source.Configuration configuration = new Source.Configuration();

        //Assert
        Assert.That(configuration.Accept, Is.EqualTo("text/csv"));
        Assert.That(configuration.Delimiter, Is.EqualTo(','));
        Assert.That(configuration.Authentication!.Mode, Is.EqualTo(AuthenticationMode.ApiKey));
        Assert.That(configuration.Authentication.HeaderName, Is.EqualTo("x-apikey"));
        Assert.That(configuration.Authentication.ValueFormat, Is.EqualTo("accesskey={0};secretkey={1};"));
        Assert.That(configuration.ReportEndPoint, Is.EqualTo("rest/report"));
        Assert.That(configuration.ReportResultPath, Is.EqualTo("response.usable"));
    }

    [Test]
    public void GetEncoding_WithAnUnknownName_FallsBackToUtf8()
    {
        //Arrange
        Source.Configuration configuration = new Source.Configuration { Encoding = "not-an-encoding" };

        //Act
        Encoding encoding = configuration.GetEncoding();

        //Assert
        Assert.That(encoding, Is.EqualTo(Encoding.UTF8));
    }

    [Test]
    public async Task RetrieveAsync_ReadsTheExportAsDelimitedRatherThanJson()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Text(
                "Plugin ID,Name,Severity\n10863,\"SSL Certificate, self-signed\",Medium\n51192,SSL Untrusted,High"));

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "tenable.example.com",
            EndPoint = "rest/report/4711/download",
            IdentityProperty = "Plugin ID"
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities, Has.Count.EqualTo(2));
        Assert.That(entities[0].Identifier, Is.EqualTo("10863"));
        Assert.That(entities[0]["Name"], Is.EqualTo("SSL Certificate, self-signed"));
        Assert.That(entities[1]["Severity"], Is.EqualTo("High"));
    }

    [Test]
    public async Task RetrieveAsync_WithASemicolonExport_HonoursTheConfiguredDelimiter()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Text("Plugin ID;Severity\n10863;Medium"));

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "tenable.example.com",
            EndPoint = "rest/report/4711/download",
            Delimiter = ';'
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities[0]["Severity"], Is.EqualTo("Medium"));
    }

    [Test]
    public async Task RetrieveAsync_WithAReportNameInTheEndpoint_ResolvesTheMostRecentRun()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("/rest/report", StringComparison.Ordinal)
                ? StubHttpMessageHandler.Json(
                    """
                    {"response":{"usable":[
                      {"id":"41","name":"Weekly Scan"},
                      {"id":"77","name":"Weekly Scan"},
                      {"id":"99","name":"Monthly Scan"}
                    ]}}
                    """)
                : StubHttpMessageHandler.Text("Plugin ID,Severity\n10863,Medium"));

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "tenable.example.com",
            EndPoint = "rest/report/<%ReportId(Weekly Scan)%>/download"
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        // A report keeps its name across runs and takes a new identifier each time, so the
        // highest is the most recent.
        Assert.That(entities, Has.Count.EqualTo(1));
        Assert.That(transport.RequestUris[0], Is.EqualTo("https://tenable.example.com/rest/report"));
        Assert.That(transport.RequestUris[1], Is.EqualTo("https://tenable.example.com/rest/report/77/download"));
    }

    [Test]
    public async Task RetrieveAsync_WithTheLegacyFunctionName_ResolvesItToo()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("/rest/report", StringComparison.Ordinal)
                ? StubHttpMessageHandler.Json("""{"response":{"usable":[{"id":"12","name":"Weekly Scan"}]}}""")
                : StubHttpMessageHandler.Text("Plugin ID\n10863"));

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "tenable.example.com",
            EndPoint = "rest/report/<%GETReportMaxId(Weekly Scan)%>/download"
        });

        //Act
        await CollectAsync(provider);

        //Assert
        Assert.That(transport.RequestUris[1], Is.EqualTo("https://tenable.example.com/rest/report/12/download"));
    }

    [Test]
    public async Task RetrieveAsync_WithAReportNameThatIsNotListed_ReportsAndLeavesTheEndpointAlone()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("/rest/report", StringComparison.Ordinal)
                ? StubHttpMessageHandler.Json("""{"response":{"usable":[{"id":"1","name":"Other"}]}}""")
                : StubHttpMessageHandler.Text("Plugin ID\n10863"));

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "tenable.example.com",
            EndPoint = "rest/report/<%ReportId(Missing)%>/download"
        });

        //Act
        await CollectAsync(provider);

        //Assert
        // The placeholder is left as written rather than being replaced with a wrong identifier.
        Assert.That(
            Uri.UnescapeDataString(transport.RequestUris[1]),
            Does.Contain("<%ReportId(Missing)%>"));
    }

    [Test]
    public async Task RetrieveAsync_WithoutAPlaceholder_ReadsTheEndpointDirectly()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Text("Plugin ID\n10863"));

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "tenable.example.com",
            EndPoint = "rest/report/4711/download"
        });

        //Act
        await CollectAsync(provider);

        //Assert
        Assert.That(transport.RequestUris, Has.Count.EqualTo(1), "no report listing is read");
    }

    [Test]
    public void ProviderBuilder_KeepsTheApiKeyShapeWhenTheFileNamesOnlyTheKeys()
    {
        //Arrange
        ProviderBuilder builder = new ProviderBuilder();

        builder.AddLogger(NullLogger.Instance);
        builder.AddDeserializer((bytes, type) =>
            System.Text.Json.JsonSerializer.Deserialize(Encoding.UTF8.GetString(bytes), type)!);
        builder.AddConfiguration(Encoding.UTF8.GetBytes(
            """
            {
              "Host": "tenable.example.com",
              "EndPoint": "rest/report/4711/download",
              "Authentication": { "Key": { "Value": "AAA" }, "SecondaryKey": { "Value": "BBB" } }
            }
            """));

        //Act
        Provider provider = (Provider)builder.Build();

        //Assert
        Assert.That(provider.Configuration, Is.InstanceOf<Source.Configuration>());
        Assert.That(provider.Configuration!.Authentication!.Mode, Is.EqualTo(AuthenticationMode.ApiKey));
        Assert.That(provider.Configuration.Authentication.HeaderName, Is.EqualTo("x-apikey"));
        Assert.That(provider.Configuration.Authentication.ValueFormat, Is.EqualTo("accesskey={0};secretkey={1};"));
        Assert.That(provider.Configuration.Accept, Is.EqualTo("text/csv"));

        provider.Dispose();
    }

    [TestCase("rest/report/<%ReportId(Weekly)%>/download", true, "ReportId", "Weekly")]
    [TestCase("rest/report/<%GETReportMaxId(A, B)%>/x", true, "GETReportMaxId", "A")]
    [TestCase("devices/<%id%>/policies", false, "", "")]
    [TestCase("rest/report/4711/download", false, "", "")]
    public void FunctionPlaceholder_ReadsOnlyACall(
        string endPoint, bool expected, string name, string firstArgument)
    {
        //Arrange
        //Act
        bool parsed = FunctionPlaceholder.TryParse(
            endPoint, out _, out string parsedName, out string[] arguments);

        //Assert
        Assert.That(parsed, Is.EqualTo(expected));

        if (expected)
        {
            Assert.That(parsedName, Is.EqualTo(name));
            Assert.That(arguments[0], Is.EqualTo(firstArgument));
        }
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
    /// <param name="properties">The properties the consumer asks for.</param>
    /// <returns>Every entity the retrieval produced.</returns>
    private static async Task<List<IEntity>> CollectAsync(Provider provider, params string[] properties)
    {
        List<IEntity> entities = [];

        await foreach (IEntity entity in provider.RetrieveAsync(properties))
        {
            entities.Add(entity);
        }

        return entities;
    }

    #endregion
}
