using OutWit.Cloud.SDK;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

/// <summary>
/// Authenticated cloud connection lifecycle built on the public <see cref="WitCloudClient"/>.
/// Authentication uses the SDK token-provider seam fed by the plugin's interactive sign-in
/// (<see cref="IMaxCloudSessionService"/>), so the plugin behaves exactly like any
/// third-party initiator that performed an OIDC login itself.
/// </summary>
public sealed class MaxCloudConnectionService : IMaxCloudConnectionService
{
    #region Fields

    private readonly IMaxCloudSessionService m_sessionService;

    private IWitCloudClient? m_client;

    private string? m_connectedServerUrl;

    private string? m_connectedAccessToken;

    #endregion

    #region Constructors

    public MaxCloudConnectionService(IMaxCloudSessionService sessionService)
    {
        m_sessionService = sessionService;
    }

    #endregion

    #region IMaxCloudConnectionService

    /// <summary>
    /// Returns a connected authenticated client for the given server URL, or null when
    /// no signed-in session is available or the connection cannot be established.
    /// </summary>
    /// <param name="serverUrl">The OmnibusCloud engine base URL.</param>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <returns>The connected client, or null.</returns>
    public async Task<IWitCloudClient?> GetClientAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            return null;

        var accessToken = await m_sessionService.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        var normalizedServerUrl = serverUrl.TrimEnd('/');
        if (m_client != null
            && string.Equals(m_connectedServerUrl, normalizedServerUrl, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m_connectedAccessToken, accessToken, StringComparison.Ordinal))
        {
            return m_client;
        }

        await DisconnectCoreAsync();

        // The token-provider is invoked by the SDK on connect/reconnect, so the live session
        // token (including silent refresh) is always used without rebuilding the client.
        var client = new WitCloudClient(
            normalizedServerUrl,
            async ct => await m_sessionService.GetAccessTokenAsync(ct) ?? string.Empty);

        try
        {
            await client.ConnectAsync(cancellationToken);
        }
        catch (Exception)
        {
            await client.DisposeAsync();
            return null;
        }

        m_client = client;
        m_connectedServerUrl = normalizedServerUrl;
        m_connectedAccessToken = accessToken;
        return m_client;
    }

    #endregion

    #region Tools

    private async Task DisconnectCoreAsync()
    {
        if (m_client != null)
            await m_client.DisposeAsync();

        m_client = null;
        m_connectedServerUrl = null;
        m_connectedAccessToken = null;
    }

    #endregion

    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        await DisconnectCoreAsync();
    }

    #endregion
}
