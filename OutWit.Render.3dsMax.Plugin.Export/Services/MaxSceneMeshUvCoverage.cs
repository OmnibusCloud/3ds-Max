using System;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Measures how much UV space a mesh's primary layer actually spans. A render-to-texture bake
/// lands on the node's own channel-1 UVs — a mesh whose unwrap is collapsed into a sliver (no
/// artist unwrap, as on the Automotive car body) bakes into a few texels and samples back as a
/// near-black smear, so the bake gate needs a cheap coverage estimate from the already-extracted
/// corners.
/// </summary>
internal static class MaxSceneMeshUvCoverage
{
    #region Functions

    /// <summary>
    /// Total unsigned area of the UV triangles (corners are consecutive triples). Overlapping
    /// islands double-count, which is fine for a lower-bound gate: a collapsed unwrap measures
    /// near zero no matter how many faces pile into it.
    /// </summary>
    public static double ComputeUv0Area(MaxSceneMeshSnapshotData mesh)
    {
        var uv = mesh.Uv0;
        var total = 0d;

        for (var corner = 0; corner + 2 < uv.Count; corner += 3)
        {
            var ax = uv[corner + 1].X - uv[corner].X;
            var ay = uv[corner + 1].Y - uv[corner].Y;
            var bx = uv[corner + 2].X - uv[corner].X;
            var by = uv[corner + 2].Y - uv[corner].Y;
            total += Math.Abs(ax * by - ay * bx) * 0.5d;
        }

        return total;
    }

    #endregion
}
