using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

internal sealed class FakeMaxSessionStore : IMaxSessionStore
{
    #region IMaxSessionStore

    public Task<MaxStoredSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StoredSession);
    }

    public Task SaveAsync(MaxStoredSession session, CancellationToken cancellationToken = default)
    {
        StoredSession = session;
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        StoredSession = null;
        ClearCount++;
        return Task.CompletedTask;
    }

    #endregion

    #region Properties

    public MaxStoredSession? StoredSession { get; set; }

    public int SaveCount { get; private set; }

    public int ClearCount { get; private set; }

    #endregion
}
