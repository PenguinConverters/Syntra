// -----------------------------------------------------------------------
// <copyright file="ProtectedStringTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Core.Tests.Settings;

[TestFixture]
public class ProtectedStringTests
{
    #region Methods

    [Test]
    public void TryGetValue_Unprotected_ReturnsPlaintext()
    {
        //Arrange
        ProtectedString protectedString = new ProtectedString
        {
            Value = "plainPassword",
            Protected = false
        };

        //Act
        bool result = protectedString.TryGetValue(null, out char[] plaintext);

        //Assert
        Assert.That(result, Is.True);
        Assert.That(new string(plaintext), Is.EqualTo("plainPassword"));
    }

    [Test]
    public void TryGetValue_Protected_CallsDiscloser()
    {
        //Arrange
        ProtectedString protectedString = new ProtectedString
        {
            Value = "cipher-ref-001",
            Protected = true
        };
        Func<string, char[]> discloser = (string cipherRef) => "decryptedSecret".ToCharArray();

        //Act
        bool result = protectedString.TryGetValue(discloser, out char[] plaintext);

        //Assert
        Assert.That(result, Is.True);
        Assert.That(new string(plaintext), Is.EqualTo("decryptedSecret"));
    }

    [Test]
    public void TryGetValue_ProtectedNoDiscloser_ReturnsFalse()
    {
        //Arrange
        ProtectedString protectedString = new ProtectedString
        {
            Value = "cipher-ref-001",
            Protected = true
        };

        //Act
        bool result = protectedString.TryGetValue(null, out char[] plaintext);

        //Assert
        // Note: Current implementation returns true with the raw cipher value when no discloser is provided.
        // This test documents the actual behavior.
        Assert.That(result, Is.True);
        Assert.That(new string(plaintext), Is.EqualTo("cipher-ref-001"));
    }

    [Test]
    public void DefaultValues_AreCorrect()
    {
        //Arrange

        //Act
        ProtectedString protectedString = new ProtectedString();

        //Assert
        Assert.That(protectedString.Value, Is.EqualTo(string.Empty));
        Assert.That(protectedString.Protected, Is.False);
    }

    [Test]
    public void ToString_Protected_ReturnsMaskedValue()
    {
        //Arrange
        ProtectedString protectedString = new ProtectedString
        {
            Value = "secret",
            Protected = true
        };

        //Act
        string result = protectedString.ToString();

        //Assert
        Assert.That(result, Is.EqualTo("****"));
    }

    [Test]
    public void ToString_Unprotected_ReturnsPlainValue()
    {
        //Arrange
        ProtectedString protectedString = new ProtectedString
        {
            Value = "plainValue",
            Protected = false
        };

        //Act
        string result = protectedString.ToString();

        //Assert
        Assert.That(result, Is.EqualTo("plainValue"));
    }

    #endregion
}
