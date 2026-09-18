using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Picks the scope a server-only job is submitted under, with no choice put to the artist.
/// </summary>
/// <remarks>
/// The <c>ExportBlend</c> build runs entirely inside the WitCloud server: the Render.Dcc controller is
/// host-only, so no render node ever loads its activities, and the job starts even when no node of the
/// selected group is online. The scope is only the server's submit-time permission check — no scope
/// needs the whole-network right, a group needs membership, a project needs launch rights. The Export
/// dialog used to ask "Run on" as if it mattered where the build ran; it never did.
/// </remarks>
public static class MaxServerJobScopeResolver
{
    #region Functions

    /// <summary>
    /// Resolves the authorizing scope: unscoped when the account holds the whole-network right (the
    /// historic behaviour), otherwise the first render GROUP, and a project only as a last resort — a
    /// project is a campaign, and an export has no business showing up in one just because it was
    /// listed first.
    /// </summary>
    /// <param name="scope">The execution scope loaded for the signed-in account.</param>
    /// <returns>The scope to submit under; not resolved when the account has none.</returns>
    public static MaxServerJobScope Resolve(MaxConnectedExecutionScopeResult? scope)
    {
        if (scope is not { IsSuccess: true })
            return new MaxServerJobScope();

        if (scope.CanRunOnAllClients)
            return new MaxServerJobScope { IsResolved = true, UseAllClients = true };

        var group = scope.Groups.FirstOrDefault(me => !string.IsNullOrWhiteSpace(me.Name));
        if (group is not null)
            return new MaxServerJobScope { IsResolved = true, GroupName = group.Name };

        var project = scope.Projects.FirstOrDefault(me => !string.IsNullOrWhiteSpace(me.Name));
        if (project is not null)
            return new MaxServerJobScope { IsResolved = true, ProjectName = project.Name };

        return new MaxServerJobScope();
    }

    #endregion
}
