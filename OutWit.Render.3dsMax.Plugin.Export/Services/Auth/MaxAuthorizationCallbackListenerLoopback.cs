using System.Net;
using System.Text;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

/// <summary>
/// Loopback HTTP listener for the OAuth authorization-code callback. Uses the same
/// loopback ports as the other OmnibusCloud native clients so the redirect URIs stay
/// within the identity server's registered set.
/// </summary>
public sealed class MaxAuthorizationCallbackListenerLoopback : IMaxAuthorizationCallbackListener
{
    #region Constants

    private static readonly int[] PORTS = [17892, 17893, 17894];

    private const string CALLBACK_PATH = "/callback";

    private const string SUCCESS_HTML = """
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"/><title>Authentication Successful</title></head>
        <body><h2>Authentication Successful</h2><p>You can close this tab and return to 3ds Max.</p></body>
        </html>
        """;

    private const string ERROR_HTML = """
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"/><title>Authentication Failed</title></head>
        <body><h2>Authentication Failed</h2><p>{0}</p></body>
        </html>
        """;

    #endregion

    #region Fields

    private readonly HttpListener m_listener = new();

    #endregion

    #region IMaxAuthorizationCallbackListener

    /// <summary>
    /// Starts the listener and returns the redirect URI to register in the authorize request,
    /// or null when no callback endpoint could be bound.
    /// </summary>
    /// <returns>The redirect URI, or null when binding failed.</returns>
    public string? TryStart()
    {
        foreach (var port in PORTS)
        {
            var prefix = $"http://127.0.0.1:{port}{CALLBACK_PATH}/";
            try
            {
                m_listener.Prefixes.Clear();
                m_listener.Prefixes.Add(prefix);
                m_listener.Start();

                RedirectUri = $"http://127.0.0.1:{port}{CALLBACK_PATH}";
                return RedirectUri;
            }
            catch (HttpListenerException)
            {
                // Port in use — try the next registered loopback port.
            }
        }

        return null;
    }

    /// <summary>
    /// Waits for the browser redirect and returns the authorization code, or null on
    /// error / state mismatch / cancellation.
    /// </summary>
    /// <param name="expectedState">The state value the callback must echo back.</param>
    /// <param name="completionUrl">Optional branded completion page to forward the browser to.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The authorization code, or null.</returns>
    public async Task<string?> WaitForCallbackAsync(string expectedState, string? completionUrl, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(Stop);

        try
        {
            var context = await m_listener.GetContextAsync();
            var query = context.Request.QueryString;
            var error = query["error"];
            var state = query["state"];
            var code = query["code"];

            if (!string.IsNullOrEmpty(error))
            {
                var description = query["error_description"] ?? error;
                await RespondAsync(context, completionUrl, isError: true, errorDetail: description);
                return null;
            }

            if (state != expectedState)
            {
                await RespondAsync(context, completionUrl, isError: true, errorDetail: "Security validation failed (state mismatch).");
                return null;
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                await RespondAsync(context, completionUrl, isError: true, errorDetail: "No authorization code received.");
                return null;
            }

            await RespondAsync(context, completionUrl, isError: false);
            return code;
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    #endregion

    #region Tools

    private void Stop()
    {
        try
        {
            m_listener.Stop();
        }
        catch
        {
        }
    }

    private static async Task RespondAsync(HttpListenerContext context, string? completionUrl, bool isError, string? errorDetail = null)
    {
        // With a completion URL the browser lands on the shared branded WitIdentity page
        // (status=error appended on failure); the raw error detail never leaves the plugin.
        if (!string.IsNullOrWhiteSpace(completionUrl))
        {
            var location = isError ? AppendStatusError(completionUrl) : completionUrl;
            context.Response.Redirect(location);
            context.Response.Close();
            return;
        }

        var html = isError
            ? string.Format(ERROR_HTML, WebUtility.HtmlEncode(errorDetail ?? "Authentication failed."))
            : SUCCESS_HTML;
        await WriteResponseAsync(context, html);
    }

    private static string AppendStatusError(string completionUrl)
    {
        return completionUrl.Contains('?')
            ? $"{completionUrl}&status=error"
            : $"{completionUrl}?status=error";
    }

    private static async Task WriteResponseAsync(HttpListenerContext context, string html)
    {
        var buffer = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = buffer.Length;
        context.Response.StatusCode = 200;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.Close();
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Stop();
        m_listener.Close();
    }

    #endregion

    #region Properties

    public string? RedirectUri { get; private set; }

    #endregion
}
