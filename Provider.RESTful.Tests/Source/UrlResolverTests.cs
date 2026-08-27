using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.RESTful.Tests.Source;

[TestFixture]
public class UrlResolverTests
{
    #region Methods

    [Test]
    public void IsAbsolute_WithARootRelativePath_SaysNo()
    {
        //Arrange
        // On a Unix host a leading slash parses as an absolute file URL, so UriKind.Absolute
        // alone would accept this and address the local filesystem instead of the API.
        //Act
        bool absolute = UrlResolver.IsAbsolute("/api/jwt/login", out Uri? parsed);

        //Assert
        Assert.That(absolute, Is.False);
        Assert.That(parsed, Is.Null);
    }

    [TestCase("file:///etc/passwd")]
    [TestCase("ftp://host/file")]
    [TestCase("C:/Windows/System32")]
    public void IsAbsolute_WithAUrlThatIsNotWeb_SaysNo(string url)
    {
        //Arrange
        //Act
        bool absolute = UrlResolver.IsAbsolute(url, out _);

        //Assert
        Assert.That(absolute, Is.False);
    }

    [TestCase("https://host/api/page2")]
    [TestCase("http://host/api/page2")]
    public void IsAbsolute_WithAWebUrl_SaysYes(string url)
    {
        //Arrange
        //Act
        bool absolute = UrlResolver.IsAbsolute(url, out Uri? parsed);

        //Assert
        Assert.That(absolute, Is.True);
        Assert.That(parsed, Is.Not.Null);
    }

    [Test]
    public void Resolve_WithARootRelativePath_KeepsTheSchemeAndHostOfTheBase()
    {
        //Arrange
        //Act
        string resolved = UrlResolver.Resolve("https://host/api/records?page=1", "/api/page2");

        //Assert
        Assert.That(resolved, Is.EqualTo("https://host/api/page2"));
    }

    [Test]
    public void Resolve_WithADocumentRelativePath_ResolvesAgainstTheRequest()
    {
        //Arrange
        //Act
        string resolved = UrlResolver.Resolve("https://host/api/records", "page2");

        //Assert
        Assert.That(resolved, Is.EqualTo("https://host/api/page2"));
    }

    [Test]
    public void Resolve_WithAProtocolRelativeUrl_TakesTheSchemeOfTheBase()
    {
        //Arrange
        //Act
        string resolved = UrlResolver.Resolve("https://host/api/records", "//other.example.com/api/page2");

        //Assert
        Assert.That(resolved, Is.EqualTo("https://other.example.com/api/page2"));
    }

    [Test]
    public void Resolve_WithAnAbsoluteUrl_UsesItAsItStands()
    {
        //Arrange
        //Act
        string resolved = UrlResolver.Resolve("https://host/api/records", "https://other.example.com/next");

        //Assert
        Assert.That(resolved, Is.EqualTo("https://other.example.com/next"));
    }

    #endregion
}
