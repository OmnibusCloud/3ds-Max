using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

public sealed class MaxSceneSummaryData
{
    #region Properties

    public string SceneName { get; set; } = string.Empty;

    public string SceneFilePath { get; set; } = string.Empty;

    public string SourceApplicationLabel { get; set; } = string.Empty;

    public string SourceApplicationVersion { get; set; } = string.Empty;

    public string ActiveRenderCameraName { get; set; } = string.Empty;

    public bool HasActiveViewportRenderFallbackCandidate { get; set; }

    public string ActiveViewportType { get; set; } = string.Empty;

    public bool ActiveViewportIsPerspective { get; set; }

    public double ActiveViewportVerticalFovDegrees { get; set; } = 45d;

    public MaxSceneTransformSnapshotData? ActiveViewportTransform { get; set; }

    public bool UsesSyntheticViewportCamera { get; set; }

    public bool UsesSyntheticDefaultLights { get; set; }

    public List<string> CameraNames { get; set; } = [];

    public List<string> LightNames { get; set; } = [];

    public List<string> MaterialNames { get; set; } = [];

    public List<string> TextureNames { get; set; } = [];

    public List<MaxSceneNodeSnapshotData> Nodes { get; set; } = [];

    public List<MaxSceneMeshSnapshotData> Meshes { get; set; } = [];

    public List<MaxSceneCameraSnapshotData> Cameras { get; set; } = [];

    public List<MaxSceneLightSnapshotData> Lights { get; set; } = [];

    public List<MaxSceneMaterialSnapshotData> Materials { get; set; } = [];

    public List<MaxSceneImageAssetSnapshotData> ImageAssets { get; set; } = [];

    public int NodesCount { get; set; }

    public int MeshesCount { get; set; }

    public int MaterialsCount { get; set; }

    public int TexturesCount { get; set; }

    public int CamerasCount { get; set; }

    public int LightsCount { get; set; }

    // Non-renderable content the collector deliberately dropped so it never aborts the server-side
    // build (an empty mesh / a light with no positive intensity both fail Dcc validation).
    public int SkippedEmptyMeshCount { get; set; }

    public int SkippedInactiveLightCount { get; set; }

    public int AnimatedChannelsCount { get; set; }

    public int FrameStart { get; set; } = 1;

    public int FrameEnd { get; set; } = 1;

    public int FrameRate { get; set; } = 30;

    public int RenderWidth { get; set; } = 1920;

    public int RenderHeight { get; set; } = 1080;

    // Scene environment/background colour (3ds Max GetBackGround). Null when the scene uses the
    // default black environment, so the neutral payload keeps an empty world (unchanged renders).
    public MaxSceneColorSnapshotData? EnvironmentColor { get; set; }

    // Id of the environment HDRI image asset (added to ImageAssets) when the scene environment map is
    // a bitmap. Null/empty when there is no environment image (the constant colour world is used).
    public string? EnvironmentImageId { get; set; }

    // Z-axis rotation (degrees) for the environment image. Ignored when EnvironmentImageId is unset.
    public double EnvironmentRotationDegrees { get; set; }

    // True when the environment map uses Environ/Screen coordinates — a 2D backdrop stretched
    // across the render window rather than a panorama. Ignored when EnvironmentImageId is unset.
    public bool EnvironmentIsScreenMapped { get; set; }

    // True when the environment IS the scene's authored light source (a V-Ray dome light routed
    // into the world). Suppresses the default three-point rig a light-less scene would get.
    public bool EnvironmentIsLightSource { get; set; }

    // Third-party plugin classes ("kind:ClassName" → occurrence count) exported through the
    // minimal safe path — surfaced to the user as scene-profile diagnostics.
    public Dictionary<string, int> UnmappedPluginClasses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Whether any renderable node has 3ds Max motion blur enabled (drives scene-level motion blur).
    public bool MotionBlur { get; set; }

    public int ImageMotionBlurObjectCount { get; set; }

    public int ObjectMotionBlurObjectCount { get; set; }

    public double? ExposureControlEv { get; set; }

    // Motion-blur shutter (fraction of a frame). Only applied when MotionBlur is true.
    public double MotionBlurShutter { get; set; } = 0.5d;

    // True when the scene's production renderer is the Default Scanline renderer. Scanline
    // renders straight clamped sRGB (no filmic tone mapping) — the mapper picks the matching
    // Blender view transform from this.
    public bool UsesScanlineRenderer { get; set; }

    #endregion
}
