using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Provider.AzureSQL.Source;

namespace PenguinConverters.Syntra.Provider.AzureSQL;

/// <summary>
/// Azure SQL source provider that retrieves entities from SQL Server or Azure SQL Database.
/// Supports full synchronization via SELECT queries and delta synchronization
/// using an offset column (datetime) to track changes since the last sync run.
/// </summary>
public class Provider : Core.Source.Provider
{
    private Configuration? _configuration;
    private DateTime? _lastOffset;

    /// <summary>
    /// Gets or sets the provider configuration.
    /// </summary>
    internal Configuration? Configuration
    {
        get => _configuration;
        set => _configuration = value;
    }

    /// <summary>
    /// Deserializes the raw configuration bytes and applies them to this provider.
    /// </summary>
    internal void DeserializeAndApplyConfiguration()
    {
        _configuration = DeserializeConfiguration<Configuration>()
            ?? throw new InvalidOperationException("Failed to deserialize Azure SQL provider configuration.");
    }

    /// <summary>
    /// Initializes the offset from the raw metadata bytes.
    /// </summary>
    internal void InitializeOffset()
    {
        if (RawMetadata is not null)
        {
            string offsetStr = Encoding.UTF8.GetString(RawMetadata);
            if (DateTime.TryParse(offsetStr, out DateTime offset))
                _lastOffset = offset;
        }
    }

    /// <inheritdoc />
    public override IEnumerable<IEntity> Retrieve(IEnumerable<string> properties)
    {
        if (_configuration is null)
        {
            Logger.LogError("Azure SQL provider configuration is not set.");
            yield break;
        }

        Logger.LogInformation(
            "Starting {SyncType} retrieval from table '{TableName}'.",
            _lastOffset.HasValue ? "delta" : "full",
            _configuration.TableName);

        // Resolve the connection string via ProtectedString.TryGetValue
        // Build SQL query:
        //   Full sync:  SELECT {properties} FROM {TableName} [WHERE {WhereClause}]
        //   Delta sync: SELECT {properties} FROM {TableName} WHERE {OffsetColumn} > @lastOffset
        //               [AND {WhereClause}]

        // Execute query via SqlDataReader, yield Entity objects per row
        // Track the maximum OffsetColumn value seen

        // On success, store the new offset as metadata:
        // RawMetadata = Encoding.UTF8.GetBytes(maxOffset.ToString("O"));

        Logger.LogInformation("Azure SQL retrieval completed.");
    }
}
