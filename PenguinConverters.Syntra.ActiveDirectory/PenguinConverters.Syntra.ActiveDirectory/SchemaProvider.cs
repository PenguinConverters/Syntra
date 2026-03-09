using System.DirectoryServices.Protocols;
using System.Security.Principal;
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
    private readonly Connection _connection;
    private readonly ILogger _logger;

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

    /// <summary>
    /// Queries the AD schema to build a dictionary of attribute decoders.
    /// Each decoder maps a raw LDAP byte array to the appropriate .NET type
    /// based on the attribute's <c>oMSyntax</c> and <c>attributeSyntax</c> values.
    /// </summary>
    /// <returns>
    /// A dictionary mapping attribute names (case-insensitive) to decoder functions.
    /// </returns>
    public Dictionary<string, Func<byte[], object?>> GetDecoders()
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

            SearchResponse rootDseResponse = (SearchResponse)ldapConnection.SendRequest(rootDseRequest);

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
                SearchResponse schemaResponse = (SearchResponse)ldapConnection.SendRequest(schemaRequest);

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
            SecurityIdentifier sid = new SecurityIdentifier(bytes, 0);
            return sid.ToString();
        }
        catch
        {
            return Convert.ToBase64String(bytes);
        }
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
    public static byte[] EncoderObjectSID(string sidString)
    {
        SecurityIdentifier sid = new SecurityIdentifier(sidString);
        byte[] bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
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
}
