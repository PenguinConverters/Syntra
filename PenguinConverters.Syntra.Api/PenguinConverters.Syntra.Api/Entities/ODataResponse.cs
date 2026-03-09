using System.Text.Json.Serialization;

namespace PenguinConverters.Syntra.Api.Entities;

/// <summary>
/// OData-compliant response wrapper supporting pagination, count, and metadata.
/// </summary>
/// <typeparam name="T">The type of elements in the value collection.</typeparam>
public sealed class ODataResponse<T>
{
    /// <summary>
    /// Gets or sets the OData context URL.
    /// </summary>
    [JsonPropertyName("@odata.context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Context { get; set; }

    /// <summary>
    /// Gets or sets the total count of matching entities (present when $count=true).
    /// </summary>
    [JsonPropertyName("@odata.count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Count { get; set; }

    /// <summary>
    /// Gets or sets the link to the next page of results.
    /// </summary>
    [JsonPropertyName("@odata.nextLink")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextLink { get; set; }

    /// <summary>
    /// Gets or sets the collection of result entities.
    /// </summary>
    [JsonPropertyName("value")]
    public IReadOnlyList<T> Value { get; set; } = [];
}

/// <summary>
/// Non-generic OData response using dictionaries for dynamic entity shapes.
/// </summary>
public sealed class ODataResponse
{
    /// <summary>
    /// Gets or sets the OData context URL.
    /// </summary>
    [JsonPropertyName("@odata.context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Context { get; set; }

    /// <summary>
    /// Gets or sets the total count of matching entities.
    /// </summary>
    [JsonPropertyName("@odata.count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Count { get; set; }

    /// <summary>
    /// Gets or sets the link to the next page of results.
    /// </summary>
    [JsonPropertyName("@odata.nextLink")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextLink { get; set; }

    /// <summary>
    /// Gets or sets the collection of result entities as dynamic dictionaries.
    /// </summary>
    [JsonPropertyName("value")]
    public IReadOnlyList<Dictionary<string, object?>> Value { get; set; } = [];
}
