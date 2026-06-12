using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

[TestFixture]
public sealed class MaxCloudSessionServiceTests
{
    #region Constants

    private const string IDENTITY_URL = "https://auth.omnibuscloud.local";

    #endregion

    #region Fields

    private FakeMaxSessionStore m_store = null!;

    private FakeMaxSystemBrowserLauncher m_browser = null!;

    private FakeMaxAuthorizationCallbackListener m_listener = null!;

    private StubAuthHttpMessageHandler m_http = null!;

    #endregion

    [SetUp]
    public void Setup()
    {
        m_store = new FakeMaxSessionStore();
        m_browser = new FakeMaxSystemBrowserLauncher();
        m_listener = new FakeMaxAuthorizationCallbackListener();
        m_http = new StubAuthHttpMessageHandler();
    }

    [TearDown]
    public void TearDown()
    {
        m_listener.Dispose();
        m_http.Dispose();
    }

    #region Sign In Tests

    [Test]
    public async Task SignInCompletesFullPkceFlowTest()
    {
        var service = CreateService();

        var state = await service.SignInAsync(IDENTITY_URL);

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

        await service.SignInAsync(IDENTITY_URL);

        Assert.That(m_browser.OpenedUrls, Has.Count.EqualTo(1));
        var authorizeUrl = m_browser.OpenedUrls[0];
        Assert.Multiple(() =>
        {
            Assert.That(authorizeUrl, Does.StartWith($"{IDENTITY_URL}/connect/authorize?"));
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

        await service.SignInAsync(IDENTITY_URL);

        Assert.That(m_listener.LastCompletionUrl, Is.EqualTo($"{IDENTITY_URL}/auth/complete"));
    }

    [Test]
    public async Task SignInExchangesCodeWithVerifierTest()
    {
        var service = CreateService();

        await service.SignInAsync(IDENTITY_URL);

        Assert.That(m_http.LastTokenRequestBody, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(m_http.LastTokenRequestBody, Does.Contain("grant_type=authorization_code"));
            Assert.That(m_http.LastTokenRequestBody, Does.Contain("code=auth-code"));
            Assert.That(m_http.LastTokenRequestBody, Does.Contain("code_verifier="));
        });
    }

    [Test]
    public async Task SignInPersistsSessionWithRefreshTokenTest()
    {
        var service = CreateService();

        await service.SignInAsync(IDENTITY_URL);

        Assert.That(m_store.StoredSession, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(m_store.StoredSession!.RefreshToken, Is.EqualTo("refresh-token-1"));
            Assert.That(m_store.StoredSession.TokenEndpoint, Is.EqualTo($"{IDENTITY_URL}/connect/token"));
            Assert.That(m_store.StoredSession.DisplayName, Is.EqualTo("Artist One"));
        });
    }

    [Test]
    public async Task SignInFailsWhenListenerCannotStartTest()
    {
        m_listener.RedirectUri = null;
        var service = CreateService();

        var state = await service.SignInAsync(IDENTITY_URL);

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

        var state = await service.SignInAsync(IDENTITY_URL);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSignedIn, Is.False);
            Assert.That(state.LastError, Does.Contain("timed out or was cancelled"));
        });
    }

    [Test]
    public async Task SignInFailsWhenDiscoveryFailsTest()
    {
        m_http.FailDiscovery = true;
        var service = CreateService();

        var state = await service.SignInAsync(IDENTITY_URL);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSignedIn, Is.False);
            Assert.That(state.LastError, Does.Contain("discover"));
        });
    }

    [Test]
    public async Task SignInFailsWhenTokenExchangeFailsTest()
    {
        m_http.FailTokenEndpoint = true;
        var service = CreateService();

        var state = await service.SignInAsync(IDENTITY_URL);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSignedIn, Is.False);
            Assert.That(state.LastError, Does.Contain("token exchange failed"));
        });
    }

    [Test]
    public async Task SignInUsesPreferredUsernameWhenNameClaimMissingTest()
    {
        m_http.AccessToken = StubAuthHttpMessageHandler.CreateUnsignedJwt("user-2", "artist2@omnibuscloud.local", claimName: "preferred_username");
        var service = CreateService();

        var state = await service.SignInAsync(IDENTITY_URL);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSignedIn, Is.True);
            Assert.That(state.DisplayName, Is.EqualTo("artist2@omnibuscloud.local"));
            Assert.That(state.UserId, Is.EqualTo("user-2"));
        });
    }

    #endregion

    #region Restore Tests

    [Test]
    public async Task TryRestoreSessionRefreshesStoredSessionTest()
    {
        m_store.StoredSession = new MaxStoredSession
        {
            RefreshToken = "stored-refresh-token",
            TokenEndpoint = $"{IDENTITY_URL}/connect/token"
        };
        var service = CreateService();

        var restored = await service.TryRestoreSessionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.True);
            Assert.That(service.GetState().IsSignedIn, Is.True);
            Assert.That(service.GetState().DisplayName, Is.EqualTo("Artist One"));
            Assert.That(m_http.LastTokenRequestBody, Does.Contain("grant_type=refresh_token"));
            Assert.That(m_http.LastTokenRequestBody, Does.Contain("refresh_token=stored-refresh-token"));
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
        m_store.StoredSession = new MaxStoredSession
        {
            RefreshToken = "revoked-refresh-token",
            TokenEndpoint = $"{IDENTITY_URL}/connect/token"
        };
        m_http.FailTokenEndpoint = true;
        var service = CreateService();

        var restored = await service.TryRestoreSessionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.False);
            Assert.That(m_store.StoredSession, Is.Null);
            Assert.That(service.GetState().IsSignedIn, Is.False);
        });
    }

    #endregion

    #region Sign Out / Token Tests

    [Test]
    public async Task SignOutClearsRuntimeAndPersistedSessionTest()
    {
        var service = CreateService();
        await service.SignInAsync(IDENTITY_URL);

        await service.SignOutAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.GetState().IsSignedIn, Is.False);
            Assert.That(m_store.StoredSession, Is.Null);
            Assert.That(m_store.ClearCount, Is.GreaterThanOrEqualTo(1));
        });
    }

    [Test]
    public async Task GetAccessTokenReturnsCurrentTokenWhileValidTest()
    {
        var service = CreateService();
        await service.SignInAsync(IDENTITY_URL);

        var token = await service.GetAccessTokenAsync();

        Assert.That(token, Is.EqualTo(m_http.AccessToken));
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
        return new MaxCloudSessionService(m_store, m_browser, () => m_listener, m_http);
    }

    #endregion
}
