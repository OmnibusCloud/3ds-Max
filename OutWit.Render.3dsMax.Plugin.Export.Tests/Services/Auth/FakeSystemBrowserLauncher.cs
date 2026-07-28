using OutWit.Cloud.Auth.Interfaces;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

internal sealed class FakeSystemBrowserLauncher : ISystemBrowserLauncher
{
    #region ISystemBrowserLauncher

    public Task OpenAsync(string url)
    {
        OpenedUrls.Add(url);
        return Task.CompletedTask;
    }

    #endregion

    #region Properties

    public List<string> OpenedUrls { get; } = [];

    #endregion
}
