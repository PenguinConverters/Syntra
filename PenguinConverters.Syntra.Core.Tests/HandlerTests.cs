// -----------------------------------------------------------------------
// <copyright file="HandlerTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Core.Tests;

[TestFixture]
public class HandlerTests
{
    [Test]
    public void HandlerBuilder_BuildsHandler()
    {
        //Arrange
        HandlerBuilder builder = new HandlerBuilder();
        Configuration configuration = new Configuration();

        //Act
        Handler handler = builder
            .WithConfiguration(configuration)
            .Build();

        //Assert
        Assert.That(handler, Is.Not.Null);
    }

    [Test]
    public void Handler_WithNullConfiguration_ThrowsException()
    {
        //Arrange
        HandlerBuilder builder = new HandlerBuilder();

        //Act
        TestDelegate action = () => builder.Build();

        //Assert
        Assert.Throws<InvalidOperationException>(action);
    }

    [Test]
    public void HandlerBuilder_WithConfiguration_ReturnsBuilder()
    {
        //Arrange
        HandlerBuilder builder = new HandlerBuilder();
        Configuration configuration = new Configuration();

        //Act
        HandlerBuilder result = builder.WithConfiguration(configuration);

        //Assert
        Assert.That(result, Is.SameAs(builder));
    }

    [Test]
    public void HandlerBuilder_WithSourceMetadata_ReturnsBuilder()
    {
        //Arrange
        HandlerBuilder builder = new HandlerBuilder();
        byte[] metadata = new byte[] { 1, 2, 3 };

        //Act
        HandlerBuilder result = builder.WithSourceMetadata(metadata);

        //Assert
        Assert.That(result, Is.SameAs(builder));
    }

    [Test]
    public void HandlerBuilder_WithLogger_Null_ReturnsBuilder()
    {
        //Arrange
        HandlerBuilder builder = new HandlerBuilder();

        //Act
        HandlerBuilder result = builder.WithLogger(null!);

        //Assert
        Assert.That(result, Is.SameAs(builder));
    }

    [Test]
    public void Handler_Build_HadErrors_DefaultFalse()
    {
        //Arrange
        HandlerBuilder builder = new HandlerBuilder();
        Configuration configuration = new Configuration();

        //Act
        Handler handler = builder.WithConfiguration(configuration).Build();

        //Assert
        Assert.That(handler.HadErrors, Is.False);
    }

    [Test]
    public void Handler_Build_SourceMetadata_DefaultNull()
    {
        //Arrange
        HandlerBuilder builder = new HandlerBuilder();
        Configuration configuration = new Configuration();

        //Act
        Handler handler = builder.WithConfiguration(configuration).Build();

        //Assert
        Assert.That(handler.SourceMetadata, Is.Null);
    }
}
