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

    public int AnimatedChannelsCount { get; set; }

    public int FrameStart { get; set; } = 1;

    public int FrameEnd { get; set; } = 1;

    public int FrameRate { get; set; } = 30;

    public int RenderWidth { get; set; } = 1920;

    public int RenderHeight { get; set; } = 1080;

    // Scene environment/background colour (3ds Max GetBackGround). Null when the scene uses the
    // default black environment, so the neutral payload keeps an empty world (unchanged renders).
    public MaxSceneColorSnapshotData? EnvironmentColor { get; set; }

    #endregion
}
