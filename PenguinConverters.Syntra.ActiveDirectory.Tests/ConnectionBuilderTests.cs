// -----------------------------------------------------------------------
// <copyright file="ConnectionBuilderTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

namespace PenguinConverters.Syntra.ActiveDirectory.Tests;

[TestFixture]
public class ConnectionBuilderTests
{
    [Test]
    public void AddDomainController_StoresValue()
    {
        //Arrange
        ConnectionBuilder builder = new ConnectionBuilder();
        string domainController = "dc01.contoso.com";

        //Act
        Connection connection = builder
            .AddDomainController(domainController)
            .AddBaseDN("DC=contoso,DC=com")
            .Build();

        //Assert
        Assert.That(connection.DomainControllers, Contains.Item(domainController));
    }

    [Test]
    public void AddBaseDN_StoresValue()
    {
        //Arrange
        ConnectionBuilder builder = new ConnectionBuilder();
        string baseDn = "DC=contoso,DC=com";

        //Act
        Connection connection = builder
            .AddDomainController("dc01.contoso.com")
            .AddBaseDN(baseDn)
            .Build();

        //Assert
        Assert.That(connection.BaseSearchDn, Is.EqualTo(baseDn));
    }

    [Test]
    public void AddPort_StoresValue()
    {
        //Arrange
        ConnectionBuilder builder = new ConnectionBuilder();
        int port = 389;

        //Act
        Connection connection = builder
            .AddDomainController("dc01.contoso.com")
            .AddBaseDN("DC=contoso,DC=com")
            .AddPort(port)
            .Build();

        //Assert
        Assert.That(connection.Port, Is.EqualTo(port));
    }

    [Test]
    public void SetSecureSocketLayer_StoresValue()
    {
        //Arrange
        ConnectionBuilder builder = new ConnectionBuilder();

        //Act
        Connection connection = builder
            .AddDomainController("dc01.contoso.com")
            .AddBaseDN("DC=contoso,DC=com")
            .SetSecureSocketLayer(false)
            .Build();

        //Assert
        Assert.That(connection.SecureSocketLayer, Is.False);
    }

    [Test]
    public void Build_ReturnsConnection()
    {
        //Arrange
        ConnectionBuilder builder = new ConnectionBuilder();

        //Act
        Connection connection = builder
            .AddDomainController("dc01.contoso.com")
            .AddBaseDN("DC=contoso,DC=com")
            .Build();

        //Assert
        Assert.That(connection, Is.Not.Null);
        Assert.That(connection, Is.InstanceOf<Connection>());
    }

    [Test]
    public void DefaultPort_Is636()
    {
        //Arrange
        ConnectionBuilder builder = new ConnectionBuilder();

        //Act
        Connection connection = builder
            .AddDomainController("dc01.contoso.com")
            .AddBaseDN("DC=contoso,DC=com")
            .Build();

        //Assert
        Assert.That(connection.Port, Is.EqualTo(636));
    }

    [Test]
    public void Build_WithNoDomainController_ThrowsException()
    {
        //Arrange
        ConnectionBuilder builder = new ConnectionBuilder();
        builder.AddBaseDN("DC=contoso,DC=com");

        //Act
        TestDelegate action = () => builder.Build();

        //Assert
        Assert.Throws<InvalidOperationException>(action);
    }

    [Test]
    public void Build_WithNoBaseDN_ThrowsException()
    {
        //Arrange
        ConnectionBuilder builder = new ConnectionBuilder();
        builder.AddDomainController("dc01.contoso.com");

        //Act
        TestDelegate action = () => builder.Build();

        //Assert
        Assert.Throws<InvalidOperationException>(action);
    }
}
