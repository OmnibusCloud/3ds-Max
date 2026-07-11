using Autodesk.Max;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;
using System.Reflection;
using System.Runtime.InteropServices;

namespace OutWit.Render.ThreeDsMax.Plugin.Services;

/// <summary>
/// Captures a TriObject mesh's arrays in bulk through the native pointers the managed SDK
/// exposes (GetVertPtr/GetFacePtr/GetRVertPtr + NativePointer) — a handful of memcpys instead of
/// tens of millions of per-corner interop calls. Every stride and field offset is VERIFIED
/// against the per-element wrapper API (first/last elements) before a bulk copy is trusted; any
/// mismatch returns null and the caller keeps the legacy per-corner read. All capture work runs
/// on the Max main thread while the (possibly temporary) TriObject is alive — the returned
/// buffers are managed copies safe to assemble on a worker thread.
/// </summary>
internal static class MaxMeshBulkCapture
{
    #region Constants

    private const int POINT3_STRIDE = 12;

    private const int MAX_SANE_STRIDE = 256;

    #endregion

    #region Functions

    public static MaxMeshBulkData? TryCapture(IMesh mesh, bool hasUv0, bool hasUv1, bool hasColors, (IMatrix3 ObjectTm, IMatrix3 InverseNodeTm)? correction)
    {
        try
        {
            var data = TryCaptureGeometry(mesh, correction);
            if (data is null)
                return null;

            if (!TryCaptureRenderNormals(mesh, data))
                return null;

            if (hasUv0 && !TryCapturePrimaryUvChannel(mesh, data))
                return null;

            if (hasUv1)
            {
                if (!TryCaptureMapChannel(mesh, 2, data.FaceCount, out var uv1Verts, out var uv1Faces))
                    return null;

                data.Uv1Requested = true;
                data.Uv1Verts = uv1Verts;
                data.Uv1Faces = uv1Faces;
            }

            if (hasColors)
            {
                if (!TryCaptureMapChannel(mesh, 0, data.FaceCount, out var colorVerts, out var colorFaces))
                    return null;

                data.ColorsRequested = true;
                data.ColorVerts = colorVerts;
                data.ColorFaces = colorFaces;
            }

            return data;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Vertices + faces + correction only — the deformation-frame sampler re-reads positions per
    /// frame and needs nothing else.
    /// </summary>
    public static MaxMeshBulkData? TryCaptureGeometryOnly(IMesh mesh, (IMatrix3 ObjectTm, IMatrix3 InverseNodeTm)? correction)
    {
        try
        {
            return TryCaptureGeometry(mesh, correction);
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Tools

    private static MaxMeshBulkData? TryCaptureGeometry(IMesh mesh, (IMatrix3 ObjectTm, IMatrix3 InverseNodeTm)? correction)
    {
        var faceCount = mesh.NumFaces;
        var vertexCount = mesh.NumVerts;
        if (faceCount <= 0 || vertexCount <= 0)
            return null;

        var vertices = CapturePoint3Array(vertexCount, i => mesh.GetVertPtr(i));
        if (vertices is null)
            return null;

        var firstVertex = mesh.GetVert(0);
        var lastVertex = mesh.GetVert(vertexCount - 1);
        if (vertices[0] != firstVertex.X || vertices[1] != firstVertex.Y || vertices[2] != firstVertex.Z
            || vertices[(vertexCount - 1) * 3] != lastVertex.X
            || vertices[(vertexCount - 1) * 3 + 1] != lastVertex.Y
            || vertices[(vertexCount - 1) * 3 + 2] != lastVertex.Z)
            return null;

        var faceBase = PointerOf(mesh.GetFacePtr(0));
        if (faceBase == IntPtr.Zero)
            return null;

        var faceStride = MaxMeshBulkData.FACE_EXPECTED_STRIDE;
        if (faceCount > 1)
        {
            faceStride = (int)((long)PointerOf(mesh.GetFacePtr(1)) - (long)faceBase);
            if (faceStride < MaxMeshBulkData.FACE_EXPECTED_STRIDE || faceStride > MAX_SANE_STRIDE)
                return null;
        }

        var faces = new byte[(long)faceCount * faceStride];
        Marshal.Copy(faceBase, faces, 0, faces.Length);

        if (!VerifyFace(mesh, faces, faceStride, 0) || !VerifyFace(mesh, faces, faceStride, faceCount - 1)
            || !VerifyFace(mesh, faces, faceStride, faceCount / 2))
            return null;

        var data = new MaxMeshBulkData
        {
            FaceCount = faceCount,
            VertexCount = vertexCount,
            Faces = faces,
            FaceStride = faceStride,
            Vertices = vertices
        };

        if (correction is not null)
        {
            data.CorrectionObjectTm = CaptureMatrixRows(correction.Value.ObjectTm);
            data.CorrectionInverseNodeTm = CaptureMatrixRows(correction.Value.InverseNodeTm);
            if (data.CorrectionObjectTm is null || data.CorrectionInverseNodeTm is null)
                return null;
        }

        return data;
    }

    // Face normals + RVertex records (both built by Mesh.BuildNormals, which the caller runs
    // first). The RVertex stride comes from consecutive element pointers, the embedded RNormal
    // offset from the wrapper's own Rn pointer, and the extra-normal array pointer sits in the
    // record's last pointer-sized slot — verified against the wrapper on the first split vertex.
    private static bool TryCaptureRenderNormals(IMesh mesh, MaxMeshBulkData data)
    {
        var faceNormals = CapturePoint3Array(data.FaceCount, i => mesh.GetFaceNormalPtr(i));
        if (faceNormals is null)
            return false;

        var firstFaceNormal = mesh.GetFaceNormal(0);
        if (faceNormals[0] != firstFaceNormal.X || faceNormals[1] != firstFaceNormal.Y || faceNormals[2] != firstFaceNormal.Z)
            return false;

        data.FaceNormals = faceNormals;

        // Single-vertex meshes cannot derive a record stride — keep the legacy path (rare and tiny).
        if (data.VertexCount < 2)
            return false;

        var firstRVertex = mesh.GetRVertPtr(0);
        var rvBase = PointerOf(firstRVertex);
        if (rvBase == IntPtr.Zero)
            return false;

        var rvStride = (int)((long)PointerOf(mesh.GetRVertPtr(1)) - (long)rvBase);
        var rnOffset = (int)((long)PointerOf(firstRVertex.Rn) - (long)rvBase);
        if (rvStride < MaxMeshBulkData.RNORMAL_STRIDE || rvStride > MAX_SANE_STRIDE
            || rnOffset < 0 || rnOffset + MaxMeshBulkData.RNORMAL_STRIDE > rvStride)
            return false;

        var rvBytes = new byte[(long)data.VertexCount * rvStride];
        Marshal.Copy(rvBase, rvBytes, 0, rvBytes.Length);

        if (BitConverter.ToUInt32(rvBytes, 0) != firstRVertex.RFlags
            || BitConverter.ToUInt32(rvBytes, (data.VertexCount - 1) * rvStride) != mesh.GetRVertPtr(data.VertexCount - 1).RFlags)
            return false;

        data.RVerts = rvBytes;
        data.RVertStride = rvStride;
        data.RVertRnOffset = rnOffset;

        var ernOffset = rvStride - IntPtr.Size;
        var ernOffsetVerified = false;
        for (var vertexIndex = 0; vertexIndex < data.VertexCount; vertexIndex++)
        {
            var normalCount = (int)(BitConverter.ToUInt32(rvBytes, vertexIndex * rvStride) & 0xFFFF);
            if (normalCount <= 1)
                continue;

            var ernPointer = (IntPtr)BitConverter.ToInt64(rvBytes, vertexIndex * rvStride + ernOffset);
            if (!ernOffsetVerified)
            {
                var wrapperErn = mesh.GetRVertPtr(vertexIndex).Ern;
                var wrapperPointer = wrapperErn is null ? IntPtr.Zero : PointerOf(wrapperErn);
                if (wrapperPointer != ernPointer)
                    return false;

                // A null==null match proves nothing about the offset — only a NON-ZERO pointer
                // confirmed against the wrapper earns trust; until then every split vertex is
                // re-checked (a wrong offset here would dereference garbage and crash Max).
                ernOffsetVerified = ernPointer != IntPtr.Zero;
            }

            if (ernPointer == IntPtr.Zero)
                continue;

            var ernBytes = new byte[normalCount * MaxMeshBulkData.RNORMAL_STRIDE];
            Marshal.Copy(ernPointer, ernBytes, 0, ernBytes.Length);
            data.ErnNormals[vertexIndex] = ernBytes;
        }

        return true;
    }

    // Channel 1 lives in the mesh's main tVert array (what GetTVert reads); its TVFace array is
    // reached through the MapFaces(1) element wrappers.
    private static bool TryCapturePrimaryUvChannel(IMesh mesh, MaxMeshBulkData data)
    {
        var tvertCount = mesh.NumTVerts;
        if (tvertCount <= 0 || mesh.GetNumMapFaces(1) != data.FaceCount)
            return true; // Channel effectively empty — legacy per-corner reads defaulted to (0,0) too.

        var tverts = CapturePoint3Array(tvertCount, i => mesh.GetTVertPtr(i));
        if (tverts is null)
            return false;

        var firstTVert = mesh.GetTVert(0);
        if (tverts[0] != firstTVert.X || tverts[1] != firstTVert.Y)
            return false;

        var tvFaces = CaptureTriIndexArray(mesh.MapFaces(1), data.FaceCount);
        if (tvFaces is null)
            return false;

        data.TVerts = tverts;
        data.TvFaces = tvFaces;
        return true;
    }

    // Generic map channel (2 = second UV set, 0 = vertex colours): both element arrays are
    // Point3/TVFace tabs reached through the MapVerts/MapFaces element wrappers.
    private static bool TryCaptureMapChannel(IMesh mesh, int channel, int faceCount, out float[]? mapVerts, out int[]? mapFaces)
    {
        mapVerts = null;
        mapFaces = null;

        var mapVertexCount = mesh.GetNumMapVerts(channel);
        if (mapVertexCount <= 0 || mesh.GetNumMapFaces(channel) != faceCount)
            return true; // Channel effectively empty — legacy reads defaulted per corner too.

        var vertList = mesh.MapVerts(channel);
        var verts = CapturePoint3Array(mapVertexCount, i => GetListItem(vertList, i));
        if (verts is null)
            return false;

        var faces = CaptureTriIndexArray(mesh.MapFaces(channel), faceCount);
        if (faces is null)
            return false;

        mapVerts = verts;
        mapFaces = faces;
        return true;
    }

    // Bulk-copies a contiguous Point3 array via the pointer of element 0, verifying the 12-byte
    // stride against element 1 (heap-copied wrappers can never sit exactly 12 bytes apart).
    private static float[]? CapturePoint3Array(int count, Func<int, object?> elementAt)
    {
        if (count <= 0)
            return null;

        var basePointer = PointerOf(elementAt(0));
        if (basePointer == IntPtr.Zero)
            return null;

        if (count > 1 && (long)PointerOf(elementAt(1)) - (long)basePointer != POINT3_STRIDE)
            return null;

        var values = new float[count * 3];
        Marshal.Copy(basePointer, values, 0, values.Length);
        return values;
    }

    // Bulk-copies a contiguous TVFace (3 × DWORD) array via the MapFaces element wrappers,
    // verifying the stride and the first/last records against the wrapper reads.
    private static int[]? CaptureTriIndexArray(object? faceList, int faceCount)
    {
        if (faceList is null)
            return null;

        var firstFace = GetListItem(faceList, 0) as ITVFace;
        var basePointer = PointerOf(firstFace);
        if (firstFace is null || basePointer == IntPtr.Zero)
            return null;

        if (faceCount > 1 && (long)PointerOf(GetListItem(faceList, 1)) - (long)basePointer != POINT3_STRIDE)
            return null;

        var indices = new int[faceCount * 3];
        Marshal.Copy(basePointer, indices, 0, indices.Length);

        for (var corner = 0; corner < 3; corner++)
        {
            if ((uint)indices[corner] != firstFace.GetTVert(corner))
                return null;
        }

        if (faceCount > 1 && GetListItem(faceList, faceCount - 1) is ITVFace lastFace)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                if ((uint)indices[(faceCount - 1) * 3 + corner] != lastFace.GetTVert(corner))
                    return null;
            }
        }

        return indices;
    }

    private static bool VerifyFace(IMesh mesh, byte[] faces, int faceStride, int faceIndex)
    {
        var face = mesh.GetFace(faceIndex);
        var record = faceIndex * faceStride;

        for (var corner = 0; corner < 3; corner++)
        {
            if (BitConverter.ToUInt32(faces, record + MaxMeshBulkData.FACE_VERT_OFFSET + corner * 4) != face.GetVert(corner))
                return false;
        }

        if (BitConverter.ToUInt32(faces, record + MaxMeshBulkData.FACE_SMOOTHING_GROUP_OFFSET) != face.SmGroup)
            return false;

        var flags = BitConverter.ToUInt32(faces, record + MaxMeshBulkData.FACE_FLAGS_OFFSET);
        return (ushort)((flags >> 16) & 0xFFFF) == mesh.GetFaceMtlIndex(faceIndex);
    }

    private static double[]? CaptureMatrixRows(IMatrix3 matrix)
    {
        var rows = new double[12];
        for (var rowIndex = 0; rowIndex < 4; rowIndex++)
        {
            var row = matrix.GetRow(rowIndex);
            if (row is null)
                return null;

            rows[rowIndex * 3] = row.X;
            rows[rowIndex * 3 + 1] = row.Y;
            rows[rowIndex * 3 + 2] = row.Z;
        }

        return rows;
    }

    private static IntPtr PointerOf(object? wrapper)
    {
        return wrapper is INativeObject nativeObject ? nativeObject.NativePointer : IntPtr.Zero;
    }

    // The managed SDK's tab lists are not uniformly typed — index them reflectively, the same
    // way the collector's TryGetIndexedValue does.
    private static object? GetListItem(object? source, int index)
    {
        if (source is null)
            return null;

        if (source is Array array)
            return index >= 0 && index < array.Length ? array.GetValue(index) : null;

        var sourceType = source.GetType();
        var itemProperty = sourceType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(me => me.Name == "Item" && me.GetIndexParameters().Length == 1 && me.GetIndexParameters()[0].ParameterType == typeof(int));

        if (itemProperty is not null)
            return itemProperty.GetValue(source, [index]);

        var getValueMethod = sourceType.GetMethod("GetValue", BindingFlags.Instance | BindingFlags.Public, [typeof(int)])
                             ?? sourceType.GetMethod("get_Item", BindingFlags.Instance | BindingFlags.Public, [typeof(int)]);

        return getValueMethod?.Invoke(source, [index]);
    }

    #endregion
}
