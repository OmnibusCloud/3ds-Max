using OutWit.Common.Aspects;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

public sealed class ExportSummaryViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public ExportSummaryViewModel(ApplicationViewModel applicationVm) : base(applicationVm)
    {
        SceneName = "No scene collected";
        SceneFilePath = string.Empty;
        SourceApplicationLabel = "3ds Max 2027";
        ActiveRenderCameraName = string.Empty;
        RenderResolutionText = "1920 x 1080";
    }

    #endregion

    #region Functions

    public void Apply(MaxSceneSummaryData summary)
    {
        SceneName = summary.SceneName;
        SceneFilePath = summary.SceneFilePath;
        SourceApplicationLabel = summary.SourceApplicationLabel;
        ActiveRenderCameraName = summary.ActiveRenderCameraName;
        NodesCount = summary.NodesCount;
        MeshesCount = summary.MeshesCount;
        MaterialsCount = summary.MaterialsCount;
        TexturesCount = summary.TexturesCount;
        CamerasCount = summary.CamerasCount;
        LightsCount = summary.LightsCount;
        AnimatedChannelsCount = summary.AnimatedChannelsCount;
        FrameStart = summary.FrameStart;
        FrameEnd = summary.FrameEnd;
        FrameRangeText = $"{summary.FrameStart} - {summary.FrameEnd}";
        RenderResolutionText = $"{summary.RenderWidth} x {summary.RenderHeight}";
    }

    #endregion

    #region Properties

    [Notify]
    public string SceneName { get; set; }

    [Notify]
    public string SceneFilePath { get; set; }

    [Notify]
    public string SourceApplicationLabel { get; set; }

    [Notify]
    public string ActiveRenderCameraName { get; set; }

    [Notify]
    public int NodesCount { get; set; }

    [Notify]
    public int MeshesCount { get; set; }

    [Notify]
    public int MaterialsCount { get; set; }

    [Notify]
    public int TexturesCount { get; set; }

    [Notify]
    public int CamerasCount { get; set; }

    [Notify]
    public int LightsCount { get; set; }

    [Notify]
    public int AnimatedChannelsCount { get; set; }

    [Notify]
    public int FrameStart { get; set; } = 1;

    [Notify]
    public int FrameEnd { get; set; } = 1;

    [Notify]
    public string FrameRangeText { get; set; } = "1 - 1";

    [Notify]
    public string RenderResolutionText { get; set; }

    #endregion
}
