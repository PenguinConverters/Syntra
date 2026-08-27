using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.Infoblox.Tests;

[TestFixture]
public class ProviderTests
{
    #region Methods

    [Test]
    public void Configuration_CarriesTheDefaultsTheWapiNeeds()
    {
        //Arrange
        //Act
        Source.Configuration configuration = new Source.Configuration();

        //Assert
        Assert.That(configuration.ResultPath, Is.EqualTo("result"));
        Assert.That(configuration.IdentityProperty, Is.EqualTo("_ref"));
        Assert.That(configuration.PropertiesParameter, Is.EqualTo("_return_fields"));
        Assert.That(configuration.PropertiesToIgnore, Is.EqualTo(new[] { "_ref" }));
        Assert.That(configuration.Pagination!.Mode, Is.EqualTo(PaginationMode.Token));
        Assert.That(configuration.Pagination.TokenPath, Is.EqualTo("next_page_id"));
        Assert.That(configuration.Pagination.TokenParameter, Is.EqualTo("_page_id"));
        Assert.That(configuration.Pagination.PageSize, Is.EqualTo(1000));
        Assert.That(configuration.Authentication!.Mode, Is.EqualTo(AuthenticationMode.Basic));
        Assert.That(configuration.Parameters!["_return_as_object"], Is.EqualTo(1));
        Assert.That(configuration.Parameters["_paging"], Is.EqualTo(1));
    }

    [Test]
    public async Task RetrieveAsync_ReadsTheEnvelopeAndFollowsTheContinuationToken()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"result":[{"_ref":"record:host/AAA","name":"a.example.com"}],"next_page_id":"page2"}""",
            """{"result":[{"_ref":"record:host/BBB","name":"b.example.com"}]}""");

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "grid.example.com",
            EndPoint = "wapi/v2.12/record:host"
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider, "name");

        //Assert
        Assert.That(entities, Has.Count.EqualTo(2));
        Assert.That(entities[0].Identifier, Is.EqualTo("record:host/AAA"));
        Assert.That(entities[1]["name"], Is.EqualTo("b.example.com"));

        // A continuation token replaces the query, because the WAPI has already bound the
        // original one to it.
        Assert.That(transport.RequestUris[1], Is.EqualTo("https://grid.example.com/wapi/v2.12/record:host?_page_id=page2"));
    }

    [Test]
    public async Task RetrieveAsync_NamesTheObjectReferenceAsTheIdentityButNeverAsksForIt()
    {
        //Arrange
        // The WAPI returns _ref whether or not it was asked for, and rejects a request that names
        // it in _return_fields.
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""{"result":[]}""");

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "grid.example.com",
            EndPoint = "wapi/v2.12/record:host"
        });

        //Act
        await CollectAsync(provider, "name", "ipv4addr", "_ref");

        //Assert
        string uri = Uri.UnescapeDataString(transport.RequestUris[0]);

        Assert.That(uri, Does.Contain("_return_fields="));
        Assert.That(uri, Does.Contain("name"));
        Assert.That(uri, Does.Contain("ipv4addr"));
        Assert.That(uri, Does.Not.Contain("_return_fields=_ref"));
        Assert.That(uri, Does.Not.Contain(",_ref"));
        Assert.That(uri, Does.Not.Contain("_ref,"));
    }

    [Test]
    public async Task RetrieveAsync_AsksForTheEnvelopeAndThePageSize()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""{"result":[]}""");

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "grid.example.com",
            EndPoint = "wapi/v2.12/record:host"
        });

        //Act
        await CollectAsync(provider, "name");

        //Assert
        string uri = Uri.UnescapeDataString(transport.RequestUris[0]);

        Assert.That(uri, Does.Contain("_return_as_object=1"));
        Assert.That(uri, Does.Contain("_paging=1"));
        Assert.That(uri, Does.Contain("_max_results=1000"));
    }

    [Test]
    public void ProviderBuilder_BuildsTheInfobloxProviderWithBasicCredentials()
    {
        //Arrange
        ProviderBuilder builder = new ProviderBuilder();

        builder.AddLogger(NullLogger.Instance);
        builder.AddDeserializer((bytes, type) =>
            System.Text.Json.JsonSerializer.Deserialize(Encoding.UTF8.GetString(bytes), type)!);
        builder.AddConfiguration(Encoding.UTF8.GetBytes(
            """
            {
              "Host": "grid.example.com",
              "EndPoint": "wapi/v2.12/record:host",
              "Authentication": { "Username": { "Value": "admin" }, "Password": { "Value": "infoblox" } }
            }
            """));

        //Act
        object built = builder.Build();

        //Assert
        Assert.That(built, Is.InstanceOf<Provider>());

        Provider provider = (Provider)built;

        // The configuration file names only the credentials; the mode, the envelope and the
        // paging come from the derived configuration's defaults.
        Assert.That(provider.Configuration, Is.InstanceOf<Source.Configuration>());
        Assert.That(provider.Configuration!.Authentication!.Mode, Is.EqualTo(AuthenticationMode.Basic));
        Assert.That(provider.Configuration.ResultPath, Is.EqualTo("result"));
        Assert.That(provider.RestClient, Is.Not.Null);

        provider.Dispose();
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
