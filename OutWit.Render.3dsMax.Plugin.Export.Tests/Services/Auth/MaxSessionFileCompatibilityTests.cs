using System.Security.Cryptography;
using System.Text.Json;
using OutWit.Cloud.Auth;
using OutWit.Cloud.Auth.Sessions;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;
using Serilog;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

/// <summary>
/// Pins the migration behaviour for the on-disk session file. The shared
/// OutWit.Cloud.Auth <see cref="SessionStore"/> reuses the SAME file the retired in-repo
/// DPAPI store wrote (%APPDATA%\OmnibusCloud\3dsMax\3dsmax-session.json), but the payload
/// encoding differs: the old store put base64(DPAPI(json)) into the envelope, the shared
/// one puts "dpapi:"+base64. A legacy file must therefore fail CLOSED — load as
/// "no session" without throwing — so existing users get a clean one-time re-login,
/// never a crash.
/// </summary>
[TestFixture]
public sealed class MaxSessionFileCompatibilityTests
{
    #region Constants

    private static readonly ILogger LOGGER = Serilog.Core.Logger.None;

    #endregion

    #region Fields

    private string m_testDir = null!;

    private string m_sessionFilePath = null!;

    #endregion

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), "omnibuscloud-3dsmax-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_testDir);
        m_sessionFilePath = Path.Combine(m_testDir, "3dsmax-session.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_testDir))
            Directory.Delete(m_testDir, recursive: true);
    }

    #region Legacy File Tests

    [Test]
    public void LoadFailsClosedOnLegacyDpapiSessionFileTest()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("The legacy store only DPAPI-protected sessions on Windows.");

        WriteLegacySessionFile();
        var store = new SessionStore(m_sessionFilePath, LOGGER);

        Assert.That(store.Load(), Is.Null);
    }

    [Test]
    public async Task TryRestoreSessionReturnsFalseOnLegacySessionFileTest()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("The legacy store only DPAPI-protected sessions on Windows.");

        WriteLegacySessionFile();
        var store = new SessionStore(m_sessionFilePath, LOGGER);
        var service = new MaxCloudSessionService(
            new TokenService(
                LOGGER,
                new FakeSystemBrowserLauncher(),
                new FakeAuthorizationCallbackListenerFactory(new FakeAuthorizationCallbackListener()),
                MaxCloudSessionService.CLIENT_ID),
            store);

        var restored = await service.TryRestoreSessionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.False);
            Assert.That(service.GetState().IsSignedIn, Is.False);
        });
    }

    [Test]
    public void SaveThenLoadRoundTripsAtTheMaxSessionPathTest()
    {
        var store = new SessionStore(m_sessionFilePath, LOGGER);
        var session = new StoredSession
        {
            RefreshToken = "refresh-token-1",
            TokenEndpoint = "https://auth.omnibuscloud.local/connect/token",
            LastLoginUtc = DateTime.UtcNow.ToString("O")
        };

        store.Save(session);
        var loaded = store.Load();

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Is(session), Is.True);
    }

    #endregion

    #region Tools

    private void WriteLegacySessionFile()
    {
        // Byte-for-byte what the retired MaxSessionStoreDpapi wrote on Windows:
        // envelope { Protected: true, Payload: base64(DPAPI(session json)) }.
        var sessionJson = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
        {
            ["RefreshToken"] = "legacy-refresh-token",
            ["TokenEndpoint"] = "https://auth.omnibuscloud.local/connect/token",
            ["DisplayName"] = "Artist One",
            ["UserId"] = "user-1",
            ["LastLoginUtc"] = DateTime.UtcNow.ToString("O")
        });

        var protectedPayload = ProtectedData.Protect(sessionJson, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["Protected"] = true,
            ["Payload"] = Convert.ToBase64String(protectedPayload)
        });

        File.WriteAllBytes(m_sessionFilePath, envelope);
    }

    #endregion
}
