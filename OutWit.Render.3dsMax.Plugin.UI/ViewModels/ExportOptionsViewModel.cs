using System.ComponentModel;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

/// <summary>
/// Session-wide output options. Its <see cref="OutputFolder"/> is THE "Save to" — one live value shared
/// by the Render dialog, the Export dialog and Settings ▸ Output, persisted the moment it changes.
/// </summary>
/// <remarks>
/// Each surface used to keep its own copy, read once when its window opened: a folder changed in
/// Settings did not reach a dialog that was already open, and that dialog then wrote its stale copy
/// back on Render / Export — silently undoing the change — while a folder picked with Browse was lost
/// unless the artist also rendered. This view model outlives every dialog, so a change made anywhere
/// is visible everywhere at once.
/// </remarks>
public sealed class ExportOptionsViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public ExportOptionsViewModel(ApplicationViewModel applicationVm) : base(applicationVm)
    {
        InitDefault();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        OutputFolder = Settings.OutputFolder ?? string.Empty;
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
    }

    #endregion

    #region Tools

    /// <summary>The folder with the Desktop standing in for an empty one (the historic default).</summary>
    private static string ResolveFolder(string? folder) =>
        string.IsNullOrWhiteSpace(folder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : folder.Trim();

    private void PersistOutputFolder()
    {
        if (string.Equals(Settings.OutputFolder, OutputFolder, StringComparison.Ordinal))
            return;

        Settings.OutputFolder = OutputFolder;
        Settings.SettingsManager.Save();
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OutputFolder))
            PersistOutputFolder();
    }

    #endregion

    #region Properties

    /// <summary>
    /// The folder results are saved to — what every "Save to" field shows and edits. Empty means the
    /// Desktop; use <see cref="EffectiveOutputFolder"/> to write anything.
    /// </summary>
    [Notify]
    public string OutputFolder { get; set; } = string.Empty;

    /// <summary>The folder to actually write into: <see cref="OutputFolder"/>, or the Desktop when empty.</summary>
    public string EffectiveOutputFolder => ResolveFolder(OutputFolder);

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
    public MaxSceneExportOutputFormat OutputFormat { get; set; } = MaxSceneExportOutputFormat.Json;

    [Notify]
    public bool OpenFolderAfterExport { get; set; } = true;

    #endregion

    #region Services

    private MaxPluginSettings Settings => ApplicationVm.Settings;

    #endregion
}
