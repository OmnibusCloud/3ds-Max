using OutWit.Cloud.SDK;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

internal sealed class FakeMaxCloudConnectionService : IMaxCloudConnectionService
{
    #region IMaxCloudConnectionService

    public Task<IWitCloudClient?> GetClientAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        LastRequestedServerUrl = serverUrl;
        return Task.FromResult(Client);
    }

    #endregion

    #region IAsyncDisposable

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Properties

    public IWitCloudClient? Client { get; set; }

    public string? LastRequestedServerUrl { get; private set; }

    #endregion
}
