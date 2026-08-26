// -----------------------------------------------------------------------
// <copyright file="EntityTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Types;

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
    public void CreateEntity_FromDictionary_ExposesProperties()
    {
        //Arrange
        Dictionary<string, object?> properties = new Dictionary<string, object?>
        {
            { "displayName", "John Doe" },
            { "mail", "john.doe@example.com" }
        };

        //Act
        Entity entity = new Entity(properties);

        //Assert
        Assert.That(entity.Identifier, Is.Null);
        Assert.That(entity["displayName"], Is.EqualTo("John Doe"));
        Assert.That(entity["mail"], Is.EqualTo("john.doe@example.com"));
    }

    [Test]
    public void CreateEntity_FromDictionary_WithIdentifier_SetsBoth()
    {
        //Arrange
        Dictionary<string, object?> properties = new Dictionary<string, object?>
        {
            { "displayName", "John Doe" }
        };

        //Act
        Entity entity = new Entity("user-001", properties);

        //Assert
        Assert.That(entity.Identifier, Is.EqualTo("user-001"));
        Assert.That(entity["displayName"], Is.EqualTo("John Doe"));
    }

    [Test]
    public void CreateEntity_FromCaseSensitiveDictionary_PropertiesAreCaseInsensitive()
    {
        //Arrange
        Dictionary<string, object?> properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            { "DisplayName", "John Doe" }
        };

        //Act
        Entity entity = new Entity(properties);

        //Assert
        Assert.That(entity["displayname"], Is.EqualTo("John Doe"));
        Assert.That(entity.Properties, Is.Not.SameAs(properties));
    }

    [Test]
    public void CreateEntity_FromCaseInsensitiveQuickDictionary_TakesItOver()
    {
        //Arrange
        QuickDictionary properties = new QuickDictionary(StringComparer.OrdinalIgnoreCase)
        {
            { "displayName", "John Doe" }
        };

        //Act
        Entity entity = new Entity(properties);

        //Assert
        Assert.That(entity.Properties, Is.SameAs(properties));
        Assert.That(entity["DISPLAYNAME"], Is.EqualTo("John Doe"));
    }

    [Test]
    public void CreateEntity_FromCaseSensitiveQuickDictionary_IsCopied()
    {
        //Arrange
        QuickDictionary properties = new QuickDictionary(StringComparer.Ordinal)
        {
            { "displayName", "John Doe" }
        };

        //Act
        Entity entity = new Entity(properties);

        //Assert
        Assert.That(entity.Properties, Is.Not.SameAs(properties));
        Assert.That(entity["DISPLAYNAME"], Is.EqualTo("John Doe"));
    }

    [Test]
    public void CreateEntity_FromNullDictionary_Throws()
    {
        //Arrange
        IDictionary<string, object?>? properties = null;

        //Act

        //Assert
        Assert.That(() => new Entity(properties!), Throws.ArgumentNullException);
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
