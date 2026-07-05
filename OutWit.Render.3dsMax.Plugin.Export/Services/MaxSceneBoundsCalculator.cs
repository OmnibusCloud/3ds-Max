using OutWit.Controller.Render.Dcc.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// The geometric bounds of a scene's HERO geometry — the renderable meshes, with wide-spread outliers
/// (a particle system's box, a skybox) excluded — used to calibrate light power, frame the fallback
/// camera, and place synthesized default lights. Framing/lighting against the hero geometry rather
/// than the full scene extent keeps a large spread object from inflating the light distance (blowing
/// out the render) or shrinking the subject to a dot. Computed in summary (pre-mapper) space as the
/// AABB of the kept meshes (node translation ± each mesh's local vertex radius). Mirrors the
/// hero-bounds logic in <c>MaxSceneCameraFramer</c>.
/// </summary>
internal readonly record struct MaxSceneBounds(double CenterX, double CenterY, double CenterZ, double Radius)
{
    #region Constants

    // A mesh whose local extent exceeds this multiple of the median is a wide-spread outlier.
    private const double OUTLIER_RADIUS_FACTOR = 4d;

    #endregion

    #region Functions

    /// <summary>
    /// Computes the hero bounding sphere, or null when the scene has no meshes.
    /// </summary>
    public static MaxSceneBounds? Compute(MaxSceneSummaryData summary)
    {
        return ComputeForMeshes(summary, renderableOnly: true)
               ?? ComputeForMeshes(summary, renderableOnly: false);
    }

    private static MaxSceneBounds? ComputeForMeshes(MaxSceneSummaryData summary, bool renderableOnly)
    {
        var meshesById = summary.Meshes.ToDictionary(me => me.Id, StringComparer.Ordinal);

        var meshes = summary.Nodes
            .Where(me => me.Kind == DccNodeKind.Mesh && !string.IsNullOrWhiteSpace(me.MeshId) && meshesById.ContainsKey(me.MeshId!))
            .Where(me => !renderableOnly || me.Renderable)
            .Select(me => (Node: me, LocalRadius: ComputeMeshLocalRadius(meshesById[me.MeshId!])))
            .ToArray();

        if (meshes.Length == 0)
            return null;

        // Drop wide-spread outlier meshes so a particle box / skybox does not inflate the bounds.
        var median = Median(meshes.Select(me => me.LocalRadius));
        var kept = meshes.Where(me => me.LocalRadius <= median * OUTLIER_RADIUS_FACTOR).ToArray();
        if (kept.Length == 0)
            kept = meshes;

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var (node, localRadius) in kept)
        {
            var t = node.LocalTransform.Translation;
            minX = Math.Min(minX, t.X - localRadius); maxX = Math.Max(maxX, t.X + localRadius);
            minY = Math.Min(minY, t.Y - localRadius); maxY = Math.Max(maxY, t.Y + localRadius);
            minZ = Math.Min(minZ, t.Z - localRadius); maxZ = Math.Max(maxZ, t.Z + localRadius);
        }

        var radius = Math.Max(Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY) + (maxZ - minZ) * (maxZ - minZ)) / 2d, 1d);
        return new MaxSceneBounds((minX + maxX) / 2d, (minY + maxY) / 2d, (minZ + maxZ) / 2d, radius);
    }

    private static double ComputeMeshLocalRadius(MaxSceneMeshSnapshotData mesh)
    {
        var radius = 0d;

        foreach (var position in mesh.Positions)
            radius = Math.Max(radius, Math.Sqrt(position.X * position.X + position.Y * position.Y + position.Z * position.Z));

        return radius;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(me => me).ToArray();
        if (sorted.Length == 0)
            return 0d;

        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2d;
    }

    #endregion
}
