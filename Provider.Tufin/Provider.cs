namespace PenguinConverters.Syntra.Provider.Tufin;

/// <summary>
/// Tufin source provider, reading devices, policies and rules from a Tufin appliance.
/// </summary>
/// <remarks>
/// Everything this connector does is configuration against <see cref="RESTful.Provider"/>: Basic
/// authentication, the nested response path, and the parent-child endpoint walk that turns a
/// device into its policies and a policy into its rules. Naming the configuration type is all
/// that is left.
/// </remarks>
public class Provider : RESTful.Provider
{
    #region Methods

    /// <inheritdoc />
    protected override RESTful.Source.Configuration? ReadConfiguration()
    {
        return DeserializeConfiguration<Source.Configuration>();
    }

    #endregion
}
