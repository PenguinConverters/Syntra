using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.Tufin.Tests;

[TestFixture]
public class ProviderTests
{
    #region Methods

    [Test]
    public void Configuration_CarriesTheDefaultsTheApplianceNeeds()
    {
        //Arrange
        //Act
        Source.Configuration configuration = new Source.Configuration();

        //Assert
        Assert.That(configuration.IdentityProperty, Is.EqualTo("id"));
        Assert.That(configuration.Authentication!.Mode, Is.EqualTo(AuthenticationMode.Basic));
    }

    [Test]
    public async Task RetrieveAsync_ReadsTheCollectionFromItsNestedPath()
    {
        //Arrange
        // Tufin nests its collection two levels deep under names that vary per endpoint, which a
        // result path expresses directly.
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"devices":{"device":[{"id":"1","name":"edge-fw"},{"id":"2","name":"core-fw"}]}}""");

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "tufin.example.com",
            EndPoint = "securetrack/api/devices",
            ResultPath = "devices.device"
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities, Has.Count.EqualTo(2));
        Assert.That(entities[0].Identifier, Is.EqualTo("1"));
        Assert.That(entities[1]["name"], Is.EqualTo("core-fw"));
    }

    [Test]
    public async Task RetrieveAsync_WalksFromDeviceToPolicyToRule()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/devices", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.Json("""{"devices":{"device":[{"id":"7","name":"edge-fw"}]}}""");
            }

            if (path.EndsWith("/policies", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.Json("""{"policies":{"policy":[{"id":"70","name":"outbound"}]}}""");
            }

            return StubHttpMessageHandler.Json("""{"rules":{"rule":[{"id":"700","action":"accept"}]}}""");
        });

        Provider provider = Build(transport, new Source.Configuration
        {
            Host = "tufin.example.com",
            EndPoint = "securetrack/api/devices",
            ResultPath = "devices.device",
            Properties = new SortedList<string, object> { { "DeviceName", "<%name%>" } },
            Children =
            [
                new Source.Configuration
                {
                    EndPoint = "securetrack/api/devices/<%id%>/policies",
                    ResultPath = "policies.policy",
                    // Inheritance is per level: the policy has to take the device's properties
                    // for the rule below it to receive them in turn.
                    InheritParentProperties = true,
                    Children =
                    [
                        new Source.Configuration
                        {
                            EndPoint = "securetrack/api/policies/<%id%>/rules",
                            ResultPath = "rules.rule",
                            ParentIdentityProperty = "policyId",
                            InheritParentProperties = true
                        }
                    ]
                }
            ]
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        // Only the leaf endpoint is streamed; the device and the policy are the keys that reach it.
        Assert.That(entities, Has.Count.EqualTo(1));
        Assert.That(entities[0].Identifier, Is.EqualTo("700"));
        Assert.That(entities[0]["action"], Is.EqualTo("accept"));
        Assert.That(entities[0]["policyId"], Is.EqualTo("70"));
        Assert.That(entities[0]["DeviceName"], Is.EqualTo("edge-fw"), "inherited from the device through the policy");

        Assert.That(transport.RequestUris, Is.EqualTo(new[]
        {
            "https://tufin.example.com/securetrack/api/devices",
            "https://tufin.example.com/securetrack/api/devices/7/policies",
            "https://tufin.example.com/securetrack/api/policies/70/rules"
        }));
    }

    [Test]
    public void ProviderBuilder_KeepsTheAuthenticationModeWhenTheFileNamesOnlyCredentials()
    {
        //Arrange
        // Deserialization replaces the whole Authentication section, so a file naming only a
        // username and a password would otherwise leave the mode at None and go out anonymous.
        ProviderBuilder builder = new ProviderBuilder();

        builder.AddLogger(NullLogger.Instance);
        builder.AddDeserializer((bytes, type) =>
            System.Text.Json.JsonSerializer.Deserialize(Encoding.UTF8.GetString(bytes), type)!);
        builder.AddConfiguration(Encoding.UTF8.GetBytes(
            """
            {
              "Host": "tufin.example.com",
              "EndPoint": "securetrack/api/devices",
              "ResultPath": "devices.device",
              "Authentication": { "Username": { "Value": "svc" }, "Password": { "Value": "pw" } }
            }
            """));

        //Act
        Provider provider = (Provider)builder.Build();

        //Assert
        Assert.That(provider.Configuration, Is.InstanceOf<Source.Configuration>());
        Assert.That(provider.Configuration!.Authentication!.Mode, Is.EqualTo(AuthenticationMode.Basic));
        Assert.That(provider.Configuration.IdentityProperty, Is.EqualTo("id"));
        Assert.That(provider.Configuration.ResultPath, Is.EqualTo("devices.device"));

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
