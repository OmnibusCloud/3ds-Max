using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

/// <summary>
/// File-backed session store. The persisted session holds the OIDC refresh token, so it is
/// encrypted at rest with DPAPI (CurrentUser scope — only the same OS user can read it back).
/// Lives under the per-OS-user application-data directory so it survives plugin reinstalls.
/// </summary>
public sealed class MaxSessionStoreDpapi : IMaxSessionStore
{
    #region Constants

    private const string SESSION_FILE_NAME = "3dsmax-session.json";

    #endregion

    #region Fields

    private readonly string m_storageFolderPath;

    #endregion

    #region Constructors

    public MaxSessionStoreDpapi(string? storageFolderPath = null)
    {
        m_storageFolderPath = !string.IsNullOrWhiteSpace(storageFolderPath) && Path.IsPathRooted(storageFolderPath)
            ? storageFolderPath
            : ResolveDefaultStorageFolderPath();
    }

    #endregion

    #region IMaxSessionStore

    /// <summary>
    /// Loads the persisted session, or null when none exists or it cannot be read back.
    /// </summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The stored session, or null.</returns>
    public async Task<MaxStoredSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = GetSessionFilePath();
        if (!File.Exists(path))
            return null;

        try
        {
            var envelopeBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var envelope = JsonSerializer.Deserialize<SessionEnvelope>(envelopeBytes);
            if (envelope?.Payload == null)
                return null;

            var payload = Convert.FromBase64String(envelope.Payload);

            if (envelope.Protected)
            {
                if (!OperatingSystem.IsWindows())
                    return null;

                payload = Unprotect(payload);
            }

            return JsonSerializer.Deserialize<MaxStoredSession>(payload);
        }
        catch (Exception)
        {
            // Corrupt / foreign / undecryptable session → treat as no session (clean re-login).
            return null;
        }
    }

    /// <summary>
    /// Persists the provided session.
    /// </summary>
    /// <param name="session">The session to persist.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public async Task SaveAsync(MaxStoredSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var path = GetSessionFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var json = JsonSerializer.SerializeToUtf8Bytes(session);

        var isProtected = false;
        if (OperatingSystem.IsWindows())
        {
            json = Protect(json);
            isProtected = true;
        }

        var envelope = new SessionEnvelope
        {
            Protected = isProtected,
            Payload = Convert.ToBase64String(json)
        };

        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(envelope), cancellationToken);
    }

    /// <summary>
    /// Removes any persisted session.
    /// </summary>
    /// <param name="cancellationToken">Cancels the clear.</param>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var path = GetSessionFilePath();
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    #endregion

    #region Tools

    [SupportedOSPlatform("windows")]
    private static byte[] Protect(byte[] data)
        => ProtectedData.Protect(data, optionalEntropy: null, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] Unprotect(byte[] data)
        => ProtectedData.Unprotect(data, optionalEntropy: null, DataProtectionScope.CurrentUser);

    private static string ResolveDefaultStorageFolderPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            appData = AppContext.BaseDirectory;

        return Path.Combine(appData, "OmnibusCloud", "3dsMax");
    }

    private string GetSessionFilePath()
    {
        return Path.Combine(m_storageFolderPath, SESSION_FILE_NAME);
    }

    #endregion

    #region Models

    private sealed class SessionEnvelope
    {
        public bool Protected { get; set; }

        public string? Payload { get; set; }
    }

    #endregion
}
