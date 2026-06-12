namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Describes the first connected-launch package to prepare from the current 3ds Max scene.
/// </summary>
public sealed class MaxSceneLaunchPackageRequest
{
    #region Properties

    public string CloudUrl { get; set; } = string.Empty;

    public string IdentityUrl { get; set; } = string.Empty;

    public string RenderMode { get; set; } = "RenderStill";

    public int ResolutionX { get; set; } = 1920;

    public int ResolutionY { get; set; } = 1080;

    public int FrameStart { get; set; } = 1;

    public int FrameEnd { get; set; } = 1;

    public int Samples { get; set; } = 64;

    public bool UseAllClients { get; set; }

    public string SelectedGroupName { get; set; } = string.Empty;

    public string OutputFolder { get; set; } = string.Empty;

    #endregion
}
