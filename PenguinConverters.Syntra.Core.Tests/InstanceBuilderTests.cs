// -----------------------------------------------------------------------
// <copyright file="InstanceBuilderTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using PenguinConverters.Syntra.Core.Source;

namespace PenguinConverters.Syntra.Core.Tests;

[TestFixture]
public class InstanceBuilderTests
{
    [Test]
    public void Build_WithNullAssemblyName_ThrowsArgumentException()
    {
        //Arrange

        //Act
        TestDelegate action = () => new InstanceBuilder<IProviderBuilder>(null!);

        //Assert
        Assert.Throws<ArgumentNullException>(action);
    }

    [Test]
    public void Build_WithEmptyAssemblyName_ThrowsArgumentException()
    {
        //Arrange

        //Act
        TestDelegate action = () => new InstanceBuilder<IProviderBuilder>(string.Empty);

        //Assert
        Assert.Throws<ArgumentNullException>(action);
    }

    [Test]
    public void Build_WithWhitespaceAssemblyName_ThrowsArgumentException()
    {
        //Arrange

        //Act
        TestDelegate action = () => new InstanceBuilder<IProviderBuilder>("   ");

        //Assert
        Assert.Throws<ArgumentNullException>(action);
    }

    [Test]
    public void With_AppliesAction()
    {
        //Arrange
        InstanceBuilder<IProviderBuilder> builder = new InstanceBuilder<IProviderBuilder>("TestAssembly");
        byte[] configBytes = new byte[] { 1, 2, 3 };

        //Act
        InstanceBuilder<IProviderBuilder> result = builder.WithConfiguration(configBytes);

        //Assert
        Assert.That(result, Is.SameAs(builder));
    }

    [Test]
    public void WithLogger_Null_DoesNotThrow()
    {
        //Arrange
        InstanceBuilder<IProviderBuilder> builder = new InstanceBuilder<IProviderBuilder>("TestAssembly");

        //Act
        InstanceBuilder<IProviderBuilder> result = builder.WithLogger(null!);

        //Assert
        Assert.That(result, Is.SameAs(builder));
    }

    [Test]
    public void WithMetadata_ReturnsBuilder()
    {
        //Arrange
        InstanceBuilder<IProviderBuilder> builder = new InstanceBuilder<IProviderBuilder>("TestAssembly");
        byte[] metadata = new byte[] { 10, 20, 30 };

        //Act
        InstanceBuilder<IProviderBuilder> result = builder.WithMetadata(metadata);

        //Assert
        Assert.That(result, Is.SameAs(builder));
    }
}
