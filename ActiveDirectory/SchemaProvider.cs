using System.Buffers.Binary;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PenguinConverters.Syntra.ActiveDirectory;

/// <summary>
/// Queries the Active Directory schema naming context to discover attribute definitions
/// and provides decoder functions that map LDAP syntax types to .NET types.
/// </summary>
public class SchemaProvider
{
    #region Constants

    /// <summary>Bytes preceding the sub-authorities: revision, count, and the 6-byte identifier authority.</summary>
    private const int SidHeaderLength = 8;

    /// <summary>Each sub-authority is a little-endian 32-bit value.</summary>
    private const int SidSubAuthorityLength = 4;

    /// <summary>The SID structure stores the sub-authority count in a single byte, but Windows caps it at 15.</summary>
    private const int SidMaximumSubAuthorityCount = 15;

    #endregion

    #region Fields

    private readonly Connection _connection;
    private readonly ILogger _logger;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaProvider"/> class.
    /// </summary>
    /// <param name="connection">The LDAP connection to use for schema queries.</param>
    /// <param name="logger">The logger instance.</param>
    public SchemaProvider(Connection connection, ILogger? logger = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? NullLogger.Instance;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Asynchronously queries the AD schema to build a dictionary of attribute decoders.
    /// Each decoder maps a raw LDAP byte array to the appropriate .NET type
    /// based on the attribute's <c>oMSyntax</c> and <c>attributeSyntax</c> values.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation of the schema query.</param>
    /// <returns>
    /// A dictionary mapping attribute names (case-insensitive) to decoder functions.
    /// </returns>
    public async Task<Dictionary<string, Func<byte[], object?>>> GetDecodersAsync(
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, Func<byte[], object?>> decoders = new Dictionary<string, Func<byte[], object?>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            LdapConnection ldapConnection = _connection.OpenLdapConnection();

            // Query RootDSE for schema naming context
            SearchRequest rootDseRequest = new SearchRequest(
                null,
                "(objectClass=*)",
                SearchScope.Base,
                "schemaNamingContext");

            SearchResponse rootDseResponse = await ldapConnection
                .SendRequestAsync<SearchResponse>(rootDseRequest, cancellationToken)
                .ConfigureAwait(false);

            if (rootDseResponse.Entries.Count == 0)
            {
                _logger.LogWarning("Could not retrieve RootDSE");
                return decoders;
            }

            string? schemaNc = rootDseResponse.Entries[0].Attributes["schemaNamingContext"][0]?.ToString();

            if (string.IsNullOrEmpty(schemaNc))
            {
                _logger.LogWarning("Schema naming context is empty");
                return decoders;
            }

            // Query all attributeSchema objects
            SearchRequest schemaRequest = new SearchRequest(
                schemaNc,
                "(objectClass=attributeSchema)",
                SearchScope.OneLevel,
                "lDAPDisplayName", "oMSyntax", "attributeSyntax");

            PageResultRequestControl pageControl = new PageResultRequestControl(1000);
            schemaRequest.Controls.Add(pageControl);

            while (true)
            {
                SearchResponse schemaResponse = await ldapConnection
                    .SendRequestAsync<SearchResponse>(schemaRequest, cancellationToken)
                    .ConfigureAwait(false);

                foreach (SearchResultEntry entry in schemaResponse.Entries)
                {
                    string? displayName = entry.Attributes["lDAPDisplayName"]?[0]?.ToString();
                    if (string.IsNullOrEmpty(displayName))
                    {
                        continue;
                    }

                    string? omSyntaxStr = entry.Attributes["oMSyntax"]?[0]?.ToString();
                    string? attributeSyntax = entry.Attributes["attributeSyntax"]?[0]?.ToString();

                    if (!int.TryParse(omSyntaxStr, out int omSyntax))
                    {
                        continue;
                    }

                    Func<byte[], object?>? decoder = MapDecoder(omSyntax, attributeSyntax);
                    if (decoder is not null)
                    {
                        decoders[displayName] = decoder;
                    }
                }

                PageResultResponseControl? pageResponse = schemaResponse.Controls
                    .OfType<PageResultResponseControl>()
                    .FirstOrDefault();

                if (pageResponse is null || pageResponse.Cookie.Length == 0)
                {
                    break;
                }

                pageControl.Cookie = pageResponse.Cookie;
            }

            _logger.LogInformation("Loaded {Count} attribute decoders from schema", decoders.Count);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a schema failure; let the caller observe it.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load schema decoders");
        }

        return decoders;
    }

    /// <summary>
    /// Maps an oMSyntax and attributeSyntax combination to the appropriate decoder function.
    /// </summary>
    /// <param name="omSyntax">The oMSyntax value from the AD schema.</param>
    /// <param name="attributeSyntax">The attributeSyntax OID from the AD schema.</param>
    /// <returns>A decoder function, or <c>null</c> if no mapping exists.</returns>
    private static Func<byte[], object?>? MapDecoder(int omSyntax, string? attributeSyntax)
    {
        return omSyntax switch
        {
            // Boolean
            1 => DecoderBoolean,
            // Integer (Enumeration / Integer)
            2 or 10 => DecoderInteger,
            // LargeInteger
            65 when attributeSyntax == "2.5.5.16" => DecoderLargeInteger,
            // Generalized Time / UTC Time
            24 or 23 => DecoderDateTime,
            // Octet String - special handling based on attributeSyntax
            4 when attributeSyntax == "2.5.5.10" => DecoderOctetString,
            // Object Identifier (String)
            6 => DecoderUnicode,
            // Unicode String / Case-Ignore String / Printable String / DN String
            64 or 20 or 19 or 12 => DecoderUnicode,
            // NT Security Descriptor
            66 => DecoderOctetString,
            // SID
            4 when attributeSyntax == "2.5.5.17" => DecoderObjectSID,
            _ => DecoderUnicode
        };
    }

    /// <summary>
    /// Decodes a byte array to a boolean value.
    /// </summary>
    /// <param name="bytes">The raw attribute value.</param>
    /// <returns>A boolean value.</returns>
    public static object? DecoderBoolean(byte[] bytes)
    {
        string value = Encoding.UTF8.GetString(bytes);
        return string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decodes a byte array to a 32-bit integer.
    /// </summary>
    /// <param name="bytes">The raw attribute value.</param>
    /// <returns>An integer value.</returns>
    public static object? DecoderInteger(byte[] bytes)
    {
        string value = Encoding.UTF8.GetString(bytes);
        return int.TryParse(value, out int result) ? result : 0;
    }

    /// <summary>
    /// Decodes a byte array to a 64-bit integer (large integer).
    /// </summary>
    /// <param name="bytes">The raw attribute value.</param>
    /// <returns>A long value.</returns>
    public static object? DecoderLargeInteger(byte[] bytes)
    {
        string value = Encoding.UTF8.GetString(bytes);
        return long.TryParse(value, out long result) ? result : 0L;
    }

    /// <summary>
    /// Decodes a byte array to a <see cref="DateTime"/> from generalized time format.
    /// </summary>
    /// <param name="bytes">The raw attribute value.</param>
    /// <returns>A DateTime value, or <c>null</c> if parsing fails.</returns>
    public static object? DecoderDateTime(byte[] bytes)
    {
        string value = Encoding.UTF8.GetString(bytes);

        // Generalized time format: yyyyMMddHHmmss.0Z
        if (value.Length >= 14)
        {
            string dateStr = value[..14];
            if (DateTime.TryParseExact(dateStr, "yyyyMMddHHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime dt))
            {
                return dt.ToUniversalTime();
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes a byte array to a Unicode string.
    /// </summary>
    /// <param name="bytes">The raw attribute value.</param>
    /// <returns>A string value.</returns>
    public static object? DecoderUnicode(byte[] bytes)
    {
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Decodes a byte array as a raw octet string (returns the byte array as-is).
    /// </summary>
    /// <param name="bytes">The raw attribute value.</param>
    /// <returns>The byte array.</returns>
    public static object? DecoderOctetString(byte[] bytes)
    {
        return bytes;
    }

    /// <summary>
    /// Decodes a byte array containing a Windows Security Identifier (SID) to its string representation.
    /// </summary>
    /// <param name="bytes">The raw SID bytes.</param>
    /// <returns>The SID string (e.g., <c>S-1-5-21-...</c>).</returns>
    public static object? DecoderObjectSID(byte[] bytes)
    {
        try
        {
            return SidToString(bytes);
        }
        catch
        {
            return Convert.ToBase64String(bytes);
        }
    }

    /// <summary>
    /// Converts a binary SID to its string form.
    /// </summary>
    /// <remarks>
    /// Implemented against the SID wire format rather than <c>System.Security.Principal.SecurityIdentifier</c>,
    /// which is Windows-only and would throw on Linux hosts. The layout is one revision byte, one
    /// sub-authority count byte, a 6-byte big-endian identifier authority, then that many
    /// little-endian 32-bit sub-authorities.
    /// </remarks>
    /// <param name="bytes">The raw SID bytes.</param>
    /// <returns>The SID string (e.g., <c>S-1-5-21-...</c>).</returns>
    /// <exception cref="ArgumentException">Thrown when the buffer is not a well-formed SID.</exception>
    private static string SidToString(byte[] bytes)
    {
        if (bytes.Length < SidHeaderLength)
            throw new ArgumentException("A SID must be at least 8 bytes long.", nameof(bytes));

        byte revision = bytes[0];
        int subAuthorityCount = bytes[1];

        if (subAuthorityCount > SidMaximumSubAuthorityCount)
            throw new ArgumentException("A SID may not declare more than 15 sub-authorities.", nameof(bytes));

        if (bytes.Length < SidHeaderLength + (subAuthorityCount * SidSubAuthorityLength))
            throw new ArgumentException("The buffer is shorter than its sub-authority count requires.", nameof(bytes));

        ulong identifierAuthority = 0;
        for (int index = 2; index < SidHeaderLength; index++)
            identifierAuthority = (identifierAuthority << 8) | bytes[index];

        StringBuilder builder = new StringBuilder();
        builder.Append("S-").Append(revision).Append('-');

        // Windows renders an authority that does not fit in 32 bits as hexadecimal.
        if (identifierAuthority > uint.MaxValue)
            builder.Append("0x").Append(identifierAuthority.ToString("x12", CultureInfo.InvariantCulture));
        else
            builder.Append(identifierAuthority.ToString(CultureInfo.InvariantCulture));

        for (int index = 0; index < subAuthorityCount; index++)
        {
            uint subAuthority = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(SidHeaderLength + (index * SidSubAuthorityLength)));

            builder.Append('-').Append(subAuthority.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Decodes a byte array containing a GUID to its string representation.
    /// </summary>
    /// <param name="bytes">The raw GUID bytes (16 bytes).</param>
    /// <returns>The GUID string.</returns>
    public static object? DecoderObjectGUID(byte[] bytes)
    {
        try
        {
            return new Guid(bytes).ToString();
        }
        catch
        {
            return Convert.ToBase64String(bytes);
        }
    }

    /// <summary>
    /// Encodes a GUID string to the byte array format used by Active Directory for objectGUID searches.
    /// </summary>
    /// <param name="guidString">The GUID string to encode.</param>
    /// <returns>The byte array representation of the GUID.</returns>
    public static byte[] EncoderObjectGUID(string guidString)
    {
        return Guid.Parse(guidString).ToByteArray();
    }

    /// <summary>
    /// Encodes a SID string (e.g., <c>S-1-5-21-...</c>) to the byte array format used by Active Directory.
    /// </summary>
    /// <param name="sidString">The SID string to encode.</param>
    /// <returns>The byte array representation of the SID.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sidString"/> is not a well-formed SID.</exception>
    public static byte[] EncoderObjectSID(string sidString)
    {
        // Implemented against the SID wire format rather than SecurityIdentifier, which is
        // Windows-only and would throw on Linux hosts. See SidToString for the layout.
        string[] parts = sidString.Split('-');

        if (parts.Length < 3 || !string.Equals(parts[0], "S", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{sidString}' is not a valid SID.", nameof(sidString));

        int subAuthorityCount = parts.Length - 3;

        if (subAuthorityCount > SidMaximumSubAuthorityCount)
            throw new ArgumentException("A SID may not declare more than 15 sub-authorities.", nameof(sidString));

        if (!byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out byte revision))
            throw new ArgumentException($"'{sidString}' has a non-numeric revision.", nameof(sidString));

        string authorityText = parts[2];
        bool authorityIsHex = authorityText.StartsWith("0x", StringComparison.OrdinalIgnoreCase);

        bool authorityParsed = authorityIsHex
            ? ulong.TryParse(authorityText.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsedAuthority)
            : ulong.TryParse(authorityText, NumberStyles.None, CultureInfo.InvariantCulture, out parsedAuthority);

        if (!authorityParsed || parsedAuthority > 0xFFFFFFFFFFFFUL)
            throw new ArgumentException($"'{sidString}' has an invalid identifier authority.", nameof(sidString));

        byte[] bytes = new byte[SidHeaderLength + (subAuthorityCount * SidSubAuthorityLength)];
        bytes[0] = revision;
        bytes[1] = (byte)subAuthorityCount;

        // Identifier authority: 6 bytes, big-endian.
        for (int index = 0; index < 6; index++)
            bytes[2 + index] = (byte)(parsedAuthority >> (8 * (5 - index)));

        for (int index = 0; index < subAuthorityCount; index++)
        {
            if (!uint.TryParse(parts[3 + index], NumberStyles.None, CultureInfo.InvariantCulture, out uint subAuthority))
                throw new ArgumentException($"'{sidString}' has a non-numeric sub-authority.", nameof(sidString));

            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(SidHeaderLength + (index * SidSubAuthorityLength)), subAuthority);
        }

        return bytes;
    }

    /// <summary>
    /// Encodes a Unicode string to a UTF-8 byte array for LDAP attribute writes.
    /// </summary>
    /// <param name="value">The string value to encode.</param>
    /// <returns>The UTF-8 byte array.</returns>
    public static byte[] EncoderUnicode(string value)
    {
        return Encoding.UTF8.GetBytes(value);
    }

    /// <summary>
    /// Encodes a boolean value to the string format expected by Active Directory.
    /// </summary>
    /// <param name="value">The boolean value to encode.</param>
    /// <returns>The UTF-8 byte array of <c>"TRUE"</c> or <c>"FALSE"</c>.</returns>
    public static byte[] EncoderBoolean(bool value)
    {
        return Encoding.UTF8.GetBytes(value ? "TRUE" : "FALSE");
    }

    /// <summary>
    /// Encodes an integer value to the string format expected by Active Directory.
    /// </summary>
    /// <param name="value">The integer value to encode.</param>
    /// <returns>The UTF-8 byte array of the integer string.</returns>
    public static byte[] EncoderInteger(int value)
    {
        return Encoding.UTF8.GetBytes(value.ToString());
    }

    #endregion
}
