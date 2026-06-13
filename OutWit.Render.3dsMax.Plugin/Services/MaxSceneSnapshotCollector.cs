using Autodesk.Max;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OutWit.Render.ThreeDsMax.Plugin.Services;

internal sealed class MaxSceneSnapshotCollector
{
    #region Fields

    private readonly IGlobal m_global;
    private readonly IInterface m_coreInterface;
    private readonly MaxSceneSnapshotData m_summary;
    private readonly HashSet<object> m_materials;
    private readonly HashSet<object> m_textures;
    private readonly SortedSet<string> m_materialNames;
    private readonly SortedSet<string> m_textureNames;
    private readonly HashSet<object> m_exportedMaterials;
    private readonly HashSet<object> m_exportedTextures;
    private readonly Dictionary<object, string> m_materialIdsByReference;
    private readonly Dictionary<string, string> m_imageAssetIdsByPath;
    private readonly HashSet<string> m_usedMaterialIds;
    private readonly HashSet<string> m_usedImageAssetIds;

    #endregion

    #region Constructors

    public MaxSceneSnapshotCollector(IGlobal global, IInterface coreInterface, MaxSceneSnapshotData summary)
    {
        m_global = global;
        m_coreInterface = coreInterface;
        m_summary = summary;
        m_materials = new HashSet<object>(ReferenceEqualityComparer.Instance);
        m_textures = new HashSet<object>(ReferenceEqualityComparer.Instance);
        m_materialNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        m_textureNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        m_exportedMaterials = new HashSet<object>(ReferenceEqualityComparer.Instance);
        m_exportedTextures = new HashSet<object>(ReferenceEqualityComparer.Instance);
        m_materialIdsByReference = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        m_imageAssetIdsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        m_usedMaterialIds = new HashSet<string>(StringComparer.Ordinal);
        m_usedImageAssetIds = new HashSet<string>(StringComparer.Ordinal);
    }

    #endregion

    #region Functions

    public void Collect(IINode rootNode)
    {
        CollectSceneContent(rootNode);
        m_summary.MaterialsCount = m_materials.Count;
        m_summary.TexturesCount = m_textures.Count;
        m_summary.MaterialNames = [.. m_materialNames];
        m_summary.TextureNames = [.. m_textureNames];
    }

    private void CollectSceneContent(IINode rootNode)
    {
        for (var i = 0; i < rootNode.NumberOfChildren; i++)
        {
            var childNode = rootNode.GetChildNode(i);
            m_summary.NodesCount++;

            var objectState = childNode.ObjectRef.Eval(m_coreInterface.Time);
            var sceneObject = objectState.Obj;
            var nodeId = $"node:{childNode.Handle}";
            var parentId = rootNode.IsRootNode ? null : $"node:{rootNode.Handle}";
            var localTransform = ExtractLocalTransform(childNode, rootNode);

            if (sceneObject is ICameraObject cameraObject)
            {
                m_summary.CamerasCount++;
                AddName(m_summary.CameraNames, childNode.Name);
                var cameraId = $"camera:{childNode.Handle}";
                m_summary.Nodes.Add(new MaxSceneNodeSnapshotData
                {
                    Id = nodeId,
                    Name = childNode.Name,
                    ParentId = parentId,
                    Kind = DccNodeKind.Camera,
                    LocalTransform = localTransform,
                    CameraId = cameraId,
                    Visible = true,
                    Renderable = true
                });
                m_summary.Cameras.Add(ExtractCamera(childNode, cameraObject, cameraId));
            }

            if (sceneObject is ILightObject lightObject)
            {
                m_summary.LightsCount++;
                AddName(m_summary.LightNames, childNode.Name);
                var lightId = $"light:{childNode.Handle}";
                m_summary.Nodes.Add(new MaxSceneNodeSnapshotData
                {
                    Id = nodeId,
                    Name = childNode.Name,
                    ParentId = parentId,
                    Kind = DccNodeKind.Light,
                    LocalTransform = localTransform,
                    LightId = lightId,
                    Visible = true,
                    Renderable = true
                });
                m_summary.Lights.Add(ExtractLight(childNode, lightObject, lightId));
            }

            if (sceneObject.CanConvertToType(m_global.TriObjectClassID) == 1)
            {
                m_summary.MeshesCount++;
                var meshId = $"mesh:{childNode.Handle}";
                var meshSnapshot = ExtractMesh(childNode, sceneObject, meshId);
                var materialBindingMap = GetMaterialBindingMap(childNode.Mtl);
                NormalizeMaterialIndices(meshSnapshot, materialBindingMap);
                var materialBindingId = ResolveMaterialBindingId(meshSnapshot, materialBindingMap.MaterialIds);

                if (UsesPerTriangleMaterialBinding(meshSnapshot))
                    materialBindingId = null;

                m_summary.Nodes.Add(new MaxSceneNodeSnapshotData
                {
                    Id = nodeId,
                    Name = childNode.Name,
                    ParentId = parentId,
                    Kind = DccNodeKind.Mesh,
                    LocalTransform = localTransform,
                    MeshId = meshId,
                    MaterialBindingId = materialBindingId,
                    Visible = true,
                    Renderable = true
                });
                m_summary.Meshes.Add(meshSnapshot);
            }

            CollectSceneContent(childNode);
        }
    }

    private MaxSceneMeshSnapshotData ExtractMesh(IINode node, IObject sceneObject, string meshId)
    {
        var convertedObject = sceneObject;

        if (convertedObject.CanConvertToType(m_global.TriObjectClassID) == 1)
            convertedObject = convertedObject.ConvertToType(m_coreInterface.Time, m_global.TriObjectClassID);

        if (convertedObject is not ITriObject triObject)
        {
            return new MaxSceneMeshSnapshotData
            {
                Id = meshId,
                Name = $"{node.Name}Mesh"
            };
        }

        var mesh = triObject.Mesh;
        var meshData = new MaxSceneMeshSnapshotData
        {
            Id = meshId,
            Name = $"{node.Name}Mesh"
        };

        for (var faceIndex = 0; faceIndex < mesh.NumFaces; faceIndex++)
        {
            var face = mesh.GetFace(faceIndex);
            var faceNormal = mesh.GetFaceNormal(faceIndex);

            for (var vertexIndex = 0; vertexIndex < 3; vertexIndex++)
            {
                var sourceVertexIndex = face.GetVert(vertexIndex);
                var point = mesh.GetVert((int)sourceVertexIndex);
                meshData.Positions.Add(new MaxSceneVector3SnapshotData { X = point.X, Y = point.Y, Z = point.Z });
                meshData.Normals.Add(new MaxSceneVector3SnapshotData { X = faceNormal.X, Y = faceNormal.Y, Z = faceNormal.Z });
                meshData.Uv0.Add(ExtractUv(mesh, faceIndex, vertexIndex));
                meshData.TriangleIndices.Add(meshData.TriangleIndices.Count);
            }

            meshData.MaterialIndices.Add(mesh.GetFaceMtlIndex(faceIndex));
        }

        return meshData;
    }

    private MaxMaterialBindingMap GetMaterialBindingMap(IMtl? material)
    {
        if (material is null)
            return new MaxMaterialBindingMap();

        if (material.NumSubMtls <= 0)
        {
            var materialId = TryExtractMaterialBinding(material);
            return string.IsNullOrWhiteSpace(materialId)
                ? new MaxMaterialBindingMap()
                : new MaxMaterialBindingMap
                {
                    MaterialIds = [materialId],
                    CompactMaterialIndexByRawIndex =
                    {
                        [0] = 0,
                        [1] = 0
                    }
                };
        }

        var materialBindingMap = new MaxMaterialBindingMap();

        for (var i = 0; i < material.NumSubMtls; i++)
        {
            var subMaterial = material.GetSubMtl(i);
            if (subMaterial is null)
                continue;

            var materialId = TryExtractMaterialBinding(subMaterial);
            if (string.IsNullOrWhiteSpace(materialId))
                continue;

            var compactMaterialIndex = materialBindingMap.MaterialIds.Count;
            materialBindingMap.MaterialIds.Add(materialId);
            materialBindingMap.CompactMaterialIndexByRawIndex.TryAdd(i, compactMaterialIndex);
            materialBindingMap.CompactMaterialIndexByRawIndex.TryAdd(i + 1, compactMaterialIndex);
        }

        if (materialBindingMap.MaterialIds.Count > 0)
            return materialBindingMap;

        var fallbackMaterialId = TryExtractMaterialBinding(material);
        return string.IsNullOrWhiteSpace(fallbackMaterialId)
            ? new MaxMaterialBindingMap()
            : new MaxMaterialBindingMap
            {
                MaterialIds = [fallbackMaterialId],
                CompactMaterialIndexByRawIndex =
                {
                    [0] = 0,
                    [1] = 0
                }
            };
    }

    private MaxSceneCameraSnapshotData ExtractCamera(IINode node, ICameraObject cameraObject, string cameraId)
    {
        return new MaxSceneCameraSnapshotData
        {
            Id = cameraId,
            Name = node.Name,
            VerticalFovDegrees = RadiansToDegrees(cameraObject.GetFOV(m_coreInterface.Time)),
            NearClip = ResolveCameraClipDistance(cameraObject, m_coreInterface.Time, 0, 0.1d),
            FarClip = ResolveCameraClipDistance(cameraObject, m_coreInterface.Time, 1, 1000d),
            IsPerspective = !cameraObject.IsOrtho
        };
    }

    private MaxSceneLightSnapshotData ExtractLight(IINode node, ILightObject lightObject, string lightId)
    {
        var color = lightObject.GetRGBColor(m_coreInterface.Time);
        var hotspotDegrees = RadiansToDegrees(lightObject.GetHotspot(m_coreInterface.Time));
        var kind = ResolveLightKind(node, lightObject, hotspotDegrees);

        return new MaxSceneLightSnapshotData
        {
            Id = lightId,
            Name = node.Name,
            Kind = kind,
            Color = new MaxSceneColorSnapshotData { R = color.X, G = color.Y, B = color.Z, A = 1d },
            Intensity = lightObject.GetIntensity(m_coreInterface.Time),
            Range = Math.Max(lightObject.GetTDist(m_coreInterface.Time), 0.01d),
            SpotAngleDegrees = ResolveSpotAngleDegrees(kind, hotspotDegrees)
        };
    }

    private string? TryExtractMaterialBinding(IMtl? material)
    {
        if (material is null)
            return null;

        CollectMaterialInfo(material);

        if (m_materialIdsByReference.TryGetValue(material, out var existingMaterialId))
            return existingMaterialId;

        var materialId = CreateUniquePrefixedId(m_usedMaterialIds, "material", string.IsNullOrWhiteSpace(material.Name) ? "material" : material.Name);
        m_materialIdsByReference[material] = materialId;

        if (!m_exportedMaterials.Add(material))
            return materialId;

        var materialSnapshot = new MaxSceneMaterialSnapshotData
        {
            Id = materialId,
            Name = string.IsNullOrWhiteSpace(material.Name) ? materialId : material.Name
        };

        ReadMaterialAppearance(material, materialSnapshot);

        var bitmapTexture = FindFirstBitmapTexture(material);

        if (bitmapTexture is not null)
        {
            var imageAssetId = GetOrCreateImageAsset(bitmapTexture);

            if (!string.IsNullOrWhiteSpace(imageAssetId))
            {
                materialSnapshot.TextureSlots.Add(new MaxSceneTextureSlotSnapshotData
                {
                    Slot = DccTextureSlotKind.BaseColor,
                    ImageAssetId = imageAssetId
                });
            }
        }

        m_summary.Materials.Add(materialSnapshot);
        return materialId;
    }

    private void ReadMaterialAppearance(IMtl material, MaxSceneMaterialSnapshotData snapshot)
    {
        // Read the standard material parameters 3ds Max exposes on IMtl. Materials that do not
        // support them (or throw) keep the snapshot defaults rather than failing the export.
        try
        {
            var time = m_coreInterface.Time;

            var diffuse = material.GetDiffuse(time, false);
            snapshot.BaseColor = new MaxSceneColorSnapshotData { R = diffuse.R, G = diffuse.G, B = diffuse.B, A = 1d };

            // Material-level transparency in 3ds Max is refractive (glass), so map it to the
            // neutral material's transmission rather than alpha — alpha transparency comes from
            // opacity texture maps, handled via texture slots. Opacity stays 1 for transmissive
            // materials so the renderer refracts instead of alpha-blending.
            var transparency = Math.Clamp(material.GetXParency(time, false), 0d, 1d);
            snapshot.Transmission = transparency;
            snapshot.Opacity = 1d;

            // 3ds Max glass shaders (e.g. Raytrace) keep their visible tint in a separate
            // transparency/filter channel and leave the diffuse near-black. A black base color on
            // a transmissive material reads as opaque black glass that absorbs all light, so when
            // a material is meaningfully transmissive but has a near-black diffuse, treat it as
            // clear glass (white base) rather than letting it swallow the scene.
            if (transparency > 0.1d && Math.Max(diffuse.R, Math.Max(diffuse.G, diffuse.B)) < 0.05d)
                snapshot.BaseColor = new MaxSceneColorSnapshotData { R = 1d, G = 1d, B = 1d, A = 1d };

            // Max glossiness (0..1, higher = sharper highlight) maps inversely to Blender roughness.
            var shininess = material.GetShininess(time, false);
            snapshot.Roughness = Math.Clamp(1d - shininess, 0d, 1d);
        }
        catch
        {
            // Leave the snapshot defaults in place for material types that do not expose the
            // legacy IMtl getters (e.g. some renderer-specific shaders).
        }
    }

    private static void NormalizeMaterialIndices(MaxSceneMeshSnapshotData meshSnapshot, MaxMaterialBindingMap materialBindingMap)
    {
        if (materialBindingMap.MaterialIds.Count <= 0 || meshSnapshot.MaterialIndices.Count == 0)
            return;

        for (var i = 0; i < meshSnapshot.MaterialIndices.Count; i++)
        {
            meshSnapshot.MaterialIndices[i] = NormalizeMaterialIndex(meshSnapshot.MaterialIndices[i], materialBindingMap);
        }
    }

    private static int NormalizeMaterialIndex(int rawMaterialIndex, MaxMaterialBindingMap materialBindingMap)
    {
        if (materialBindingMap.CompactMaterialIndexByRawIndex.TryGetValue(rawMaterialIndex, out var compactMaterialIndex))
            return compactMaterialIndex;

        var oneBasedIndex = rawMaterialIndex - 1;
        if (materialBindingMap.CompactMaterialIndexByRawIndex.TryGetValue(oneBasedIndex, out compactMaterialIndex))
            return compactMaterialIndex;

        var materialBindingCount = materialBindingMap.MaterialIds.Count;
        if (rawMaterialIndex >= 0 && rawMaterialIndex < materialBindingCount)
            return rawMaterialIndex;

        if (oneBasedIndex >= 0 && oneBasedIndex < materialBindingCount)
            return oneBasedIndex;

        if (materialBindingCount == 1)
            return 0;

        if (rawMaterialIndex >= materialBindingCount)
            return materialBindingCount - 1;

        if (rawMaterialIndex < 0)
            return 0;

        return rawMaterialIndex;
    }

    private static string? ResolveMaterialBindingId(MaxSceneMeshSnapshotData meshSnapshot, IReadOnlyList<string> materialBindingIds)
    {
        if (materialBindingIds.Count == 0)
            return null;

        if (materialBindingIds.Count == 1)
            return materialBindingIds[0];

        var distinctMaterialIndices = meshSnapshot.MaterialIndices.Distinct().ToArray();
        if (distinctMaterialIndices.Length != 1)
            return materialBindingIds[0];

        var materialIndex = distinctMaterialIndices[0];
        if (materialIndex < 0 || materialIndex >= materialBindingIds.Count)
            return null;

        return materialBindingIds[materialIndex];
    }

    private string GetOrCreateImageAsset(IBitmapTex bitmapTexture)
    {
        var sourcePath = bitmapTexture.MapName ?? string.Empty;

        if (string.IsNullOrWhiteSpace(sourcePath))
            return string.Empty;

        if (m_imageAssetIdsByPath.TryGetValue(sourcePath, out var existingImageAssetId))
            return existingImageAssetId;

        var fileName = Path.GetFileName(sourcePath);
        var imageAssetId = CreateUniquePrefixedId(m_usedImageAssetIds, "image", Path.GetFileNameWithoutExtension(fileName));
        m_imageAssetIdsByPath[sourcePath] = imageAssetId;

        if (m_exportedTextures.Add(bitmapTexture))
        {
            m_summary.ImageAssets.Add(new MaxSceneImageAssetSnapshotData
            {
                Id = imageAssetId,
                Name = string.IsNullOrWhiteSpace(fileName) ? imageAssetId : Path.GetFileNameWithoutExtension(fileName),
                SourcePath = sourcePath,
                RelativePath = string.IsNullOrWhiteSpace(fileName) ? sourcePath : $"textures/{fileName}",
                AssetKind = "ImageAsset"
            });
        }

        return imageAssetId;
    }

    private MaxSceneTransformSnapshotData ExtractLocalTransform(IINode node, IINode parentNode)
    {
        try
        {
            var nodeTm = node.GetNodeTM(m_coreInterface.Time, m_global.Interval.Create());
            var localTm = nodeTm;

            if (!parentNode.IsRootNode)
            {
                var parentTm = parentNode.GetNodeTM(m_coreInterface.Time, m_global.Interval.Create());
                var inverseParentTm = m_global.Matrix3.Create();
                m_global.Inverse(parentTm, inverseParentTm);
                var resultTm = m_global.Matrix3.Create();
                m_global.MatrixMultiply(nodeTm, inverseParentTm, resultTm);
                localTm = resultTm;
            }

            return ConvertMatrixToTransform(localTm);
        }
        catch
        {
            return new MaxSceneTransformSnapshotData();
        }
    }

    private MaxSceneTransformSnapshotData ConvertMatrixToTransform(IMatrix3 matrix)
    {
        var translation = matrix.Trans;
        var row0 = matrix.GetRow(0);
        var row1 = matrix.GetRow(1);
        var row2 = matrix.GetRow(2);
        var scaleX = m_global.Length(row0);
        var scaleY = m_global.Length(row1);
        var scaleZ = m_global.Length(row2);
        var rotationMatrix = m_global.Matrix3.Create(true);

        rotationMatrix.SetRow(0, NormalizeVector(row0, scaleX));
        rotationMatrix.SetRow(1, NormalizeVector(row1, scaleY));
        rotationMatrix.SetRow(2, NormalizeVector(row2, scaleZ));
        rotationMatrix.SetTrans(0, 0f);
        rotationMatrix.SetTrans(1, 0f);
        rotationMatrix.SetTrans(2, 0f);

        var rotation = m_global.Quat.Create(rotationMatrix);

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

    private IPoint3 NormalizeVector(IPoint3 vector, double length)
    {
        if (length <= 0d)
            return m_global.Point3.Create(0d, 0d, 0d);

        return m_global.Point3.Create(vector.X / length, vector.Y / length, vector.Z / length);
    }

    private MaxSceneVector2SnapshotData ExtractUv(IMesh mesh, int faceIndex, int vertexIndex)
    {
        try
        {
            if (!mesh.MapSupport(1))
                return new MaxSceneVector2SnapshotData();

            var mapFace = mesh.GetType().GetMethod("GetTVFace", BindingFlags.Instance | BindingFlags.Public)?.Invoke(mesh, [faceIndex]) as ITVFace
                          ?? TryGetIndexedValue(mesh.MapFaces(1), faceIndex) as ITVFace;

            if (mapFace is null)
                return new MaxSceneVector2SnapshotData();

            var tvIndex = (int)mapFace.GetTVert(vertexIndex);
            var uv = mesh.GetTVert(tvIndex);
            return new MaxSceneVector2SnapshotData { X = uv.X, Y = uv.Y };
        }
        catch
        {
            return new MaxSceneVector2SnapshotData();
        }
    }

    private static object? TryGetIndexedValue(object? source, int index)
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

    private static IBitmapTex? FindFirstBitmapTexture(IISubMap subMap)
    {
        for (var i = 0; i < subMap.NumSubTexmaps; i++)
        {
            var texture = subMap.GetSubTexmap(i);

            if (texture is IBitmapTex bitmapTexture && !string.IsNullOrWhiteSpace(bitmapTexture.MapName))
                return bitmapTexture;

            if (texture is IISubMap childSubMap)
            {
                var nestedBitmapTexture = FindFirstBitmapTexture(childSubMap);

                if (nestedBitmapTexture is not null)
                    return nestedBitmapTexture;
            }
        }

        return null;
    }

    private void CollectMaterialInfo(IMtlBase materialBase)
    {
        if (!m_materials.Add(materialBase))
            return;

        if (!string.IsNullOrWhiteSpace(materialBase.Name))
            m_materialNames.Add(materialBase.Name);

        if (materialBase is IMtl material)
        {
            for (var i = 0; i < material.NumSubMtls; i++)
            {
                var subMaterial = material.GetSubMtl(i);

                if (subMaterial is not null)
                    CollectMaterialInfo(subMaterial);
            }
        }

        if (materialBase is IISubMap subMap)
        {
            for (var i = 0; i < subMap.NumSubTexmaps; i++)
            {
                var texture = subMap.GetSubTexmap(i);

                if (texture is null || !m_textures.Add(texture))
                    continue;

                if (!string.IsNullOrWhiteSpace(texture.Name))
                    m_textureNames.Add(texture.Name);

                if (texture is IISubMap textureSubMap)
                    CollectTextureInfo(textureSubMap);
            }
        }
    }

    private void CollectTextureInfo(IISubMap subMap)
    {
        for (var i = 0; i < subMap.NumSubTexmaps; i++)
        {
            var texture = subMap.GetSubTexmap(i);

            if (texture is null || !m_textures.Add(texture))
                continue;

            if (!string.IsNullOrWhiteSpace(texture.Name))
                m_textureNames.Add(texture.Name);

            if (texture is IISubMap childSubMap)
                CollectTextureInfo(childSubMap);
        }
    }

    private static void AddName(List<string> names, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!names.Contains(value, StringComparer.OrdinalIgnoreCase))
            names.Add(value);
    }

    private DccLightKind ResolveLightKind(IINode node, ILightObject lightObject, double hotspotDegrees)
    {
        if (node.Name.Contains("sun", StringComparison.OrdinalIgnoreCase))
            return DccLightKind.Sun;

        return hotspotDegrees > 0.1d || lightObject.GetFallsize(0) > 0.1d
            ? DccLightKind.Spot
            : DccLightKind.Point;
    }

    private static double ResolveCameraClipDistance(ICameraObject cameraObject, int time, int which, double fallback)
    {
        try
        {
            return cameraObject.GetClipDist(time, which);
        }
        catch
        {
            return fallback;
        }
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * 180d / Math.PI;
    }

    private static string SanitizeId(string value)
    {
        return string.Concat(value.Trim().ToLowerInvariant().Select(me => char.IsLetterOrDigit(me) ? me : '_')).Trim('_');
    }

    private static double ResolveSpotAngleDegrees(DccLightKind kind, double hotspotDegrees)
    {
        if (kind != DccLightKind.Spot)
            return 45d;

        if (hotspotDegrees <= 0d || hotspotDegrees > 180d)
            return 45d;

        return hotspotDegrees;
    }

    private static bool UsesPerTriangleMaterialBinding(MaxSceneMeshSnapshotData meshSnapshot)
    {
        return meshSnapshot.MaterialIndices.Distinct().Skip(1).Any();
    }

    private static string CreateUniquePrefixedId(HashSet<string> usedIds, string prefix, string value)
    {
        var sanitizedValue = SanitizeId(value);

        if (string.IsNullOrWhiteSpace(sanitizedValue))
            sanitizedValue = prefix;

        var baseId = $"{prefix}:{sanitizedValue}";

        if (usedIds.Add(baseId))
            return baseId;

        for (var suffix = 2; ; suffix++)
        {
            var candidateId = $"{baseId}_{suffix}";

            if (usedIds.Add(candidateId))
                return candidateId;
        }
    }

    #endregion
}
