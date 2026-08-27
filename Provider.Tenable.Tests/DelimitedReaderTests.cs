using System.Text;
using PenguinConverters.Syntra.Core.Types;
using PenguinConverters.Syntra.Provider.Tenable.Source;

namespace PenguinConverters.Syntra.Provider.Tenable.Tests;

[TestFixture]
public class DelimitedReaderTests
{
    #region Methods

    [Test]
    public async Task ReadAsync_TakesTheFirstRowAsTheColumnNames()
    {
        //Arrange
        //Act
        List<QuickDictionary> rows = await ReadAsync("Id,Name\n1,alpha\n2,beta");

        //Assert
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Id"], Is.EqualTo("1"));
        Assert.That(rows[0]["Name"], Is.EqualTo("alpha"));
        Assert.That(rows[1]["Name"], Is.EqualTo("beta"));
    }

    [Test]
    public async Task ReadAsync_ComparesColumnNamesCaseInsensitively()
    {
        //Arrange
        //Act
        List<QuickDictionary> rows = await ReadAsync("PluginID,Severity\n10863,Medium");

        //Assert
        Assert.That(rows[0]["pluginid"], Is.EqualTo("10863"));
    }

    [Test]
    public async Task ReadAsync_WithAQuotedFieldContainingTheDelimiter_KeepsItWhole()
    {
        //Arrange
        //Act
        List<QuickDictionary> rows = await ReadAsync("Id,Synopsis\n1,\"Ciphers: RC4, 3DES, NULL\"");

        //Assert
        Assert.That(rows[0]["Synopsis"], Is.EqualTo("Ciphers: RC4, 3DES, NULL"));
    }

    [Test]
    public async Task ReadAsync_WithAQuotedFieldContainingLineBreaks_KeepsItAsOneField()
    {
        //Arrange
        // A Nessus plugin output routinely spans lines inside one cell.
        //Act
        List<QuickDictionary> rows = await ReadAsync("Id,Output\n1,\"line one\nline two\r\nline three\"\n2,short");

        //Assert
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Output"], Is.EqualTo("line one\nline two\r\nline three"));
        Assert.That(rows[1]["Output"], Is.EqualTo("short"));
    }

    [Test]
    public async Task ReadAsync_WithADoubledQuoteInsideAQuotedField_ReadsItAsOneQuote()
    {
        //Arrange
        //Act
        List<QuickDictionary> rows = await ReadAsync("Id,Name\n1,\"the \"\"edge\"\" firewall\"");

        //Assert
        Assert.That(rows[0]["Name"], Is.EqualTo("the \"edge\" firewall"));
    }

    [Test]
    public async Task ReadAsync_WithCarriageReturnLineFeedEndings_DoesNotProduceEmptyRows()
    {
        //Arrange
        //Act
        List<QuickDictionary> rows = await ReadAsync("Id,Name\r\n1,alpha\r\n2,beta\r\n");

        //Assert
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ReadAsync_WithNoTrailingNewline_StillReturnsTheLastRow()
    {
        //Arrange
        //Act
        List<QuickDictionary> rows = await ReadAsync("Id,Name\n1,alpha");

        //Assert
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"], Is.EqualTo("alpha"));
    }

    [Test]
    public async Task ReadAsync_WithEmptyFields_KeepsThemAsEmptyValues()
    {
        //Arrange
        //Act
        List<QuickDictionary> rows = await ReadAsync("Id,Name,Note\n1,,x");

        //Assert
        Assert.That(rows[0]["Name"], Is.EqualTo(string.Empty));
        Assert.That(rows[0]["Note"], Is.EqualTo("x"));
    }

    [Test]
    public async Task ReadAsync_WithARepeatedColumnName_KeepsBothUnderDistinctNames()
    {
        //Arrange
        // Letting the second overwrite the first would drop a column without saying so.
        //Act
        List<QuickDictionary> rows = await ReadAsync("Name,Name\nalpha,beta");

        //Assert
        Assert.That(rows[0]["Name"], Is.EqualTo("alpha"));
        Assert.That(rows[0]["Name (2)"], Is.EqualTo("beta"));
    }

    [Test]
    public async Task ReadAsync_WithARowWiderThanTheHeader_KeepsTheSurplusByPosition()
    {
        //Arrange
        //Act
        List<QuickDictionary> rows = await ReadAsync("Id,Name\n1,alpha,extra");

        //Assert
        Assert.That(rows[0]["Column 3"], Is.EqualTo("extra"));
    }

    [Test]
    public async Task ReadAsync_WithARowNarrowerThanTheHeader_LeavesTheMissingColumnsOut()
    {
        //Arrange
        //Act
        List<QuickDictionary> rows = await ReadAsync("Id,Name,Note\n1,alpha");

        //Assert
        Assert.That(rows[0].ContainsKey("Note"), Is.False);
        Assert.That(rows[0]["Name"], Is.EqualTo("alpha"));
    }

    [Test]
    public async Task ReadAsync_WithASemicolonDelimiter_SplitsOnIt()
    {
        //Arrange
        //Act
        List<QuickDictionary> rows = await ReadAsync("Id;Name\n1;alpha", delimiter: ';');

        //Assert
        Assert.That(rows[0]["Name"], Is.EqualTo("alpha"));
    }

    [Test]
    public async Task ReadAsync_WithAFieldSpanningTheReadBuffer_ReassemblesIt()
    {
        //Arrange
        // The reader works over 8k chunks, so a value longer than one has to survive the seam.
        string long1 = new string('a', 12000);
        string long2 = new string('b', 9000);

        //Act
        List<QuickDictionary> rows = await ReadAsync($"Id,Output\n1,\"{long1}\"\n2,\"{long2}, and more\"");

        //Assert
        Assert.That(rows[0]["Output"], Is.EqualTo(long1));
        Assert.That(rows[1]["Output"], Is.EqualTo($"{long2}, and more"));
    }

    [Test]
    public async Task ReadAsync_WithAnEmptyBody_ReturnsNothing()
    {
        //Arrange
        //Act
        List<QuickDictionary> rows = await ReadAsync(string.Empty);

        //Assert
        Assert.That(rows, Is.Empty);
    }

    [Test]
    public async Task ReadAsync_WithOnlyAHeader_ReturnsNothing()
    {
        //Arrange
        //Act
        List<QuickDictionary> rows = await ReadAsync("Id,Name\n");

        //Assert
        Assert.That(rows, Is.Empty);
    }

    [Test]
    public async Task ReadAsync_WithAByteOrderMark_DoesNotCarryItIntoTheFirstColumnName()
    {
        //Arrange
        byte[] body = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("Id,Name\n1,alpha"))
            .ToArray();

        //Act
        List<QuickDictionary> rows = [];

        await foreach (QuickDictionary row in DelimitedReader.ReadAsync(
            new MemoryStream(body), ',', Encoding.UTF8))
        {
            rows.Add(row);
        }

        //Assert
        Assert.That(rows[0]["Id"], Is.EqualTo("1"));
    }

    /// <summary>
    /// Reads a delimited body.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <param name="delimiter">The character separating fields.</param>
    /// <returns>One property bag per row.</returns>
    private static async Task<List<QuickDictionary>> ReadAsync(string body, char delimiter = ',')
    {
        using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(body));

        List<QuickDictionary> rows = [];

        await foreach (QuickDictionary row in DelimitedReader.ReadAsync(stream, delimiter, Encoding.UTF8))
        {
            rows.Add(row);
        }

        return rows;
    }

    #endregion
}
