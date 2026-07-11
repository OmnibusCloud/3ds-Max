using System.Net;
using System.Text;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

/// <summary>
/// Version-comparison and feed-parsing tests for the portal update check (Settings ▸ About).
/// The feed shape is pinned to the portal's /downloads/{slug}/latest.json (CC-9).
/// </summary>
[TestFixture]
public class MaxUpdateCheckServiceTests
{
    #region IsNewer

    [TestCase("0.7.54-beta", "0.7.53-beta", true)]
    [TestCase("0.7.53-beta", "0.7.54-beta", false)]
    [TestCase("0.7.53-beta", "0.7.53-beta", false)]
    [TestCase("1.0.0", "0.7.53-beta", true)]
    [TestCase("0.7.53", "0.7.53-beta", true)]  // stable beats prerelease on a numeric tie
    [TestCase("0.7.53-beta", "0.7.53", false)] // prerelease never beats the same stable
    [TestCase("0.7.53-beta.2", "0.7.53-beta.1", true)]
    [TestCase("0.7.52", "0.7.53", false)]
    [TestCase("garbage", "0.7.53", false)]     // unparseable remote is never advertised
    [TestCase("1.0.0", "garbage", false)]      // unparseable local (odd dev build) stays quiet
    public void IsNewerComparesNumericThenSuffixTest(string remote, string local, bool expected)
    {
        Assert.That(MaxUpdateCheckService.IsNewer(remote, local), Is.EqualTo(expected));
    }

    #endregion

    #region CheckAsync

    [Test]
    public async Task CheckAsyncReportsUpdateFromThePortalFeedTest()
    {
        // The real feed shape (camelCase, extra fields the check ignores).
        const string feed = """
            {
              "slug": "3dsmax",
              "name": "3ds Max plugin",
              "version": "99.0.0",
              "releases": [ { "version": "99.0.0", "platform": "win-x64", "format": "msi" } ]
            }
            """;

        var service = new MaxUpdateCheckService(new StubHttpMessageHandler(feed));
        var result = await service.CheckAsync();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.LatestVersion, Is.EqualTo("99.0.0"));
        Assert.That(result.UpdateAvailable, Is.True, "99.0.0 must beat any build of this plugin");
        Assert.That(result.StatusText, Does.Contain("99.0.0"));
    }

    [Test]
    public async Task CheckAsyncSurvivesAnUnreachablePortalTest()
    {
        var service = new MaxUpdateCheckService(new StubHttpMessageHandler(null));
        var result = await service.CheckAsync();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.UpdateAvailable, Is.False);
        Assert.That(result.StatusText, Does.Contain("Update check failed"));
    }

    [Test]
    public async Task CheckAsyncTreatsAVersionlessFeedAsAFailureTest()
    {
        var service = new MaxUpdateCheckService(new StubHttpMessageHandler("""{ "slug": "3dsmax" }"""));
        var result = await service.CheckAsync();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.UpdateAvailable, Is.False);
    }

    #endregion

    #region Tools

    /// <summary>Returns the canned feed body, or throws (null body) to simulate an unreachable portal.</summary>
    private sealed class StubHttpMessageHandler(string? body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (body == null)
                throw new HttpRequestException("Connection refused (stub).");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    #endregion
}
