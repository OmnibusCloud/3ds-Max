using System.Collections.ObjectModel;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

public sealed class ExportDiagnosticsViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public ExportDiagnosticsViewModel(ApplicationViewModel applicationVm) : base(applicationVm)
    {
    }

    #endregion

    #region Functions

    public void Apply(IEnumerable<MaxSceneDiagnosticItem> diagnostics)
    {
        Items.Clear();

        foreach (var diagnostic in diagnostics)
            Items.Add(diagnostic);

        ErrorCount = Items.Count(me => me.Severity == MaxSceneDiagnosticSeverity.Error);
        WarningCount = Items.Count(me => me.Severity == MaxSceneDiagnosticSeverity.Warning);
        UnsupportedCount = Items.Count(me => me.Severity == MaxSceneDiagnosticSeverity.Unsupported);
        InfoCount = Items.Count(me => me.Severity == MaxSceneDiagnosticSeverity.Info);
    }

    public void SetLogText(string text)
    {
        LogText = text;
    }

    #endregion

    #region Properties

    public ObservableCollection<MaxSceneDiagnosticItem> Items { get; } = [];

    [Notify]
    public int ErrorCount { get; set; }

    [Notify]
    public int WarningCount { get; set; }

    [Notify]
    public int UnsupportedCount { get; set; }

    [Notify]
    public int InfoCount { get; set; }

    [Notify]
    public string LogText { get; set; } = string.Empty;

    #endregion
}
