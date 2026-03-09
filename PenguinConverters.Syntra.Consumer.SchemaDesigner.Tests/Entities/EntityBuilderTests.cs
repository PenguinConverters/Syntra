// -----------------------------------------------------------------------
// <copyright file="EntityBuilderTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using PenguinConverters.Syntra.Consumer.SchemaDesigner.Entities;
using PenguinConverters.Syntra.Core.Entities;

namespace PenguinConverters.Syntra.Consumer.SchemaDesigner.Tests.Entities;

[TestFixture]
public class EntityBuilderTests
{
    [Test]
    public void AddEntity_TracksProperties()
    {
        //Arrange
        EntityBuilder builder = new EntityBuilder();
        Entity entity = new Entity("user-001");
        entity["DisplayName"] = "John Doe";
        entity["Age"] = 30;

        //Act
        builder.AddEntity(entity);

        //Assert
        Assert.That(builder.EntityCount, Is.EqualTo(1));
    }

    [Test]
    public void Build_ReturnsEntityWithColumns()
    {
        //Arrange
        EntityBuilder builder = new EntityBuilder();

        Entity entity1 = new Entity("user-001");
        entity1["DisplayName"] = "John Doe";
        entity1["Age"] = 30;
        entity1["IsActive"] = true;

        Entity entity2 = new Entity("user-002");
        entity2["DisplayName"] = "Jane Smith";
        entity2["Age"] = 25;
        entity2["IsActive"] = false;

        //Act
        builder.AddEntity(entity1);
        builder.AddEntity(entity2);
        List<Column> columns = builder.BuildColumns();

        //Assert
        Assert.That(columns, Is.Not.Null);
        Assert.That(columns.Count, Is.EqualTo(3));
        Assert.That(builder.EntityCount, Is.EqualTo(2));
    }

    [Test]
    public void AddEntity_MultipleEntities_MergesProperties()
    {
        //Arrange
        EntityBuilder builder = new EntityBuilder();

        Entity entity1 = new Entity("user-001");
        entity1["DisplayName"] = "John";

        Entity entity2 = new Entity("user-002");
        entity2["DisplayName"] = "Jane";
        entity2["Email"] = "jane@example.com";

        //Act
        builder.AddEntity(entity1);
        builder.AddEntity(entity2);
        List<Column> columns = builder.BuildColumns();

        //Assert
        Assert.That(columns.Count, Is.EqualTo(2));
    }

    [Test]
    public void BuildColumns_InfersCorrectTypes()
    {
        //Arrange
        EntityBuilder builder = new EntityBuilder();

        Entity entity = new Entity("user-001");
        entity["Name"] = "Test User";
        entity["Count"] = 42;
        entity["Active"] = true;
        entity["Created"] = DateTime.UtcNow;

        //Act
        builder.AddEntity(entity);
        List<Column> columns = builder.BuildColumns();

        //Assert
        Assert.That(columns, Has.Count.EqualTo(4));

        Column? nameColumn = columns.Find(c => c.Name == "Name");
        Column? countColumn = columns.Find(c => c.Name == "Count");
        Column? activeColumn = columns.Find(c => c.Name == "Active");
        Column? createdColumn = columns.Find(c => c.Name == "Created");

        Assert.That(nameColumn, Is.Not.Null);
        Assert.That(nameColumn!.SqlType, Is.EqualTo(SqlColumnType.VarChar));

        Assert.That(countColumn, Is.Not.Null);
        Assert.That(countColumn!.SqlType, Is.EqualTo(SqlColumnType.Int));

        Assert.That(activeColumn, Is.Not.Null);
        Assert.That(activeColumn!.SqlType, Is.EqualTo(SqlColumnType.Bit));

        Assert.That(createdColumn, Is.Not.Null);
        Assert.That(createdColumn!.SqlType, Is.EqualTo(SqlColumnType.DateTime2));
    }

    [Test]
    public void BuildColumns_EmptyBuilder_ReturnsEmptyList()
    {
        //Arrange
        EntityBuilder builder = new EntityBuilder();

        //Act
        List<Column> columns = builder.BuildColumns();

        //Assert
        Assert.That(columns, Is.Not.Null);
        Assert.That(columns, Is.Empty);
        Assert.That(builder.EntityCount, Is.EqualTo(0));
    }
}
