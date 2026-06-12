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
            return ConvertMatrixToTransform(global, activeView.AffineTM);
        }
        catch
        {
            return null;
        }
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

        return new MaxSceneTransformSnapshotData
        {
            Translation = new MaxSceneVector3SnapshotData { X = translation.X, Y = translation.Y, Z = translation.Z },
            Rotation = new MaxSceneQuaternionSnapshotData { X = rotation.X, Y = rotation.Y, Z = rotation.Z, W = rotation.W },
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
