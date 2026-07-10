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
    public MaxSceneSnapshotData Capture(MaxSceneCaptureOptions captureOptions)
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
            FrameStart = ResolveFrameStart(global, coreInterface),
            FrameEnd = ResolveFrameEnd(global, coreInterface),
            FrameRate = ResolveFrameRate(global),
            RenderWidth = coreInterface.RendWidth,
            RenderHeight = coreInterface.RendHeight,
            ActiveRenderCameraName = ResolveActiveRenderCameraName(renderCameraNode, activeView),
            HasActiveViewportRenderFallbackCandidate = renderCameraNode is null && activeView?.IsPerspView == true,
            ActiveViewportType = activeView?.ViewType.ToString() ?? string.Empty,
            ActiveViewportIsPerspective = activeView?.IsPerspView == true,
            ActiveViewportVerticalFovDegrees = NormalizeViewportFovDegrees(activeView?.Fov),
            ActiveViewportTransform = ResolveActiveViewportTransform(global, activeView),
            ExposureControlEv = TryReadExposureControlEv(global)
        };
        (snapshot.ImageMotionBlurObjectCount, snapshot.ObjectMotionBlurObjectCount) = CountMotionBlurKinds(global);

        // The FULL capture of a heavy scene runs for minutes synchronously on the Max main
        // thread; without feedback the whole application reads as hung. Max's native progress
        // dialog is the SDK-safe way to stay visibly alive (it pumps messages itself) — shown
        // only for interactive full captures; the SummaryOnly profile finishes in milliseconds.
        var showNativeProgress = !captureOptions.SkipGeometryData && !SafeGetQuietMode(coreInterface);
        if (showNativeProgress)
            TryProgressStart(coreInterface, "OmnibusCloud: preparing scene…");

        try
        {
            var collector = new MaxSceneSnapshotCollector(global, coreInterface, snapshot, captureOptions, showNativeProgress);
            collector.Collect(rootNode);
        }
        finally
        {
            if (showNativeProgress)
                TryProgressEnd(coreInterface);
        }

        return snapshot;
    }

    private static bool SafeGetQuietMode(IInterface coreInterface)
    {
        try
        {
            return coreInterface.GetQuietMode(true);
        }
        catch
        {
            return true;
        }
    }

    private static void TryProgressStart(IInterface coreInterface, string title)
    {
        try
        {
            coreInterface.ProgressStart(title, true);
        }
        catch
        {
            // No progress dialog is a cosmetic loss only.
        }
    }

    private static void TryProgressEnd(IInterface coreInterface)
    {
        try
        {
            coreInterface.ProgressEnd();
        }
        catch
        {
        }
    }

    private static string ResolveSceneName(IInterface coreInterface)
    {
        if (!string.IsNullOrWhiteSpace(coreInterface.CurFileName))
            return Path.GetFileNameWithoutExtension(coreInterface.CurFileName);

        return "3ds Max Scene";
    }

    private static (int ImageCount, int ObjectCount) CountMotionBlurKinds(IGlobal global)
    {
        // 3ds Max image motion blur is a post smear over a sharp frame, object blur is a real
        // shutter integration — the generator emulates them differently. The facade's per-node
        // MotBlur accessor is unreliable, so count both kinds once via MAXScript
        // (encoded image*10000 + object in a single float return).
        try
        {
            var result = global.FPValue.Create();
            const string script =
                "(local i = 0; local o = 0; for obj in objects do (try (if obj.motionBlurOn then (if obj.motionBlur == #image then i += 1 else if obj.motionBlur == #object then o += 1)) catch ()); (i * 10000 + o) as float)";
            if (!global.ExecuteMAXScriptScript(script, Autodesk.Max.MAXScript.ScriptSource.NonEmbedded, true, result, false))
                return (0, 0);

            var encoded = result.Type switch
            {
                ParamType2.Float => (double)result.F,
                ParamType2.Double => result.Dbl,
                ParamType2.Int => result.I,
                _ => 0d
            };

            var total = (int)Math.Round(encoded);
            return (total / 10000, total % 10000);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static double? TryReadExposureControlEv(IGlobal global)
    {
        // The active Exposure Control hangs off a native-only static interface, so evaluate it
        // via MAXScript. This is the artist-facing knob for darkening our renders from inside
        // Max: raise the Physical Exposure Control's global EV and the exported exposure follows.
        try
        {
            var result = global.FPValue.Create();
            const string script =
                "(try (if sceneexposurecontrol.exposureControl != undefined and (isProperty sceneexposurecontrol.exposureControl #ev) then (sceneexposurecontrol.exposureControl.ev as float) else -10000.0) catch (-10000.0))";
            if (!global.ExecuteMAXScriptScript(script, Autodesk.Max.MAXScript.ScriptSource.NonEmbedded, true, result, false))
                return null;

            double value = result.Type switch
            {
                ParamType2.Float => result.F,
                ParamType2.Double => result.Dbl,
                ParamType2.Int => result.I,
                _ => -10000d
            };

            return value <= -9999d ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static int ResolveFrameStart(IGlobal global, IInterface coreInterface)
    {
        return ResolveFrameBoundary(global, coreInterface, "Start", 1);
    }

    private static int ResolveFrameRate(IGlobal global)
    {
        // IGlobal exposes the scene frame rate as a typed property. Reflection for a GetFrameRate
        // METHOD never matched it, so every export silently pinned to the 30 fps fallback — a 25 fps
        // scene got its whole timeline resampled by 30/25 and stills stopped matching Max frames.
        try
        {
            var frameRate = global.FrameRate;
            if (frameRate > 0)
                return frameRate;
        }
        catch
        {
        }

        return 30;
    }

    private static int ResolveFrameEnd(IGlobal global, IInterface coreInterface)
    {
        var frameStart = ResolveFrameStart(global, coreInterface);
        return ResolveFrameBoundary(global, coreInterface, "End", frameStart);
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

    private static int ResolveFrameBoundary(IGlobal global, IInterface coreInterface, string propertyName, int onError)
    {
        try
        {
            var animRange = coreInterface.AnimRange;
            var rangeType = animRange.GetType();
            var boundaryValue = rangeType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(animRange);

            if (boundaryValue is null)
                return onError;

            var ticksPerFrame = ResolveTicksPerFrame(global);
            var ticks = Convert.ToInt32(boundaryValue);

            if (ticksPerFrame <= 0)
                return onError;

            // Keep Max's own frame numbering (frame = ticks / tpf, no shift): a still requested at
            // frame N must sample the same instant Max renders at frame N, or side-by-side
            // comparisons drift by a frame.
            return ticks / ticksPerFrame;
        }
        catch
        {
            return onError;
        }
    }

    private static int ResolveTicksPerFrame(IGlobal global)
    {
        // Typed property, same story as FrameRate — the old reflective GetTicksPerFrame lookup
        // always missed and returned the 30 fps constant (160), shifting every sampled timeline.
        try
        {
            var ticksPerFrame = global.TicksPerFrame;
            if (ticksPerFrame > 0)
                return ticksPerFrame;
        }
        catch
        {
        }

        return 4800 / Math.Max(ResolveFrameRate(global), 1);
    }

    #endregion
}
