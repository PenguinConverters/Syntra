using System.Text.Json;
using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.RESTful.Tests.Source;

[TestFixture]
public class JsonPathTests
{
    #region Methods

    [Test]
    public void TryResolve_WithEmptyPath_ReturnsTheElementItself()
    {
        //Arrange
        using JsonDocument document = JsonDocument.Parse("""{"a":1}""");

        //Act
        bool resolved = JsonPath.TryResolve(document.RootElement, null, out JsonElement value);

        //Assert
        Assert.That(resolved, Is.True);
        Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }

    [Test]
    public void TryResolve_WithNestedPath_ReturnsTheNestedElement()
    {
        //Arrange
        using JsonDocument document = JsonDocument.Parse("""{"response":{"usable":[{"id":7}]}}""");

        //Act
        bool resolved = JsonPath.TryResolve(document.RootElement, "response.usable", out JsonElement value);

        //Assert
        Assert.That(resolved, Is.True);
        Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(value.GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public void TryResolve_WithArrayIndexSegment_IndexesTheArray()
    {
        //Arrange
        using JsonDocument document = JsonDocument.Parse(
            """{"_links":{"next":[{"href":"https://host/next"}]}}""");

        //Act
        string? value = JsonPath.ResolveString(document.RootElement, "_links.next.0.href");

        //Assert
        Assert.That(value, Is.EqualTo("https://host/next"));
    }

    [Test]
    public void TryResolve_WithPropertyNameContainingSeparator_MatchesItWhole()
    {
        //Arrange
        // An OData annotation carries a dot in its own name, so splitting the path first would
        // look for a property "@odata" that does not exist.
        using JsonDocument document = JsonDocument.Parse(
            """{"@odata.nextLink":"https://host/page2"}""");

        //Act
        string? value = JsonPath.ResolveString(document.RootElement, "@odata.nextLink");

        //Assert
        Assert.That(value, Is.EqualTo("https://host/page2"));
    }

    [Test]
    public void TryResolve_WithMissingPath_ReturnsFalse()
    {
        //Arrange
        using JsonDocument document = JsonDocument.Parse("""{"a":1}""");

        //Act
        bool resolved = JsonPath.TryResolve(document.RootElement, "b.c", out _);

        //Assert
        Assert.That(resolved, Is.False);
    }

    [Test]
    public void ResolveString_WithEmptyValue_ReturnsNull()
    {
        //Arrange
        using JsonDocument document = JsonDocument.Parse("""{"next":"   "}""");

        //Act
        string? value = JsonPath.ResolveString(document.RootElement, "next");

        //Assert
        Assert.That(value, Is.Null);
    }

    [Test]
    public void ResolveString_WithNumericValue_ReturnsItAsText()
    {
        //Arrange
        using JsonDocument document = JsonDocument.Parse("""{"next_page_id":42}""");

        //Act
        string? value = JsonPath.ResolveString(document.RootElement, "next_page_id");

        //Assert
        Assert.That(value, Is.EqualTo("42"));
    }

    #endregion
}
