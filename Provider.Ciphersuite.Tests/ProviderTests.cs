using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.Ciphersuite.Tests;

[TestFixture]
public class ProviderTests
{
    #region Methods

    [Test]
    public void Configuration_CarriesTheDefaultsTheCatalogueNeeds()
    {
        //Arrange
        //Act
        Source.Configuration configuration = new Source.Configuration();

        //Assert
        Assert.That(configuration.ResultPath, Is.EqualTo("ciphersuites"));
        Assert.That(configuration.IdentityProperty, Is.EqualTo("iana_name"));
        // No authentication section at all is what sends an anonymous request; the catalogue
        // takes no credentials.
        Assert.That(configuration.Authentication, Is.Null);
    }

    [Test]
    public async Task RetrieveAsync_UnwrapsEachCipherSuiteAndStampsItsIanaName()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """
            {"ciphersuites":[
              {"TLS_AES_256_GCM_SHA384":{"security":"recommended","tls_version":"TLS1.3"}},
              {"TLS_RSA_WITH_RC4_128_MD5":{"security":"insecure","tls_version":"TLS1.0"}}
            ]}
            """);

        Provider provider = Build(transport, new Source.Configuration { Host = "ciphersuite.info", EndPoint = "api/cs" });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities, Has.Count.EqualTo(2));
        Assert.That(entities[0].Identifier, Is.EqualTo("TLS_AES_256_GCM_SHA384"));
        Assert.That(entities[0]["iana_name"], Is.EqualTo("TLS_AES_256_GCM_SHA384"));
        Assert.That(entities[0]["security"], Is.EqualTo("recommended"));
        Assert.That(entities[1].Identifier, Is.EqualTo("TLS_RSA_WITH_RC4_128_MD5"));
        Assert.That(entities[1]["tls_version"], Is.EqualTo("TLS1.0"));
    }

    [Test]
    public async Task RetrieveAsync_WithARecordThatIsNotWrapped_CarriesItThroughUnchanged()
    {
        //Arrange
        // A catalogue that starts returning flat records should keep working rather than silently
        // yield nothing.
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"ciphersuites":[{"iana_name":"TLS_AES_128_GCM_SHA256","security":"recommended"}]}""");

        Provider provider = Build(transport, new Source.Configuration { Host = "ciphersuite.info", EndPoint = "api/cs" });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities, Has.Count.EqualTo(1));
        Assert.That(entities[0].Identifier, Is.EqualTo("TLS_AES_128_GCM_SHA256"));
        Assert.That(entities[0]["security"], Is.EqualTo("recommended"));
    }

    [Test]
    public async Task RetrieveAsync_SendsNoCredential()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""{"ciphersuites":[]}""");

        Provider provider = Build(transport, new Source.Configuration { Host = "ciphersuite.info", EndPoint = "api/cs" });

        //Act
        await CollectAsync(provider);

        //Assert
        Assert.That(transport.Requests[0].Headers.Authorization, Is.Null);
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
