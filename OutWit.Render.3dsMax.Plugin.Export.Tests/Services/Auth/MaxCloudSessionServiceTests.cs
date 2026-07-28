using OutWit.Cloud.Auth;
using OutWit.Cloud.Auth.Sessions;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;
using Serilog;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

[TestFixture]
public sealed class MaxCloudSessionServiceTests
{
    #region Constants

    private static readonly ILogger LOGGER = Serilog.Core.Logger.None;

    #endregion

    #region Fields

    private FakeIdentityServer m_server = null!;

    private FakeSystemBrowserLauncher m_browser = null!;

    private FakeAuthorizationCallbackListener m_listener = null!;

    private SessionStore m_sessionStore = null!;

    private string m_testDir = null!;

    #endregion

    [SetUp]
    public void Setup()
    {
        m_server = new FakeIdentityServer();
        m_browser = new FakeSystemBrowserLauncher();
        m_listener = new FakeAuthorizationCallbackListener();
        m_testDir = Path.Combine(Path.GetTempPath(), "omnibuscloud-3dsmax-tests", Guid.NewGuid().ToString("N"));
        m_sessionStore = new SessionStore(Path.Combine(m_testDir, "3dsmax-session.json"), LOGGER);
    }

    [TearDown]
    public void TearDown()
    {
        m_server.Dispose();
        m_listener.Dispose();

        if (Directory.Exists(m_testDir))
            Directory.Delete(m_testDir, recursive: true);
    }

    #region Sign In Tests

    [Test]
    public async Task SignInCompletesFullPkceFlowTest()
    {
        var service = CreateService();

        var state = await service.SignInAsync(m_server.BaseUrl);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSignedIn, Is.True);
            Assert.That(state.DisplayName, Is.EqualTo("Artist One"));
            Assert.That(state.UserId, Is.EqualTo("user-1"));
            Assert.That(state.LastError, Is.Empty);
        });
    }

    [Test]
    public async Task SignInOpensAuthorizeUrlWithPkceParametersTest()
    {
        var service = CreateService();

        await service.SignInAsync(m_server.BaseUrl);

        Assert.That(m_browser.OpenedUrls, Has.Count.EqualTo(1));
        var authorizeUrl = m_browser.OpenedUrls[0];
        Assert.Multiple(() =>
        {
            Assert.That(authorizeUrl, Does.StartWith($"{m_server.BaseUrl}/connect/authorize?"));
            Assert.That(authorizeUrl, Does.Contain("client_id=cloud-client"));
            Assert.That(authorizeUrl, Does.Contain("response_type=code"));
            Assert.That(authorizeUrl, Does.Contain("code_challenge_method=S256"));
            Assert.That(authorizeUrl, Does.Contain("code_challenge="));
            Assert.That(authorizeUrl, Does.Contain($"state={m_listener.LastExpectedState}"));
            Assert.That(authorizeUrl, Does.Contain(Uri.EscapeDataString(m_listener.RedirectUri!)));
        });
    }

    [Test]
    public async Task SignInForwardsBrowserToSharedCompletionPageTest()
    {
        var service = CreateService();

        await service.SignInAsync(m_server.BaseUrl);

        Assert.That(m_listener.LastCompletionUrl, Is.EqualTo($"{m_server.BaseUrl}/auth/complete"));
    }

    [Test]
    public async Task SignInExchangesCodeWithVerifierTest()
    {
        var service = CreateService();

        await service.SignInAsync(m_server.BaseUrl);

        Assert.That(m_server.LastTokenRequestBody, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(m_server.LastTokenRequestBody, Does.Contain("grant_type=authorization_code"));
            Assert.That(m_server.LastTokenRequestBody, Does.Contain("code=auth-code"));
            Assert.That(m_server.LastTokenRequestBody, Does.Contain("code_verifier="));
        });
    }

    [Test]
    public async Task SignInPersistsSessionWithRefreshTokenTest()
    {
        var service = CreateService();

        var state = await service.SignInAsync(m_server.BaseUrl);

        var storedSession = m_sessionStore.Load();
        Assert.That(storedSession, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(storedSession!.RefreshToken, Is.EqualTo("refresh-token-1"));
            Assert.That(storedSession.TokenEndpoint, Is.EqualTo($"{m_server.BaseUrl}/connect/token"));
            // DisplayName is no longer persisted with the session — it is derived from the
            // access token's claims at runtime.
            Assert.That(state.DisplayName, Is.EqualTo("Artist One"));
        });
    }

    [Test]
    public async Task SignInFailsWhenListenerCannotStartTest()
    {
        m_listener.RedirectUri = null;
        var service = CreateService();

        var state = await service.SignInAsync(m_server.BaseUrl);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSignedIn, Is.False);
            Assert.That(state.LastError, Does.Contain("loopback"));
            Assert.That(m_browser.OpenedUrls, Is.Empty);
        });
    }

    [Test]
    public async Task SignInFailsWhenCallbackReturnsNoCodeTest()
    {
        m_listener.AuthorizationCode = null;
        var service = CreateService();

        var state = await service.SignInAsync(m_server.BaseUrl);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSignedIn, Is.False);
            Assert.That(state.LastError, Does.Contain("timed out or was cancelled"));
        });
    }

    [Test]
    public async Task SignInFailsWhenDiscoveryFailsTest()
    {
        m_server.FailDiscovery = true;
        var service = CreateService();

        var state = await service.SignInAsync(m_server.BaseUrl);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSignedIn, Is.False);
            Assert.That(state.LastError, Does.Contain("discover"));
        });
    }

    [Test]
    public async Task SignInFailsWhenTokenExchangeFailsTest()
    {
        m_server.FailTokenEndpoint = true;
        var service = CreateService();

        var state = await service.SignInAsync(m_server.BaseUrl);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSignedIn, Is.False);
            Assert.That(state.LastError, Does.Contain("token exchange failed"));
        });
    }

    [Test]
    public async Task SignInUsesPreferredUsernameWhenNameClaimMissingTest()
    {
        m_server.AccessToken = FakeIdentityServer.CreateUnsignedJwt("user-2", "artist2@omnibuscloud.local", claimName: "preferred_username");
        var service = CreateService();

        var state = await service.SignInAsync(m_server.BaseUrl);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSignedIn, Is.True);
            Assert.That(state.DisplayName, Is.EqualTo("artist2@omnibuscloud.local"));
            Assert.That(state.UserId, Is.EqualTo("user-2"));
        });
    }

    [Test]
    public async Task SignInFailsWhenIdentityUrlMissingTest()
    {
        var service = CreateService();

        var state = await service.SignInAsync(string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSignedIn, Is.False);
            Assert.That(state.LastError, Does.Contain("Identity URL is required"));
            Assert.That(m_browser.OpenedUrls, Is.Empty);
        });
    }

    #endregion

    #region Restore Tests

    [Test]
    public async Task TryRestoreSessionRefreshesStoredSessionTest()
    {
        m_sessionStore.Save(new StoredSession
        {
            RefreshToken = "stored-refresh-token",
            TokenEndpoint = $"{m_server.BaseUrl}/connect/token",
            LastLoginUtc = DateTime.UtcNow.ToString("O")
        });
        var service = CreateService();

        var restored = await service.TryRestoreSessionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.True);
            Assert.That(service.GetState().IsSignedIn, Is.True);
            Assert.That(service.GetState().DisplayName, Is.EqualTo("Artist One"));
            Assert.That(m_server.LastTokenRequestBody, Does.Contain("grant_type=refresh_token"));
            Assert.That(m_server.LastTokenRequestBody, Does.Contain("refresh_token=stored-refresh-token"));
        });
    }

    [Test]
    public async Task TryRestoreSessionPersistsRotatedRefreshTokenTest()
    {
        m_sessionStore.Save(new StoredSession
        {
            RefreshToken = "stored-refresh-token",
            TokenEndpoint = $"{m_server.BaseUrl}/connect/token",
            LastLoginUtc = DateTime.UtcNow.ToString("O")
        });
        var service = CreateService();

        var restored = await service.TryRestoreSessionAsync();

        // The identity server rotated the refresh token during the restore refresh; the
        // rotated token must be persisted or the next start would present a revoked one.
        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.True);
            Assert.That(m_sessionStore.Load()?.RefreshToken, Is.EqualTo("refresh-token-1"));
        });
    }

    [Test]
    public async Task TryRestoreSessionReturnsFalseWithoutStoredSessionTest()
    {
        var service = CreateService();

        var restored = await service.TryRestoreSessionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.False);
            Assert.That(service.GetState().IsSignedIn, Is.False);
        });
    }

    [Test]
    public async Task TryRestoreSessionClearsStoreWhenRefreshIsRejectedTest()
    {
        m_sessionStore.Save(new StoredSession
        {
            RefreshToken = "revoked-refresh-token",
            TokenEndpoint = $"{m_server.BaseUrl}/connect/token",
            LastLoginUtc = DateTime.UtcNow.ToString("O")
        });
        m_server.FailTokenEndpoint = true;
        var service = CreateService();

        var restored = await service.TryRestoreSessionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.False);
            Assert.That(m_sessionStore.Load(), Is.Null);
            Assert.That(service.GetState().IsSignedIn, Is.False);
        });
    }

    #endregion

    #region Sign Out / Token Tests

    [Test]
    public async Task SignOutClearsRuntimeAndPersistedSessionTest()
    {
        var service = CreateService();
        await service.SignInAsync(m_server.BaseUrl);

        await service.SignOutAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.GetState().IsSignedIn, Is.False);
            Assert.That(m_sessionStore.Load(), Is.Null);
            Assert.That(File.Exists(m_sessionStore.SessionFilePath), Is.False);
        });
    }

    [Test]
    public async Task GetAccessTokenReturnsCurrentTokenWhileValidTest()
    {
        var service = CreateService();
        await service.SignInAsync(m_server.BaseUrl);

        var token = await service.GetAccessTokenAsync();

        Assert.That(token, Is.EqualTo(m_server.AccessToken));
    }

    [Test]
    public async Task GetAccessTokenReturnsNullWhenSignedOutTest()
    {
        var service = CreateService();

        var token = await service.GetAccessTokenAsync();

        Assert.That(token, Is.Null);
    }

    #endregion

    #region Tools

    private MaxCloudSessionService CreateService()
    {
        var tokenService = new TokenService(
            LOGGER,
            m_browser,
            new FakeAuthorizationCallbackListenerFactory(m_listener),
            MaxCloudSessionService.CLIENT_ID);

        return new MaxCloudSessionService(tokenService, m_sessionStore);
    }

    #endregion
}
