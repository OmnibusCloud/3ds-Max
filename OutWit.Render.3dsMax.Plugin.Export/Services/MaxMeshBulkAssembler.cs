using System;
using System.Collections.Generic;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Assembles a mesh snapshot from bulk-captured native arrays — pure managed code with no SDK
/// dependency, safe on a worker thread. Replicates the per-corner wrapper walk EXACTLY
/// (positions/normals/UVs/colours and the smoothing-group render-normal resolution), so a scene
/// exported through the bulk path is byte-identical to the legacy per-corner read.
/// </summary>
internal static class MaxMeshBulkAssembler
{
    #region Functions

    /// <summary>
    /// Per-face material ids — the high word of the native face flags (what GetFaceMtlIndex
    /// returns). Cheap and synchronous: the caller needs MaterialIndices for the binding
    /// resolution before the corner assembly completes.
    /// </summary>
    public static void AppendMaterialIndices(MaxMeshBulkData data, List<int> target)
    {
        target.Capacity = Math.Max(target.Capacity, target.Count + data.FaceCount);
        for (var faceIndex = 0; faceIndex < data.FaceCount; faceIndex++)
        {
            var flags = ReadUInt32(data.Faces, faceIndex * data.FaceStride + MaxMeshBulkData.FACE_FLAGS_OFFSET);
            target.Add((int)((flags >> 16) & 0xFFFF));
        }
    }

    /// <summary>
    /// Worker-thread entry point: a faulted assembly task would take the whole export down at
    /// the join, so any unexpected failure (realistically only OOM — Assemble is bounds-guarded)
    /// degrades to a positions-only mesh instead.
    /// </summary>
    public static void AssembleGuarded(MaxMeshBulkData data, MaxSceneMeshSnapshotData meshData)
    {
        try
        {
            Assemble(data, meshData);
        }
        catch
        {
            try
            {
                ClearCornerLists(meshData);
                var positions = AssembleCornerPositions(data);
                if (positions is null)
                    return;

                meshData.Positions.AddRange(positions);
                for (var corner = 0; corner < positions.Count; corner++)
                {
                    var faceIndex = corner / 3;
                    meshData.Normals.Add(new MaxSceneVector3SnapshotData
                    {
                        X = data.FaceNormals[faceIndex * 3],
                        Y = data.FaceNormals[faceIndex * 3 + 1],
                        Z = data.FaceNormals[faceIndex * 3 + 2]
                    });
                    meshData.Uv0.Add(new MaxSceneVector2SnapshotData());
                    if (data.Uv1Requested)
                        meshData.Uv1.Add(new MaxSceneVector2SnapshotData());
                    if (data.ColorsRequested)
                        meshData.Colors.Add(new MaxSceneColorSnapshotData());
                    meshData.TriangleIndices.Add(meshData.TriangleIndices.Count);
                }
            }
            catch
            {
                ClearCornerLists(meshData);
            }
        }
    }

    private static void ClearCornerLists(MaxSceneMeshSnapshotData meshData)
    {
        meshData.Positions.Clear();
        meshData.Normals.Clear();
        meshData.Uv0.Clear();
        meshData.Uv1.Clear();
        meshData.Colors.Clear();
        meshData.TriangleIndices.Clear();
    }

    /// <summary>
    /// Fills Positions/Normals/Uv0/Uv1/Colors/TriangleIndices. Never throws on valid buffers —
    /// out-of-range indices (corrupt meshes the legacy path would have crashed on) degrade to
    /// zero entries instead.
    /// </summary>
    public static void Assemble(MaxMeshBulkData data, MaxSceneMeshSnapshotData meshData)
    {
        var cornerCount = data.FaceCount * 3;
        meshData.Positions.Capacity = cornerCount;
        meshData.Normals.Capacity = cornerCount;
        meshData.Uv0.Capacity = cornerCount;
        if (data.Uv1Requested)
            meshData.Uv1.Capacity = cornerCount;
        if (data.ColorsRequested)
            meshData.Colors.Capacity = cornerCount;
        meshData.TriangleIndices.Capacity = cornerCount;

        for (var faceIndex = 0; faceIndex < data.FaceCount; faceIndex++)
        {
            var faceRecord = faceIndex * data.FaceStride;
            var smoothingGroup = ReadUInt32(data.Faces, faceRecord + MaxMeshBulkData.FACE_SMOOTHING_GROUP_OFFSET);
            var faceNormalX = data.FaceNormals[faceIndex * 3];
            var faceNormalY = data.FaceNormals[faceIndex * 3 + 1];
            var faceNormalZ = data.FaceNormals[faceIndex * 3 + 2];

            for (var cornerIndex = 0; cornerIndex < 3; cornerIndex++)
            {
                var vertexIndex = (int)ReadUInt32(data.Faces, faceRecord + MaxMeshBulkData.FACE_VERT_OFFSET + cornerIndex * 4);

                float px = 0f, py = 0f, pz = 0f;
                if (vertexIndex >= 0 && vertexIndex < data.VertexCount)
                {
                    px = data.Vertices[vertexIndex * 3];
                    py = data.Vertices[vertexIndex * 3 + 1];
                    pz = data.Vertices[vertexIndex * 3 + 2];
                }

                var (nx, ny, nz) = ResolveVertexNormal(data, vertexIndex, smoothingGroup, faceNormalX, faceNormalY, faceNormalZ);

                if (data.CorrectionObjectTm is null || data.CorrectionInverseNodeTm is null)
                {
                    meshData.Positions.Add(new MaxSceneVector3SnapshotData { X = px, Y = py, Z = pz });
                    meshData.Normals.Add(new MaxSceneVector3SnapshotData { X = nx, Y = ny, Z = nz });
                }
                else
                {
                    meshData.Positions.Add(ApplyCorrection(data.CorrectionObjectTm, data.CorrectionInverseNodeTm, px, py, pz));
                    meshData.Normals.Add(ApplyCorrectionToNormal(data.CorrectionObjectTm, data.CorrectionInverseNodeTm, nx, ny, nz));
                }

                meshData.Uv0.Add(ReadMapCorner(data.TvFaces, data.TVerts, faceIndex, cornerIndex));

                if (data.Uv1Requested)
                    meshData.Uv1.Add(ReadMapCorner(data.Uv1Faces, data.Uv1Verts, faceIndex, cornerIndex));

                if (data.ColorsRequested)
                    meshData.Colors.Add(ReadColorCorner(data.ColorFaces, data.ColorVerts, faceIndex, cornerIndex));

                meshData.TriangleIndices.Add(meshData.TriangleIndices.Count);
            }
        }
    }

    /// <summary>
    /// Corner positions only (deformation-frame sampling). Returns null when a face references a
    /// vertex outside the captured array — the legacy read treated that as "topology changed" and
    /// skipped deformation for the mesh.
    /// </summary>
    public static List<MaxSceneVector3SnapshotData>? AssembleCornerPositions(MaxMeshBulkData data)
    {
        var positions = new List<MaxSceneVector3SnapshotData>(data.FaceCount * 3);
        for (var faceIndex = 0; faceIndex < data.FaceCount; faceIndex++)
        {
            var faceRecord = faceIndex * data.FaceStride;
            for (var cornerIndex = 0; cornerIndex < 3; cornerIndex++)
            {
                var vertexIndex = (int)ReadUInt32(data.Faces, faceRecord + MaxMeshBulkData.FACE_VERT_OFFSET + cornerIndex * 4);
                if (vertexIndex < 0 || vertexIndex >= data.VertexCount)
                    return null;

                var px = data.Vertices[vertexIndex * 3];
                var py = data.Vertices[vertexIndex * 3 + 1];
                var pz = data.Vertices[vertexIndex * 3 + 2];

                positions.Add(data.CorrectionObjectTm is null || data.CorrectionInverseNodeTm is null
                    ? new MaxSceneVector3SnapshotData { X = px, Y = py, Z = pz }
                    : ApplyCorrection(data.CorrectionObjectTm, data.CorrectionInverseNodeTm, px, py, pz));
            }
        }

        return positions;
    }

    /// <summary>
    /// Folds a mirror (diag(-1,1,1) in the node basis) into already-assembled mesh data: X of
    /// every position/normal is negated and the corner order of each triangle flips (1↔2) so the
    /// winding stays consistent with the negated normals. The node transform is decomposed from
    /// D·TM by the collector, so the composition reproduces the exact original world placement —
    /// this is how mirrored nodes survive a TRS contract that cannot carry reflections.
    /// </summary>
    public static void ApplyMirrorBake(MaxSceneMeshSnapshotData meshData)
    {
        SwapCornerTriples(meshData.Positions);
        SwapCornerTriples(meshData.Normals);
        SwapCornerTriples(meshData.Uv0);
        SwapCornerTriples(meshData.Uv1);
        SwapCornerTriples(meshData.Colors);

        foreach (var position in meshData.Positions)
            position.X = -position.X;
        foreach (var normal in meshData.Normals)
            normal.X = -normal.X;

        foreach (var frame in meshData.DeformationFrames)
            ApplyMirrorBakeToCornerPositions(frame.Positions);
    }

    /// <summary>Positions-only variant for per-frame deformation samples.</summary>
    public static void ApplyMirrorBakeToCornerPositions(List<MaxSceneVector3SnapshotData> positions)
    {
        SwapCornerTriples(positions);
        foreach (var position in positions)
            position.X = -position.X;
    }

    private static void SwapCornerTriples<T>(List<T> corners)
    {
        for (var corner = 0; corner + 2 < corners.Count; corner += 3)
        {
            (corners[corner + 1], corners[corner + 2]) = (corners[corner + 2], corners[corner + 1]);
        }
    }

    #endregion

    #region Tools

    // The exact legacy semantics (MaxSceneSnapshotCollector.ResolveVertexNormal): a face with no
    // smoothing group is hard → face normal; a single render normal is fully smooth → use it; a
    // vertex split across groups walks the extra RNormal array for the first entry whose mask
    // intersects the face's group. Any anomaly falls back to the face normal.
    private static (float X, float Y, float Z) ResolveVertexNormal(MaxMeshBulkData data, int vertexIndex, uint smoothingGroup, float faceNormalX, float faceNormalY, float faceNormalZ)
    {
        if (smoothingGroup == 0 || vertexIndex < 0 || vertexIndex >= data.VertexCount)
            return (faceNormalX, faceNormalY, faceNormalZ);

        var record = vertexIndex * data.RVertStride;
        var normalCount = (int)(ReadUInt32(data.RVerts, record) & 0xFFFF);
        if (normalCount == 1)
        {
            var rnOffset = record + data.RVertRnOffset;
            return (ReadSingle(data.RVerts, rnOffset), ReadSingle(data.RVerts, rnOffset + 4), ReadSingle(data.RVerts, rnOffset + 8));
        }

        if (!data.ErnNormals.TryGetValue(vertexIndex, out var extraNormals))
            return (faceNormalX, faceNormalY, faceNormalZ);

        for (var i = 0; i < normalCount; i++)
        {
            var entry = i * MaxMeshBulkData.RNORMAL_STRIDE;
            var mask = ReadUInt32(extraNormals, entry + MaxMeshBulkData.RNORMAL_SMOOTHING_GROUP_OFFSET);
            if ((mask & smoothingGroup) == 0)
                continue;

            var x = ReadSingle(extraNormals, entry);
            var y = ReadSingle(extraNormals, entry + 4);
            var z = ReadSingle(extraNormals, entry + 8);
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)
                || (x == 0f && y == 0f && z == 0f))
                return (faceNormalX, faceNormalY, faceNormalZ);

            return (x, y, z);
        }

        return (faceNormalX, faceNormalY, faceNormalZ);
    }

    private static MaxSceneVector2SnapshotData ReadMapCorner(int[]? mapFaces, float[]? mapVerts, int faceIndex, int cornerIndex)
    {
        if (mapFaces is null || mapVerts is null)
            return new MaxSceneVector2SnapshotData();

        var mapVertexIndex = mapFaces[faceIndex * 3 + cornerIndex];
        if (mapVertexIndex < 0 || mapVertexIndex * 3 + 1 >= mapVerts.Length)
            return new MaxSceneVector2SnapshotData();

        return new MaxSceneVector2SnapshotData
        {
            X = mapVerts[mapVertexIndex * 3],
            Y = mapVerts[mapVertexIndex * 3 + 1]
        };
    }

    private static MaxSceneColorSnapshotData ReadColorCorner(int[]? mapFaces, float[]? mapVerts, int faceIndex, int cornerIndex)
    {
        if (mapFaces is null || mapVerts is null)
            return new MaxSceneColorSnapshotData();

        var mapVertexIndex = mapFaces[faceIndex * 3 + cornerIndex];
        if (mapVertexIndex < 0 || mapVertexIndex * 3 + 2 >= mapVerts.Length)
            return new MaxSceneColorSnapshotData();

        return new MaxSceneColorSnapshotData
        {
            R = mapVerts[mapVertexIndex * 3],
            G = mapVerts[mapVertexIndex * 3 + 1],
            B = mapVerts[mapVertexIndex * 3 + 2],
            A = 1d
        };
    }

    // v' = (v · objTM) · nodeTM⁻¹ in row-vector convention — the exact legacy arithmetic
    // (floats widened to double, same operation order) so corrected meshes stay byte-identical.
    private static MaxSceneVector3SnapshotData ApplyCorrection(double[] objectTm, double[] inverseNodeTm, double x, double y, double z)
    {
        var (wx, wy, wz) = TransformPoint(objectTm, x, y, z);
        var (rx, ry, rz) = TransformPoint(inverseNodeTm, wx, wy, wz);
        return new MaxSceneVector3SnapshotData { X = rx, Y = ry, Z = rz };
    }

    private static MaxSceneVector3SnapshotData ApplyCorrectionToNormal(double[] objectTm, double[] inverseNodeTm, double x, double y, double z)
    {
        var (ax, ay, az) = TransformVector(objectTm, x, y, z);
        var (bx, by, bz) = TransformVector(inverseNodeTm, ax, ay, az);
        var length = Math.Sqrt(bx * bx + by * by + bz * bz);
        if (length < 1e-9d)
            return new MaxSceneVector3SnapshotData { X = x, Y = y, Z = z };
        return new MaxSceneVector3SnapshotData { X = bx / length, Y = by / length, Z = bz / length };
    }

    // Rows layout: [r0x r0y r0z r1x r1y r1z r2x r2y r2z r3x r3y r3z].
    private static (double X, double Y, double Z) TransformPoint(double[] m, double x, double y, double z)
    {
        return (
            x * m[0] + y * m[3] + z * m[6] + m[9],
            x * m[1] + y * m[4] + z * m[7] + m[10],
            x * m[2] + y * m[5] + z * m[8] + m[11]);
    }

    private static (double X, double Y, double Z) TransformVector(double[] m, double x, double y, double z)
    {
        return (
            x * m[0] + y * m[3] + z * m[6],
            x * m[1] + y * m[4] + z * m[7],
            x * m[2] + y * m[5] + z * m[8]);
    }

    private static uint ReadUInt32(byte[] buffer, int offset)
    {
        return BitConverter.ToUInt32(buffer, offset);
    }

    private static float ReadSingle(byte[] buffer, int offset)
    {
        return BitConverter.ToSingle(buffer, offset);
    }

    #endregion
}
