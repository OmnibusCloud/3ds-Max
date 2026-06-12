using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

/// <summary>
/// Persists the plugin's OmnibusCloud session across 3ds Max restarts.
/// </summary>
public interface IMaxSessionStore
{
    /// <summary>
    /// Loads the persisted session, or null when none exists or it cannot be read back.
    /// </summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The stored session, or null.</returns>
    Task<MaxStoredSession?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the provided session.
    /// </summary>
    /// <param name="session">The session to persist.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    Task SaveAsync(MaxStoredSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes any persisted session.
    /// </summary>
    /// <param name="cancellationToken">Cancels the clear.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
