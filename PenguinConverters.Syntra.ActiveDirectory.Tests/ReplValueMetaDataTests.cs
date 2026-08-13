// -----------------------------------------------------------------------
// <copyright file="ReplValueMetaDataTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

namespace PenguinConverters.Syntra.ActiveDirectory.Tests;

[TestFixture]
public class ReplValueMetaDataTests
{
    #region Constants

    private const string PresentMemberXml = @"<DS_REPL_VALUE_META_DATA>
        <pszAttributeName>member</pszAttributeName>
        <pszObjectDn>CN=John Doe,OU=Users,DC=contoso,DC=com</pszObjectDn>
        <cbData>24</cbData>
        <dwVersion>1</dwVersion>
        <ftimeLastOriginatingChange>133456789012345678</ftimeLastOriginatingChange>
        <ftimeCreated>133456789012345678</ftimeCreated>
        <ftimeDeleted>0</ftimeDeleted>
        <uuidLastOriginatingDsaInvocationID>a1b2c3d4-e5f6-7890-abcd-ef1234567890</uuidLastOriginatingDsaInvocationID>
        <usnOriginatingChange>12345</usnOriginatingChange>
        <usnLocalChange>67890</usnLocalChange>
        <pszLastOriginatingDsaDN>CN=NTDS Settings,CN=DC01,CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,DC=contoso,DC=com</pszLastOriginatingDsaDN>
    </DS_REPL_VALUE_META_DATA>";

    private const string RemovedMemberXml = @"<DS_REPL_VALUE_META_DATA>
        <pszAttributeName>member</pszAttributeName>
        <pszObjectDn>CN=Jane Roe,OU=Users,DC=contoso,DC=com</pszObjectDn>
        <cbData>24</cbData>
        <dwVersion>2</dwVersion>
        <ftimeLastOriginatingChange>133456789012345678</ftimeLastOriginatingChange>
        <ftimeCreated>133456789012345678</ftimeCreated>
        <ftimeDeleted>133456789012345679</ftimeDeleted>
        <usnOriginatingChange>12346</usnOriginatingChange>
        <usnLocalChange>67891</usnLocalChange>
    </DS_REPL_VALUE_META_DATA>";

    #endregion

    #region Methods

    [Test]
    public void TryParse_PresentValue_ReturnsMetaData()
    {
        //Arrange

        //Act
        bool parsed = ReplValueMetaData.TryParse(PresentMemberXml, out ReplValueMetaData? metaData);

        //Assert
        Assert.That(parsed, Is.True);
        Assert.That(metaData, Is.Not.Null);
        Assert.That(metaData!.AttributeName, Is.EqualTo("member"));
        Assert.That(metaData.ObjectDn, Is.EqualTo("CN=John Doe,OU=Users,DC=contoso,DC=com"));
        Assert.That(metaData.Version, Is.EqualTo(1));
        Assert.That(metaData.LocalChangeUsn, Is.EqualTo(67890));
        Assert.That(metaData.IsDeleted, Is.False);
    }

    [Test]
    public void TryParse_RemovedValue_IsDeleted()
    {
        //Arrange

        //Act
        bool parsed = ReplValueMetaData.TryParse(RemovedMemberXml, out ReplValueMetaData? metaData);

        //Assert
        Assert.That(parsed, Is.True);
        Assert.That(metaData!.IsDeleted, Is.True);
        Assert.That(metaData.Version % 2, Is.EqualTo(0));
        Assert.That(metaData.ObjectDn, Is.EqualTo("CN=Jane Roe,OU=Users,DC=contoso,DC=com"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not xml at all")]
    [TestCase("<DS_REPL_VALUE_META_DATA><unclosed>")]
    public void TryParse_InvalidValue_ReturnsFalse(string? value)
    {
        //Arrange

        //Act
        bool parsed = ReplValueMetaData.TryParse(value, out ReplValueMetaData? metaData);

        //Assert
        Assert.That(parsed, Is.False);
        Assert.That(metaData, Is.Null);
    }

    [Test]
    public void Attribute_IsReplValueMetaData()
    {
        //Arrange

        //Act
        string attribute = ReplValueMetaData.Attribute;

        //Assert
        Assert.That(attribute, Is.EqualTo("msDS-ReplValueMetaData"));
    }

    [Test]
    public void IsDeleted_ZeroDeletedTime_ReturnsFalse()
    {
        //Arrange
        ReplValueMetaData metaData = new ReplValueMetaData { DeletedTime = "0" };

        //Act
        bool deleted = metaData.IsDeleted;

        //Assert
        Assert.That(deleted, Is.False);
    }

    #endregion
}
