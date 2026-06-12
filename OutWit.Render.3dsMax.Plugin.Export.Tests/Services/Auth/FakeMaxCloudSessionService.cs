using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

internal sealed class FakeMaxCloudSessionService : IMaxCloudSessionService
{
    #region IMaxCloudSessionService

    public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(State.IsSignedIn);
    }

    public Task<MaxConnectedSessionState> SignInAsync(string identityUrl, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(State);
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        State = new MaxConnectedSessionState { LastError = "No active user session." };
        return Task.CompletedTask;
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(State.IsSignedIn ? AccessToken : null);
    }

    public MaxConnectedSessionState GetState()
    {
        return State;
    }

    #endregion

    #region Properties

    public MaxConnectedSessionState State { get; set; } = new();

    public string? AccessToken { get; set; } = "fake-access-token";

    #endregion
}
