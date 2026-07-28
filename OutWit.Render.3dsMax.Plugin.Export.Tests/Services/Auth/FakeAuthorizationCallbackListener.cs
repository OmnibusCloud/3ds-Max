using OutWit.Cloud.Auth.Interfaces;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

internal sealed class FakeAuthorizationCallbackListener : IAuthorizationCallbackListener
{
    #region IAuthorizationCallbackListener

    public string? TryStart()
    {
        return RedirectUri;
    }

    public Task<string?> WaitForCallbackAsync(string expectedState, string? completionUrl, CancellationToken cancellationToken)
    {
        LastExpectedState = expectedState;
        LastCompletionUrl = completionUrl;
        return Task.FromResult(AuthorizationCode);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
    }

    #endregion

    #region Properties

    public string? RedirectUri { get; set; } = "http://127.0.0.1:17892/callback";

    public string? AuthorizationCode { get; set; } = "auth-code";

    public string? LastExpectedState { get; private set; }

    public string? LastCompletionUrl { get; private set; }

    #endregion
}
