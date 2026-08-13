using System.Text.Json;
using System.Text.Json.Serialization;

namespace PenguinConverters.Syntra.Core.Extensions;

/// <summary>
/// Provides reusable <see cref="JsonSerializerOptions"/> and helper methods
/// for consistent JSON serialization across the Syntra framework.
/// </summary>
public static class JsonSerializerExtensions
{
    #region Fields

    private static readonly JsonSerializerOptions DefaultOptions = CreateDefaultOptions();

    #endregion

    #region Methods

    /// <summary>
    /// Gets the default <see cref="JsonSerializerOptions"/> used throughout the Syntra framework.
    /// Configured with case-insensitive property matching, string enum conversion,
    /// and number-from-string reading.
    /// </summary>
    /// <returns>The default options instance.</returns>
    public static JsonSerializerOptions GetDefaultOptions() => DefaultOptions;

    /// <summary>
    /// Deserializes a byte array to the specified type using the default Syntra options.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="data">The UTF-8 encoded JSON bytes.</param>
    /// <returns>The deserialized object, or <c>null</c> if deserialization fails.</returns>
    public static T? Deserialize<T>(byte[] data)
    {
        return JsonSerializer.Deserialize<T>(data, DefaultOptions);
    }

    /// <summary>
    /// Deserializes a byte array to the specified type using the default Syntra options.
    /// </summary>
    /// <param name="data">The UTF-8 encoded JSON bytes.</param>
    /// <param name="type">The target type.</param>
    /// <returns>The deserialized object, or <c>null</c> if deserialization fails.</returns>
    public static object? Deserialize(byte[] data, Type type)
    {
        return JsonSerializer.Deserialize(data, type, DefaultOptions);
    }

    /// <summary>
    /// Serializes an object to a UTF-8 encoded byte array using the default Syntra options.
    /// </summary>
    /// <typeparam name="T">The source type.</typeparam>
    /// <param name="value">The object to serialize.</param>
    /// <returns>The serialized UTF-8 JSON bytes.</returns>
    public static byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, DefaultOptions);
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
    }

    #endregion
}
