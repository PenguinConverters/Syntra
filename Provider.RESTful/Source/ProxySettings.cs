using System.Net;
using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Provider.RESTful.Authentication;

namespace PenguinConverters.Syntra.Provider.RESTful.Source;

/// <summary>
/// The forward proxy requests are routed through.
/// </summary>
public class ProxySettings
{
    #region Properties

    /// <summary>
    /// Gets or sets a value indicating whether requests are routed through a proxy.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the proxy address. Leaving it unset while <see cref="Enabled"/> is
    /// <c>true</c> routes through the proxy the host is configured with.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the username the proxy authenticates the caller by.
    /// </summary>
    public Secret? Username { get; set; }

    /// <summary>
    /// Gets or sets the password the proxy authenticates the caller by.
    /// </summary>
    public Secret? Password { get; set; }

    #endregion

    #region Methods

    /// <summary>
    /// Builds the proxy these settings describe.
    /// </summary>
    /// <param name="disclose">The delegate that discloses a configured secret.</param>
    /// <param name="proxy">
    /// When this method returns <c>true</c>, the proxy to route requests through.
    /// </param>
    /// <returns><c>true</c> when a proxy is configured; otherwise, <c>false</c>.</returns>
    public bool TryGetProxy(DiscloseSecret disclose, out IWebProxy? proxy)
    {
        proxy = null;

        if (!Enabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Address))
        {
            proxy = WebRequest.DefaultWebProxy;
            return true;
        }

        WebProxy webProxy = new WebProxy(Address);

        if (disclose(Username, out char[] username) && disclose(Password, out char[] password))
        {
            try
            {
                webProxy.Credentials = new NetworkCredential(new string(username), new string(password));
            }
            finally
            {
                Array.Clear(username);
                Array.Clear(password);
            }
        }

        proxy = webProxy;
        return true;
    }

    #endregion
}
