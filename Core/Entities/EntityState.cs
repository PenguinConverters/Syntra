namespace PenguinConverters.Syntra.Core.Entities;

/// <summary>
/// Represents the synchronization state of an entity.
/// </summary>
public enum EntityState
{
    /// <summary>
    /// The entity has not yet been classified.
    /// </summary>
    Unclassified = 0,

    /// <summary>
    /// The entity was created since the last synchronization.
    /// </summary>
    Created = 1,

    /// <summary>
    /// The entity was updated since the last synchronization.
    /// </summary>
    Updated = 2,

    /// <summary>
    /// The entity was deleted since the last synchronization.
    /// </summary>
    Deleted = 3
}
