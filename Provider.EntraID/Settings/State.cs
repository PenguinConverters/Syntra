namespace PenguinConverters.Syntra.Provider.EntraID.Settings;

/// <summary>
/// Tracks the synchronization state for the Entra ID provider.
/// Stores the Microsoft Graph delta token issued by the last successful run, alongside the
/// endpoint it was issued for.
/// </summary>
/// <remarks>
/// A delta token is scoped to the endpoint and the property projection that produced it, so
/// replaying one against a different endpoint yields an error rather than a delta.
/// Recording <see cref="EndPoint"/> next to the token lets a changed configuration be detected
/// and answered with a full pass instead.
/// </remarks>
public class State
{
    #region Properties

    /// <summary>
    /// Gets or sets the Graph delta token extracted from the <c>@odata.deltaLink</c> of the last
    /// successful run. <c>null</c> when no delta pass has completed yet.
    /// </summary>
    public string? DeltaToken { get; set; }

    /// <summary>
    /// Gets or sets the Graph endpoint the <see cref="DeltaToken"/> was issued for.
    /// </summary>
    public string? EndPoint { get; set; }

    /// <summary>
    /// Gets the UTC timestamp when this state was recorded.
    /// </summary>
    public DateTime DateTime => DateTime.UtcNow;

    #endregion
}
