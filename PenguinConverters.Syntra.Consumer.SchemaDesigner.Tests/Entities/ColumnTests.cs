// -----------------------------------------------------------------------
// <copyright file="ColumnTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using PenguinConverters.Syntra.Consumer.SchemaDesigner.Entities;

namespace PenguinConverters.Syntra.Consumer.SchemaDesigner.Tests.Entities;

[TestFixture]
public class ColumnTests
{
    [Test]
    public void GetTSQLDefinition_Varchar_CorrectOutput()
    {
        //Arrange
        Column column = new Column("DisplayName")
        {
            SqlType = SqlColumnType.VarChar,
            MaxLength = 100,
            IsNullable = false
        };

        //Act
        string result = column.GetTSQLDefinition();

        //Assert
        Assert.That(result, Is.EqualTo("[DisplayName] VARCHAR(128) NOT NULL"));
    }

    [Test]
    public void GetTSQLDefinition_Int_CorrectOutput()
    {
        //Arrange
        Column column = new Column("Age")
        {
            SqlType = SqlColumnType.Int,
            IsNullable = false
        };

        //Act
        string result = column.GetTSQLDefinition();

        //Assert
        Assert.That(result, Is.EqualTo("[Age] INT NOT NULL"));
    }

    [Test]
    public void GetTSQLDefinition_Nullable_IncludesNull()
    {
        //Arrange
        Column column = new Column("MiddleName")
        {
            SqlType = SqlColumnType.NVarChar,
            MaxLength = 50,
            IsNullable = true
        };

        //Act
        string result = column.GetTSQLDefinition();

        //Assert
        Assert.That(result, Does.Contain("NULL"));
        Assert.That(result, Does.Not.Contain("NOT NULL"));
    }

    [Test]
    public void GetTSQLDefinition_Bit_CorrectOutput()
    {
        //Arrange
        Column column = new Column("IsActive")
        {
            SqlType = SqlColumnType.Bit,
            IsNullable = false
        };

        //Act
        string result = column.GetTSQLDefinition();

        //Assert
        Assert.That(result, Is.EqualTo("[IsActive] BIT NOT NULL"));
    }

    [Test]
    public void GetTSQLDefinition_DateTime2_CorrectOutput()
    {
        //Arrange
        Column column = new Column("CreatedDate")
        {
            SqlType = SqlColumnType.DateTime2,
            IsNullable = true
        };

        //Act
        string result = column.GetTSQLDefinition();

        //Assert
        Assert.That(result, Is.EqualTo("[CreatedDate] DATETIME2(7) NULL"));
    }

    [Test]
    public void GetTSQLDefinition_NVarCharMax_CorrectOutput()
    {
        //Arrange
        Column column = new Column("Description")
        {
            SqlType = SqlColumnType.NVarChar,
            MaxLength = null,
            IsNullable = true
        };

        //Act
        string result = column.GetTSQLDefinition();

        //Assert
        Assert.That(result, Is.EqualTo("[Description] NVARCHAR(MAX) NULL"));
    }

    [Test]
    public void CalculateLength_NullMaxLength_ReturnsMax()
    {
        //Arrange
        Column column = new Column("Test")
        {
            MaxLength = null
        };

        //Act
        int result = column.CalculateLength();

        //Assert
        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void CalculateLength_SmallValue_RoundsUp()
    {
        //Arrange
        Column column = new Column("Test")
        {
            MaxLength = 10
        };

        //Act
        int result = column.CalculateLength();

        //Assert
        Assert.That(result, Is.EqualTo(16));
    }

    [Test]
    public void ToString_ReturnsTSQLDefinition()
    {
        //Arrange
        Column column = new Column("Id")
        {
            SqlType = SqlColumnType.Int,
            IsNullable = false
        };

        //Act
        string result = column.ToString();

        //Assert
        Assert.That(result, Is.EqualTo(column.GetTSQLDefinition()));
    }
}
