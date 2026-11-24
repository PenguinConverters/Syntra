using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace PenguinConverters.Syntra.Consumer.AzureSQL.Target;

/// <summary>
/// Converts source property values into the CLR type the target column stores, so that every
/// staged value arrives at the server already in the column's own type.
/// </summary>
internal static class SqlValueConverter
{
    #region Methods

    /// <summary>
    /// Returns the CLR type that carries values of a SQL system type.
    /// </summary>
    /// <param name="baseType">The upper-case system type name, such as <c>DATETIME2</c>.</param>
    /// <returns>
    /// The matching CLR type, or <see cref="object"/> for a type that is passed to the server
    /// unconverted.
    /// </returns>
    public static Type GetClrType(string baseType)
    {
        switch (baseType)
        {
            case "BIT":
                return typeof(bool);
            case "TINYINT":
                return typeof(byte);
            case "SMALLINT":
                return typeof(short);
            case "INT":
                return typeof(int);
            case "BIGINT":
                return typeof(long);
            case "DECIMAL":
            case "NUMERIC":
            case "MONEY":
            case "SMALLMONEY":
                return typeof(decimal);
            case "FLOAT":
                return typeof(double);
            case "REAL":
                return typeof(float);
            case "UNIQUEIDENTIFIER":
                return typeof(Guid);
            case "DATE":
            case "DATETIME":
            case "DATETIME2":
            case "SMALLDATETIME":
                return typeof(DateTime);
            case "DATETIMEOFFSET":
                return typeof(DateTimeOffset);
            case "TIME":
                return typeof(TimeSpan);
            case "BINARY":
            case "VARBINARY":
            case "IMAGE":
                return typeof(byte[]);
            case "CHAR":
            case "VARCHAR":
            case "NCHAR":
            case "NVARCHAR":
            case "TEXT":
            case "NTEXT":
            case "XML":
                return typeof(string);
            default:
                return typeof(object);
        }
    }

    /// <summary>
    /// Converts a source value into the CLR type of a target column.
    /// </summary>
    /// <param name="value">The source value; may be <c>null</c> or a <see cref="JsonElement"/>.</param>
    /// <param name="clrType">The CLR type returned by <see cref="GetClrType"/> for the column.</param>
    /// <returns>The converted value, or <see cref="DBNull.Value"/> for a missing value.</returns>
    /// <exception cref="FormatException">Thrown when the value cannot be read as the target type.</exception>
    /// <exception cref="InvalidCastException">Thrown when no conversion to the target type exists.</exception>
    /// <exception cref="OverflowException">Thrown when the value is outside the target type's range.</exception>
    public static object Convert(object? value, Type clrType)
    {
        if (value is JsonElement element)
            value = Unwrap(element);

        if (value is null || value is DBNull)
            return DBNull.Value;

        if (clrType == typeof(object) || clrType.IsInstanceOfType(value))
            return value;

        if (clrType == typeof(string))
            return ToText(value);

        if (value is string text)
        {
            if (text.Length == 0)
                return DBNull.Value;

            return Parse(text.Trim(), clrType);
        }

        if (clrType == typeof(Guid) && value is byte[] guidBytes && guidBytes.Length == 16)
            return new Guid(guidBytes);

        if (clrType == typeof(byte[]) && value is Guid guid)
            return guid.ToByteArray();

        if (clrType == typeof(DateTime) && value is DateTimeOffset offset)
            return offset.UtcDateTime;

        if (clrType == typeof(DateTime) && value is DateOnly date)
            return date.ToDateTime(TimeOnly.MinValue);

        if (clrType == typeof(DateTimeOffset) && value is DateTime dateTime)
            return new DateTimeOffset(dateTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                : dateTime);

        if (clrType == typeof(TimeSpan) && value is TimeOnly time)
            return time.ToTimeSpan();

        return System.Convert.ChangeType(value, clrType, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reduces a JSON element to the plain CLR value it represents.
    /// </summary>
    /// <param name="element">The element to unwrap.</param>
    /// <returns>
    /// A string, number or boolean for a scalar, <c>null</c> for a JSON null, and the raw JSON
    /// text for an object or an array.
    /// </returns>
    private static object? Unwrap(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out long integer))
                    return integer;
                if (element.TryGetDecimal(out decimal number))
                    return number;
                return element.GetDouble();
            default:
                return element.GetRawText();
        }
    }

    /// <summary>
    /// Renders a value as the text a character column stores.
    /// </summary>
    /// <param name="value">The value to render.</param>
    /// <returns>
    /// Round-trip text for dates, base64 for binary data, JSON for a collection, and
    /// culture-invariant text for everything else.
    /// </returns>
    private static string ToText(object value)
    {
        switch (value)
        {
            case DateTime dateTime:
                return dateTime.ToString("O", CultureInfo.InvariantCulture);
            case DateTimeOffset offset:
                return offset.ToString("O", CultureInfo.InvariantCulture);
            case byte[] bytes:
                return System.Convert.ToBase64String(bytes);
            case bool flag:
                return flag ? "true" : "false";
            case IEnumerable:
                return JsonSerializer.Serialize(value);
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            default:
                return value.ToString() ?? string.Empty;
        }
    }

    /// <summary>
    /// Reads culture-invariant text as a value of the target type.
    /// </summary>
    /// <param name="text">The non-empty, trimmed text.</param>
    /// <param name="clrType">The CLR type to produce.</param>
    /// <returns>The parsed value.</returns>
    private static object Parse(string text, Type clrType)
    {
        if (clrType == typeof(Guid))
            return Guid.Parse(text);

        if (clrType == typeof(DateTime))
            return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        if (clrType == typeof(DateTimeOffset))
            return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

        if (clrType == typeof(TimeSpan))
            return TimeSpan.Parse(text, CultureInfo.InvariantCulture);

        if (clrType == typeof(byte[]))
            return System.Convert.FromBase64String(text);

        if (clrType == typeof(bool))
        {
            if (text == "1")
                return true;
            if (text == "0")
                return false;

            return bool.Parse(text);
        }

        return System.Convert.ChangeType(text, clrType, CultureInfo.InvariantCulture);
    }

    #endregion
}
