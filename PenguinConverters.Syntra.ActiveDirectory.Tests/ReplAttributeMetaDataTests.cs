// -----------------------------------------------------------------------
// <copyright file="ReplAttributeMetaDataTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using System.Xml.Serialization;

namespace PenguinConverters.Syntra.ActiveDirectory.Tests;

[TestFixture]
public class ReplAttributeMetaDataTests
{
    [Test]
    public void Deserialization_FromXml_Works()
    {
        //Arrange
        string xml = @"<DS_REPL_ATTR_META_DATA>
            <pszAttributeName>displayName</pszAttributeName>
            <dwVersion>3</dwVersion>
            <ftimeLastOriginatingChange>133456789012345678</ftimeLastOriginatingChange>
            <uuidLastOriginatingDsaInvocationID>a1b2c3d4-e5f6-7890-abcd-ef1234567890</uuidLastOriginatingDsaInvocationID>
            <usnOriginatingChange>12345</usnOriginatingChange>
            <usnLocalChange>67890</usnLocalChange>
            <pszLastOriginatingDsaDN>CN=NTDS Settings,CN=DC01,CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,DC=contoso,DC=com</pszLastOriginatingDsaDN>
        </DS_REPL_ATTR_META_DATA>";

        XmlSerializer serializer = new XmlSerializer(typeof(ReplAttributeMetaData));

        //Act
        ReplAttributeMetaData? result;
        using (StringReader reader = new StringReader(xml))
        {
            result = (ReplAttributeMetaData?)serializer.Deserialize(reader);
        }

        //Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.AttributeName, Is.EqualTo("displayName"));
        Assert.That(result.Version, Is.EqualTo(3));
        Assert.That(result.OriginatingChangeUsn, Is.EqualTo(12345));
        Assert.That(result.LocalChangeUsn, Is.EqualTo(67890));
    }

    [Test]
    public void GetLastOriginatingChangeDateTime_ValidFileTime_ReturnsDateTime()
    {
        //Arrange
        ReplAttributeMetaData metaData = new ReplAttributeMetaData
        {
            LastOriginatingChangeTime = "133456789012345678"
        };

        //Act
        DateTime? result = metaData.GetLastOriginatingChangeDateTime();

        //Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public void GetLastOriginatingChangeDateTime_NullValue_ReturnsNull()
    {
        //Arrange
        ReplAttributeMetaData metaData = new ReplAttributeMetaData
        {
            LastOriginatingChangeTime = null
        };

        //Act
        DateTime? result = metaData.GetLastOriginatingChangeDateTime();

        //Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetLastOriginatingChangeDateTime_EmptyValue_ReturnsNull()
    {
        //Arrange
        ReplAttributeMetaData metaData = new ReplAttributeMetaData
        {
            LastOriginatingChangeTime = ""
        };

        //Act
        DateTime? result = metaData.GetLastOriginatingChangeDateTime();

        //Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void DefaultValues_AreCorrect()
    {
        //Arrange

        //Act
        ReplAttributeMetaData metaData = new ReplAttributeMetaData();

        //Assert
        Assert.That(metaData.AttributeName, Is.Null);
        Assert.That(metaData.Version, Is.EqualTo(0));
        Assert.That(metaData.OriginatingChangeUsn, Is.EqualTo(0));
        Assert.That(metaData.LocalChangeUsn, Is.EqualTo(0));
    }
}
