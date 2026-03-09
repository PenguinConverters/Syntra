// -----------------------------------------------------------------------
// <copyright file="EntityStateTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using PenguinConverters.Syntra.Core.Entities;

namespace PenguinConverters.Syntra.Core.Tests.Entities;

[TestFixture]
public class EntityStateTests
{
    [TestCase(EntityState.Unclassified, 0)]
    [TestCase(EntityState.Created, 1)]
    [TestCase(EntityState.Updated, 2)]
    [TestCase(EntityState.Deleted, 3)]
    public void EntityState_HasExpectedValue(EntityState state, int expectedValue)
    {
        //Arrange

        //Act
        int actualValue = (int)state;

        //Assert
        Assert.That(actualValue, Is.EqualTo(expectedValue));
    }

    [Test]
    public void EntityState_ContainsAllExpectedValues()
    {
        //Arrange
        string[] expectedNames = { "Unclassified", "Created", "Updated", "Deleted" };

        //Act
        string[] actualNames = Enum.GetNames(typeof(EntityState));

        //Assert
        Assert.That(actualNames, Is.EquivalentTo(expectedNames));
    }
}
