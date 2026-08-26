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
    #region Methods

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

    // The SID codecs are implemented against the wire format rather than
    // System.Security.Principal.SecurityIdentifier, which lives in the Windows-only
    // System.Security.Principal.Windows assembly. These tests therefore run on every platform.
    [Test]
    [TestCase("S-1-5-21-3623811015-3361044348-30300820-1013")]
    [TestCase("S-1-5-18")]
    [TestCase("S-1-5-32-544")]
    [TestCase("S-1-1-0")]
    [TestCase("S-1-16-12288")]
    [TestCase("S-1-5-80-3139157870-2983391045-3678747466-658725712-1809340420")]
    [TestCase("S-1-5-21-4294967295-4294967295-4294967295-4294967295")]
    public void EncoderObjectSID_ConvertsCorrectly(string sidString)
    {
        //Act
        byte[] encodedBytes = SchemaProvider.EncoderObjectSID(sidString);
        object? decodedValue = SchemaProvider.DecoderObjectSID(encodedBytes);

        //Assert
        Assert.That(decodedValue, Is.EqualTo(sidString));
    }

    [Test]
    public void EncoderObjectSID_ProducesDocumentedWireFormat()
    {
        //Arrange
        // S-1-5-18 (LocalSystem): revision 01, one sub-authority, authority 5 big-endian,
        // then sub-authority 18 (0x12) little-endian.
        string sidString = "S-1-5-18";

        //Act
        byte[] encodedBytes = SchemaProvider.EncoderObjectSID(sidString);

        //Assert
        Assert.That(Convert.ToHexString(encodedBytes), Is.EqualTo("010100000000000512000000"));
    }

    [Test]
    public void DecoderObjectSID_MalformedBuffer_FallsBackToBase64()
    {
        //Arrange
        byte[] tooShort = new byte[] { 0x01, 0x01, 0x00 };

        //Act
        object? decodedValue = SchemaProvider.DecoderObjectSID(tooShort);

        //Assert
        Assert.That(decodedValue, Is.EqualTo(Convert.ToBase64String(tooShort)));
    }

    [Test]
    [TestCase("not-a-sid")]
    [TestCase("S-1")]
    [TestCase("S-x-5-18")]
    [TestCase("S-1-5-notanumber")]
    public void EncoderObjectSID_InvalidInput_Throws(string sidString)
    {
        //Assert
        Assert.That(() => SchemaProvider.EncoderObjectSID(sidString), Throws.ArgumentException);
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

    #endregion
}
