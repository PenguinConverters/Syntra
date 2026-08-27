using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Types;
using PenguinConverters.Syntra.Provider.RESTful.Settings;
using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.RESTful.Tests;

[TestFixture]
public class ProviderTests
{
    #region Methods

    [Test]
    public async Task RetrieveAsync_WithoutAnySubclassing_StreamsTheConfiguredEndpoint()
    {
        //Arrange
        // The base provider is a working connector: configuration alone is enough.
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"entries":[{"values":{"Id":"1","Name":"a"}},{"values":{"Id":"2","Name":"b"}}]}""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "cmdb.example.com",
            EndPoint = "api/arsys/v1/entry/HPD:Help Desk",
            ResultPath = "entries",
            EntryPath = "values",
            IdentityProperty = "Id"
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities, Has.Count.EqualTo(2));
        Assert.That(entities[0].Identifier, Is.EqualTo("1"));
        Assert.That(entities[1]["Name"], Is.EqualTo("b"));
        Assert.That(
            transport.RequestUris[0],
            Is.EqualTo("https://cmdb.example.com/api/arsys/v1/entry/HPD:Help%20Desk"));
    }

    [Test]
    public async Task RetrieveAsync_WithNoConfiguredProjection_SendsThePropertiesTheConsumerAskedFor()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""{"entries":[]}""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "records",
            ResultPath = "entries",
            PropertiesParameter = "fields",
            PropertiesFormat = "values({0})"
        });

        //Act
        await CollectAsync(provider, "Id", "Name");

        //Assert
        Assert.That(Uri.UnescapeDataString(transport.RequestUris[0]), Does.Contain("fields=values(Id,Name)"));
    }

    [Test]
    public async Task RetrieveAsync_WithConfiguredProjection_PrefersItOverTheConsumerRequest()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""{"entries":[]}""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "records",
            ResultPath = "entries",
            PropertiesParameter = "_return_fields",
            PropertiesToLoad = ["name", "ipv4addr"]
        });

        //Act
        await CollectAsync(provider, "Id", "Name");

        //Assert
        string uri = Uri.UnescapeDataString(transport.RequestUris[0]);

        Assert.That(uri, Does.Contain("name"));
        Assert.That(uri, Does.Contain("ipv4addr"));
        Assert.That(uri, Does.Not.Contain("Id"));
    }

    [Test]
    public async Task RetrieveAsync_WithIgnoredProperties_WithholdsThemFromTheProjection()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""{"entries":[]}""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "records",
            ResultPath = "entries",
            PropertiesParameter = "fields",
            PropertiesToIgnore = ["_ref"]
        });

        //Act
        await CollectAsync(provider, "name", "_ref");

        //Assert
        string uri = Uri.UnescapeDataString(transport.RequestUris[0]);

        Assert.That(uri, Does.Contain("name"));
        Assert.That(uri, Does.Not.Contain("_ref"));
    }

    [Test]
    public async Task RetrieveAsync_WithDeltaAndAStoredWatermark_FiltersOnItAndAdvancesIt()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"entries":[{"values":{"Id":"1","Modified Date":"2026-02-01T10:00:00.000Z"}},{"values":{"Id":"2","Modified Date":"2026-03-05T08:30:00.000Z"}}]}""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "records",
            ResultPath = "entries",
            EntryPath = "values",
            IdentityProperty = "Id",
            Delta = true,
            OffsetProperty = "Modified Date",
            FilterParameter = "q",
            FilterFormat = "'{0}' > \"{1}\""
        });

        provider.State = new State
        {
            Offset = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndPoint = "records"
        };

        //Act
        await CollectAsync(provider);

        //Assert
        Assert.That(
            Uri.UnescapeDataString(transport.RequestUris[0]),
            Does.Contain("q='Modified Date' > \"2026-01-01T00:00:00.000Z\""));

        Assert.That(
            provider.State.Offset,
            Is.EqualTo(new DateTime(2026, 3, 5, 8, 30, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task RetrieveAsync_WithAFilterAlreadyConfigured_NarrowsItRatherThanReplacingIt()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""{"entries":[]}""");

        Configuration configuration = new Configuration
        {
            Host = "host",
            EndPoint = "records",
            ResultPath = "entries",
            Delta = true,
            OffsetProperty = "Modified",
            FilterParameter = "q",
            FilterFormat = "'{0}' > \"{1}\"",
            Parameters = new SortedList<string, object> { { "q", "'Status' = \"Open\"" } }
        };

        Provider provider = Build(transport, configuration);

        provider.State = new State
        {
            Offset = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndPoint = "records"
        };

        //Act
        await CollectAsync(provider);

        //Assert
        string uri = Uri.UnescapeDataString(transport.RequestUris[0]);

        Assert.That(uri, Does.Contain("'Status' = \"Open\""));
        Assert.That(uri, Does.Contain("AND"));
        Assert.That(uri, Does.Contain("'Modified' >"));
    }

    [Test]
    public async Task RetrieveAsync_RunTwice_CombinesTheDeltaFilterFromTheConfiguredOneEachTime()
    {
        //Arrange
        // The first run writes the combined filter into the configuration. A second run must
        // combine from what the operator configured, not from what the first run left there.
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Json("""{"entries":[]}"""));

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "records",
            ResultPath = "entries",
            Delta = true,
            OffsetProperty = "Modified",
            FilterParameter = "q",
            FilterFormat = "'{0}' > \"{1}\"",
            Parameters = new SortedList<string, object> { { "q", "'Status' = \"Open\"" } }
        });

        provider.State = new State
        {
            Offset = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndPoint = "records"
        };

        //Act
        await CollectAsync(provider);
        await CollectAsync(provider);

        //Assert
        Assert.That(
            Uri.UnescapeDataString(transport.RequestUris[1]),
            Is.EqualTo(Uri.UnescapeDataString(transport.RequestUris[0])));
    }

    [Test]
    public async Task RetrieveAsync_AfterAFailedRun_ClearsTheErrorStateForTheNextOne()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, index) => index == 0
                ? StubHttpMessageHandler.Json("boom", System.Net.HttpStatusCode.InternalServerError)
                : StubHttpMessageHandler.Json("""[{"id":1}]"""));

        Provider provider = Build(transport, new Configuration { Host = "host", EndPoint = "records" });

        //Act
        await CollectAsync(provider);
        bool failedFirst = provider.HadErrors;

        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(failedFirst, Is.True);
        Assert.That(provider.HadErrors, Is.False);
        Assert.That(entities, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RetrieveAsync_WithAWatermarkFromAnotherEndpoint_DiscardsItAndReadsEverything()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""{"entries":[]}""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "incidents",
            ResultPath = "entries",
            Delta = true,
            OffsetProperty = "Modified",
            FilterParameter = "q",
            FilterFormat = "'{0}' > \"{1}\""
        });

        provider.State = new State
        {
            Offset = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndPoint = "assets"
        };

        //Act
        await CollectAsync(provider);

        //Assert
        Assert.That(transport.RequestUris[0], Does.Not.Contain("q="));
    }

    [Test]
    public async Task RetrieveAsync_WithADeletionMarker_ClassifiesTheRecordAsDeleted()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"entries":[{"Id":"1","Mark As Deleted":"Yes"},{"Id":"2","Mark As Deleted":"No"}]}""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "records",
            ResultPath = "entries",
            IdentityProperty = "Id",
            DeletedProperty = "Mark As Deleted",
            DeletedValue = "Yes"
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities[0].State, Is.EqualTo(EntityState.Deleted));
        Assert.That(entities[1].State, Is.EqualTo(EntityState.Unclassified));
    }

    [Test]
    public async Task RetrieveAsync_WithValueHandlers_CoercesThatPropertyOnly()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """[{"Id":"1","Modified":"2026-03-05T08:30:00.000Z","Note":"2026-03-05T08:30:00.000Z"}]""");

        Provider provider = Build(transport, new Configuration { Host = "host", EndPoint = "records" });

        provider.AddValueHandler(
            "Modified",
            value => DateTime.Parse(
                (string)value!, null, System.Globalization.DateTimeStyles.AdjustToUniversal));

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities[0]["Modified"], Is.InstanceOf<DateTime>());
        Assert.That(entities[0]["Note"], Is.InstanceOf<string>());
    }

    [Test]
    public async Task RetrieveAsync_WithAFallbackValueHandler_AppliesItToEveryUnclaimedProperty()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[{"a":" x ","b":" y "}]""");

        Provider provider = Build(transport, new Configuration { Host = "host", EndPoint = "records" });

        provider.AddValueHandler("a", value => value);
        provider.ValueHandler = (_, value) => (value as string)?.Trim() ?? value;

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        // The keyed handler wins for "a"; the fallback trims everything else.
        Assert.That(entities[0]["a"], Is.EqualTo(" x "));
        Assert.That(entities[0]["b"], Is.EqualTo("y"));
    }

    [Test]
    public async Task RetrieveAsync_WithAnEntryTransformReturningNull_DropsTheRecord()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """[{"id":1,"kind":"keep"},{"id":2,"kind":"skip"},{"id":3,"kind":"keep"}]""");

        Provider provider = Build(transport, new Configuration { Host = "host", EndPoint = "records" });

        provider.EntryTransform = (properties, _) =>
            (string?)properties["kind"] == "skip" ? null : properties;

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities, Has.Count.EqualTo(2));
        Assert.That(entities.Select(entity => entity["id"]), Is.EqualTo(new object[] { 1L, 3L }));
    }

    [Test]
    public async Task RetrieveAsync_WithAnEntryTransformAddingProperties_CarriesThemThrough()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[{"id":1}]""");

        Provider provider = Build(transport, new Configuration { Host = "host", EndPoint = "records" });

        provider.EntryTransform = (properties, configuration) =>
        {
            properties["source"] = configuration.EndPoint;
            return properties;
        };

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities[0]["source"], Is.EqualTo("records"));
    }

    [Test]
    public async Task RetrieveAsync_WithSelectors_OverridesTheConfiguredIdentityAndState()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[{"a":"1","b":"2"}]""");

        Provider provider = Build(transport, new Configuration { Host = "host", EndPoint = "records" });

        provider.IdentitySelector = (properties, _) => $"{properties["a"]}-{properties["b"]}";
        provider.StateSelector = (_, _) => EntityState.Created;

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities[0].Identifier, Is.EqualTo("1-2"));
        Assert.That(entities[0].State, Is.EqualTo(EntityState.Created));
    }

    [Test]
    public async Task RetrieveAsync_WithChildEndpoints_StreamsTheChildRecordsStampedWithTheParent()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath == "/devices"
                ? StubHttpMessageHandler.Json("""[{"id":"d1","name":"edge"}]""")
                : StubHttpMessageHandler.Json("""[{"rule":"r1"},{"rule":"r2"}]"""));

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "devices",
            IdentityProperty = "id",
            Children =
            [
                new Configuration
                {
                    EndPoint = "devices/<%id%>/policies",
                    IdentityProperty = "rule",
                    ParentIdentityProperty = "deviceId",
                    InheritParentProperties = true
                }
            ]
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities, Has.Count.EqualTo(2));
        Assert.That(entities[0]["deviceId"], Is.EqualTo("d1"));
        Assert.That(entities[0]["name"], Is.EqualTo("edge"), "the parent's properties are inherited");
        Assert.That(entities[1].Identifier, Is.EqualTo("r2"));
        Assert.That(transport.RequestUris[1], Is.EqualTo("https://host/devices/d1/policies"));
    }

    [Test]
    public async Task RetrieveAsync_WithConfiguredProperties_RenamesAndTags()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[{"name":"edge"}]""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "devices",
            Properties = new SortedList<string, object>
            {
                { "DeviceName", "<%name%>" },
                { "Vendor", "acme" }
            }
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities[0]["DeviceName"], Is.EqualTo("edge"));
        Assert.That(entities[0]["Vendor"], Is.EqualTo("acme"));
        Assert.That(entities[0]["name"], Is.EqualTo("edge"));
    }

    [Test]
    public async Task RetrieveAsync_WithAFailedChildRead_ReportsErrorsAndWithholdsTheWatermark()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath == "/devices"
                ? StubHttpMessageHandler.Json("""[{"id":"d1"},{"id":"d2"}]""")
                : StubHttpMessageHandler.Json("boom", System.Net.HttpStatusCode.InternalServerError));

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "devices",
            IdentityProperty = "id",
            Delta = true,
            OffsetProperty = "Modified",
            Children = [new Configuration { EndPoint = "devices/<%id%>/policies" }]
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities, Is.Empty);
        Assert.That(provider.HadErrors, Is.True);
        Assert.That(provider.Metadata, Is.Null, "a partial read must not advance the watermark");
    }

    [Test]
    public async Task RetrieveAsync_OnSuccess_RecordsTheWatermarkAsMetadata()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """[{"Id":"1","Modified":"2026-03-05T08:30:00.000Z"}]""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "records",
            IdentityProperty = "Id",
            Delta = true,
            OffsetProperty = "Modified"
        });

        //Act
        await CollectAsync(provider);

        //Assert
        Assert.That(provider.Metadata, Is.Not.Null);

        State? recorded = JsonSerializer.Deserialize<State>(Encoding.UTF8.GetString(provider.Metadata!));

        Assert.That(recorded!.EndPoint, Is.EqualTo("records"));
        Assert.That(recorded.Offset, Is.EqualTo(new DateTime(2026, 3, 5, 8, 30, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task RetrieveAsync_WithNoRecords_LeavesTheWatermarkWhereItWas()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[]""");

        DateTime previous = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "records",
            Delta = true,
            OffsetProperty = "Modified",
            FilterParameter = "q",
            FilterFormat = "'{0}' > \"{1}\""
        });

        provider.State = new State { Offset = previous, EndPoint = "records" };

        //Act
        await CollectAsync(provider);

        //Assert
        Assert.That(provider.State.Offset, Is.EqualTo(previous));
    }

    [Test]
    public async Task RetrieveAsync_WithADeltaRun_ClassifiesRecordsAsUpdated()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[{"Id":"1"}]""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "records",
            IdentityProperty = "Id",
            Delta = true
        });

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities[0].State, Is.EqualTo(EntityState.Updated));
    }

    [Test]
    public async Task RetrieveAsync_WithAnEndPointResolver_ReadsTheResolvedEndpoint()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[]""");

        Provider provider = Build(transport, new Configuration
        {
            Host = "host",
            EndPoint = "rest/report/<%latest%>/download"
        });

        provider.EndPointResolver = (_, endPoint, _) =>
            ValueTask.FromResult(endPoint.Replace("<%latest%>", "4711"));

        //Act
        await CollectAsync(provider);

        //Assert
        Assert.That(transport.RequestUris[0], Is.EqualTo("https://host/rest/report/4711/download"));
    }

    [Test]
    public async Task RetrieveAsync_WithoutAConfiguration_ReportsRatherThanThrows()
    {
        //Arrange
        Provider provider = new Provider();

        provider.SetLogger(NullLogger.Instance);

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities, Is.Empty);
        Assert.That(provider.HadErrors, Is.False);
    }

    [Test]
    public async Task RetrieveAsync_WithoutAnHttpClient_ReportsErrors()
    {
        //Arrange
        Provider provider = new Provider { Configuration = new Configuration { Host = "host" } };

        provider.SetLogger(NullLogger.Instance);

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities, Is.Empty);
        Assert.That(provider.HadErrors, Is.True);
    }

    [Test]
    public async Task RetrieveAsync_WithADerivedProviderOverridingAHook_UsesTheOverride()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[{"a":"1"}]""");

        DerivedProvider provider = new DerivedProvider
        {
            Configuration = new Configuration { Host = "host", EndPoint = "records" },
            RestClient = new RestClient(new HttpClient(transport), NullLogger.Instance)
        };

        provider.SetLogger(NullLogger.Instance);

        //Act
        List<IEntity> entities = await CollectAsync(provider);

        //Assert
        Assert.That(entities[0].Identifier, Is.EqualTo("derived"));
        Assert.That(entities[0].State, Is.EqualTo(EntityState.Deleted));
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

    #region Nested Types

    /// <summary>
    /// A connector that customizes retrieval by overriding the hooks rather than by assigning the
    /// delegates, which is the other half of the seam.
    /// </summary>
    private sealed class DerivedProvider : Provider
    {
        #region Methods

        /// <inheritdoc />
        protected override string? ResolveIdentity(QuickDictionary properties, Configuration configuration)
        {
            return "derived";
        }

        /// <inheritdoc />
        protected override EntityState ResolveState(QuickDictionary properties, Configuration configuration)
        {
            return EntityState.Deleted;
        }

        #endregion
    }

    #endregion
}
