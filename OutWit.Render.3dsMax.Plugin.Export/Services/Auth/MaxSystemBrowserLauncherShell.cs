using System.Diagnostics;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

/// <summary>
/// Default browser launcher: shell-executes the URL so Windows routes it to the user's default browser.
/// </summary>
public sealed class MaxSystemBrowserLauncherShell : IMaxSystemBrowserLauncher
{
    #region IMaxSystemBrowserLauncher

    /// <summary>
    /// Opens the provided URL in the system browser.
    /// </summary>
    /// <param name="url">The absolute URL to open.</param>
    public void Open(string url)
    {
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    #endregion
}
