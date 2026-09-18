namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// The execution scope a server-only job (the <c>ExportBlend</c> build) is submitted under. Such a job
/// never reaches a render node, so the scope decides nothing about WHERE it runs — the server checks it
/// once, at submit time, as permission to launch at all. Resolved by
/// <see cref="Services.MaxServerJobScopeResolver"/>.
/// </summary>
public sealed class MaxServerJobScope
{
    #region Properties

    /// <summary>True when an authorizing scope was found; false leaves the export unable to submit.</summary>
    public bool IsResolved { get; set; }

    /// <summary>Submit unscoped — only for accounts with the whole-network right.</summary>
    public bool UseAllClients { get; set; }

    /// <summary>The render group the job is authorized through, or empty.</summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>The project the job is authorized through, or empty (the last resort).</summary>
    public string ProjectName { get; set; } = string.Empty;

    #endregion
}
