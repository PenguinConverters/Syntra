using System.Text.Json;
using PenguinConverters.Syntra.Consumer.AzureSQL.Target;

namespace PenguinConverters.Syntra.Consumer.AzureSQL.Tests.Target;

/// <summary>
/// Verifies the conversion of source values into target column types by <see cref="SqlValueConverter"/>.
/// </summary>
[TestFixture]
public class SqlValueConverterTests
{
    #region Methods

    /// <summary>
    /// Each SQL system type maps to the CLR type that carries it.
    /// </summary>
    /// <param name="baseType">The SQL system type name.</param>
    /// <param name="expected">The expected CLR type.</param>
    [TestCase("BIT", typeof(bool))]
    [TestCase("INT", typeof(int))]
    [TestCase("BIGINT", typeof(long))]
    [TestCase("DECIMAL", typeof(decimal))]
    [TestCase("UNIQUEIDENTIFIER", typeof(Guid))]
    [TestCase("DATETIME2", typeof(DateTime))]
    [TestCase("DATETIMEOFFSET", typeof(DateTimeOffset))]
    [TestCase("VARBINARY", typeof(byte[]))]
    [TestCase("NVARCHAR", typeof(string))]
    [TestCase("SQL_VARIANT", typeof(object))]
    public void GetClrType_MapsSystemType(string baseType, Type expected)
    {
        //Arrange

        //Act
        Type actual = SqlValueConverter.GetClrType(baseType);

        //Assert
        Assert.That(actual, Is.EqualTo(expected));
    }

    /// <summary>
    /// Missing values become database nulls.
    /// </summary>
    [Test]
    public void Convert_MissingValues_BecomeDbNull()
    {
        //Arrange
        JsonElement jsonNull = JsonDocument.Parse("null").RootElement;

        //Act
        object fromNull = SqlValueConverter.Convert(null, typeof(int));
        object fromJsonNull = SqlValueConverter.Convert(jsonNull, typeof(string));
        object fromEmptyText = SqlValueConverter.Convert(string.Empty, typeof(Guid));

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(fromNull, Is.EqualTo(DBNull.Value));
            Assert.That(fromJsonNull, Is.EqualTo(DBNull.Value));
            Assert.That(fromEmptyText, Is.EqualTo(DBNull.Value));
        });
    }

    /// <summary>
    /// Text is parsed into the type of the target column.
    /// </summary>
    [Test]
    public void Convert_Text_IsParsedIntoColumnType()
    {
        //Arrange
        Guid guid = Guid.NewGuid();

        //Act
        object asGuid = SqlValueConverter.Convert(guid.ToString(), typeof(Guid));
        object asInt = SqlValueConverter.Convert(" 42 ", typeof(int));
        object asDecimal = SqlValueConverter.Convert("12.50", typeof(decimal));
        object asBool = SqlValueConverter.Convert("1", typeof(bool));
        object asDate = SqlValueConverter.Convert("2026-03-01T10:15:00Z", typeof(DateTime));

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(asGuid, Is.EqualTo(guid));
            Assert.That(asInt, Is.EqualTo(42));
            Assert.That(asDecimal, Is.EqualTo(12.5m));
            Assert.That(asBool, Is.EqualTo(true));
            Assert.That(asDate, Is.EqualTo(new DateTime(2026, 3, 1, 10, 15, 0, DateTimeKind.Utc)));
        });
    }

    /// <summary>
    /// JSON elements are unwrapped before conversion.
    /// </summary>
    [Test]
    public void Convert_JsonElements_AreUnwrapped()
    {
        //Arrange
        JsonElement root = JsonDocument.Parse("{\"n\":7,\"s\":\"x\",\"b\":true,\"a\":[1,2]}").RootElement;

        //Act
        object number = SqlValueConverter.Convert(root.GetProperty("n"), typeof(int));
        object text = SqlValueConverter.Convert(root.GetProperty("s"), typeof(string));
        object flag = SqlValueConverter.Convert(root.GetProperty("b"), typeof(bool));
        object array = SqlValueConverter.Convert(root.GetProperty("a"), typeof(string));

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(number, Is.EqualTo(7));
            Assert.That(text, Is.EqualTo("x"));
            Assert.That(flag, Is.EqualTo(true));
            Assert.That(array, Is.EqualTo("[1,2]"));
        });
    }

    /// <summary>
    /// Sixteen bytes become a unique identifier, and a collection becomes JSON text.
    /// </summary>
    [Test]
    public void Convert_BinaryGuidAndCollections_AreConverted()
    {
        //Arrange
        Guid guid = Guid.NewGuid();

        //Act
        object asGuid = SqlValueConverter.Convert(guid.ToByteArray(), typeof(Guid));
        object asText = SqlValueConverter.Convert(new List<string> { "a", "b" }, typeof(string));

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(asGuid, Is.EqualTo(guid));
            Assert.That(asText, Is.EqualTo("[\"a\",\"b\"]"));
        });
    }

    /// <summary>
    /// Text that is not a value of the column type is reported.
    /// </summary>
    [Test]
    public void Convert_UnreadableText_Throws()
    {
        //Arrange

        //Act
        Action action = () => SqlValueConverter.Convert("not-a-guid", typeof(Guid));

        //Assert
        Assert.Throws<FormatException>(action);
    }

    #endregion
}
