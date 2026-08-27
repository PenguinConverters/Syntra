using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Syntra.Core.Types;
using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.RESTful.Tests.Source;

[TestFixture]
public class RestClientTests
{
    #region Methods

    [Test]
    public async Task ReadAsync_WithResultPathAndEntryPath_ProjectsTheWrappedRecords()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"entries":[{"values":{"Id":"1","Name":"a"}},{"values":{"Id":"2","Name":"b"}}]}""");

        Configuration configuration = new Configuration { ResultPath = "entries", EntryPath = "values" };

        //Act
        List<QuickDictionary> entries = await ReadAllAsync(transport, configuration);

        //Assert
        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0]["Name"], Is.EqualTo("a"));
        Assert.That(entries[1]["Id"], Is.EqualTo("2"));
    }

    [Test]
    public async Task ReadAsync_WithBareArrayResponse_ProjectsEveryElement()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[{"id":1},{"id":2},{"id":3}]""");

        //Act
        List<QuickDictionary> entries = await ReadAllAsync(transport, new Configuration());

        //Assert
        Assert.That(entries, Has.Count.EqualTo(3));
        Assert.That(entries[2]["id"], Is.EqualTo(3L));
    }

    [Test]
    public async Task ReadAsync_WithSingleObjectResponse_ProjectsOneRecord()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""{"id":1,"name":"only"}""");

        //Act
        List<QuickDictionary> entries = await ReadAllAsync(transport, new Configuration());

        //Assert
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0]["name"], Is.EqualTo("only"));
    }

    [Test]
    public async Task ReadAsync_WithNextLinkPagination_FollowsTheLinkUntilItStops()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"entries":[{"id":1}],"_links":{"next":[{"href":"https://host/api/page2"}]}}""",
            """{"entries":[{"id":2}]}""");

        Configuration configuration = new Configuration
        {
            ResultPath = "entries",
            Pagination = new PaginationSettings
            {
                Mode = PaginationMode.NextLink,
                NextLinkPath = "_links.next.0.href"
            }
        };

        //Act
        List<QuickDictionary> entries = await ReadAllAsync(transport, configuration);

        //Assert
        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(transport.RequestUris, Is.EqualTo(new[]
        {
            "https://host/api/records",
            "https://host/api/page2"
        }));
    }

    [Test]
    public async Task ReadAsync_WithRelativeNextLink_ResolvesItAgainstTheRequest()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"value":[{"id":1}],"nextLink":"/api/page2"}""",
            """{"value":[{"id":2}]}""");

        Configuration configuration = new Configuration
        {
            ResultPath = "value",
            Pagination = new PaginationSettings
            {
                Mode = PaginationMode.NextLink,
                NextLinkPath = "nextLink"
            }
        };

        //Act
        await ReadAllAsync(transport, configuration);

        //Assert
        Assert.That(transport.RequestUris[1], Is.EqualTo("https://host/api/page2"));
    }

    [Test]
    public async Task ReadAsync_WithTokenPagination_SendsTheTokenAndDropsTheOriginalQuery()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"result":[{"id":1}],"next_page_id":"abc"}""",
            """{"result":[{"id":2}]}""");

        Configuration configuration = new Configuration
        {
            ResultPath = "result",
            Pagination = new PaginationSettings
            {
                Mode = PaginationMode.Token,
                TokenPath = "next_page_id",
                TokenParameter = "_page_id"
            }
        };

        //Act
        List<QuickDictionary> entries = await ReadAllAsync(
            transport, configuration, "https://host/api/records?_return_fields=a,b");

        //Assert
        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(transport.RequestUris[1], Is.EqualTo("https://host/api/records?_page_id=abc"));
    }

    [Test]
    public async Task ReadAsync_WithTokenPaginationKeepingTheQuery_JoinsTheTokenToIt()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"result":[{"id":1}],"cursor":"abc"}""",
            """{"result":[{"id":2}]}""");

        Configuration configuration = new Configuration
        {
            ResultPath = "result",
            Pagination = new PaginationSettings
            {
                Mode = PaginationMode.Token,
                TokenPath = "cursor",
                TokenParameter = "cursor",
                TokenReplacesQuery = false
            }
        };

        //Act
        await ReadAllAsync(transport, configuration, "https://host/api/records?limit=1");

        //Assert
        Assert.That(transport.RequestUris[1], Is.EqualTo("https://host/api/records?limit=1&cursor=abc"));
    }

    [Test]
    public async Task ReadAsync_WithOffsetPagination_StopsOnTheFirstShortPage()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """[{"id":1},{"id":2}]""",
            """[{"id":3},{"id":4}]""",
            """[{"id":5}]""");

        Configuration configuration = new Configuration
        {
            Pagination = new PaginationSettings
            {
                Mode = PaginationMode.Offset,
                OffsetParameter = "offset",
                PageSize = 2
            }
        };

        //Act
        List<QuickDictionary> entries = await ReadAllAsync(
            transport, configuration, "https://host/api/records?offset=0");

        //Assert
        Assert.That(entries, Has.Count.EqualTo(5));
        Assert.That(transport.RequestUris, Has.Count.EqualTo(3));
        Assert.That(transport.RequestUris[1], Does.Contain("offset=2"));
        Assert.That(transport.RequestUris[2], Does.Contain("offset=4"));
    }

    [Test]
    public async Task ReadAsync_WithPagePagination_CountsFromTheConfiguredFirstPage()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """[{"id":1}]""",
            """[]""");

        Configuration configuration = new Configuration
        {
            Pagination = new PaginationSettings
            {
                Mode = PaginationMode.Page,
                PageParameter = "page",
                PageSize = 1,
                FirstPage = 1
            }
        };

        //Act
        await ReadAllAsync(transport, configuration, "https://host/api/records?page=1");

        //Assert
        Assert.That(transport.RequestUris[1], Does.Contain("page=2"));
    }

    [Test]
    public async Task ReadAsync_WithMaximumPagesReached_StopsInsteadOfLooping()
    {
        //Arrange
        // A server that keeps offering the same next link would otherwise never let go.
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Json("""{"value":[{"id":1}],"next":"https://host/api/again"}"""));

        Configuration configuration = new Configuration
        {
            ResultPath = "value",
            Pagination = new PaginationSettings
            {
                Mode = PaginationMode.NextLink,
                NextLinkPath = "next",
                MaximumPages = 3
            }
        };

        //Act
        List<QuickDictionary> entries = await ReadAllAsync(transport, configuration);

        //Assert
        Assert.That(entries, Has.Count.EqualTo(3));
    }

    [Test]
    public void ReadAsync_WithUnsuccessfulResponse_ThrowsCarryingTheBody()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Json(
                """{"error":"no such form"}""", HttpStatusCode.BadRequest));

        //Act
        HttpRequestException? exception = Assert.ThrowsAsync<HttpRequestException>(
            async () => await ReadAllAsync(transport, new Configuration()));

        //Assert
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(exception.Message, Does.Contain("no such form"));
    }

    [Test]
    public async Task ReadAsync_WithConfiguredHeadersAndMethod_SendsThem()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[]""");

        Configuration configuration = new Configuration
        {
            HttpMethod = "POST",
            Body = """{"query":"all"}""",
            HttpHeaders = new SortedList<string, string> { { "X-AR-Client-Type", "34" } }
        };

        //Act
        await ReadAllAsync(transport, configuration);

        //Assert
        HttpRequestMessage request = transport.Requests[0];

        Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(request.Headers.GetValues("X-AR-Client-Type").Single(), Is.EqualTo("34"));
        Assert.That(transport.RequestBodies[0], Is.EqualTo("""{"query":"all"}"""));
    }

    [Test]
    public async Task ReadAsync_WithContentReader_ReadsTheBodyThroughItInsteadOfParsingJson()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Text("Id,Name\n1,alpha\n2,beta"));

        //Act
        List<QuickDictionary> entries = await ReadAllAsync(
            transport, new Configuration(), contentReader: ReadCsvAsync);

        //Assert
        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0]["Name"], Is.EqualTo("alpha"));
        Assert.That(entries[1]["Id"], Is.EqualTo("2"));
    }

    [Test]
    public void ReadProperties_KeepsNestedStructuresAsRawJson()
    {
        //Arrange
        //Act
        List<QuickDictionary> entries = RestClient.ParseEntries(
            """[{"id":1,"owner":{"name":"a"},"tags":["x","y"],"live":true,"gone":null}]""");

        //Assert
        Assert.That(entries[0]["id"], Is.EqualTo(1L));
        Assert.That(entries[0]["owner"], Is.EqualTo("""{"name":"a"}"""));
        Assert.That(entries[0]["tags"], Is.EqualTo("""["x","y"]"""));
        Assert.That(entries[0]["live"], Is.EqualTo(true));
        Assert.That(entries[0]["gone"], Is.Null);
    }

    [Test]
    public void ReadProperties_ComparesKeysCaseInsensitively()
    {
        //Arrange
        //Act
        List<QuickDictionary> entries = RestClient.ParseEntries("""{"DisplayName":"a"}""");

        //Assert
        Assert.That(entries[0]["displayname"], Is.EqualTo("a"));
    }

    /// <summary>
    /// Reads every page of a request and flattens the records.
    /// </summary>
    /// <param name="transport">The transport answering the requests.</param>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <param name="requestUri">The URL to read.</param>
    /// <param name="contentReader">The reader for a body that is not JSON, or <c>null</c>.</param>
    /// <returns>Every record the pages carried.</returns>
    private static async Task<List<QuickDictionary>> ReadAllAsync(
        StubHttpMessageHandler transport,
        Configuration configuration,
        string requestUri = "https://host/api/records",
        Func<Stream, Configuration, CancellationToken, IAsyncEnumerable<QuickDictionary>>? contentReader = null)
    {
        using RestClient client = new RestClient(new HttpClient(transport), NullLogger.Instance);

        List<QuickDictionary> entries = [];

        await foreach (RestPage page in client.ReadAsync(requestUri, configuration, contentReader))
        {
            entries.AddRange(page.Entries);
        }

        return entries;
    }

    /// <summary>
    /// Reads a comma-separated body whose first line names the columns.
    /// </summary>
    /// <param name="content">The response body.</param>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the read.</param>
    /// <returns>One property bag per row.</returns>
    private static async IAsyncEnumerable<QuickDictionary> ReadCsvAsync(
        Stream content,
        Configuration configuration,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using StreamReader reader = new StreamReader(content);

        string[] columns = (await reader.ReadLineAsync(cancellationToken))?.Split(',') ?? [];

        while (await reader.ReadLineAsync(cancellationToken) is string line)
        {
            string[] cells = line.Split(',');
            QuickDictionary row = new QuickDictionary(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < columns.Length && index < cells.Length; index++)
            {
                row[columns[index]] = cells[index];
            }

            yield return row;
        }
    }

    #endregion
}
