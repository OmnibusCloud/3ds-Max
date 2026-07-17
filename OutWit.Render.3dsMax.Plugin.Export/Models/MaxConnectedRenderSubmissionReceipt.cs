namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Persisted placeholder submission receipt for the phased 3ds Max connected render flow.
/// </summary>
public sealed class MaxConnectedRenderSubmissionReceipt
{
    #region Properties

    public string SubmissionId { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public DateTime SubmittedUtc { get; set; }

    public string CloudUrl { get; set; } = string.Empty;

    public string IdentityUrl { get; set; } = string.Empty;

    public string RenderMode { get; set; } = string.Empty;

    public bool UseAllClients { get; set; }

    public string SelectedGroupName { get; set; } = string.Empty;

    public string SelectedProjectName { get; set; } = string.Empty;

    public string PackageArchivePath { get; set; } = string.Empty;

    public string StatusText { get; set; } = string.Empty;

    #endregion
}
