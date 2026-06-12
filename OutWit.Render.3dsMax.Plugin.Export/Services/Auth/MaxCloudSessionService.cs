using System.Text;
using System.Text.Json;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

/// <summary>
/// In-process OmnibusCloud user session for the 3ds Max plugin. Mirrors the sign-in
/// behaviour of the other OmnibusCloud native clients: OIDC authorization-code + PKCE
/// through the system browser with a loopback callback, silent refresh, and a
/// DPAPI-protected persisted refresh token. No sidecar process — everything runs
/// inside the plugin.
/// </summary>
public sealed class MaxCloudSessionService : IMaxCloudSessionService
{
    #region Constants

    private const string CLIENT_ID = "cloud-client";

    private const string SCOPE = "openid profile roles offline_access";

    private const int TOKEN_EXPIRY_BUFFER_SECONDS = 30;

    private const int AUTH_TIMEOUT_SECONDS = 300;

    #endregion

    #region Fields

    private readonly IMaxSessionStore m_sessionStore;

    private readonly IMaxSystemBrowserLauncher m_browserLauncher;

    private readonly Func<IMaxAuthorizationCallbackListener> m_callbackListenerFactory;

    private readonly HttpMessageHandler? m_httpMessageHandler;

    private string? m_accessToken;

    private DateTime m_accessTokenExpiry = DateTime.MinValue;

    private string? m_refreshToken;

    private string? m_tokenEndpoint;

    private bool m_isSignedIn;

    private string? m_displayName;

    private string? m_userId;

    private string? m_lastError = "No active user session.";

    #endregion

    #region Constructors

    public MaxCloudSessionService(
        IMaxSessionStore sessionStore,
        IMaxSystemBrowserLauncher browserLauncher,
        Func<IMaxAuthorizationCallbackListener> callbackListenerFactory,
        HttpMessageHandler? httpMessageHandler = null)
    {
        m_sessionStore = sessionStore;
        m_browserLauncher = browserLauncher;
        m_callbackListenerFactory = callbackListenerFactory;
        m_httpMessageHandler = httpMessageHandler;
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
        var storedSession = await m_sessionStore.LoadAsync(cancellationToken);
        if (storedSession == null || string.IsNullOrWhiteSpace(storedSession.RefreshToken) || string.IsNullOrWhiteSpace(storedSession.TokenEndpoint))
            return false;

        m_refreshToken = storedSession.RefreshToken;
        m_tokenEndpoint = storedSession.TokenEndpoint;
        m_displayName = string.IsNullOrWhiteSpace(storedSession.DisplayName) ? null : storedSession.DisplayName;
        m_userId = string.IsNullOrWhiteSpace(storedSession.UserId) ? null : storedSession.UserId;

        var restored = await RefreshTokenAsync(cancellationToken);
        if (!restored)
        {
            await m_sessionStore.ClearAsync(cancellationToken);
            ClearRuntimeSession();
            return false;
        }

        await SaveCurrentSessionAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Runs the full interactive browser sign-in flow (PKCE, loopback callback, token exchange).
    /// </summary>
    /// <param name="identityUrl">The identity server base URL.</param>
    /// <param name="cancellationToken">Cancels the flow.</param>
    /// <returns>The resulting session state.</returns>
    public async Task<MaxConnectedSessionState> SignInAsync(string identityUrl, CancellationToken cancellationToken = default)
    {
        ClearLastError();

        try
        {
            if (string.IsNullOrWhiteSpace(identityUrl))
            {
                SetLastError("Identity URL is required before sign-in.");
                return GetState();
            }

            var endpoints = await DiscoverEndpointsAsync(identityUrl, cancellationToken);
            if (endpoints == null)
                return GetState();

            m_tokenEndpoint = endpoints.TokenEndpoint;

            var codeVerifier = MaxPkceUtils.GenerateCodeVerifier();
            var codeChallenge = MaxPkceUtils.ComputeCodeChallenge(codeVerifier);
            var state = Guid.NewGuid().ToString("N");

            using var listener = m_callbackListenerFactory();
            var redirectUri = listener.TryStart();
            if (redirectUri == null)
            {
                SetLastError("Interactive authentication failed because the local loopback callback listener could not start.");
                return GetState();
            }

            var authorizeUrl = BuildAuthorizeUrl(endpoints.AuthorizationEndpoint, redirectUri, codeChallenge, state);
            m_browserLauncher.Open(authorizeUrl);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(AUTH_TIMEOUT_SECONDS));

            // After capturing the code the listener forwards the browser to WitIdentity's shared
            // completion page so the user sees the same branded "signed in" screen as every
            // other OmnibusCloud native client.
            var completionUrl = $"{identityUrl.TrimEnd('/')}/auth/complete";
            var code = await listener.WaitForCallbackAsync(state, completionUrl, cts.Token);
            if (string.IsNullOrWhiteSpace(code))
            {
                SetLastError("Interactive authentication timed out or was cancelled while waiting for the browser callback.");
                return GetState();
            }

            var exchanged = await ExchangeCodeForTokensAsync(code, redirectUri, codeVerifier, cancellationToken);
            if (!exchanged)
                return GetState();

            await SaveCurrentSessionAsync(cancellationToken);
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
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await m_sessionStore.ClearAsync(cancellationToken);
        ClearRuntimeSession();
        SetLastError("No active user session.");
    }

    /// <summary>
    /// Returns a valid access token (refreshing silently when needed), or null when signed out.
    /// </summary>
    /// <param name="cancellationToken">Cancels the token acquisition.</param>
    /// <returns>The access token, or null.</returns>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(m_accessToken) && DateTime.UtcNow < m_accessTokenExpiry)
            return m_accessToken;

        if (!string.IsNullOrWhiteSpace(m_refreshToken) && !string.IsNullOrWhiteSpace(m_tokenEndpoint))
        {
            var refreshed = await RefreshTokenAsync(cancellationToken);
            if (refreshed)
            {
                await SaveCurrentSessionAsync(cancellationToken);
                return m_accessToken;
            }
        }

        return null;
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

    private async Task<MaxOidcEndpoints?> DiscoverEndpointsAsync(string identityUrl, CancellationToken cancellationToken)
    {
        try
        {
            var discoveryUrl = $"{identityUrl.TrimEnd('/')}/.well-known/openid-configuration";

            using var httpClient = CreateHttpClient();
            var json = await httpClient.GetStringAsync(discoveryUrl, cancellationToken);
            var document = JsonSerializer.Deserialize<JsonElement>(json);

            var authorizationEndpoint = document.TryGetProperty("authorization_endpoint", out var authorizeProp)
                ? authorizeProp.GetString()
                : null;
            var tokenEndpoint = document.TryGetProperty("token_endpoint", out var tokenProp)
                ? tokenProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(authorizationEndpoint) || string.IsNullOrWhiteSpace(tokenEndpoint))
            {
                SetLastError("Identity discovery succeeded but the required authorization/token endpoints were missing.");
                return null;
            }

            return new MaxOidcEndpoints
            {
                AuthorizationEndpoint = authorizationEndpoint,
                TokenEndpoint = tokenEndpoint
            };
        }
        catch (Exception)
        {
            SetLastError($"Failed to discover identity configuration from {identityUrl.TrimEnd('/')}/.well-known/openid-configuration.");
            return null;
        }
    }

    private static string BuildAuthorizeUrl(string authorizationEndpoint, string redirectUri, string codeChallenge, string state)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = CLIENT_ID,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = SCOPE,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state
        };

        var query = string.Join("&", parameters.Select(me => $"{Uri.EscapeDataString(me.Key)}={Uri.EscapeDataString(me.Value)}"));
        return $"{authorizationEndpoint}?{query}";
    }

    private async Task<bool> ExchangeCodeForTokensAsync(string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = CreateHttpClient();
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = CLIENT_ID,
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier
            });

            var response = await httpClient.PostAsync(m_tokenEndpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                SetLastError($"Interactive authentication token exchange failed with status {(int)response.StatusCode}.");
                return false;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            ApplyTokenResponse(json);
            ClearLastError();
            return m_isSignedIn;
        }
        catch (Exception)
        {
            SetLastError("Interactive authentication token exchange failed unexpectedly.");
            return false;
        }
    }

    private async Task<bool> RefreshTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = CreateHttpClient();
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = CLIENT_ID,
                ["refresh_token"] = m_refreshToken!
            });

            var response = await httpClient.PostAsync(m_tokenEndpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                SetLastError($"Token refresh failed with status {(int)response.StatusCode}.");
                return false;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            ApplyTokenResponse(json);
            ClearLastError();
            return m_isSignedIn;
        }
        catch (Exception)
        {
            SetLastError("Token refresh failed unexpectedly.");
            return false;
        }
    }

    private void ApplyTokenResponse(string json)
    {
        var tokenResponse = JsonSerializer.Deserialize<JsonElement>(json);

        m_accessToken = tokenResponse.GetProperty("access_token").GetString();

        if (tokenResponse.TryGetProperty("refresh_token", out var refreshProp))
            m_refreshToken = refreshProp.GetString();

        var expiresIn = tokenResponse.GetProperty("expires_in").GetInt32();
        m_accessTokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - TOKEN_EXPIRY_BUFFER_SECONDS);
        UpdateIdentityFromAccessToken();
        m_isSignedIn = !string.IsNullOrWhiteSpace(m_accessToken);
    }

    private void UpdateIdentityFromAccessToken()
    {
        if (string.IsNullOrWhiteSpace(m_accessToken))
            return;

        var claims = ParseJwtClaims(m_accessToken);
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

    private async Task SaveCurrentSessionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(m_refreshToken) || string.IsNullOrWhiteSpace(m_tokenEndpoint))
            return;

        await m_sessionStore.SaveAsync(new MaxStoredSession
        {
            RefreshToken = m_refreshToken,
            TokenEndpoint = m_tokenEndpoint,
            DisplayName = m_displayName ?? string.Empty,
            UserId = m_userId ?? string.Empty,
            LastLoginUtc = DateTime.UtcNow.ToString("O")
        }, cancellationToken);
    }

    private HttpClient CreateHttpClient()
    {
        return m_httpMessageHandler == null
            ? new HttpClient()
            : new HttpClient(m_httpMessageHandler, disposeHandler: false);
    }

    private void ClearRuntimeSession()
    {
        m_accessToken = null;
        m_refreshToken = null;
        m_tokenEndpoint = null;
        m_accessTokenExpiry = DateTime.MinValue;
        m_isSignedIn = false;
        m_displayName = null;
        m_userId = null;
    }

    private void SetLastError(string? text)
    {
        m_lastError = text;
    }

    private void ClearLastError()
    {
        m_lastError = null;
    }

    #endregion

    #region Models

    private sealed class MaxOidcEndpoints
    {
        public string AuthorizationEndpoint { get; init; } = string.Empty;

        public string TokenEndpoint { get; init; } = string.Empty;
    }

    #endregion
}
