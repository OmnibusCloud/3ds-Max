using Autodesk.Max;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;
using System.IO;
using System.Reflection;

namespace OutWit.Render.ThreeDsMax.Plugin.Services;

/// <summary>
/// Thin 3ds Max host boundary that captures scene metadata and delegates detailed extraction to the snapshot collector.
/// </summary>
public sealed class MaxHostApplicationService : IMaxSceneSnapshotProvider
{
    #region Functions

    /// <summary>
    /// Captures the current 3ds Max scene into the export snapshot model.
    /// </summary>
    public MaxSceneSnapshotData Capture()
    {
        var global = GlobalInterface.Instance;
        var coreInterface = global.COREInterface;
        var rootNode = coreInterface.RootNode;
        var renderCameraNode = ResolveRenderCameraNode(coreInterface);
        var activeView = ResolveActiveView(coreInterface);
        var snapshot = new MaxSceneSnapshotData
        {
            SceneName = ResolveSceneName(coreInterface),
            SceneFilePath = coreInterface.CurFilePath ?? string.Empty,
            SourceApplicationLabel = "3ds Max 2027",
            SourceApplicationVersion = "2027",
            FrameStart = ResolveFrameStart(coreInterface),
            FrameEnd = ResolveFrameEnd(coreInterface),
            FrameRate = ResolveFrameRate(global),
            RenderWidth = coreInterface.RendWidth,
            RenderHeight = coreInterface.RendHeight,
            ActiveRenderCameraName = ResolveActiveRenderCameraName(renderCameraNode, activeView),
            HasActiveViewportRenderFallbackCandidate = renderCameraNode is null && activeView?.IsPerspView == true,
            ActiveViewportType = activeView?.ViewType.ToString() ?? string.Empty,
            ActiveViewportIsPerspective = activeView?.IsPerspView == true,
            ActiveViewportVerticalFovDegrees = NormalizeViewportFovDegrees(activeView?.Fov),
            ActiveViewportTransform = ResolveActiveViewportTransform(global, activeView)
        };

        var collector = new MaxSceneSnapshotCollector(global, coreInterface, snapshot);
        collector.Collect(rootNode);
        return snapshot;
    }

    private static string ResolveSceneName(IInterface coreInterface)
    {
        if (!string.IsNullOrWhiteSpace(coreInterface.CurFileName))
            return Path.GetFileNameWithoutExtension(coreInterface.CurFileName);

        return "3ds Max Scene";
    }

    private static int ResolveFrameStart(IInterface coreInterface)
    {
        return ResolveFrameBoundary(coreInterface, "Start", 1);
    }

    private static int ResolveFrameRate(IGlobal global)
    {
        // Max's frame rate is the global GetFrameRate(); the typed managed surface does not expose
        // it, so resolve it reflectively (the same pattern this service uses for other gaps) and
        // fall back to 30 — Max's default — never Blender's 24, which would mistime video output.
        try
        {
            var frameRate = global.GetType().GetMethod("GetFrameRate", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)?.Invoke(global, null);
            if (frameRate is int value && value > 0)
                return value;
        }
        catch
        {
        }

        return 30;
    }

    private static int ResolveFrameEnd(IInterface coreInterface)
    {
        var frameStart = ResolveFrameStart(coreInterface);
        return ResolveFrameBoundary(coreInterface, "End", frameStart);
    }

    private static IINode? ResolveRenderCameraNode(IInterface coreInterface)
    {
        try
        {
            var renderCameraNode = coreInterface.GetType().GetProperty("RendCamNode", BindingFlags.Instance | BindingFlags.Public)?.GetValue(coreInterface);

            if (renderCameraNode is IINode renderCamera)
                return renderCamera;
        }
        catch
        {
        }

        return null;
    }

    private static IViewExp? ResolveActiveView(IInterface coreInterface)
    {
        try
        {
            return coreInterface.GetType().GetProperty("ActiveViewExp", BindingFlags.Instance | BindingFlags.Public)?.GetValue(coreInterface) as IViewExp;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveActiveRenderCameraName(IINode? renderCameraNode, IViewExp? activeView)
    {
        if (renderCameraNode != null)
            return renderCameraNode.Name;

        try
        {
            if (activeView?.ViewCamera is IINode activeViewCamera)
                return activeViewCamera.Name;
        }
        catch
        {
        }

        return string.Empty;
    }

    private static double NormalizeViewportFovDegrees(float? rawFov)
    {
        if (!rawFov.HasValue)
            return 45d;

        var fov = Convert.ToDouble(rawFov.Value);
        if (fov <= 0d)
            return 45d;

        return fov <= (Math.PI * 2d) + 0.001d
            ? fov * 180d / Math.PI
            : fov;
    }

    private static MaxSceneTransformSnapshotData? ResolveActiveViewportTransform(IGlobal global, IViewExp? activeView)
    {
        if (activeView?.AffineTM == null)
            return null;

        try
        {
            // AffineTM is the WORLD->VIEW matrix; the camera's world placement is its inverse.
            // Feeding the raw matrix (the original behaviour) produced a garbage camera transform,
            // so the synthesizer's "does the viewport face the geometry" test always failed and
            // camera-less scenes rendered from the framing fallback instead of the authored view.
            var cameraTm = global.Inverse(activeView.AffineTM);
            var transform = ConvertMatrixToTransform(global, cameraTm);

            // The synthesized camera flows through the generator's camera path (rotation @
            // RotX(-90 deg)); compose the cancelling RotX(+90 deg) so the view survives exactly —
            // same convention the collector applies to real camera nodes.
            transform.Rotation = ComposeRotXPlus90(transform.Rotation);
            return transform;
        }
        catch
        {
            return null;
        }
    }

    private static MaxSceneQuaternionSnapshotData ComposeRotXPlus90(MaxSceneQuaternionSnapshotData q)
    {
        const double s = 0.70710678118654752d;
        return new MaxSceneQuaternionSnapshotData
        {
            X = (q.W + q.X) * s,
            Y = (q.Y + q.Z) * s,
            Z = (q.Z - q.Y) * s,
            W = (q.W - q.X) * s
        };
    }

    private static MaxSceneTransformSnapshotData ConvertMatrixToTransform(IGlobal global, IMatrix3 matrix)
    {
        var translation = matrix.Trans;
        var row0 = matrix.GetRow(0);
        var row1 = matrix.GetRow(1);
        var row2 = matrix.GetRow(2);
        var scaleX = global.Length(row0);
        var scaleY = global.Length(row1);
        var scaleZ = global.Length(row2);
        var rotationMatrix = global.Matrix3.Create(true);

        rotationMatrix.SetRow(0, NormalizeVector(global, row0, scaleX));
        rotationMatrix.SetRow(1, NormalizeVector(global, row1, scaleY));
        rotationMatrix.SetRow(2, NormalizeVector(global, row2, scaleZ));
        rotationMatrix.SetTrans(0, 0f);
        rotationMatrix.SetTrans(1, 0f);
        rotationMatrix.SetTrans(2, 0f);

        var rotation = global.Quat.Create(rotationMatrix);

        // Quat.Create returns 3ds Max's row-vector-convention quaternion — the conjugate of the
        // Hamilton rotation downstream consumers apply (same fix as the collector's decomposition).
        return new MaxSceneTransformSnapshotData
        {
            Translation = new MaxSceneVector3SnapshotData { X = translation.X, Y = translation.Y, Z = translation.Z },
            Rotation = new MaxSceneQuaternionSnapshotData { X = -rotation.X, Y = -rotation.Y, Z = -rotation.Z, W = rotation.W },
            Scale = new MaxSceneVector3SnapshotData
            {
                X = scaleX <= 0d ? 1d : scaleX,
                Y = scaleY <= 0d ? 1d : scaleY,
                Z = scaleZ <= 0d ? 1d : scaleZ
            }
        };
    }

    private static IPoint3 NormalizeVector(IGlobal global, IPoint3 vector, double length)
    {
        if (length <= 0d)
            return global.Point3.Create(0d, 0d, 0d);

        return global.Point3.Create(vector.X / length, vector.Y / length, vector.Z / length);
    }

    private static int ResolveFrameBoundary(IInterface coreInterface, string propertyName, int onError)
    {
        try
        {
            var animRange = coreInterface.AnimRange;
            var rangeType = animRange.GetType();
            var boundaryValue = rangeType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(animRange);

            if (boundaryValue is null)
                return onError;

            var ticksPerFrame = ResolveTicksPerFrame(coreInterface);
            var ticks = Convert.ToInt32(boundaryValue);

            if (ticksPerFrame <= 0)
                return onError;

            return (ticks / ticksPerFrame) + 1;
        }
        catch
        {
            return onError;
        }
    }

    private static int ResolveTicksPerFrame(IInterface coreInterface)
    {
        try
        {
            var interfaceType = coreInterface.GetType();
            var directMethod = interfaceType.GetMethod("GetTicksPerFrame", BindingFlags.Instance | BindingFlags.Public);

            if (directMethod?.Invoke(coreInterface, null) is int ticksPerFrame)
                return ticksPerFrame;

            if (directMethod?.Invoke(coreInterface, null) is short ticksPerFrameShort)
                return ticksPerFrameShort;

            var staticMethod = interfaceType.Assembly.GetType("Autodesk.Max.GlobalInterface")?.GetMethod("GetTicksPerFrame", BindingFlags.Static | BindingFlags.Public);

            if (staticMethod?.Invoke(null, null) is int staticTicksPerFrame)
                return staticTicksPerFrame;
        }
        catch
        {
        }

        return 160;
    }

    #endregion
}
