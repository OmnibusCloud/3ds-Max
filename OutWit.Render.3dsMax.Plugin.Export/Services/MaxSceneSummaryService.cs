using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Normalizes raw host-side snapshot data into the UI-facing scene-summary model.
/// </summary>
public sealed class MaxSceneSummaryService
{
    #region Fields

    private readonly IMaxSceneSnapshotProvider m_sceneSnapshotProvider;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new summary service over the provided host-scene snapshot provider.
    /// </summary>
    /// <param name="sceneSnapshotProvider">The host-scene snapshot provider.</param>
    public MaxSceneSummaryService(IMaxSceneSnapshotProvider sceneSnapshotProvider)
    {
        m_sceneSnapshotProvider = sceneSnapshotProvider;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Collects and normalizes the current 3ds Max scene summary.
    /// </summary>
    public MaxSceneSummaryData Collect()
    {
        var snapshot = m_sceneSnapshotProvider.Capture();
        var frameStart = snapshot.FrameStart <= 0 ? 1 : snapshot.FrameStart;
        var frameEnd = snapshot.FrameEnd < frameStart ? frameStart : snapshot.FrameEnd;
        var renderWidth = snapshot.RenderWidth <= 0 ? 1920 : snapshot.RenderWidth;
        var renderHeight = snapshot.RenderHeight <= 0 ? 1080 : snapshot.RenderHeight;
        var frameRate = snapshot.FrameRate <= 0 ? 30 : snapshot.FrameRate;

        var summary = new MaxSceneSummaryData
        {
            SceneName = string.IsNullOrWhiteSpace(snapshot.SceneName) ? "3ds Max Scene" : snapshot.SceneName,
            SceneFilePath = snapshot.SceneFilePath,
            SourceApplicationLabel = string.IsNullOrWhiteSpace(snapshot.SourceApplicationLabel) ? "3ds Max 2027" : snapshot.SourceApplicationLabel,
            SourceApplicationVersion = snapshot.SourceApplicationVersion,
            ActiveRenderCameraName = snapshot.ActiveRenderCameraName,
            HasActiveViewportRenderFallbackCandidate = snapshot.HasActiveViewportRenderFallbackCandidate,
            ActiveViewportType = snapshot.ActiveViewportType,
            ActiveViewportIsPerspective = snapshot.ActiveViewportIsPerspective,
            ActiveViewportVerticalFovDegrees = snapshot.ActiveViewportVerticalFovDegrees,
            ActiveViewportTransform = snapshot.ActiveViewportTransform,
            CameraNames = [.. snapshot.CameraNames],
            LightNames = [.. snapshot.LightNames],
            MaterialNames = [.. snapshot.MaterialNames],
            TextureNames = [.. snapshot.TextureNames],
            Nodes = [.. snapshot.Nodes],
            Meshes = [.. snapshot.Meshes],
            Cameras = [.. snapshot.Cameras],
            Lights = [.. snapshot.Lights],
            Materials = [.. snapshot.Materials],
            ImageAssets = [.. snapshot.ImageAssets],
            NodesCount = snapshot.NodesCount,
            MeshesCount = snapshot.MeshesCount,
            MaterialsCount = snapshot.MaterialsCount,
            TexturesCount = snapshot.TexturesCount,
            CamerasCount = snapshot.CamerasCount,
            LightsCount = snapshot.LightsCount,
            SkippedEmptyMeshCount = snapshot.SkippedEmptyMeshCount,
            SkippedInactiveLightCount = snapshot.SkippedInactiveLightCount,
            AnimatedChannelsCount = snapshot.AnimatedChannelsCount,
            FrameStart = frameStart,
            FrameEnd = frameEnd,
            FrameRate = frameRate,
            RenderWidth = renderWidth,
            RenderHeight = renderHeight,
            EnvironmentColor = snapshot.EnvironmentColor,
            EnvironmentImageId = snapshot.EnvironmentImageId,
            EnvironmentRotationDegrees = snapshot.EnvironmentRotationDegrees,
            MotionBlur = snapshot.MotionBlur,
            MotionBlurShutter = snapshot.MotionBlurShutter,
            UsesScanlineRenderer = snapshot.UsesScanlineRenderer
        };

        MaxSceneActiveViewportCameraSynthesizer.Apply(summary);
        MaxSceneDefaultLightSynthesizer.Apply(summary);
        return summary;
    }

    #endregion
}
