using System.Numerics;
using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;

/// <summary>
/// Aims the render camera at the scene so the subject is actually in frame.
///
/// 3ds Max camera orientation does not survive the neutral-DCC round trip reliably (target vs free
/// cameras decompose to inconsistent quaternions, and the headless viewport camera has no transform),
/// which made most scenes render the camera pointed at empty space. Rather than trust the captured
/// orientation, this recomputes the render camera to look at the scene's geometric centre and fits the
/// vertical FOV to the bounds — deterministic framing that guarantees the subject is visible.
///
/// The captured-quaternion convention the Blender generator expects (it applies
/// <c>rotation @ RotX(-90deg)</c> and Blender looks down local -Z) is: the quaternion maps local
/// <b>-Y → forward</b> and local <b>+Z → up</b>. The look-at below builds exactly that basis.
/// </summary>
internal static class MaxSceneCameraFramer
{
    #region Constants

    // Fraction of the bounds radius kept as margin around the subject when fitting the FOV.
    private const double FRAMING_MARGIN = 1.25d;

    // A camera closer than this (scene units) to the bounds centre is treated as degenerate (e.g. a
    // headless synthetic viewport camera) and repositioned to a 3/4 view.
    private const double MIN_USABLE_CAMERA_DISTANCE = 1d;

    private const double MIN_VERTICAL_FOV_DEGREES = 10d;

    private const double MAX_VERTICAL_FOV_DEGREES = 120d;

    // A mesh whose local extent is more than this multiple of the median is treated as a wide-spread
    // outlier (particle system, skybox) and excluded from the framing bounds.
    private const double OUTLIER_RADIUS_FACTOR = 4d;

    // An animated authored camera is kept when its forward is within ~75° of the direction to the
    // scene centre (a generous cone — it only has to be pointed the right general way, since it may
    // track a subject that has moved off the rest-pose centre).
    private const float AIMED_AT_SUBJECT_DOT = 0.25f;

    #endregion

    #region Functions

    /// <summary>
    /// Reorders the active render camera first (so the generator renders through it) and re-aims it at
    /// the scene bounds. No-op when the scene has no renderable geometry or no camera.
    /// </summary>
    public static void Apply(DccSceneData scene, string activeRenderCameraName)
    {
        var cameraNodes = scene.Nodes.Where(me => me.Kind == DccNodeKind.Camera).ToList();
        if (cameraNodes.Count == 0)
            return;

        if (!TryComputeSceneBounds(scene, out var center, out var radius))
            return;

        var renderCameraNode = ResolveRenderCameraNode(cameraNodes, activeRenderCameraName);

        // Make the chosen camera the first camera node — the generator renders through the first one.
        if (!ReferenceEquals(scene.Nodes[0], renderCameraNode))
        {
            scene.Nodes.Remove(renderCameraNode);
            scene.Nodes.Insert(0, renderCameraNode);
        }

        var camera = scene.Cameras.FirstOrDefault(me => me.Id == renderCameraNode.CameraId);
        if (camera == null)
            return;

        var eye = new Vector3(
            (float)renderCameraNode.LocalTransform.Translation.X,
            (float)renderCameraNode.LocalTransform.Translation.Y,
            (float)renderCameraNode.LocalTransform.Translation.Z);

        var toCentre = center - eye;
        var distanceToCentre = toCentre.Length();

        // Preserve the authored camera only for ANIMATED scenes: a crafted moving shot (an animated
        // camera, or a static camera framing an animated subject) intentionally tracks motion that a
        // single static auto-frame cannot follow, so overriding it would make it worse. A static scene
        // is always auto-framed (centres the subject) because its captured orientation is unreliable.
        // The guard still requires the camera to aim generally toward the geometry, so a genuinely
        // broken animated camera is not preserved.
        if (distanceToCentre >= (float)Math.Max(MIN_USABLE_CAMERA_DISTANCE, radius * 0.05d) && IsSceneAnimated(scene, renderCameraNode))
        {
            var forward = RotateCapturedForward(renderCameraNode.LocalTransform.Rotation);
            if (Vector3.Dot(Vector3.Normalize(forward), toCentre / distanceToCentre) >= AIMED_AT_SUBJECT_DOT)
                return;
        }

        // Keep the artist's viewpoint when it is a usable distance from the subject; otherwise (a
        // headless synthetic viewport camera, or a camera sitting on the subject) synthesize a 3/4 view.
        if (distanceToCentre < (float)Math.Max(MIN_USABLE_CAMERA_DISTANCE, radius * 0.05d))
            eye = center + Vector3.Normalize(new Vector3(0.6f, -1f, 0.5f)) * (float)(radius * 2.6d);

        var distance = (center - eye).Length();
        renderCameraNode.LocalTransform.Translation = new DccVector3Data { X = eye.X, Y = eye.Y, Z = eye.Z };
        renderCameraNode.LocalTransform.Rotation = LookAt(eye, center);
        renderCameraNode.TransformKeyframes.Clear();

        var halfFovRadians = Math.Atan(radius * FRAMING_MARGIN / Math.Max(distance, 1d));
        camera.VerticalFovDegrees = Math.Clamp(halfFovRadians * 2d * 180d / Math.PI, MIN_VERTICAL_FOV_DEGREES, MAX_VERTICAL_FOV_DEGREES);
        camera.VerticalFovKeyframes.Clear();

        // Keep the subject well within the clip range regardless of the fitted distance.
        camera.NearClip = Math.Max(distance * 0.01d, 0.01d);
        camera.FarClip = distance + radius * 4d + 10d;
        camera.NearClipKeyframes.Clear();
        camera.FarClipKeyframes.Clear();
    }

    private static bool IsSceneAnimated(DccSceneData scene, DccNodeData renderCameraNode)
    {
        if (renderCameraNode.TransformKeyframes.Count > 0)
            return true;

        // A renderable mesh with baked per-frame deformation means the subject moves; the authored
        // camera was framed around that motion.
        var renderableMeshIds = scene.Nodes
            .Where(me => me.Kind == DccNodeKind.Mesh && me.Renderable && me.MeshId != null)
            .Select(me => me.MeshId!)
            .ToHashSet(StringComparer.Ordinal);

        return scene.Meshes.Any(me => renderableMeshIds.Contains(me.Id) && me.DeformationFrames.Count > 0);
    }

    /// <summary>
    /// The world-space forward direction the generator will give this camera: it applies
    /// <c>Q @ RotX(-90°)</c> and Blender looks along local -Z, so the world forward is
    /// <c>Q · (RotX(-90°)·(0,0,-1))</c> = <c>Q · (0,-1,0)</c> — the captured quaternion applied to local -Y.
    /// </summary>
    private static Vector3 RotateCapturedForward(DccQuaternionData rotation)
    {
        var q = new Quaternion((float)rotation.X, (float)rotation.Y, (float)rotation.Z, (float)rotation.W);
        return Vector3.Transform(new Vector3(0f, -1f, 0f), q);
    }

    private static DccNodeData ResolveRenderCameraNode(List<DccNodeData> cameraNodes, string activeRenderCameraName)
    {
        if (!string.IsNullOrWhiteSpace(activeRenderCameraName))
        {
            var named = cameraNodes.FirstOrDefault(me => string.Equals(me.Name, activeRenderCameraName, StringComparison.Ordinal));
            if (named != null)
                return named;
        }

        return cameraNodes[0];
    }

    /// <summary>
    /// Axis-aligned bounding box of the scene's renderable mesh geometry. Framing the AABB of
    /// *renderable* meshes only (not bones/helpers, which are typically non-renderable and would
    /// balloon the box on a rigged/animated scene so the hero subject shrinks to a dot) keeps the
    /// camera fitted to what actually renders. Falls back to all meshes when nothing is marked
    /// renderable.
    /// </summary>
    private static bool TryComputeSceneBounds(DccSceneData scene, out Vector3 center, out double radius)
    {
        if (TryComputeMeshBounds(scene, renderableOnly: true, out center, out radius))
            return true;

        return TryComputeMeshBounds(scene, renderableOnly: false, out center, out radius);
    }

    private static bool TryComputeMeshBounds(DccSceneData scene, bool renderableOnly, out Vector3 center, out double radius)
    {
        center = Vector3.Zero;
        radius = 0d;

        var meshesById = scene.Meshes.ToDictionary(me => me.Id, StringComparer.Ordinal);

        var meshes = new List<(Vector3 Position, double LocalRadius)>();
        foreach (var node in scene.Nodes.Where(me => me.Kind == DccNodeKind.Mesh && me.MeshId != null))
        {
            if (renderableOnly && !node.Renderable)
                continue;

            if (!meshesById.TryGetValue(node.MeshId!, out var mesh) || mesh.Positions.Count == 0)
                continue;

            var localRadius = 0d;
            foreach (var p in mesh.Positions)
                localRadius = Math.Max(localRadius, Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z));

            var t = node.LocalTransform.Translation;
            meshes.Add((new Vector3((float)t.X, (float)t.Y, (float)t.Z), localRadius));
        }

        if (meshes.Count == 0)
            return false;

        // Exclude wide-spread outlier meshes (a particle system's bounding box, a huge skybox) that
        // would balloon the frame so the hero subject shrinks to a dot. A mesh whose local extent is
        // far above the median is dropped from the framing bounds.
        var median = Median(meshes.Select(me => me.LocalRadius));
        var kept = meshes.Where(me => me.LocalRadius <= median * OUTLIER_RADIUS_FACTOR).ToList();
        if (kept.Count == 0)
            kept = meshes;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var (position, localRadius) in kept)
        {
            var r = (float)localRadius;
            min = Vector3.Min(min, position - new Vector3(r));
            max = Vector3.Max(max, position + new Vector3(r));
        }

        center = (min + max) * 0.5f;
        radius = Math.Max((max - min).Length() * 0.5d, 1d);
        return true;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(me => me).ToList();
        if (sorted.Count == 0)
            return 0d;

        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2d;
    }

    /// <summary>
    /// Builds the captured-convention quaternion whose local -Y points from <paramref name="eye"/> to
    /// <paramref name="target"/> and whose local +Z is world up.
    /// </summary>
    private static DccQuaternionData LookAt(Vector3 eye, Vector3 target)
    {
        var worldUp = new Vector3(0f, 0f, 1f);
        var forward = Vector3.Normalize(target - eye);

        var yAxis = -forward; // local +Y is backward (local -Y = forward)
        var zAxis = worldUp - Vector3.Dot(worldUp, yAxis) * yAxis;
        if (zAxis.LengthSquared() < 1e-6f)
            zAxis = new Vector3(0f, 1f, 0f) - Vector3.Dot(new Vector3(0f, 1f, 0f), yAxis) * yAxis;
        zAxis = Vector3.Normalize(zAxis);
        var xAxis = Vector3.Normalize(Vector3.Cross(yAxis, zAxis));

        // System.Numerics matrices transform row vectors (v * M), so the basis axes are the rows.
        var matrix = new Matrix4x4(
            xAxis.X, xAxis.Y, xAxis.Z, 0f,
            yAxis.X, yAxis.Y, yAxis.Z, 0f,
            zAxis.X, zAxis.Y, zAxis.Z, 0f,
            0f, 0f, 0f, 1f);

        var q = Quaternion.CreateFromRotationMatrix(matrix);
        return new DccQuaternionData { X = q.X, Y = q.Y, Z = q.Z, W = q.W };
    }

    #endregion
}
