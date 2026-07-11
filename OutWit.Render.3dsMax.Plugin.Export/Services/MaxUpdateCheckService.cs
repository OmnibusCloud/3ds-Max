using System.Text.Json;
using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Checks the OmnibusCloud portal's version feed (<c>/downloads/3dsmax/latest.json</c>, CC-9) for a
/// newer plugin build. Anonymous read-only GET — the portal counts the poll as its update-check
/// metric. Never throws: a failed check returns a result with the reason.
/// </summary>
public sealed class MaxUpdateCheckService
{
    #region Constants

    private const string FEED_URL = OmnibusCloudDefaults.PORTAL_URL + "/downloads/3dsmax/latest.json";

    /// <summary>Where "Download" sends the user — the portal page with builds + install steps.</summary>
    public const string DOWNLOAD_PAGE_URL = OmnibusCloudDefaults.PORTAL_URL + "/download?product=3dsmax";

    #endregion

    #region Fields

    private readonly HttpMessageHandler? m_httpMessageHandler;

    #endregion

    #region Constructors

    public MaxUpdateCheckService(HttpMessageHandler? httpMessageHandler = null)
    {
        m_httpMessageHandler = httpMessageHandler;
    }

    #endregion

    #region Functions

    public async Task<MaxUpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = MaxPluginVersionInfo.Resolve();

        try
        {
            using var httpClient = m_httpMessageHandler == null
                ? new HttpClient()
                : new HttpClient(m_httpMessageHandler, disposeHandler: false);
            httpClient.Timeout = TimeSpan.FromSeconds(15);

            var json = await httpClient.GetStringAsync(FEED_URL, cancellationToken);
            var document = JsonSerializer.Deserialize<JsonElement>(json);
            var latestVersion = document.TryGetProperty("version", out var versionProperty)
                ? versionProperty.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                MaxPluginLogging.Logger.Warning("Update check: the portal feed carried no version.");
                return MaxUpdateCheckResult.Failed(currentVersion, "The update feed carried no version.");
            }

            var updateAvailable = IsNewer(latestVersion!, currentVersion);
            MaxPluginLogging.Logger.Information(
                "Update check: current {Current}, latest {Latest}, update available: {Available}.",
                currentVersion, latestVersion, updateAvailable);

            return new MaxUpdateCheckResult
            {
                IsSuccess = true,
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion!,
                UpdateAvailable = updateAvailable,
                StatusText = updateAvailable
                    ? $"Version {latestVersion} is available."
                    : "You are up to date."
            };
        }
        catch (Exception ex)
        {
            MaxPluginLogging.Logger.Warning("Update check failed: {Error}", ex.Message);
            return MaxUpdateCheckResult.Failed(currentVersion, $"Update check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// True when <paramref name="remote"/> is a newer version than <paramref name="local"/>: numeric
    /// parts compare as versions; on a numeric tie a stable build beats a prerelease, and two
    /// prereleases compare their suffixes ordinally ("beta.2" &gt; "beta.1"). Unparseable versions
    /// are never advertised as updates.
    /// </summary>
    public static bool IsNewer(string remote, string local)
    {
        var (remoteNumeric, remoteSuffix) = SplitVersion(remote);
        var (localNumeric, localSuffix) = SplitVersion(local);

        if (remoteNumeric is null || localNumeric is null)
            return false;

        if (remoteNumeric != localNumeric)
            return remoteNumeric > localNumeric;

        if (remoteSuffix.Length == 0)
            return localSuffix.Length > 0;

        return localSuffix.Length > 0 && string.CompareOrdinal(remoteSuffix, localSuffix) > 0;
    }

    #endregion

    #region Tools

    private static (Version? Numeric, string Suffix) SplitVersion(string version)
    {
        var dashIndex = version.IndexOf('-');
        var numericPart = dashIndex >= 0 ? version[..dashIndex] : version;
        var suffix = dashIndex >= 0 ? version[(dashIndex + 1)..] : string.Empty;

        return (Version.TryParse(numericPart, out var parsed) ? parsed : null, suffix);
    }

    #endregion
}
