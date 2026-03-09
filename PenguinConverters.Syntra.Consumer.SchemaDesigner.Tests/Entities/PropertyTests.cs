// -----------------------------------------------------------------------
// <copyright file="PropertyTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using PenguinConverters.Syntra.Consumer.SchemaDesigner.Entities;

namespace PenguinConverters.Syntra.Consumer.SchemaDesigner.Tests.Entities;

[TestFixture]
public class PropertyTests
{
    [Test]
    public void InferType_Integer_ReturnsInt()
    {
        //Arrange
        Property property = new Property("Age");
        property.Observe(25);
        property.Observe(30);

        //Act
        Column column = property.ToColumn();

        //Assert
        Assert.That(column.SqlType, Is.EqualTo(SqlColumnType.Int));
    }

    [Test]
    public void InferType_Boolean_ReturnsBool()
    {
        //Arrange
        Property property = new Property("IsActive");
        property.Observe(true);
        property.Observe(false);

        //Act
        Column column = property.ToColumn();

        //Assert
        Assert.That(column.SqlType, Is.EqualTo(SqlColumnType.Bit));
    }

    [Test]
    public void InferType_DateTime_ReturnsDateTime()
    {
        //Arrange
        Property property = new Property("CreatedDate");
        property.Observe(DateTime.UtcNow);
        property.Observe(DateTime.UtcNow.AddDays(-1));

        //Act
        Column column = property.ToColumn();

        //Assert
        Assert.That(column.SqlType, Is.EqualTo(SqlColumnType.DateTime2));
    }

    [Test]
    public void InferType_String_ReturnsString()
    {
        //Arrange
        Property property = new Property("DisplayName");
        property.Observe("John Doe");
        property.Observe("Jane Smith");

        //Act
        Column column = property.ToColumn();

        //Assert
        Assert.That(column.SqlType, Is.EqualTo(SqlColumnType.VarChar));
    }

    [Test]
    public void InferType_Unicode_ReturnsNvarchar()
    {
        //Arrange
        Property property = new Property("Description");
        property.Observe("Hello \u00FC\u00F6\u00E4");

        //Act
        Column column = property.ToColumn();

        //Assert
        Assert.That(column.SqlType, Is.EqualTo(SqlColumnType.NVarChar));
    }

    [Test]
    public void InferType_Long_ReturnsBigInt()
    {
        //Arrange
        Property property = new Property("LargeNumber");
        property.Observe((long)int.MaxValue + 1);

        //Act
        Column column = property.ToColumn();

        //Assert
        Assert.That(column.SqlType, Is.EqualTo(SqlColumnType.BigInt));
    }

    [Test]
    public void Observe_Null_IncrementsNullCount()
    {
        //Arrange
        Property property = new Property("NullableField");

        //Act
        property.Observe(null);
        property.Observe(null);

        //Assert
        Assert.That(property.NullCount, Is.EqualTo(2));
        Assert.That(property.ObservationCount, Is.EqualTo(2));
    }

    [Test]
    public void Observe_String_TracksMaxLength()
    {
        //Arrange
        Property property = new Property("Name");

        //Act
        property.Observe("Short");
        property.Observe("A much longer string value");

        //Assert
        Assert.That(property.MaxLength, Is.EqualTo(26));
    }

    [Test]
    public void Observe_UnicodeString_SetsHasUnicode()
    {
        //Arrange
        Property property = new Property("UnicodeField");

        //Act
        property.Observe("Caf\u00E9");

        //Assert
        Assert.That(property.HasUnicode, Is.True);
    }
}
