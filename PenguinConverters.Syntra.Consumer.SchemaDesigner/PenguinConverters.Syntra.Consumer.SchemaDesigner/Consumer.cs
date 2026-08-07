using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Consumer.SchemaDesigner.Entities;
using PenguinConverters.Syntra.Consumer.SchemaDesigner.Extensions;

namespace PenguinConverters.Syntra.Consumer.SchemaDesigner;

/// <summary>
/// An <see cref="IConsumer"/> implementation that analyzes entities from a provider
/// to infer a SQL schema. Rather than writing data to a target system, it profiles
/// each property's data types, lengths, and value patterns, then outputs a T-SQL
/// table definition to the console.
/// </summary>
public class Consumer : Core.Target.Consumer
{
    private readonly ConcurrentDictionary<string, EntityBuilder> _entityBuilders = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override async Task SynchronizeAsync(IProvider provider, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("SchemaDesigner: Starting entity analysis");

        string[] properties = Array.Empty<string>();
        int entityCount = 0;

        await foreach (IEntity entity in provider.RetrieveAsync(properties, cancellationToken).ConfigureAwait(false))
        {
            ProcessEntity(entity);
            entityCount++;

            if (entityCount % 1000 == 0)
            {
                Logger.LogInformation("SchemaDesigner: Processed {Count} entities", entityCount);
            }
        }

        Logger.LogInformation("SchemaDesigner: Completed analysis of {Count} entities across {Tables} table(s)",
            entityCount, _entityBuilders.Count);
    }

    /// <inheritdoc />
    public override async Task FinalizeAsync(IProvider provider, CancellationToken cancellationToken = default)
    {
        ConsoleWriter writer = new ConsoleWriter();

        foreach (KeyValuePair<string, EntityBuilder> kvp in _entityBuilders.OrderBy(x => x.Key))
        {
            string entityName = kvp.Key;
            EntityBuilder builder = kvp.Value;
            List<Column> columns = builder.BuildColumns();

            writer.WriteLine();
            writer.WriteLine($"-- Table: [{entityName}]");
            writer.WriteLine($"-- Entities analyzed: {builder.EntityCount}");
            writer.WriteLine($"CREATE TABLE [{entityName}] (");

            List<Column> sortedColumns = columns.OrderBy(c => c.Name).ToList();
            for (int i = 0; i < sortedColumns.Count; i++)
            {
                Column column = sortedColumns[i];
                string separator = i < sortedColumns.Count - 1 ? "," : string.Empty;
                string definition = column.GetTSQLDefinition();
                writer.WriteLine($"    {definition}{separator}");
            }

            writer.WriteLine(");");
            writer.WriteLine();
        }

        await writer.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Processes a single entity, adding its property values to the statistical profile.
    /// </summary>
    /// <param name="entity">The entity to analyze.</param>
    private void ProcessEntity(IEntity entity)
    {
        string tableName = entity.Identifier ?? "Default";

        EntityBuilder builder = _entityBuilders.GetOrAdd(tableName, _ => new EntityBuilder());
        builder.AddEntity(entity);
    }
}
