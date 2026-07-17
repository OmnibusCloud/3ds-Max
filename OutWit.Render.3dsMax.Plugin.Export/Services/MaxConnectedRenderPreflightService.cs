using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Runs the first local preflight for the 3ds Max connected render flow before remote OmnibusCloud submission is wired.
/// </summary>
public sealed class MaxConnectedRenderPreflightService
{
    #region Constants

    private const string EXPORT_BLEND_MODE = "ExportBlend";

    #endregion

    #region Fields

    private static readonly HashSet<string> VALID_RENDER_MODES =
    [
        "RenderStill",
        "RenderStillTiled",
        "RenderFrames",
        "RenderVideo",
        EXPORT_BLEND_MODE
    ];

    private readonly MaxSceneExportService m_sceneExportService;

    #endregion

    #region Constructors

    public MaxConnectedRenderPreflightService(MaxSceneExportService sceneExportService)
    {
        m_sceneExportService = sceneExportService;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Runs a local preflight over the current scene and requested connected-launch settings.
    /// </summary>
    public MaxConnectedRenderPreflightResult Run(MaxSceneLaunchPackageRequest request)
    {
        var diagnostics = new List<MaxSceneDiagnosticItem>();

        // Preflight reads the scene through the SummaryOnly profile: a full geometry capture
        // takes minutes on heavy scenes and runs anyway during launch preparation — which is
        // also where the deep DCC-contract validation happens (its failure fails the launch).
        // Preflight checks the request fields and the scene's coarse shape.
        var summary = m_sceneExportService.CollectSummary(MaxSceneCaptureOptions.SummaryOnly);
        if (summary.MeshesCount == 0)
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Warning, "The scene contains no renderable meshes."));

        if (string.IsNullOrWhiteSpace(request.CloudUrl) && string.IsNullOrWhiteSpace(request.IdentityUrl))
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "OmnibusCloud URL or Identity URL is required before connected launch."));

        if (!VALID_RENDER_MODES.Contains(request.RenderMode))
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, $"Unsupported render mode '{request.RenderMode}'."));

        // ExportBlend builds the .blend host-side: no farm group, resolution, or frame range applies.
        if (request.RenderMode != EXPORT_BLEND_MODE)
        {
            var hasGroup = !string.IsNullOrWhiteSpace(request.SelectedGroupName);
            var hasProject = !string.IsNullOrWhiteSpace(request.SelectedProjectName);

            if (hasGroup && hasProject)
                diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "A launch may target a project or a group, not both."));

            if (!request.UseAllClients && !hasGroup && !hasProject)
                diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "Select a project or an execution group (or enable 'Run on all clients') before connected launch."));

            if (request.ResolutionX <= 0 || request.ResolutionY <= 0)
                diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "Render resolution must be greater than zero."));

            if (request.FrameStart <= 0 || request.FrameEnd < request.FrameStart)
                diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "Frame range is invalid for connected launch."));

            if ((request.RenderMode == "RenderStill" || request.RenderMode == "RenderStillTiled") && request.FrameEnd != request.FrameStart)
                diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Warning, "Still render modes usually expect a single frame. The current frame range will be reduced later unless changed."));

            if (request.RenderMode == "RenderVideo" && request.FrameEnd == request.FrameStart)
                diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Warning, "Video launch usually expects more than one frame."));
        }

        var hasErrors = diagnostics.Any(me => me.Severity == MaxSceneDiagnosticSeverity.Error);

        if (!hasErrors)
        {
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, "Connected render preflight passed locally. The next step is remote OmnibusCloud submission."));
        }

        return new MaxConnectedRenderPreflightResult
        {
            CanLaunch = !hasErrors,
            StatusText = hasErrors
                ? "Connected render preflight failed. Review the diagnostics before launch."
                : "Connected render preflight passed locally.",
            Diagnostics = diagnostics
        };
    }

    private static MaxSceneDiagnosticItem CreateDiagnostic(MaxSceneDiagnosticSeverity severity, string message)
    {
        return new MaxSceneDiagnosticItem
        {
            Severity = severity,
            Message = message
        };
    }

    #endregion
}
