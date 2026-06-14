using Autodesk.Max;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Services;

/// <summary>
/// Reports render status to the 3ds Max prompt line (design section 3, MX-6) via
/// <see cref="IInterface.PushPrompt"/> / <see cref="IInterface.ReplacePrompt"/>. The first report pushes
/// the plugin's message onto Max's prompt stack; later reports replace it; <see cref="Clear"/> pops it.
/// All calls are guarded so a status update can never break the render flow.
/// </summary>
public sealed class MaxStatusBarService : IMaxStatusBarService
{
    #region Fields

    private readonly IInterface m_coreInterface;
    private bool m_pushed;

    #endregion

    #region Constructors

    public MaxStatusBarService(IInterface coreInterface)
    {
        m_coreInterface = coreInterface;
    }

    #endregion

    #region IMaxStatusBarService

    public void Report(MaxRenderStatus status)
    {
        try
        {
            var text = MaxStatusBarText.Format(status);

            if (m_pushed)
            {
                m_coreInterface.ReplacePrompt(text);
            }
            else
            {
                m_coreInterface.PushPrompt(text);
                m_pushed = true;
            }
        }
        catch
        {
            // The status bar is best-effort; never let a prompt update interrupt the render.
        }
    }

    public void Clear()
    {
        try
        {
            if (!m_pushed)
                return;

            m_coreInterface.PopPrompt();
            m_pushed = false;
        }
        catch
        {
            // Best-effort.
        }
    }

    #endregion
}
