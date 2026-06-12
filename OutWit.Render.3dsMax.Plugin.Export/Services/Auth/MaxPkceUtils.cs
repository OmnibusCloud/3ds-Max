using System.Security.Cryptography;
using System.Text;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

/// <summary>
/// Generates PKCE parameters for the plugin authentication flow.
/// </summary>
internal static class MaxPkceUtils
{
    #region Constants

    private const int CODE_VERIFIER_LENGTH = 64;

    #endregion

    #region Functions

    public static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(CODE_VERIFIER_LENGTH);
        return Base64UrlEncode(bytes);
    }

    public static string ComputeCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    #endregion

    #region Tools

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    #endregion
}
