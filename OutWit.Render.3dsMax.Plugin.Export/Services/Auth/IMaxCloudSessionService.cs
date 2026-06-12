using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

/// <summary>
/// Owns the plugin's OmnibusCloud user session: interactive browser sign-in (PKCE),
/// silent restore via the persisted refresh token, sign-out, and access-token supply
/// for authenticated cloud calls.
/// </summary>
public interface IMaxCloudSessionService
{
    /// <summary>
    /// Attempts to silently restore the persisted session by refreshing its token.
    /// </summary>
    /// <param name="cancellationToken">Cancels the restore.</param>
    /// <returns>True when a signed-in session was restored.</returns>
    Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the full interactive browser sign-in flow (PKCE, loopback callback, token exchange).
    /// </summary>
    /// <param name="identityUrl">The identity server base URL.</param>
    /// <param name="cancellationToken">Cancels the flow.</param>
    /// <returns>The resulting session state.</returns>
    Task<MaxConnectedSessionState> SignInAsync(string identityUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the runtime and persisted session.
    /// </summary>
    /// <param name="cancellationToken">Cancels the sign-out.</param>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a valid access token (refreshing silently when needed), or null when signed out.
    /// </summary>
    /// <param name="cancellationToken">Cancels the token acquisition.</param>
    /// <returns>The access token, or null.</returns>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current session state snapshot.
    /// </summary>
    /// <returns>The session state.</returns>
    MaxConnectedSessionState GetState();
}
