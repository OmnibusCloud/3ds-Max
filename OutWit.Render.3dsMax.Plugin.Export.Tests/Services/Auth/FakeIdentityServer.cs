using System.Net;
using System.Text;
using System.Text.Json;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

/// <summary>
/// Minimal loopback identity server for session-service tests. The shared
/// OutWit.Cloud.Auth <c>TokenService</c> talks real HTTP (it exposes no message-handler
/// seam), so the tests serve the OIDC discovery document and the token endpoint from an
/// actual <see cref="HttpListener"/> and record what the service sent.
/// </summary>
internal sealed class FakeIdentityServer : IDisposable
{
    #region Constants

    private const int PORT_ATTEMPTS = 20;

    #endregion

    #region Fields

    private readonly HttpListener m_listener;

    private readonly Task m_serveTask;

    #endregion

    #region Constructors

    public FakeIdentityServer()
    {
        (m_listener, BaseUrl) = Start();
        m_serveTask = Task.Run(ServeAsync);
    }

    #endregion

    #region Functions

    public static string CreateUnsignedJwt(string userId, string displayName, string claimName = "name")
    {
        var header = Base64UrlEncode("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64UrlEncode(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["sub"] = userId,
            [claimName] = displayName
        }));
        return $"{header}.{payload}.signature";
    }

    #endregion

    #region Tools

    private static (HttpListener Listener, string BaseUrl) Start()
    {
        for (var attempt = 0; attempt < PORT_ATTEMPTS; attempt++)
        {
            var port = Random.Shared.Next(20000, 60000);
            var baseUrl = $"http://127.0.0.1:{port}";

            // A failed Start disposes the HttpListener, so each attempt needs a fresh one.
            var listener = new HttpListener();
            listener.Prefixes.Add($"{baseUrl}/");
            try
            {
                listener.Start();
                return (listener, baseUrl);
            }
            catch (HttpListenerException)
            {
                // Port in use — try another.
                listener.Close();
            }
        }

        throw new InvalidOperationException("Failed to bind the fake identity server to a loopback port.");
    }

    private async Task ServeAsync()
    {
        while (m_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await m_listener.GetContextAsync();
            }
            catch (Exception)
            {
                // Listener stopped — server disposed.
                return;
            }

            await HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var path = context.Request.Url!.AbsolutePath;

        if (path.EndsWith("/.well-known/openid-configuration", StringComparison.OrdinalIgnoreCase))
        {
            if (FailDiscovery)
            {
                await RespondAsync(context, HttpStatusCode.InternalServerError, string.Empty);
                return;
            }

            await RespondJsonAsync(context, new Dictionary<string, string>
            {
                ["issuer"] = BaseUrl,
                ["authorization_endpoint"] = $"{BaseUrl}/connect/authorize",
                ["token_endpoint"] = $"{BaseUrl}/connect/token"
            });
            return;
        }

        if (path.EndsWith("/connect/token", StringComparison.OrdinalIgnoreCase))
        {
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                LastTokenRequestBody = await reader.ReadToEndAsync();

            if (FailTokenEndpoint)
            {
                await RespondAsync(context, HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
                return;
            }

            await RespondJsonAsync(context, new Dictionary<string, object>
            {
                ["access_token"] = AccessToken,
                ["refresh_token"] = RefreshToken,
                ["expires_in"] = 3600
            });
            return;
        }

        await RespondAsync(context, HttpStatusCode.NotFound, string.Empty);
    }

    private static Task RespondJsonAsync(HttpListenerContext context, object payload)
    {
        return RespondAsync(context, HttpStatusCode.OK, JsonSerializer.Serialize(payload));
    }

    private static async Task RespondAsync(HttpListenerContext context, HttpStatusCode statusCode, string body)
    {
        try
        {
            var buffer = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        }
        catch (Exception)
        {
            // Client went away — irrelevant for the test outcome.
        }
    }

    private static string Base64UrlEncode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        try
        {
            m_listener.Stop();
            m_listener.Close();
        }
        catch (Exception)
        {
        }

        try
        {
            m_serveTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
        }
    }

    #endregion

    #region Properties

    public string BaseUrl { get; }

    public string AccessToken { get; set; } = CreateUnsignedJwt("user-1", "Artist One");

    public string RefreshToken { get; set; } = "refresh-token-1";

    public bool FailDiscovery { get; set; }

    public bool FailTokenEndpoint { get; set; }

    public string? LastTokenRequestBody { get; private set; }

    #endregion
}
