using System.Text;
using System.Text.Json;
using OutWit.Cloud.Auth;
using OutWit.Cloud.Auth.Sessions;
using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

/// <summary>
/// In-process OmnibusCloud user session for the 3ds Max plugin — a thin adapter over the shared
/// OutWit.Cloud.Auth stack (<see cref="TokenService"/> + <see cref="SessionStore"/>) that every
/// OmnibusCloud native client uses: OIDC authorization-code + PKCE through the system browser
/// with a loopback callback, silent refresh, and an encrypted-at-rest persisted refresh token.
/// The adapter keeps the plugin-facing <see cref="IMaxCloudSessionService"/> surface unchanged
/// and derives DisplayName/UserId from the current access token's JWT claims (they are no longer
/// persisted alongside the refresh token).
/// </summary>
public sealed class MaxCloudSessionService : IMaxCloudSessionService
{
    #region Constants

    /// <summary>
    /// The OIDC client id the 3ds Max plugin is registered under at WitIdentity. Passed
    /// explicitly to the shared <see cref="TokenService"/> (whose default is the worker
    /// client's id) — changing it would break sign-in against deployed identity servers.
    /// </summary>
    public const string CLIENT_ID = "cloud-client";

    private const string SESSION_FILE_NAME = "3dsmax-session.json";

    // A DCC plugin session must not silently expire under the artist — the session lives
    // until an explicit sign-out (or the identity server revokes the refresh token).
    private const SessionPolicy SESSION_POLICY = SessionPolicy.RememberUntilLogout;

    private const string NO_SESSION_TEXT = "No active user session.";

    #endregion

    #region Fields

    private readonly TokenService m_tokenService;

    private readonly SessionStore m_sessionStore;

    private bool m_isSignedIn;

    private string? m_displayName;

    private string? m_userId;

    private string? m_lastError = NO_SESSION_TEXT;

    #endregion

    #region Constructors

    public MaxCloudSessionService(TokenService tokenService, SessionStore sessionStore)
    {
        m_tokenService = tokenService;
        m_sessionStore = sessionStore;

        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitEvents()
    {
        // A rotated refresh token must be persisted immediately: the identity server revokes
        // the previous one, so losing the rotation would force an interactive re-login on the
        // next 3ds Max start.
        m_tokenService.RefreshTokenRotated += OnRefreshTokenRotated;
        m_tokenService.ReauthenticationRequired += OnReauthenticationRequired;
    }

    #endregion

    #region IMaxCloudSessionService

    /// <summary>
    /// Attempts to silently restore the persisted session by refreshing its token.
    /// </summary>
    /// <param name="cancellationToken">Cancels the restore.</param>
    /// <returns>True when a signed-in session was restored.</returns>
    public async Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var restored = await m_tokenService.TryRestoreSessionAsync(SESSION_POLICY, m_sessionStore);
        if (!restored)
        {
            MaxPluginLogging.Logger.Information("Session restore: no restorable session.");
            ClearRuntimeSession();
            return false;
        }

        ApplySignedInState(await m_tokenService.GetTokenAsync());
        MaxPluginLogging.Logger.Information("Session restored silently as {DisplayName}.", m_displayName);
        return m_isSignedIn;
    }

    /// <summary>
    /// Runs the full interactive browser sign-in flow (PKCE, loopback callback, token exchange).
    /// </summary>
    /// <param name="identityUrl">The identity server base URL.</param>
    /// <param name="cancellationToken">Cancels the flow.</param>
    /// <returns>The resulting session state.</returns>
    public async Task<MaxConnectedSessionState> SignInAsync(string identityUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(identityUrl))
            {
                SetLastError("Identity URL is required before sign-in.");
                return GetState();
            }

            var signedIn = await m_tokenService.LoginWithBrowserAsync(identityUrl);
            if (!signedIn)
            {
                SetLastError(string.IsNullOrWhiteSpace(m_tokenService.LastInteractiveFailureText)
                    ? "Interactive sign-in failed."
                    : m_tokenService.LastInteractiveFailureText);
                MaxPluginLogging.Logger.Warning("Interactive sign-in failed: {Error}", m_lastError);
                return GetState();
            }

            m_tokenService.SaveSession(SESSION_POLICY, m_sessionStore);
            ApplySignedInState(await m_tokenService.GetTokenAsync());
            MaxPluginLogging.Logger.Information("Interactive sign-in completed as {DisplayName}.", m_displayName);
            return GetState();
        }
        catch (Exception ex)
        {
            SetLastError(ex.Message);
            return GetState();
        }
    }

    /// <summary>
    /// Clears the runtime and persisted session.
    /// </summary>
    /// <param name="cancellationToken">Cancels the sign-out.</param>
    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        m_tokenService.ClearSession(m_sessionStore);
        ClearRuntimeSession();
        MaxPluginLogging.Logger.Information("Signed out; persisted session cleared.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns a valid access token (refreshing silently when needed), or null when signed out.
    /// </summary>
    /// <param name="cancellationToken">Cancels the token acquisition.</param>
    /// <returns>The access token, or null.</returns>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = await m_tokenService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        ApplySignedInState(accessToken);
        return accessToken;
    }

    /// <summary>
    /// Returns the current session state snapshot.
    /// </summary>
    /// <returns>The session state.</returns>
    public MaxConnectedSessionState GetState()
    {
        return new MaxConnectedSessionState
        {
            IsSignedIn = m_isSignedIn,
            DisplayName = m_displayName ?? string.Empty,
            UserId = m_userId ?? string.Empty,
            LastError = m_lastError ?? string.Empty
        };
    }

    #endregion

    #region Tools

    /// <summary>
    /// Resolves the default session-file path: %APPDATA%\OmnibusCloud\3dsMax\3dsmax-session.json.
    /// The SAME file the previous in-repo DPAPI store used, so the storage location survives the
    /// migration to the shared stack (the old payload encoding does not — the shared store fails
    /// closed on it, which means a one-time re-login).
    /// </summary>
    /// <returns>The absolute session-file path.</returns>
    public static string ResolveDefaultSessionFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            appData = AppContext.BaseDirectory;

        return Path.Combine(appData, "OmnibusCloud", "3dsMax", SESSION_FILE_NAME);
    }

    private void ApplySignedInState(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            ClearRuntimeSession();
            return;
        }

        UpdateIdentity(accessToken);
        m_isSignedIn = true;
        m_lastError = null;
    }

    private void UpdateIdentity(string accessToken)
    {
        var claims = ParseJwtClaims(accessToken);
        if (claims == null)
            return;

        m_userId = GetClaim(claims.Value, "sub");
        m_displayName = GetClaim(claims.Value, "name")
                        ?? GetClaim(claims.Value, "preferred_username")
                        ?? GetClaim(claims.Value, "email")
                        ?? m_userId;
    }

    private static JsonElement? ParseJwtClaims(string accessToken)
    {
        try
        {
            var segments = accessToken.Split('.');
            if (segments.Length < 2)
                return null;

            var payload = segments[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? GetClaim(JsonElement claims, string name)
    {
        return claims.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private void ClearRuntimeSession()
    {
        m_isSignedIn = false;
        m_displayName = null;
        m_userId = null;
        m_lastError = NO_SESSION_TEXT;
    }

    private void SetLastError(string? text)
    {
        m_lastError = text;
    }

    #endregion

    #region Event Handlers

    private void OnRefreshTokenRotated()
    {
        m_tokenService.SaveSession(SESSION_POLICY, m_sessionStore);
        MaxPluginLogging.Logger.Information("Rotated refresh token persisted.");
    }

    private void OnReauthenticationRequired()
    {
        ClearRuntimeSession();
        MaxPluginLogging.Logger.Warning("Refresh token permanently rejected; interactive re-login required.");
    }

    #endregion
}
