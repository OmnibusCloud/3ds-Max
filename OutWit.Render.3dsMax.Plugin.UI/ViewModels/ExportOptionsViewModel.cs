using OutWit.Common.Aspects;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

public sealed class ExportOptionsViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public ExportOptionsViewModel(ApplicationViewModel applicationVm) : base(applicationVm)
    {
        OutputFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    #endregion

    #region Properties

    [Notify]
    public bool ExportSelectedOnly { get; set; }

    [Notify]
    public bool IncludeHiddenObjects { get; set; }

    [Notify]
    public bool IncludeCameras { get; set; } = true;

    [Notify]
    public bool IncludeLights { get; set; } = true;

    [Notify]
    public bool IncludeMaterials { get; set; } = true;

    [Notify]
    public bool IncludeAnimations { get; set; } = true;

    [Notify]
    public bool UseSceneFrameRange { get; set; } = true;

    [Notify]
    public int FrameStart { get; set; } = 1;

    [Notify]
    public int FrameEnd { get; set; } = 1;

    [Notify]
    public string OutputFolder { get; set; }

    [Notify]
    public MaxSceneExportOutputFormat OutputFormat { get; set; } = MaxSceneExportOutputFormat.Json;

    [Notify]
    public bool OpenFolderAfterExport { get; set; } = true;

    #endregion
}
