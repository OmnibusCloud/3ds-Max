using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

internal sealed class FakeMaxSystemBrowserLauncher : IMaxSystemBrowserLauncher
{
    #region IMaxSystemBrowserLauncher

    public void Open(string url)
    {
        OpenedUrls.Add(url);
    }

    #endregion

    #region Properties

    public List<string> OpenedUrls { get; } = [];

    #endregion
}
