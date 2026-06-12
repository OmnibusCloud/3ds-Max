using System.Text;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

[TestFixture]
public sealed class MaxSessionStoreDpapiTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.SessionStore.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_testDir))
            Directory.Delete(m_testDir, recursive: true);
    }

    #region Round Trip Tests

    [Test]
    public async Task SaveAndLoadRoundTripsSessionTest()
    {
        var store = new MaxSessionStoreDpapi(m_testDir);
        var session = new MaxStoredSession
        {
            RefreshToken = "refresh-token-value",
            TokenEndpoint = "https://auth.omnibuscloud.local/connect/token",
            DisplayName = "Artist One",
            UserId = "user-1",
            LastLoginUtc = DateTime.UtcNow.ToString("O")
        };

        await store.SaveAsync(session);
        var loaded = await store.LoadAsync();

        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.RefreshToken, Is.EqualTo(session.RefreshToken));
            Assert.That(loaded.TokenEndpoint, Is.EqualTo(session.TokenEndpoint));
            Assert.That(loaded.DisplayName, Is.EqualTo(session.DisplayName));
            Assert.That(loaded.UserId, Is.EqualTo(session.UserId));
        });
    }

    [Test]
    public async Task SavedSessionFileDoesNotContainPlaintextRefreshTokenTest()
    {
        var store = new MaxSessionStoreDpapi(m_testDir);
        const string refreshToken = "very-secret-refresh-token";

        await store.SaveAsync(new MaxStoredSession
        {
            RefreshToken = refreshToken,
            TokenEndpoint = "https://auth.omnibuscloud.local/connect/token"
        });

        var sessionFile = Directory.EnumerateFiles(m_testDir).Single();
        var rawContent = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(sessionFile));
        Assert.That(rawContent, Does.Not.Contain(refreshToken),
            "The refresh token must be encrypted at rest, never stored in plaintext.");
    }

    #endregion

    #region Missing / Corrupt Tests

    [Test]
    public async Task LoadReturnsNullWhenNoSessionExistsTest()
    {
        var store = new MaxSessionStoreDpapi(m_testDir);

        var loaded = await store.LoadAsync();

        Assert.That(loaded, Is.Null);
    }

    [Test]
    public async Task LoadReturnsNullForCorruptSessionFileTest()
    {
        var store = new MaxSessionStoreDpapi(m_testDir);
        await File.WriteAllTextAsync(Path.Combine(m_testDir, "3dsmax-session.json"), "not-json-at-all");

        var loaded = await store.LoadAsync();

        Assert.That(loaded, Is.Null);
    }

    [Test]
    public async Task ClearRemovesPersistedSessionTest()
    {
        var store = new MaxSessionStoreDpapi(m_testDir);
        await store.SaveAsync(new MaxStoredSession
        {
            RefreshToken = "refresh-token-value",
            TokenEndpoint = "https://auth.omnibuscloud.local/connect/token"
        });

        await store.ClearAsync();
        var loaded = await store.LoadAsync();

        Assert.That(loaded, Is.Null);
    }

    #endregion
}
