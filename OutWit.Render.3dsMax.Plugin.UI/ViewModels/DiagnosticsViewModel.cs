using System.Windows.Input;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

/// <summary>
/// Details / Diagnostics dialog (design 4.1.5): a fixed-size window over the already-populated
/// <see cref="ExportDiagnosticsViewModel"/> / <see cref="ExportSummaryViewModel"/> — the validation
/// grid (the honesty-wave warnings with suggested actions), the scene summary and the versions.
/// Validate / Preflight delegate to the owning Render dialog VM, which owns the launch request.
/// </summary>
public sealed class DiagnosticsViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Events

    public event Action<bool>? DialogClosed;

    #endregion

    #region Constructors

    public DiagnosticsViewModel(ApplicationViewModel applicationVm, Action refreshValidation, Action runPreflight)
        : base(applicationVm)
    {
        PluginVersion = MaxPluginVersionInfo.Resolve();

        ValidateCommand = new RelayCommand(_ => refreshValidation());
        PreflightCommand = new RelayCommand(_ => runPreflight());
        CloseCommand = new RelayCommand(_ => DialogClosed?.Invoke(true));
    }

    #endregion

    #region Properties

    public ExportDiagnosticsViewModel DiagnosticsVm => ApplicationVm.MainVm.DiagnosticsVm;

    public ExportSummaryViewModel SummaryVm => ApplicationVm.MainVm.SummaryVm;

    public ExportOptionsViewModel OptionsVm => ApplicationVm.MainVm.OptionsVm;

    public string PluginVersion { get; }

    public string HostVersion => "3ds Max 2027 · SDK 2027";

    #endregion

    #region Commands

    public ICommand ValidateCommand { get; }

    public ICommand PreflightCommand { get; }

    public ICommand CloseCommand { get; }

    #endregion
}
