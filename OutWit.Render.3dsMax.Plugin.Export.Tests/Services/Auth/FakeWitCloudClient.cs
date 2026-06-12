using OutWit.Cloud.Data.Access;
using OutWit.Cloud.SDK;
using OutWit.Cloud.SDK.Blobs;
using OutWit.Cloud.SDK.Jobs;
using OutWit.Cloud.SDK.Scripts;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

internal sealed class FakeWitCloudClient : IWitCloudClient
{
    #region IWitCloudClient

    public Task ConnectAsync(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task<ExecutionScopeOptions> GetExecutionScopeOptionsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(ScopeOptions);
    }

    public IWitCloudScripts Scripts => throw new NotSupportedException("Scripts facet is not faked.");

    public IWitCloudJobs Jobs => throw new NotSupportedException("Jobs facet is not faked.");

    public IWitCloudBlobs Blobs => throw new NotSupportedException("Blobs facet is not faked.");

    #endregion

    #region IAsyncDisposable

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Properties

    public ExecutionScopeOptions ScopeOptions { get; set; } = new();

    #endregion
}
