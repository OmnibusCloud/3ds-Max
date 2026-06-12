using OutWit.Cloud.SDK;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

/// <summary>
/// Owns the authenticated OmnibusCloud client connection for the plugin session.
/// </summary>
public interface IMaxCloudConnectionService : IAsyncDisposable
{
    /// <summary>
    /// Returns a connected authenticated client for the given server URL, or null when
    /// no signed-in session is available or the connection cannot be established.
    /// </summary>
    /// <param name="serverUrl">The OmnibusCloud engine base URL.</param>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <returns>The connected client, or null.</returns>
    Task<IWitCloudClient?> GetClientAsync(string serverUrl, CancellationToken cancellationToken = default);
}
