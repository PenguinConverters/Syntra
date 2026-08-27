namespace PenguinConverters.Syntra.Provider.Infoblox;

/// <summary>
/// Infoblox source provider, reading DNS, DHCP and IPAM records from a Grid Master over the WAPI.
/// </summary>
/// <remarks>
/// Everything this connector does is configuration against <see cref="RESTful.Provider"/>: Basic
/// authentication, the response envelope, the field projection and the continuation-token paging
/// the WAPI uses. Naming the configuration type is all that is left.
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
