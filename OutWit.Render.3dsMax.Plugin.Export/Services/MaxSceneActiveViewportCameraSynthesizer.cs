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

    // The active viewport camera is kept only when its forward points at the geometry within this
    // cosine. In headless 3dsmaxbatch the restored "PerspUser" view often faces away from the
    // scene (camera looking down the -Z axis at geometry near origin) — there we frame the scene
    // instead so the render is not black.
    private const double MIN_FRAMING_DOT = 0.25d;

    // Frame the scene's bounding sphere with this much head-room.
    private const double FRAMING_MARGIN = 1.2d;

    private const double MIN_FRAMING_RADIUS = 1d;

    #endregion

    #region Functions

    public static void Apply(MaxSceneSummaryData summary)
    {
        if (!CanSynthesize(summary))
            return;

        var syntheticCameraName = string.IsNullOrWhiteSpace(summary.ActiveRenderCameraName)
            ? SYNTHETIC_CAMERA_NAME
            : summary.ActiveRenderCameraName;

        // The captured viewport FOV is HORIZONTAL (Max convention); the neutral camera stores
        // vertical. Convert with the scene's render aspect.
        var horizontalFovDegrees = summary.ActiveViewportVerticalFovDegrees > 0d ? summary.ActiveViewportVerticalFovDegrees : 45d;
        var verticalFovDegrees = horizontalFovDegrees;
        if (summary.RenderWidth > 0 && summary.RenderHeight > 0 && horizontalFovDegrees < 180d)
        {
            var halfHorizontalRadians = horizontalFovDegrees * Math.PI / 360d;
            verticalFovDegrees = 2d * Math.Atan(Math.Tan(halfHorizontalRadians) * summary.RenderHeight / summary.RenderWidth) * 180d / Math.PI;
        }
        var cameraTransform = ResolveCameraTransform(summary, verticalFovDegrees);

        summary.Cameras.Add(new MaxSceneCameraSnapshotData
        {
            Id = SYNTHETIC_CAMERA_ID,
            Name = syntheticCameraName,
            VerticalFovDegrees = verticalFovDegrees,
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
            LocalTransform = cameraTransform,
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

    private static MaxSceneTransformSnapshotData ResolveCameraTransform(MaxSceneSummaryData summary, double verticalFovDegrees)
    {
        var bounds = MaxSceneBounds.Compute(summary);

        // No geometry to frame, or the viewport already looks at it: keep the viewport view
        // (respects the user's framing in the interactive plugin flow).
        if (bounds == null || ViewportFramesGeometry(summary.ActiveViewportTransform!, bounds.Value))
            return CloneTransform(summary.ActiveViewportTransform!);

        return BuildFramingTransform(bounds.Value, verticalFovDegrees);
    }

    private static bool ViewportFramesGeometry(MaxSceneTransformSnapshotData transform, MaxSceneBounds bounds)
    {
        var cameraPosition = (transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
        var toCenter = (bounds.CenterX - cameraPosition.Item1, bounds.CenterY - cameraPosition.Item2, bounds.CenterZ - cameraPosition.Item3);

        if (Math.Sqrt(toCenter.Item1 * toCenter.Item1 + toCenter.Item2 * toCenter.Item2 + toCenter.Item3 * toCenter.Item3) <= double.Epsilon)
            return false;

        var forward = MaxCameraMath.ComputeGeneratorForward((transform.Rotation.W, transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z));
        return MaxCameraMath.Dot(forward, MaxCameraMath.Normalize(toCenter)) >= MIN_FRAMING_DOT;
    }

    private static MaxSceneTransformSnapshotData BuildFramingTransform(MaxSceneBounds bounds, double verticalFovDegrees)
    {
        var radius = Math.Max(bounds.Radius, MIN_FRAMING_RADIUS);
        var halfFovRadians = verticalFovDegrees * Math.PI / 360d;
        var distance = radius / Math.Max(Math.Sin(halfFovRadians), 0.01d) * FRAMING_MARGIN;

        // A three-quarter view in the scene's Z-up space: to the right (+X), in front (-Y), above (+Z).
        var offsetDirection = MaxCameraMath.Normalize((1d, -1d, 0.8d));
        var cameraPosition = (
            bounds.CenterX + offsetDirection.X * distance,
            bounds.CenterY + offsetDirection.Y * distance,
            bounds.CenterZ + offsetDirection.Z * distance);

        var forward = MaxCameraMath.Normalize((
            bounds.CenterX - cameraPosition.Item1,
            bounds.CenterY - cameraPosition.Item2,
            bounds.CenterZ - cameraPosition.Item3));

        var rotation = MaxCameraMath.BuildLookAtNodeRotation(forward, (0d, 0d, 1d));

        return new MaxSceneTransformSnapshotData
        {
            Translation = new MaxSceneVector3SnapshotData { X = cameraPosition.Item1, Y = cameraPosition.Item2, Z = cameraPosition.Item3 },
            Rotation = new MaxSceneQuaternionSnapshotData { W = rotation.W, X = rotation.X, Y = rotation.Y, Z = rotation.Z },
            Scale = new MaxSceneVector3SnapshotData { X = 1d, Y = 1d, Z = 1d }
        };
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
