// -----------------------------------------------------------------------
// <copyright file="KeyraSettingsTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Core.Tests.Settings;

[TestFixture]
public class KeyraSettingsTests
{
    #region Fields

    private string? _originalShare;
    private string? _originalPassword;

    #endregion

    #region Methods

    [SetUp]
    public void SetUp()
    {
        //Arrange
        _originalShare = Environment.GetEnvironmentVariable(KeyraSettings.DefaultShareVariable);
        _originalPassword = Environment.GetEnvironmentVariable(KeyraSettings.DefaultPasswordVariable);

        Environment.SetEnvironmentVariable(KeyraSettings.DefaultShareVariable, null);
        Environment.SetEnvironmentVariable(KeyraSettings.DefaultPasswordVariable, null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(KeyraSettings.DefaultShareVariable, _originalShare);
        Environment.SetEnvironmentVariable(KeyraSettings.DefaultPasswordVariable, _originalPassword);
    }

    [Test]
    public void CreateDecryptor_NoKeySource_ThrowsNamingTheShareVariable()
    {
        //Arrange
        KeyraSettings settings = new KeyraSettings();

        //Act
        InvalidOperationException? exception =
            Assert.Throws<InvalidOperationException>(() => settings.CreateDecryptor());

        //Assert
        Assert.That(exception!.Message, Does.Contain(KeyraSettings.DefaultShareVariable));
    }

    [Test]
    public void CreateDecryptor_NoKeySource_NamesTheConfiguredShareVariable()
    {
        //Arrange
        KeyraSettings settings = new KeyraSettings { ShareVariable = "CONTOSO_SHARE" };

        //Act
        InvalidOperationException? exception =
            Assert.Throws<InvalidOperationException>(() => settings.CreateDecryptor());

        //Assert
        Assert.That(exception!.Message, Does.Contain("CONTOSO_SHARE"));
    }

    [Test]
    public void CreateDecryptor_ShareVariableHoldingNonKeyraText_ReportsTheMissingMarker()
    {
        //Arrange
        KeyraSettings settings = new KeyraSettings();
        Environment.SetEnvironmentVariable(KeyraSettings.DefaultShareVariable, "not-an-armored-share");

        //Act
        InvalidOperationException? exception =
            Assert.Throws<InvalidOperationException>(() => settings.CreateDecryptor());

        //Assert
        Assert.That(exception!.Message, Does.Contain("KEYRA:"));
    }

    [Test]
    public void CreateDecryptor_MissingKeyFile_Throws()
    {
        //Arrange
        KeyraSettings settings = new KeyraSettings
        {
            KeyFile = Path.Combine(Path.GetTempPath(), $"syntra-absent-{Guid.NewGuid():N}.keyra")
        };

        //Act
        TestDelegate action = () => settings.CreateDecryptor();

        //Assert
        Assert.Throws<FileNotFoundException>(action);
    }

    [Test]
    public void Defaults_AreNull()
    {
        //Arrange

        //Act
        KeyraSettings settings = new KeyraSettings();

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(settings.KeyFile, Is.Null);
            Assert.That(settings.ShareVariable, Is.Null);
            Assert.That(settings.PasswordVariable, Is.Null);
        });
    }

    [Test]
    public void Configuration_Keyra_DefaultsToNull()
    {
        //Arrange

        //Act
        Configuration configuration = new Configuration();

        //Assert
        Assert.That(configuration.Keyra, Is.Null);
    }

    [Test]
    public void Secret_ProtectedWithoutDecryptor_IsNotDisclosed()
    {
        //Arrange
        // The placeholder this replaced handed back the ciphertext and reported success, so a
        // missing key surfaced as a rejected credential rather than as a configuration fault.
        Secret secret = new Secret { Value = "q0FhZm9yZz09", Protected = true };

        //Act
        bool disclosed = secret.TryGetValue((Func<string, char[]>)null!, out char[] plaintext);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(disclosed, Is.False);
            Assert.That(plaintext, Is.Null);
        });
    }

    [Test]
    public void Secret_Unprotected_IsDisclosedWithoutADecryptor()
    {
        //Arrange
        Secret secret = Secret.FromPlaintext("Server=localhost;Database=Syntra");

        //Act
        bool disclosed = secret.TryGetValue((Func<string, char[]>)null!, out char[] plaintext);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(disclosed, Is.True);
            Assert.That(new string(plaintext), Is.EqualTo("Server=localhost;Database=Syntra"));
        });
    }

    [Test]
    public void Secret_Protected_IsDisclosedThroughTheDecryptFunction()
    {
        //Arrange
        Secret secret = new Secret { Value = "cipher-reference", Protected = true };
        Func<string, char[]> decrypt = (string ciphertext) => "disclosed".ToCharArray();

        //Act
        bool disclosed = secret.TryGetValue(decrypt, out char[] plaintext);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(disclosed, Is.True);
            Assert.That(new string(plaintext), Is.EqualTo("disclosed"));
        });
    }

    [Test]
    public void Secret_FailingDecryption_IsNotDisclosed()
    {
        //Arrange
        Secret secret = new Secret { Value = "cipher-reference", Protected = true };
        Func<string, char[]> decrypt = (string ciphertext) => throw new InvalidOperationException("wrong key");

        //Act
        bool disclosed = secret.TryGetValue(decrypt, out char[] plaintext);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(disclosed, Is.False);
            Assert.That(plaintext, Is.Null);
        });
    }

    #endregion
}
