namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

/// <summary>
/// Captures the OAuth authorization-code redirect during interactive browser sign-in.
/// </summary>
public interface IMaxAuthorizationCallbackListener : IDisposable
{
    /// <summary>
    /// Starts the listener and returns the redirect URI to register in the authorize request,
    /// or null when no callback endpoint could be bound.
    /// </summary>
    /// <returns>The redirect URI, or null when binding failed.</returns>
    string? TryStart();

    /// <summary>
    /// Waits for the browser redirect and returns the authorization code, or null on
    /// error / state mismatch / cancellation.
    /// </summary>
    /// <param name="expectedState">The state value the callback must echo back.</param>
    /// <param name="completionUrl">Optional branded completion page to forward the browser to.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The authorization code, or null.</returns>
    Task<string?> WaitForCallbackAsync(string expectedState, string? completionUrl, CancellationToken cancellationToken);
}
