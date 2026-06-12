using OutWit.Controller.Render.Dcc.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

internal static class MaxSceneActiveViewportCameraSynthesizer
{
    #region Constants

    private const string SYNTHETIC_CAMERA_ID = "camera:active-viewport";

    private const string SYNTHETIC_CAMERA_NODE_ID = "node:active-viewport-camera";

    private const string SYNTHETIC_CAMERA_NAME = "ActiveViewportCamera";

    #endregion

    #region Functions

    public static void Apply(MaxSceneSummaryData summary)
    {
        if (!CanSynthesize(summary))
            return;

        var syntheticCameraName = string.IsNullOrWhiteSpace(summary.ActiveRenderCameraName)
            ? SYNTHETIC_CAMERA_NAME
            : summary.ActiveRenderCameraName;

        summary.Cameras.Add(new MaxSceneCameraSnapshotData
        {
            Id = SYNTHETIC_CAMERA_ID,
            Name = syntheticCameraName,
            VerticalFovDegrees = summary.ActiveViewportVerticalFovDegrees > 0d ? summary.ActiveViewportVerticalFovDegrees : 45d,
            NearClip = 0.1d,
            FarClip = 1000d,
            IsPerspective = true
        });

        summary.Nodes.Add(new MaxSceneNodeSnapshotData
        {
            Id = SYNTHETIC_CAMERA_NODE_ID,
            Name = syntheticCameraName,
            Kind = DccNodeKind.Camera,
            CameraId = SYNTHETIC_CAMERA_ID,
            LocalTransform = CloneTransform(summary.ActiveViewportTransform!),
            Visible = true,
            Renderable = true
        });

        if (!summary.CameraNames.Contains(syntheticCameraName, StringComparer.OrdinalIgnoreCase))
            summary.CameraNames.Add(syntheticCameraName);

        summary.ActiveRenderCameraName = syntheticCameraName;
        summary.CamerasCount = summary.Cameras.Count;
        summary.NodesCount = summary.Nodes.Count;
        summary.UsesSyntheticViewportCamera = true;
    }

    private static bool CanSynthesize(MaxSceneSummaryData summary)
    {
        return summary.HasActiveViewportRenderFallbackCandidate
               && summary.ActiveViewportIsPerspective
               && summary.ActiveViewportTransform != null
               && summary.Cameras.Count == 0
               && !summary.Nodes.Any(me => me.Kind == DccNodeKind.Camera);
    }

    private static MaxSceneTransformSnapshotData CloneTransform(MaxSceneTransformSnapshotData transform)
    {
        return new MaxSceneTransformSnapshotData
        {
            Translation = new MaxSceneVector3SnapshotData
            {
                X = transform.Translation.X,
                Y = transform.Translation.Y,
                Z = transform.Translation.Z
            },
            Rotation = new MaxSceneQuaternionSnapshotData
            {
                X = transform.Rotation.X,
                Y = transform.Rotation.Y,
                Z = transform.Rotation.Z,
                W = transform.Rotation.W
            },
            Scale = new MaxSceneVector3SnapshotData
            {
                X = transform.Scale.X,
                Y = transform.Scale.Y,
                Z = transform.Scale.Z
            }
        };
    }

    #endregion
}
