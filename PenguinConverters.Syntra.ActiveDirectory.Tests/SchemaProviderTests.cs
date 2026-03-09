// -----------------------------------------------------------------------
// <copyright file="SchemaProviderTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

namespace PenguinConverters.Syntra.ActiveDirectory.Tests;

[TestFixture]
public class SchemaProviderTests
{
    [Test]
    public void EncoderObjectGUID_ConvertsCorrectly()
    {
        //Arrange
        string guidString = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        Guid expectedGuid = Guid.Parse(guidString);

        //Act
        byte[] encodedBytes = SchemaProvider.EncoderObjectGUID(guidString);
        object? decodedValue = SchemaProvider.DecoderObjectGUID(encodedBytes);

        //Assert
        Assert.That(decodedValue, Is.EqualTo(expectedGuid.ToString()));
    }

    [Test]
    public void EncoderObjectSID_ConvertsCorrectly()
    {
        //Arrange
        string sidString = "S-1-5-21-3623811015-3361044348-30300820-1013";

        //Act
        byte[] encodedBytes = SchemaProvider.EncoderObjectSID(sidString);
        object? decodedValue = SchemaProvider.DecoderObjectSID(encodedBytes);

        //Assert
        Assert.That(decodedValue, Is.EqualTo(sidString));
    }

    [Test]
    public void DecoderBoolean_True_ReturnsTrue()
    {
        //Arrange
        byte[] trueBytes = System.Text.Encoding.UTF8.GetBytes("TRUE");

        //Act
        object? result = SchemaProvider.DecoderBoolean(trueBytes);

        //Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void DecoderBoolean_False_ReturnsFalse()
    {
        //Arrange
        byte[] falseBytes = System.Text.Encoding.UTF8.GetBytes("FALSE");

        //Act
        object? result = SchemaProvider.DecoderBoolean(falseBytes);

        //Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void DecoderInteger_ValidNumber_ReturnsInt()
    {
        //Arrange
        byte[] intBytes = System.Text.Encoding.UTF8.GetBytes("42");

        //Act
        object? result = SchemaProvider.DecoderInteger(intBytes);

        //Assert
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public void DecoderUnicode_ReturnsString()
    {
        //Arrange
        string expectedValue = "Hello World";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(expectedValue);

        //Act
        object? result = SchemaProvider.DecoderUnicode(bytes);

        //Assert
        Assert.That(result, Is.EqualTo(expectedValue));
    }

    [Test]
    public void EncoderObjectGUID_RoundTrip_PreservesValue()
    {
        //Arrange
        Guid originalGuid = Guid.NewGuid();
        string guidString = originalGuid.ToString();

        //Act
        byte[] encoded = SchemaProvider.EncoderObjectGUID(guidString);
        Guid roundTripped = new Guid(encoded);

        //Assert
        Assert.That(roundTripped, Is.EqualTo(originalGuid));
    }
}
