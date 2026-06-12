using System.Net;
using System.Text;
using System.Text.Json;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

/// <summary>
/// Serves the OIDC discovery document and token endpoint for session-service tests.
/// </summary>
internal sealed class StubAuthHttpMessageHandler : HttpMessageHandler
{
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

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/.well-known/openid-configuration", StringComparison.OrdinalIgnoreCase))
        {
            if (FailDiscovery)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var authority = $"{request.RequestUri.Scheme}://{request.RequestUri.Authority}";
            return Task.FromResult(CreateJsonResponse(new Dictionary<string, string>
            {
                ["authorization_endpoint"] = $"{authority}/connect/authorize",
                ["token_endpoint"] = $"{authority}/connect/token"
            }));
        }

        if (path.EndsWith("/connect/token", StringComparison.OrdinalIgnoreCase))
        {
            LastTokenRequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

            if (FailTokenEndpoint)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));

            return Task.FromResult(CreateJsonResponse(new Dictionary<string, object>
            {
                ["access_token"] = AccessToken,
                ["refresh_token"] = RefreshToken,
                ["expires_in"] = 3600
            }));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    #endregion

    #region Tools

    private static HttpResponseMessage CreateJsonResponse(object payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
    }

    private static string Base64UrlEncode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    #endregion

    #region Properties

    public string AccessToken { get; set; } = CreateUnsignedJwt("user-1", "Artist One");

    public string RefreshToken { get; set; } = "refresh-token-1";

    public bool FailDiscovery { get; set; }

    public bool FailTokenEndpoint { get; set; }

    public List<HttpRequestMessage> Requests { get; } = [];

    public string? LastTokenRequestBody { get; private set; }

    #endregion
}
