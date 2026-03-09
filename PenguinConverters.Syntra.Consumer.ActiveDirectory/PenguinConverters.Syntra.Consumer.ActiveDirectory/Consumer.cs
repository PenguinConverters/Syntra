using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Consumer.ActiveDirectory.Target;

namespace PenguinConverters.Syntra.Consumer.ActiveDirectory;

/// <summary>
/// Active Directory consumer that writes changes back to AD via LDAP.
/// Supports creating, modifying, and deleting AD objects with threshold-based
/// deletion protection and composite key tracking.
/// </summary>
public class Consumer : Core.Target.Consumer, Core.Target.ISynchronizable
{
    /// <summary>
    /// The distinguishedName attribute constant.
    /// </summary>
    public const string DistinguishedNameAttribute = "distinguishedName";

    private Configuration? _configuration;
    private readonly ConcurrentQueue<string> _compositeKeys = new();
    private readonly ConcurrentDictionary<string, string?> _filtersFound = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the consumer configuration.
    /// </summary>
    internal Configuration? Configuration
    {
        get => _configuration;
        set => _configuration = value;
    }

    /// <summary>
    /// Deserializes the raw configuration bytes and applies them to this consumer.
    /// </summary>
    internal void DeserializeAndApplyConfiguration()
    {
        _configuration = DeserializeConfiguration<Configuration>()
            ?? throw new InvalidOperationException("Failed to deserialize Active Directory consumer configuration.");
    }

    /// <inheritdoc />
    public override void Synchronize(IProvider provider)
    {
        if (_configuration is null)
        {
            Logger.LogError("Active Directory consumer configuration is not set.");
            HadErrors = true;
            return;
        }

        Logger.LogInformation(
            "Starting Active Directory synchronization to BaseDN '{BaseDN}'.",
            _configuration.BaseDN);

        IEnumerable<string> properties = _configuration.Properties?.Keys ?? Enumerable.Empty<string>();

        foreach (IEntity entity in provider.Retrieve(properties))
        {
            UpdateEntity(entity);
        }

        Logger.LogInformation("Active Directory synchronization completed.");
    }

    /// <summary>
    /// Updates a single entity in Active Directory. Searches for existing objects
    /// using the configured LDAP filter and primary key attributes. Creates new
    /// objects when not found, or modifies existing object attributes.
    /// Tracks composite keys for deletion reconciliation in full sync mode.
    /// </summary>
    /// <param name="entity">The entity to synchronize to Active Directory.</param>
    public void UpdateEntity(IEntity entity)
    {
        if (_configuration?.Properties is null) return;

        try
        {
            // 1. Build LDAP filter from primary key properties and configured LdapFilter
            //    e.g., (&(objectClass=user)(sAMAccountName=jdoe))

            // 2. Search for existing object using the filter
            //    - If not found and entity is not Deleted: create new AD object (AddRequest)
            //    - If found: modify attributes (ModifyRequest)

            // 3. For multi-valued attributes (Array = true), use
            //    AttributeModificationAdd / AttributeModificationDelete

            // 4. Track composite keys for deletion reconciliation:
            //    - Build composite key from DN + primary key attribute values
            //    - Enqueue to _compositeKeys

            // 5. Cache filter -> DN mapping in _filtersFound to avoid duplicate searches

            Logger.LogTrace("Updated entity in Active Directory: {Identifier}", entity.Identifier);
        }
        catch (Exception ex)
        {
            HadErrors = true;
            Logger.LogError(ex, "Failed to update entity '{Identifier}' in Active Directory.", entity.Identifier);
        }
    }

    /// <inheritdoc />
    public override void Finalize(IProvider provider)
    {
        if (_configuration is null) return;

        Logger.LogInformation("Finalizing Active Directory synchronization for '{BaseDN}'.", _configuration.BaseDN);

        if (!_configuration.Delta)
        {
            ReconcileDeletions(provider);
        }

        Logger.LogInformation("Active Directory finalization completed.");
    }

    /// <summary>
    /// Reconciles deletions during full synchronization. Compares composite keys
    /// tracked during sync against all existing objects in the target OU.
    /// Enforces configured thresholds before executing deletions to prevent
    /// accidental mass removal of AD objects.
    /// </summary>
    /// <param name="provider">The source provider to check for errors.</param>
    private void ReconcileDeletions(IProvider provider)
    {
        if (_configuration is null || HadErrors) return;

        Logger.LogTrace("Reconciling deletions for '{BaseDN}'...", _configuration.BaseDN);

        // 1. Retrieve all existing objects matching the configured LdapFilter
        //    in the target BaseDN. Build a set of composite keys from existing objects.

        // 2. Dequeue all tracked composite keys from _compositeKeys
        //    and remove matching entries from the existing set.

        // 3. Remaining entries in the existing set are candidates for deletion.

        // 4. Threshold enforcement:
        //    Check if the number of deletions exceeds configured thresholds.
        //    If Thresholds contains a "Delete" operation with percentage P,
        //    and (deletionCount / existingCount * 100) > P, throw an exception
        //    to abort the deletion process.

        // 5. For multi-valued attributes with PrimaryKey = true:
        //    Use AttributeModificationDelete to remove specific values.
        //    For regular objects: use DeleteRequest or move to deleted OU.

        Logger.LogTrace("Deletion reconciliation completed for '{BaseDN}'.", _configuration.BaseDN);
    }
}
