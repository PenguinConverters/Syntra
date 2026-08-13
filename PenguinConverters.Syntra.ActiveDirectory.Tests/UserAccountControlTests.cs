// -----------------------------------------------------------------------
// <copyright file="UserAccountControlTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

namespace PenguinConverters.Syntra.ActiveDirectory.Tests;

[TestFixture]
public class UserAccountControlTests
{
    #region Methods

    [Test]
    public void NormalAccount_HasCorrectValue()
    {
        //Arrange
        int expectedValue = 0x0200;

        //Act
        int actualValue = (int)UserAccountControl.NormalAccount;

        //Assert
        Assert.That(actualValue, Is.EqualTo(expectedValue));
    }

    [Test]
    public void AccountDisabled_HasCorrectValue()
    {
        //Arrange
        int expectedValue = 0x0002;

        //Act
        int actualValue = (int)UserAccountControl.AccountDisabled;

        //Assert
        Assert.That(actualValue, Is.EqualTo(expectedValue));
    }

    [Test]
    public void FlagsCanBeCombined()
    {
        //Arrange
        UserAccountControl normalAccount = UserAccountControl.NormalAccount;
        UserAccountControl accountDisabled = UserAccountControl.AccountDisabled;

        //Act
        UserAccountControl combined = normalAccount | accountDisabled;

        //Assert
        Assert.That(combined.HasFlag(UserAccountControl.NormalAccount), Is.True);
        Assert.That(combined.HasFlag(UserAccountControl.AccountDisabled), Is.True);
        Assert.That((int)combined, Is.EqualTo(0x0202));
    }

    [Test]
    public void PasswordNeverExpires_HasCorrectValue()
    {
        //Arrange
        int expectedValue = 0x10000;

        //Act
        int actualValue = (int)UserAccountControl.PasswordNeverExpires;

        //Assert
        Assert.That(actualValue, Is.EqualTo(expectedValue));
    }

    [Test]
    public void None_HasValueZero()
    {
        //Arrange

        //Act
        int actualValue = (int)UserAccountControl.None;

        //Assert
        Assert.That(actualValue, Is.EqualTo(0));
    }

    #endregion
}
