// -----------------------------------------------------------------------
// <copyright file="ConfigurationTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Core.Tests.Settings;

[TestFixture]
public class ConfigurationTests
{
    #region Methods

    [Test]
    public void DefaultValues_AreCorrect()
    {
        //Arrange

        //Act
        Configuration configuration = new Configuration();

        //Assert
        Assert.That(configuration.Source, Is.Not.Null);
        Assert.That(configuration.Target, Is.Not.Null);
        Assert.That(configuration.Delta, Is.False);
        Assert.That(configuration.Schedule, Is.Null);
        Assert.That(configuration.MaxDegreeOfParallelism, Is.EqualTo(1));
        Assert.That(configuration.Certificate, Is.Null);
        Assert.That(configuration.Thresholds, Is.Not.Null);
        Assert.That(configuration.Thresholds, Is.Empty);
        Assert.That(configuration.SchemaDesigner, Is.False);
    }

    [Test]
    public void Delta_DefaultFalse()
    {
        //Arrange
        Configuration configuration = new Configuration();

        //Act
        bool delta = configuration.Delta;

        //Assert
        Assert.That(delta, Is.False);
    }

    [Test]
    public void MaxDegreeOfParallelism_Default1()
    {
        //Arrange
        Configuration configuration = new Configuration();

        //Act
        int maxDegreeOfParallelism = configuration.MaxDegreeOfParallelism;

        //Assert
        Assert.That(maxDegreeOfParallelism, Is.EqualTo(1));
    }

    [Test]
    public void Delta_SetTrue_ReturnsTrue()
    {
        //Arrange
        Configuration configuration = new Configuration();

        //Act
        configuration.Delta = true;

        //Assert
        Assert.That(configuration.Delta, Is.True);
    }

    [Test]
    public void MaxDegreeOfParallelism_SetValue_ReturnsValue()
    {
        //Arrange
        Configuration configuration = new Configuration();

        //Act
        configuration.MaxDegreeOfParallelism = 8;

        //Assert
        Assert.That(configuration.MaxDegreeOfParallelism, Is.EqualTo(8));
    }

    #endregion
}
