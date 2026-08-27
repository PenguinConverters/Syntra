using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace PenguinConverters.Syntra.Core.Security;

/// <summary>
/// Establishes who produced a file from its Authenticode signature.
/// </summary>
/// <remarks>
/// This is the check a strong name cannot make. .NET does not verify strong-name signatures when
/// it loads an assembly, so a public key says which assembly claims to be which; Authenticode says
/// who signed it and that the bytes have not changed since.
/// <para>
/// Authenticode is a Windows facility. Everywhere else the answer is
/// <see cref="PublisherTrust.NotVerifiable"/>, which is a statement about the platform and not
/// about the file.
/// </para>
/// </remarks>
public static class PublisherVerifier
{
    #region Constants

    /// <summary>
    /// The signature is trusted.
    /// </summary>
    private const int Trusted = 0;

    /// <summary>
    /// The file carries no signature.
    /// </summary>
    private const uint NoSignature = 0x800B0100;

    /// <summary>
    /// The signature is present but the publisher is explicitly distrusted.
    /// </summary>
    private const uint ExplicitDistrust = 0x800B0111;

    /// <summary>
    /// The signature is present but the subject is not trusted.
    /// </summary>
    private const uint SubjectNotTrusted = 0x800B0004;

    /// <summary>
    /// The signature chains to a root the machine does not trust.
    /// </summary>
    private const uint UntrustedRoot = 0x800B0109;

    /// <summary>
    /// Verify the file, and do not prompt.
    /// </summary>
    private const uint DisplayNone = 2;

    /// <summary>
    /// Do not check revocation. A connector is loaded on a server that may have no route to a
    /// revocation endpoint, and a check that hangs is worse than one not made.
    /// </summary>
    private const uint RevokeNone = 0;

    /// <summary>
    /// The subject of the verification is a file.
    /// </summary>
    private const uint ChoiceFile = 1;

    /// <summary>
    /// Perform the verification and keep the state for the close that follows.
    /// </summary>
    private const uint ActionVerify = 1;

    /// <summary>
    /// Release the state the verification allocated.
    /// </summary>
    private const uint ActionClose = 2;

    #endregion

    #region Fields

    /// <summary>
    /// The generic verification action, which applies the policy a signature is normally judged by.
    /// </summary>
    private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    #endregion

    #region Methods

    /// <summary>
    /// Establishes who produced a file, and whether that is the publisher expected.
    /// </summary>
    /// <param name="path">The file to verify.</param>
    /// <param name="expectedSubject">
    /// The common name the signing certificate is expected to carry, or <c>null</c> to report the
    /// publisher without judging it.
    /// </param>
    /// <returns>What was established.</returns>
    public static PublisherVerification Verify(string path, string? expectedSubject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!OperatingSystem.IsWindows())
        {
            return new PublisherVerification(
                PublisherTrust.NotVerifiable,
                null,
                null,
                "Authenticode verification is available on Windows only.");
        }

        return VerifyOnWindows(path, expectedSubject);
    }

    /// <summary>
    /// Returns the publisher of the file an assembly was loaded from, which is the publisher a
    /// connector is expected to share.
    /// </summary>
    /// <param name="path">The file to read the publisher from.</param>
    /// <returns>The common name of the signing certificate, or <c>null</c> when there is none.</returns>
    public static string? GetPublisher(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        return ReadSigner(path)?.GetNameInfo(X509NameType.SimpleName, false);
    }

    /// <summary>
    /// Verifies a file's Authenticode signature through the trust provider.
    /// </summary>
    /// <param name="path">The file to verify.</param>
    /// <param name="expectedSubject">The publisher expected, or <c>null</c>.</param>
    /// <returns>What was established.</returns>
    [SupportedOSPlatform("windows")]
    private static PublisherVerification VerifyOnWindows(string path, string? expectedSubject)
    {
        int result;

        WintrustFileInfo file = new WintrustFileInfo
        {
            StructSize = (uint)Marshal.SizeOf<WintrustFileInfo>(),
            FilePath = Marshal.StringToCoTaskMemUni(path)
        };

        IntPtr filePointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustFileInfo>());
        IntPtr dataPointer = IntPtr.Zero;

        WintrustData data = new WintrustData
        {
            StructSize = (uint)Marshal.SizeOf<WintrustData>(),
            UIChoice = DisplayNone,
            RevocationChecks = RevokeNone,
            UnionChoice = ChoiceFile,
            StateAction = ActionVerify
        };

        try
        {
            Marshal.StructureToPtr(file, filePointer, false);
            data.FileInfo = filePointer;

            dataPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustData>());
            Marshal.StructureToPtr(data, dataPointer, false);

            Guid action = GenericVerifyV2;

            result = WinVerifyTrust(IntPtr.Zero, ref action, dataPointer);

            // The provider holds state until it is told to release it, whatever the verdict.
            data = Marshal.PtrToStructure<WintrustData>(dataPointer);
            data.StateAction = ActionClose;
            Marshal.StructureToPtr(data, dataPointer, false);

            WinVerifyTrust(IntPtr.Zero, ref action, dataPointer);
        }
        finally
        {
            if (dataPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(dataPointer);
            }

            Marshal.FreeCoTaskMem(file.FilePath);
            Marshal.FreeCoTaskMem(filePointer);
        }

        return Interpret(path, result, expectedSubject);
    }

    /// <summary>
    /// Turns the trust provider's verdict into what it means for a connector.
    /// </summary>
    /// <param name="path">The file that was verified.</param>
    /// <param name="result">The verdict.</param>
    /// <param name="expectedSubject">The publisher expected, or <c>null</c>.</param>
    /// <returns>What was established.</returns>
    [SupportedOSPlatform("windows")]
    private static PublisherVerification Interpret(string path, int result, string? expectedSubject)
    {
        if ((uint)result == NoSignature)
        {
            return new PublisherVerification(PublisherTrust.Unsigned, null, null, "The file is not signed.");
        }

        if (result != Trusted)
        {
            string detail = (uint)result switch
            {
                ExplicitDistrust => "The publisher is explicitly distrusted on this machine.",
                SubjectNotTrusted => "The signature is not trusted on this machine.",
                UntrustedRoot => "The signature chains to a root this machine does not trust.",
                _ => $"The trust provider returned 0x{result:X8}."
            };

            // The certificate is still worth reporting: naming who a file claims to be from is
            // what makes an invalid signature actionable rather than merely alarming.
            using X509Certificate2? claimed = ReadSigner(path);

            return new PublisherVerification(
                PublisherTrust.Invalid,
                claimed?.GetNameInfo(X509NameType.SimpleName, false),
                claimed?.Thumbprint,
                detail);
        }

        using X509Certificate2? signer = ReadSigner(path);

        string? subject = signer?.GetNameInfo(X509NameType.SimpleName, false);

        if (expectedSubject is null)
        {
            return new PublisherVerification(PublisherTrust.Trusted, subject, signer?.Thumbprint);
        }

        bool matches = string.Equals(subject, expectedSubject, StringComparison.OrdinalIgnoreCase);

        return new PublisherVerification(
            matches ? PublisherTrust.Trusted : PublisherTrust.Untrusted,
            subject,
            signer?.Thumbprint,
            matches ? null : $"Signed by '{subject}', not '{expectedSubject}'.");
    }

    /// <summary>
    /// Reads the certificate a file was signed with.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The certificate, or <c>null</c> when the file carries none.</returns>
    [SupportedOSPlatform("windows")]
    private static X509Certificate2? ReadSigner(string path)
    {
        try
        {
            // Extracting an Authenticode signer has no replacement that is not obsolete: the
            // guidance behind SYSLIB0057 is about loading certificate *files*, which this is not.
            // The certificate it returns is re-loaded through the supported loader so that only
            // the extraction itself relies on the old API.
#pragma warning disable SYSLIB0057
            using X509Certificate signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057

            return X509CertificateLoader.LoadCertificate(signer.GetRawCertData());
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Verifies trust in a subject through a registered provider.
    /// </summary>
    /// <param name="window">The owner window, which is never used because no prompt is shown.</param>
    /// <param name="action">The action identifying the provider.</param>
    /// <param name="data">The subject and the options.</param>
    /// <returns>Zero when trusted; otherwise a status code.</returns>
    // DllImport rather than LibraryImport: the generated variant requires the whole assembly to
    // allow unsafe code, which is a large concession for one call whose arguments are all blittable.
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, IntPtr data);

    #endregion

    #region Nested Types

    /// <summary>
    /// The file a verification applies to.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    /// <summary>
    /// The subject of a verification and the options it runs under.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr ProviderCallbackData;
        public uint UIChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UIContext;
    }

    #endregion
}
