using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PenguinConverters.Syntra.Core.Types;

namespace PenguinConverters.Syntra.Provider.Tenable.Nessus;

/// <summary>
/// Projects a parsed record onto the property bag a consumer stores, and fingerprints it.
/// </summary>
/// <remarks>
/// The record types are plain property carriers whose shape is the column set, so reading them by
/// reflection keeps the projection in step with the type instead of requiring a second list of
/// names to be maintained beside it. The property list per type is resolved once and cached.
/// </remarks>
public static class NessusProjector
{
    #region Constants

    /// <summary>
    /// Property the record fingerprint is written to.
    /// </summary>
    public const string FingerprintProperty = "MD5HashCode";

    /// <summary>
    /// Separator joining the values a fingerprint is taken over.
    /// </summary>
    private const char FingerprintSeparator = '';

    /// <summary>
    /// Separator joining the elements of a multi-valued property.
    /// </summary>
    private const string ValueSeparator = ", ";

    #endregion

    #region Fields

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    #endregion

    #region Methods

    /// <summary>
    /// Projects a record onto a property bag and stamps it with its fingerprint.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <returns>The property bag.</returns>
    public static QuickDictionary Project(NessusRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        QuickDictionary properties = new QuickDictionary(StringComparer.OrdinalIgnoreCase);

        foreach (PropertyInfo property in Read(record.GetType()))
        {
            properties[property.Name] = Flatten(property.GetValue(record));
        }

        properties[FingerprintProperty] = Fingerprint(properties);

        return properties;
    }

    /// <summary>
    /// Returns the fingerprint of a property bag.
    /// </summary>
    /// <remarks>
    /// This identifies a record by its content, so that the same observation read from two
    /// exports resolves to one row. It is not a security control: it distinguishes records, it
    /// does not authenticate them. The name is kept as the legacy connector wrote it so that a
    /// consumer keying on the column keeps working.
    /// </remarks>
    /// <param name="properties">The property bag.</param>
    /// <returns>The fingerprint, base-64 encoded.</returns>
    public static string Fingerprint(QuickDictionary properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        StringBuilder builder = new StringBuilder();

        // Ordered by name so that two bags carrying the same content fingerprint alike whatever
        // order their properties were written in.
        foreach (KeyValuePair<string, object?> property in properties.OrderBy(
            entry => entry.Key, StringComparer.Ordinal))
        {
            if (string.Equals(property.Key, FingerprintProperty, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder
                .Append(property.Key)
                .Append(FingerprintSeparator)
                .Append(Convert.ToString(property.Value, CultureInfo.InvariantCulture))
                .Append(FingerprintSeparator);
        }

        return Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>
    /// Returns the readable properties of a record type.
    /// </summary>
    /// <param name="type">The record type.</param>
    /// <returns>The properties.</returns>
    private static PropertyInfo[] Read(Type type)
    {
        return Properties.GetOrAdd(
            type,
            key => key
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .ToArray());
    }

    /// <summary>
    /// Reduces a property value to something a single column can hold.
    /// </summary>
    /// <remarks>
    /// A multi-valued property is joined rather than stored as a collection, whose default text
    /// is its type name - which is what a consumer would otherwise persist.
    /// </remarks>
    /// <param name="value">The value.</param>
    /// <returns>The value to store.</returns>
    private static object? Flatten(object? value)
    {
        if (value is null or string)
        {
            return value;
        }

        if (value is IEnumerable enumerable)
        {
            List<string> parts = [];

            foreach (object? element in enumerable)
            {
                parts.Add(Convert.ToString(element, CultureInfo.InvariantCulture) ?? string.Empty);
            }

            return string.Join(ValueSeparator, parts);
        }

        return value;
    }

    #endregion
}
