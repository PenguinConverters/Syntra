// -----------------------------------------------------------------------
// <copyright file="EntityTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using PenguinConverters.Syntra.Core.Entities;

namespace PenguinConverters.Syntra.Core.Tests.Entities;

[TestFixture]
public class EntityTests
{
    #region Methods

    [Test]
    public void CreateEntity_DefaultState_IsUnclassified()
    {
        //Arrange

        //Act
        Entity entity = new Entity();

        //Assert
        Assert.That(entity.State, Is.EqualTo(EntityState.Unclassified));
    }

    [Test]
    public void SetProperty_GetProperty_ReturnsValue()
    {
        //Arrange
        Entity entity = new Entity();
        string propertyName = "displayName";
        string expectedValue = "John Doe";

        //Act
        entity.Properties[propertyName] = expectedValue;
        object? actualValue = entity.Properties[propertyName];

        //Assert
        Assert.That(actualValue, Is.EqualTo(expectedValue));
    }

    [Test]
    public void SetIdentifier_ReturnsCorrectValue()
    {
        //Arrange
        string expectedIdentifier = "user-001";

        //Act
        Entity entity = new Entity(expectedIdentifier);

        //Assert
        Assert.That(entity.Identifier, Is.EqualTo(expectedIdentifier));
    }

    [Test]
    public void Properties_AreCaseInsensitive()
    {
        //Arrange
        Entity entity = new Entity();
        string expectedValue = "TestValue";

        //Act
        entity.Properties["DisplayName"] = expectedValue;
        object? actualValue = entity.Properties["displayname"];

        //Assert
        Assert.That(actualValue, Is.EqualTo(expectedValue));
    }

    [Test]
    public void Indexer_SetAndGet_Works()
    {
        //Arrange
        Entity entity = new Entity();
        string propertyName = "mail";
        string expectedValue = "user@example.com";

        //Act
        entity[propertyName] = expectedValue;
        object? actualValue = entity[propertyName];

        //Assert
        Assert.That(actualValue, Is.EqualTo(expectedValue));
    }

    [Test]
    public void Indexer_NonExistentProperty_ReturnsNull()
    {
        //Arrange
        Entity entity = new Entity();

        //Act
        object? actualValue = entity["nonExistent"];

        //Assert
        Assert.That(actualValue, Is.Null);
    }

    [Test]
    public void ToString_ReturnsIdentifierAndState()
    {
        //Arrange
        Entity entity = new Entity("user-001");
        entity.State = EntityState.Created;

        //Act
        string result = entity.ToString();

        //Assert
        Assert.That(result, Is.EqualTo("user-001 [Created]"));
    }

    #endregion
}
