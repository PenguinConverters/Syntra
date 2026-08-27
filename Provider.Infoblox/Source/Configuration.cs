using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.Infoblox.Source;

/// <summary>
/// Configuration for the Infoblox source provider.
/// </summary>
/// <remarks>
/// The WAPI returns a bare array by default and has to be asked for the envelope that carries a
/// continuation token, which is what <c>_return_as_object</c> and <c>_paging</c> do. Those, the
/// page size and the field projection are all query parameters, so the whole connector is these
/// defaults.
/// <para>
/// <c>_ref</c> is the object reference every WAPI record carries. It names the record, so it is
/// the identity - but the WAPI rejects a request that asks for it in <c>_return_fields</c>,
/// because it is returned whether or not it was asked for. Naming it as the identity and
/// withholding it from the projection at the same time is what these two settings do together.
/// </para>
/// </remarks>
public class Configuration : RESTful.Source.Configuration
{
    #region Constants

    /// <summary>
    /// Path to the collection within the response envelope.
    /// </summary>
    public const string DefaultResultPath = "result";

    /// <summary>
    /// Object reference every WAPI record carries, which names it.
    /// </summary>
    public const string DefaultIdentityProperty = "_ref";

    /// <summary>
    /// Query parameter the field projection is sent under.
    /// </summary>
    public const string DefaultPropertiesParameter = "_return_fields";

    /// <summary>
    /// Query parameter asking for the response envelope rather than a bare array.
    /// </summary>
    public const string ReturnAsObjectParameter = "_return_as_object";

    /// <summary>
    /// Query parameter switching paging on.
    /// </summary>
    public const string PagingParameter = "_paging";

    /// <summary>
    /// Query parameter carrying the page size.
    /// </summary>
    public const string MaxResultsParameter = "_max_results";

    /// <summary>
    /// Path to the continuation token within the response envelope.
    /// </summary>
    public const string DefaultTokenPath = "next_page_id";

    /// <summary>
    /// Query parameter the continuation token is sent back under.
    /// </summary>
    public const string DefaultTokenParameter = "_page_id";

    /// <summary>
    /// Records requested per page.
    /// </summary>
    public const int DefaultPageSize = 1000;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Configuration"/> class with the defaults the
    /// WAPI needs. Every one of them may be overridden by the configuration file.
    /// </summary>
    public Configuration()
    {
        ApplyInfobloxDefaults();
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public override void ApplyDefaults()
    {
        base.ApplyDefaults();

        ApplyInfobloxDefaults();
    }

    /// <summary>
    /// Fills in whatever the configuration file left unset. Every assignment is conditional, so
    /// running this after deserialization restores the defaults a mentioned section discarded
    /// without overwriting anything the file actually stated.
    /// </summary>
    private void ApplyInfobloxDefaults()
    {
        ResultPath ??= DefaultResultPath;
        IdentityProperty ??= DefaultIdentityProperty;
        PropertiesToIgnore ??= [DefaultIdentityProperty];
        PropertiesParameter ??= DefaultPropertiesParameter;

        Pagination ??= new PaginationSettings();

        if (Pagination.Mode == PaginationMode.None)
        {
            Pagination.Mode = PaginationMode.Token;
        }

        Pagination.TokenPath ??= DefaultTokenPath;
        Pagination.TokenParameter ??= DefaultTokenParameter;
        Pagination.PageSizeParameter ??= MaxResultsParameter;

        if (Pagination.PageSize <= 0)
        {
            Pagination.PageSize = DefaultPageSize;
        }

        Authentication ??= new AuthenticationSettings();

        if (Authentication.Mode == AuthenticationMode.None)
        {
            Authentication.Mode = AuthenticationMode.Basic;
        }

        // The envelope and the paging flag are what make a continuation token available at all.
        // They are added rather than assigned, so a configuration that sets either keeps its own.
        AddParameter(ReturnAsObjectParameter, 1, false);
        AddParameter(PagingParameter, 1, false);
    }

    #endregion
}
