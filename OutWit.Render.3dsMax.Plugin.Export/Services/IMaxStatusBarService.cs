using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Pushes render lifecycle status to the host status bar (design section 3, MX-5/6). Lets a render
/// keep reporting progress in Max's native prompt line while the dialog is minimized — the farm job
/// is remote, so the artist's machine is free. The host implementation talks to
/// <c>IInterface.PushPrompt/ReplacePrompt</c>; the null implementation is used in tests and when no
/// Max host is present, so ViewModels can depend on this unconditionally.
/// </summary>
public interface IMaxStatusBarService
{
    /// <summary>Shows (or updates) the current render status in the host prompt line.</summary>
    void Report(MaxRenderStatus status);

    /// <summary>Removes the plugin's prompt-line message.</summary>
    void Clear();
}
