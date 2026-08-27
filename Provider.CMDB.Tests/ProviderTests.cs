using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Provider.CMDB.Source;
using PenguinConverters.Syntra.Provider.RESTful.Settings;
using PenguinConverters.Syntra.Provider.RESTful.Source;
using Configuration = PenguinConverters.Syntra.Provider.CMDB.Source.Configuration;

namespace PenguinConverters.Syntra.Provider.CMDB.Tests;

[TestFixture]
public class ProviderTests
{
    #region Methods

    [Test]
    public void Configuration_CarriesTheDefaultsTheApiNeeds()
    {
        //Arrange
        //Act
        Configuration configuration = new Configuration();

        //Assert
        Assert.That(configuration.ResultPath, Is.EqualTo("entries"));
        Assert.That(configuration.EntryPath, Is.EqualTo("values"));
        Assert.That(configuration.PropertiesParameter, Is.EqualTo("fields"));
        Assert.That(configuration.PropertiesFormat, Is.EqualTo("values({0})"));
        Assert.That(configuration.OffsetProperty, Is.EqualTo("Modified Date"));
        Assert.That(configuration.DeletedProperty, Is.EqualTo("Mark As Deleted"));
        Assert.That(configuration.DeletedValue, Is.EqualTo("Yes"));
        Assert.That(configuration.Pagination!.Mode, Is.EqualTo(PaginationMode.NextLink));
        Assert.That(configuration.Pagination.NextLinkPath, Is.EqualTo("_links.next.0.href"));
        Assert.That(configuration.Authentication!.Mode, Is.EqualTo(AuthenticationMode.Session));
        Assert.That(configuration.Authentication.Scheme, Is.EqualTo("AR-JWT"));
        Assert.That(configuration.Authentication.TokenEndPoint, Is.EqualTo("/api/jwt/login"));
        Assert.That(configuration.Authentication.LogoutEndPoint, Is.EqualTo("/api/jwt/logout"));
        Assert.That(configuration.HttpHeaders!["X-AR-Client-Type"], Is.EqualTo("34"));
    }

    [Test]
    public async Task RetrieveAsync_ReadsTheEnvelopeAndFollowsTheLinks()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"entries":[{"values":{"Request ID":"INC01","Status":"Open"}}],"_links":{"next":[{"href":"https://cmdb/api/page2"}]}}""",
            """{"entries":[{"values":{"Request ID":"INC02","Status":"Closed"}}]}""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "cmdb",
            EndPoint = "api/arsys/v1/entry/HPD:Help Desk",
            IdentityProperty = "Request ID"
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities, Has.Count.EqualTo(2));
        Assert.That(entities[0].Identifier, Is.EqualTo("INC01"));
        Assert.That(entities[1]["Status"], Is.EqualTo("Closed"));
        Assert.That(transport.RequestUris[1], Is.EqualTo("https://cmdb/api/page2"));
    }

    [Test]
    public async Task RetrieveAsync_CoercesTheModificationTimestampToADateTime()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"entries":[{"values":{"Request ID":"INC01","Modified Date":"2026-03-05T08:30:00.000Z"}}]}""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "cmdb",
            EndPoint = "records",
            IdentityProperty = "Request ID",
            Delta = true
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(
            entities[0]["Modified Date"],
            Is.EqualTo(new DateTime(2026, 3, 5, 8, 30, 0, DateTimeKind.Utc)));

        Assert.That(
            provider.State.Offset,
            Is.EqualTo(new DateTime(2026, 3, 5, 8, 30, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task RetrieveAsync_WithAValueTheFormatDoesNotDescribe_LeavesItAsText()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"entries":[{"values":{"Request ID":"INC01","Modified Date":"not a date"}}]}""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "cmdb",
            EndPoint = "records",
            IdentityProperty = "Request ID"
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities[0]["Modified Date"], Is.EqualTo("not a date"));
    }

    [Test]
    public async Task RetrieveAsync_MarksADeletedRecordAndProjectsTheFieldsItNeeds()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"entries":[{"values":{"Request ID":"INC01","Mark As Deleted":"Yes"}}]}""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "cmdb",
            EndPoint = "records",
            IdentityProperty = "Request ID",
            Delta = true
        });

        provider.State = new State
        {
            Offset = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndPoint = "records"
        };

        //Act
        List<IEntity> entities = await CollectAsync(provider, "Status");

        //Assert
        Assert.That(entities[0].State, Is.EqualTo(EntityState.Deleted));

        string uri = Uri.UnescapeDataString(transport.RequestUris[0]);

        // The consumer asked only for Status, but a delta run cannot advance without the
        // timestamp and cannot see a removal without the deletion marker.
        Assert.That(uri, Does.Contain("Status"));
        Assert.That(uri, Does.Contain("Modified Date"));
        Assert.That(uri, Does.Contain("Mark As Deleted"));
        Assert.That(uri, Does.Contain("Request ID"));
        Assert.That(uri, Does.Contain("fields=values("));
        Assert.That(uri, Does.Contain("q='Modified Date' > \"2026-01-01T00:00:00.000Z\""));
    }

    [Test]
    public void ProviderBuilder_BuildsTheCmdbProvider()
    {
        //Arrange
        ProviderBuilder builder = new ProviderBuilder();

        builder.AddLogger(NullLogger.Instance);
        builder.AddDeserializer((bytes, type) =>
            System.Text.Json.JsonSerializer.Deserialize(Encoding.UTF8.GetString(bytes), type)!);
        builder.AddConfiguration(Encoding.UTF8.GetBytes(
            """{"Host":"cmdb","EndPoint":"api/arsys/v1/entry/HPD:Help Desk"}"""));

        //Act
        object built = builder.Build();

        //Assert
        Assert.That(built, Is.InstanceOf<Provider>());

        Provider provider = (Provider)built;

        Assert.That(provider.Configuration, Is.InstanceOf<Configuration>());
        Assert.That(provider.Configuration!.ResultPath, Is.EqualTo("entries"));
        Assert.That(provider.RestClient, Is.Not.Null);

        provider.Dispose();
    }

    [Test]
    public void ProviderBuilder_KeepsTheSessionLoginWhenTheFileNamesOnlyCredentials()
    {
        //Arrange
        // Deserialization replaces the whole Authentication section rather than merging into it,
        // so a file naming only a username and a password would otherwise take the JWT login, the
        // logout and the AR-JWT scheme down with it and go out anonymous.
        ProviderBuilder builder = new ProviderBuilder();

        builder.AddLogger(NullLogger.Instance);
        builder.AddDeserializer((bytes, type) =>
            System.Text.Json.JsonSerializer.Deserialize(Encoding.UTF8.GetString(bytes), type)!);
        builder.AddConfiguration(Encoding.UTF8.GetBytes(
            """
            {
              "Host": "cmdb",
              "EndPoint": "api/arsys/v1/entry/HPD:Help Desk",
              "Authentication": { "Username": { "Value": "svc" }, "Password": { "Value": "pw" } }
            }
            """));

        //Act
        Provider provider = (Provider)builder.Build();

        //Assert
        AuthenticationSettings authentication = provider.Configuration!.Authentication!;

        Assert.That(authentication.Mode, Is.EqualTo(AuthenticationMode.Session));
        Assert.That(authentication.Scheme, Is.EqualTo("AR-JWT"));
        Assert.That(authentication.TokenEndPoint, Is.EqualTo("/api/jwt/login"));
        Assert.That(authentication.LogoutEndPoint, Is.EqualTo("/api/jwt/logout"));
        Assert.That(provider.Configuration.ResultPath, Is.EqualTo("entries"));
        Assert.That(provider.Configuration.PropertiesFormat, Is.EqualTo("values({0})"));

        provider.Dispose();
    }

    [Test]
    public void ProviderBuilder_LetsTheFileOverrideADefault()
    {
        //Arrange
        ProviderBuilder builder = new ProviderBuilder();

        builder.AddLogger(NullLogger.Instance);
        builder.AddDeserializer((bytes, type) =>
            System.Text.Json.JsonSerializer.Deserialize(Encoding.UTF8.GetString(bytes), type)!);
        builder.AddConfiguration(Encoding.UTF8.GetBytes(
            """
            {
              "Host": "cmdb",
              "EndPoint": "records",
              "DeletedProperty": "Retired",
              "Authentication": { "TokenEndPoint": "/custom/login" }
            }
            """));

        //Act
        Provider provider = (Provider)builder.Build();

        //Assert
        // Restoring a default must never overwrite something the file actually stated.
        Assert.That(provider.Configuration!.DeletedProperty, Is.EqualTo("Retired"));
        Assert.That(provider.Configuration.Authentication!.TokenEndPoint, Is.EqualTo("/custom/login"));
        Assert.That(provider.Configuration.Authentication.LogoutEndPoint, Is.EqualTo("/api/jwt/logout"));

        provider.Dispose();
    }

    /// <summary>
    /// Builds a provider reading through a stub transport.
    /// </summary>
    /// <param name="transport">The transport answering the requests.</param>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <returns>The provider.</returns>
    private static Provider Build(StubHttpMessageHandler transport, Configuration configuration)
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
