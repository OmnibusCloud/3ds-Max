namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

/// <summary>
/// Opens URLs in the user's default system browser.
/// </summary>
public interface IMaxSystemBrowserLauncher
{
    /// <summary>
    /// Opens the provided URL in the system browser.
    /// </summary>
    /// <param name="url">The absolute URL to open.</param>
    void Open(string url);
}
