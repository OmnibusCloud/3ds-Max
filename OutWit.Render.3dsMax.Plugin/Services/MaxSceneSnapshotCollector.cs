using Autodesk.Max;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;
using System.IO;
using System.Linq;
using System.Numerics;
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
        CollectSceneContent(rootNode, rootNode, null);
        ReadEnvironment();
        ReadEnvironmentMap();
        m_summary.MaterialsCount = m_materials.Count;
        m_summary.TexturesCount = m_textures.Count;
        m_summary.MaterialNames = [.. m_materialNames];
        m_summary.TextureNames = [.. m_textureNames];
    }

    private void ReadEnvironment()
    {
        // 3ds Max scene environment/background colour. The default environment is black; treat a
        // near-black background as "no world" (leave EnvironmentColor null) so default scenes keep
        // the unchanged empty-world behaviour and only scenes with a set background carry a world.
        try
        {
            var background = m_coreInterface.GetBackGround(m_coreInterface.Time, m_global.Interval.Create());
            if (background is null)
                return;

            if (Math.Max(background.X, Math.Max(background.Y, background.Z)) < 0.004d)
                return;

            m_summary.EnvironmentColor = new MaxSceneColorSnapshotData
            {
                R = background.X,
                G = background.Y,
                B = background.Z,
                A = 1d
            };
        }
        catch
        {
            // Leave EnvironmentColor null on failure — the scene renders with the default world.
        }
    }

    private void ReadEnvironmentMap()
    {
        // 3ds Max scene environment map (Rendering > Environment). When a bitmap is assigned and
        // enabled, treat it as the equirectangular HDRI the generator builds the world from; the
        // image asset is registered so the neutral payload carries it. Any failure simply leaves the
        // environment image unset, so the scene falls back to the background colour / empty world.
        try
        {
            if (!m_coreInterface.UseEnvironmentMap)
                return;

            var environmentMap = m_coreInterface.EnvironmentMap;
            if (environmentMap is null)
                return;

            var bitmap = environmentMap as IBitmapTex
                         ?? (environmentMap is IISubMap subMap ? FindFirstBitmapTexture(subMap) : null);

            if (bitmap is null || string.IsNullOrWhiteSpace(bitmap.MapName))
                return;

            var imageAssetId = GetOrCreateImageAsset(bitmap);
            if (!string.IsNullOrWhiteSpace(imageAssetId))
                m_summary.EnvironmentImageId = imageAssetId;
        }
        catch
        {
            // Leave EnvironmentImageId null on failure — the scene renders with the colour world.
        }
    }

    // Walks the node hierarchy. Only mesh/camera/light nodes are emitted; non-geometry parents
    // (dummies, groups, bones, point helpers) are skipped. To avoid emitting a node whose ParentId
    // points at a skipped helper (the generator rejects a dangling parent reference), children of a
    // skipped node "re-parent" onto the nearest INCLUDED ancestor — tracked by effectiveParentNode /
    // effectiveParentId — and their local transform is computed relative to that ancestor (or world
    // when there is none).
    private void CollectSceneContent(IINode parentNode, IINode effectiveParentNode, string? effectiveParentId)
    {
        for (var i = 0; i < parentNode.NumberOfChildren; i++)
        {
            var childNode = parentNode.GetChildNode(i);
            m_summary.NodesCount++;
            DetectMotionBlur(childNode);

            var objectState = childNode.ObjectRef.Eval(m_coreInterface.Time);
            var sceneObject = objectState.Obj;
            var nodeId = $"node:{childNode.Handle}";
            var parentId = effectiveParentId;
            var localTransform = ExtractLocalTransform(childNode, effectiveParentNode);
            var transformKeyframes = SampleTransformKeyframes(childNode, effectiveParentNode);
            var added = false;

            if (sceneObject is ICameraObject cameraObject)
            {
                added = true;
                m_summary.CamerasCount++;
                AddName(m_summary.CameraNames, childNode.Name);
                var cameraId = $"camera:{childNode.Handle}";

                // Respect a target camera's exact aim: 3ds Max target cameras look at a separate
                // target node whose direction the raw matrix→quaternion decomposition does not encode
                // reliably. When present (and the camera is top-level so local == world), rebuild the
                // orientation from the real look direction so the user's framing survives the round trip.
                if (effectiveParentNode.IsRootNode && TryResolveTargetLookRotation(childNode, out var aimedRotation))
                    localTransform.Rotation = aimedRotation;

                m_summary.Nodes.Add(new MaxSceneNodeSnapshotData
                {
                    Id = nodeId,
                    Name = childNode.Name,
                    ParentId = parentId,
                    Kind = DccNodeKind.Camera,
                    LocalTransform = localTransform,
                    TransformKeyframes = transformKeyframes,
                    CameraId = cameraId,
                    Visible = !childNode.IsNodeHidden(false),
                    Renderable = childNode.Renderable
                });
                m_summary.Cameras.Add(ExtractCamera(childNode, cameraObject, cameraId));
            }

            if (sceneObject is ILightObject lightObject)
            {
                var lightId = $"light:{childNode.Handle}";
                var lightSnapshot = ExtractLight(childNode, lightObject, lightId);

                // A light that is off or carries no positive intensity contributes nothing and would
                // fail Dcc validation ("light requires positive intensity"), aborting the whole scene.
                // Drop it here so the rest of the scene still renders; the export service surfaces a
                // Warning from SkippedInactiveLightCount.
                if (IsLightActive(lightObject) && lightSnapshot.Intensity > 0d)
                {
                    added = true;
                    m_summary.LightsCount++;
                    AddName(m_summary.LightNames, childNode.Name);
                    m_summary.Nodes.Add(new MaxSceneNodeSnapshotData
                    {
                        Id = nodeId,
                        Name = childNode.Name,
                        ParentId = parentId,
                        Kind = DccNodeKind.Light,
                        LocalTransform = localTransform,
                        TransformKeyframes = transformKeyframes,
                        LightId = lightId,
                        Visible = !childNode.IsNodeHidden(false),
                        Renderable = childNode.Renderable
                    });
                    m_summary.Lights.Add(lightSnapshot);
                }
                else
                {
                    m_summary.SkippedInactiveLightCount++;
                }
            }

            if (sceneObject.CanConvertToType(m_global.TriObjectClassID) == 1)
            {
                var meshId = $"mesh:{childNode.Handle}";
                var meshSnapshot = ExtractMesh(childNode, sceneObject, meshId);

                // A mesh with no vertices (a helper/degenerate object that still converts to a
                // TriObject) fails Dcc validation ("mesh requires positions"), aborting the whole
                // scene. Drop it so the rest renders; the export service surfaces a Warning from
                // SkippedEmptyMeshCount. Children re-parent onto the nearest INCLUDED ancestor.
                if (meshSnapshot.Positions.Count == 0)
                {
                    m_summary.SkippedEmptyMeshCount++;
                }
                else
                {
                    added = true;
                    m_summary.MeshesCount++;
                    SampleDeformationFrames(childNode, meshSnapshot);
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
                        TransformKeyframes = transformKeyframes,
                        MeshId = meshId,
                        MaterialBindingId = materialBindingId,
                        Visible = !childNode.IsNodeHidden(false),
                        Renderable = childNode.Renderable
                    });
                    m_summary.Meshes.Add(meshSnapshot);
                }
            }

            // Children re-parent onto this node only if it was emitted; otherwise they keep the
            // current effective ancestor so a skipped helper never becomes a dangling ParentId.
            var childEffectiveParentNode = added ? childNode : effectiveParentNode;
            var childEffectiveParentId = added ? nodeId : effectiveParentId;
            CollectSceneContent(childNode, childEffectiveParentNode, childEffectiveParentId);
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

        // Compute the smoothing-group-aware vertex normals so curved surfaces export smooth and
        // hard edges (smoothing-group boundaries / unsmoothed faces) stay hard.
        mesh.BuildNormals();

        // Map channel 1 is the primary UV (Uv0); channel 2 is the optional second UV set. Only
        // populate Uv1 when the mesh actually carries channel 2, so meshes with a single UV set
        // keep an empty Uv1 (the mapper then emits no second layer).
        var hasSecondUv = MeshSupportsMapChannel(mesh, 2);

        // Map channel 0 is the vertex-colour channel (a colour stored per map-vert as RGB). The flag
        // gates every corner uniformly so Colors stays aligned 1:1 with Positions (a 1.4.0 contract
        // guard) — either every corner gets a colour or none do.
        var hasVertexColors = MeshSupportsMapChannel(mesh, 0);

        for (var faceIndex = 0; faceIndex < mesh.NumFaces; faceIndex++)
        {
            var face = mesh.GetFace(faceIndex);
            var faceNormal = mesh.GetFaceNormal(faceIndex);

            for (var vertexIndex = 0; vertexIndex < 3; vertexIndex++)
            {
                var sourceVertexIndex = (int)face.GetVert(vertexIndex);
                var point = mesh.GetVert(sourceVertexIndex);
                var normal = ResolveVertexNormal(mesh, face, sourceVertexIndex, faceNormal);
                meshData.Positions.Add(new MaxSceneVector3SnapshotData { X = point.X, Y = point.Y, Z = point.Z });
                meshData.Normals.Add(new MaxSceneVector3SnapshotData { X = normal.X, Y = normal.Y, Z = normal.Z });
                meshData.Uv0.Add(ExtractUv(mesh, faceIndex, vertexIndex));
                if (hasSecondUv)
                    meshData.Uv1.Add(ExtractUvFromChannel(mesh, 2, faceIndex, vertexIndex));
                if (hasVertexColors)
                    meshData.Colors.Add(ExtractVertexColor(mesh, faceIndex, vertexIndex));
                meshData.TriangleIndices.Add(meshData.TriangleIndices.Count);
            }

            meshData.MaterialIndices.Add(mesh.GetFaceMtlIndex(faceIndex));
        }

        return meshData;
    }

    private static double ReadMetalness(IMtl material, int time)
    {
        // The legacy IMtl surface has no PBR metalness; modern materials (Physical Material) expose
        // it as a parameter-block float named "metalness". Look it up by parameter internal name so
        // it works regardless of the material class, clamp, and treat any non-PBR material or read
        // failure as non-metallic.
        try
        {
            for (var blockIndex = 0; blockIndex < material.NumParamBlocks; blockIndex++)
            {
                if (material.GetParamBlock(blockIndex) is not IIParamBlock2 block)
                    continue;

                for (var paramIndex = 0u; paramIndex < block.NumParams; paramIndex++)
                {
                    var def = block.GetParamDefByIndex(paramIndex);
                    var name = def.IntName?.ToLowerInvariant();
                    if (name is "metalness" or "metallic" or "metal")
                        return Math.Clamp(block.GetFloat(def.Id, time, 0), 0d, 1d);
                }
            }
        }
        catch
        {
        }

        return 0d;
    }

    private void ReadMaterialTextureSlots(IMtl material, MaxSceneMaterialSnapshotData materialSnapshot)
    {
        // Route each of the material's texture slots to the matching neutral slot by its slot
        // name, so PBR maps (normal/roughness/metalness/opacity) survive — not just the base
        // colour. Slot-name matching is renderer-agnostic (Standard, Physical, and most third-party
        // shaders all name their slots descriptively).
        var assignedSlots = new HashSet<DccTextureSlotKind>();

        for (var i = 0; i < material.NumSubTexmaps; i++)
        {
            var slotKind = ClassifyTextureSlot(material.GetSubTexmapSlotName(i, false));
            if (slotKind is null || assignedSlots.Contains(slotKind.Value))
                continue;

            var texmap = material.GetSubTexmap(i);
            var bitmap = texmap as IBitmapTex
                         ?? (texmap is IISubMap subMap ? FindFirstBitmapTexture(subMap) : null);

            if (bitmap is null || string.IsNullOrWhiteSpace(bitmap.MapName))
                continue;

            var imageAssetId = GetOrCreateImageAsset(bitmap);
            if (string.IsNullOrWhiteSpace(imageAssetId))
                continue;

            materialSnapshot.TextureSlots.Add(new MaxSceneTextureSlotSnapshotData
            {
                Slot = slotKind.Value,
                ImageAssetId = imageAssetId
            });
            assignedSlots.Add(slotKind.Value);

            if (slotKind.Value == DccTextureSlotKind.Displacement)
                materialSnapshot.DisplacementScale = ReadDisplacementScale(material, m_coreInterface.Time);
        }

        // Fallback: a material with a bitmap but no recognised slot name still gets its base colour.
        if (!assignedSlots.Contains(DccTextureSlotKind.BaseColor))
        {
            var fallbackBitmap = FindFirstBitmapTexture(material);
            if (fallbackBitmap is not null && !string.IsNullOrWhiteSpace(fallbackBitmap.MapName))
            {
                var imageAssetId = GetOrCreateImageAsset(fallbackBitmap);
                if (!string.IsNullOrWhiteSpace(imageAssetId))
                {
                    materialSnapshot.TextureSlots.Add(new MaxSceneTextureSlotSnapshotData
                    {
                        Slot = DccTextureSlotKind.BaseColor,
                        ImageAssetId = imageAssetId
                    });
                }
            }
        }
    }

    private static DccTextureSlotKind? ClassifyTextureSlot(string? slotName)
    {
        if (string.IsNullOrWhiteSpace(slotName))
            return null;

        var name = slotName.ToLowerInvariant();

        if (name.Contains("base color") || name.Contains("base colour") || name.Contains("diffuse") || name.Contains("albedo"))
            return DccTextureSlotKind.BaseColor;

        if (name.Contains("normal") || name.Contains("bump"))
            return DccTextureSlotKind.Normal;

        if (name.Contains("roughness"))
            return DccTextureSlotKind.Roughness;

        if (name.Contains("metal"))
            return DccTextureSlotKind.Metallic;

        if (name.Contains("opacity") || name.Contains("transparency") || name.Contains("cutout"))
            return DccTextureSlotKind.Opacity;

        // Checked after "bump" so a plain bump map still routes to the normal slot; only a slot named
        // explicitly for displacement drives real geometry displacement.
        if (name.Contains("displacement") || name.Contains("displace"))
            return DccTextureSlotKind.Displacement;

        return null;
    }

    private static IPoint3 ResolveVertexNormal(IMesh mesh, IFace face, int vertexIndex, IPoint3 faceNormal)
    {
        // 3ds Max stores per-vertex render normals split by smoothing group. A face with no
        // smoothing group (0) is hard → keep the face normal. A vertex that resolves to a single
        // render normal is fully smooth → use it. A vertex split across smoothing groups (a hard
        // edge or group boundary) keeps the hard face normal — the managed RVertex API exposes
        // only the primary split normal, so the face normal is the safe, predictable choice there.
        try
        {
            var smoothingGroup = face.SmGroup;
            if (smoothingGroup == 0)
                return faceNormal;

            var renderVertex = mesh.GetRVertPtr(vertexIndex);
            if (renderVertex == null)
                return faceNormal;

            var normalCount = (int)(renderVertex.RFlags & 0xFFFF);
            if (normalCount == 1)
                return renderVertex.Rn.Normal;

            return faceNormal;
        }
        catch
        {
            return faceNormal;
        }
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
        var time = m_coreInterface.Time;
        var snapshot = new MaxSceneCameraSnapshotData
        {
            Id = cameraId,
            Name = node.Name,
            VerticalFovDegrees = RadiansToDegrees(cameraObject.GetFOV(time)),
            NearClip = ResolveCameraClipDistance(cameraObject, time, 0, 0.1d),
            FarClip = ResolveCameraClipDistance(cameraObject, time, 1, 1000d),
            IsPerspective = !cameraObject.IsOrtho
        };

        ReadCameraDepthOfField(cameraObject, time, snapshot);
        SampleCameraPropertyKeyframes(cameraObject, snapshot);
        return snapshot;
    }

    private void ReadCameraDepthOfField(ICameraObject cameraObject, int time, MaxSceneCameraSnapshotData snapshot)
    {
        // 3ds Max exposes DOF through the multi-pass camera effect. When it is enabled and the
        // camera has a target distance, treat that as the focus distance; read the effect's f-stop
        // from its parameter block if present, otherwise leave the default. Any read failure simply
        // leaves DOF disabled rather than failing the export.
        try
        {
            var enabled = cameraObject.GetMultiPassEffectEnabled(time, m_global.Interval.Create());
            var focusDistance = cameraObject.GetTDist(time);
            if (!enabled || focusDistance <= 0d)
                return;

            snapshot.EnableDepthOfField = true;
            snapshot.FocusDistance = focusDistance;

            var fStop = ReadCameraFStop(cameraObject, time);
            if (fStop is > 0d)
                snapshot.FStop = fStop.Value;
        }
        catch
        {
            // Leave DOF disabled on failure.
        }
    }

    private static double? ReadCameraFStop(ICameraObject cameraObject, int time)
    {
        try
        {
            var effect = cameraObject.IMultiPassCameraEffect;
            if (effect is null)
                return null;

            for (var blockIndex = 0; blockIndex < effect.NumParamBlocks; blockIndex++)
            {
                if (effect.GetParamBlock(blockIndex) is not IIParamBlock2 block)
                    continue;

                for (var paramIndex = 0u; paramIndex < block.NumParams; paramIndex++)
                {
                    var def = block.GetParamDefByIndex(paramIndex);
                    var name = def.IntName?.ToLowerInvariant();
                    if (name is not null && (name.Contains("fstop") || name.Contains("f_stop") || name.Contains("fnumber")))
                    {
                        var value = block.GetFloat(def.Id, time, 0);
                        if (value > 0d)
                            return value;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private MaxSceneLightSnapshotData ExtractLight(IINode node, ILightObject lightObject, string lightId)
    {
        var time = m_coreInterface.Time;
        var color = lightObject.GetRGBColor(time);
        var hotspotDegrees = RadiansToDegrees(lightObject.GetHotspot(time));

        var isArea = TryResolveAreaLight(lightObject, time, out var areaWidth, out var areaHeight);
        var kind = isArea ? DccLightKind.Area : ResolveLightKind(node, lightObject, hotspotDegrees);

        var snapshot = new MaxSceneLightSnapshotData
        {
            Id = lightId,
            Name = node.Name,
            Kind = kind,
            Color = new MaxSceneColorSnapshotData { R = color.X, G = color.Y, B = color.Z, A = 1d },
            Intensity = lightObject.GetIntensity(time),
            Range = Math.Max(lightObject.GetTDist(time), 0.01d),
            SpotAngleDegrees = ResolveSpotAngleDegrees(kind, hotspotDegrees),
            CastShadows = ReadCastShadows(lightObject),
            AreaWidth = isArea ? areaWidth : 1d,
            AreaHeight = isArea ? areaHeight : 1d
        };

        SampleLightPropertyKeyframes(lightObject, snapshot);
        return snapshot;
    }

    private static bool TryResolveAreaLight(ILightObject lightObject, int time, out double width, out double height)
    {
        // Photometric lights (ILightscapeLight) carry an explicit area shape. A rectangle/area light
        // has a real width and length; disc/sphere/cylinder lights expose a radius (mapped to a
        // square area). Point/linear photometric lights have neither and fall through to the
        // point/spot heuristic. Non-photometric lights are never area lights.
        width = 1d;
        height = 1d;

        try
        {
            if (lightObject is not ILightscapeLight photometric)
                return false;

            var w = photometric.GetWidth(time);
            var l = photometric.GetLength(time);
            if (w > 0.001d && l > 0.001d)
            {
                width = w;
                height = l;
                return true;
            }

            var radius = photometric.GetRadius(time);
            if (radius > 0.001d)
            {
                width = radius * 2d;
                height = radius * 2d;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool ReadCastShadows(ILightObject lightObject)
    {
        try
        {
            return lightObject.Shadow != 0;
        }
        catch
        {
            return true;
        }
    }

    private bool TryResolveTargetLookRotation(IINode cameraNode, out MaxSceneQuaternionSnapshotData rotation)
    {
        rotation = new MaxSceneQuaternionSnapshotData { W = 1d };

        try
        {
            // IINode.Target is the look-at target of a target camera/light (null for a free camera).
            var target = cameraNode.GetType().GetProperty("Target", BindingFlags.Instance | BindingFlags.Public)?.GetValue(cameraNode) as IINode;
            if (target == null)
                return false;

            var time = m_coreInterface.Time;
            var camPos = cameraNode.GetNodeTM(time, m_global.Interval.Create()).Trans;
            var targetPos = target.GetNodeTM(time, m_global.Interval.Create()).Trans;

            var forward = Vector3.Normalize(new Vector3(targetPos.X - camPos.X, targetPos.Y - camPos.Y, targetPos.Z - camPos.Z));
            if (!float.IsFinite(forward.X) || forward.LengthSquared() < 1e-8f)
                return false;

            rotation = LookAtRotation(forward);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Builds the captured-convention quaternion whose local -Y points along <paramref name="forward"/>
    // and local +Z is world up — the same convention MaxSceneCameraFramer uses so the Blender
    // generator's rotation @ RotX(-90deg) correction aims the camera correctly.
    private static MaxSceneQuaternionSnapshotData LookAtRotation(Vector3 forward)
    {
        var worldUp = new Vector3(0f, 0f, 1f);
        var yAxis = -forward;
        var zAxis = worldUp - Vector3.Dot(worldUp, yAxis) * yAxis;
        if (zAxis.LengthSquared() < 1e-6f)
            zAxis = new Vector3(0f, 1f, 0f) - Vector3.Dot(new Vector3(0f, 1f, 0f), yAxis) * yAxis;
        zAxis = Vector3.Normalize(zAxis);
        var xAxis = Vector3.Normalize(Vector3.Cross(yAxis, zAxis));

        var matrix = new Matrix4x4(
            xAxis.X, xAxis.Y, xAxis.Z, 0f,
            yAxis.X, yAxis.Y, yAxis.Z, 0f,
            zAxis.X, zAxis.Y, zAxis.Z, 0f,
            0f, 0f, 0f, 1f);
        var q = Quaternion.CreateFromRotationMatrix(matrix);
        return new MaxSceneQuaternionSnapshotData { X = q.X, Y = q.Y, Z = q.Z, W = q.W };
    }

    private bool IsLightActive(ILightObject lightObject)
    {
        // A light switched off in 3ds Max (the "On" checkbox) contributes nothing and would export a
        // dead source. GetUseLight reads that flag (0 = off). Its managed overload varies (no-arg vs
        // time), so resolve it reflectively and default to active on any failure — an API quirk must
        // never silently drop a real light.
        try
        {
            var method = lightObject.GetType().GetMethod("GetUseLight", [typeof(int)]);
            if (method != null && method.Invoke(lightObject, [m_coreInterface.Time]) is int withTime)
                return withTime != 0;

            method = lightObject.GetType().GetMethod("GetUseLight", Type.EmptyTypes);
            if (method?.Invoke(lightObject, null) is int noArg)
                return noArg != 0;
        }
        catch
        {
        }

        return true;
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
        ReadMaterialTextureSlots(material, materialSnapshot);

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

            snapshot.Metallic = ReadMetalness(material, time);
        }
        catch
        {
            // Leave the snapshot defaults in place for material types that do not expose the
            // legacy IMtl getters (e.g. some renderer-specific shaders).
        }
    }

    private static void NormalizeMaterialIndices(MaxSceneMeshSnapshotData meshSnapshot, MaxMaterialBindingMap materialBindingMap)
    {
        if (meshSnapshot.MaterialIndices.Count == 0)
            return;

        // With no resolvable material binding (e.g. the node has no material, or a Multi material whose
        // sub-materials didn't resolve) the raw per-face indices are meaningless and could be anything
        // 3ds Max left on the faces (sparse Multi/Sub-Object ids reach into the hundreds). The mesh then
        // has no materials at all, so ANY per-face index is out of range for the generator — drop them
        // entirely so the mesh renders with a single default material. When a binding exists,
        // NormalizeMaterialIndex clamps each index into range below.
        if (materialBindingMap.MaterialIds.Count == 0)
        {
            meshSnapshot.MaterialIndices.Clear();
            return;
        }

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
        return ExtractLocalTransformAtTime(node, parentNode, m_coreInterface.Time);
    }

    private MaxSceneTransformSnapshotData ExtractLocalTransformAtTime(IINode node, IINode parentNode, int time)
    {
        try
        {
            var nodeTm = node.GetNodeTM(time, m_global.Interval.Create());
            var localTm = nodeTm;

            if (!parentNode.IsRootNode)
            {
                var parentTm = parentNode.GetNodeTM(time, m_global.Interval.Create());
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

    private List<MaxSceneTransformKeyframeSnapshotData> SampleTransformKeyframes(IINode node, IINode parentNode)
    {
        var keyframes = new List<MaxSceneTransformKeyframeSnapshotData>();

        var frameStart = m_summary.FrameStart;
        var frameEnd = m_summary.FrameEnd;
        if (frameEnd <= frameStart)
            return keyframes;

        var ticksPerFrame = 4800 / Math.Max(m_summary.FrameRate, 1);

        var samples = new List<MaxSceneTransformKeyframeSnapshotData>();
        for (var frame = frameStart; frame <= frameEnd; frame++)
        {
            samples.Add(new MaxSceneTransformKeyframeSnapshotData
            {
                Frame = frame,
                Transform = ExtractLocalTransformAtTime(node, parentNode, frame * ticksPerFrame)
            });
        }

        // Only emit keyframes when the node actually moves over the range; a static node keeps just
        // its single LocalTransform and produces no animation.
        if (!IsTransformAnimated(samples))
            return keyframes;

        return samples;
    }

    private static bool IsTransformAnimated(List<MaxSceneTransformKeyframeSnapshotData> samples)
    {
        if (samples.Count < 2)
            return false;

        var first = samples[0].Transform;
        for (var i = 1; i < samples.Count; i++)
        {
            var t = samples[i].Transform;
            if (!IsClose(t.Translation, first.Translation)
                || !IsClose(t.Scale, first.Scale)
                || !IsClose(t.Rotation, first.Rotation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsClose(MaxSceneVector3SnapshotData a, MaxSceneVector3SnapshotData b)
    {
        const double epsilon = 1e-4;
        return Math.Abs(a.X - b.X) < epsilon && Math.Abs(a.Y - b.Y) < epsilon && Math.Abs(a.Z - b.Z) < epsilon;
    }

    private static bool IsClose(MaxSceneQuaternionSnapshotData a, MaxSceneQuaternionSnapshotData b)
    {
        const double epsilon = 1e-4;
        return Math.Abs(a.X - b.X) < epsilon && Math.Abs(a.Y - b.Y) < epsilon
            && Math.Abs(a.Z - b.Z) < epsilon && Math.Abs(a.W - b.W) < epsilon;
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

    private static bool MeshSupportsMapChannel(IMesh mesh, int channel)
    {
        try
        {
            return mesh.MapSupport(channel);
        }
        catch
        {
            return false;
        }
    }

    // Channel-agnostic UV read (used for map channel 2+). GetTVert is channel-1-specific, so for
    // other channels we go through MapFaces(channel)/MapVerts(channel). The map-vert element type
    // is not a typed interface in the managed SDK, so X/Y are read reflectively.
    private MaxSceneVector2SnapshotData ExtractUvFromChannel(IMesh mesh, int channel, int faceIndex, int vertexIndex)
    {
        try
        {
            if (!mesh.MapSupport(channel))
                return new MaxSceneVector2SnapshotData();

            if (TryGetIndexedValue(mesh.MapFaces(channel), faceIndex) is not ITVFace mapFace)
                return new MaxSceneVector2SnapshotData();

            var tvIndex = (int)mapFace.GetTVert(vertexIndex);
            var vert = TryGetIndexedValue(mesh.MapVerts(channel), tvIndex);
            if (vert is null)
                return new MaxSceneVector2SnapshotData();

            var vertType = vert.GetType();
            var x = Convert.ToDouble(vertType.GetProperty("X")?.GetValue(vert) ?? 0d);
            var y = Convert.ToDouble(vertType.GetProperty("Y")?.GetValue(vert) ?? 0d);
            return new MaxSceneVector2SnapshotData { X = x, Y = y };
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
        if (IsSunLike(node, lightObject))
            return DccLightKind.Sun;

        return hotspotDegrees > 0.1d || lightObject.GetFallsize(0) > 0.1d
            ? DccLightKind.Spot
            : DccLightKind.Point;
    }

    private static bool IsSunLike(IINode node, ILightObject lightObject)
    {
        // A real Daylight/Sunlight system (IES Sun, MR/Physical Sun, Sun Positioner) exposes its
        // directional nature through the light object's class name. Match that first, then keep the
        // node-name heuristic as a fallback for lights a user has simply named "Sun".
        try
        {
            var className = lightObject.ClassName(false);
            if (!string.IsNullOrEmpty(className)
                && (className.Contains("sun", StringComparison.OrdinalIgnoreCase)
                    || className.Contains("daylight", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        catch
        {
            // Fall through to the node-name heuristic when the class name is unavailable.
        }

        return node.Name.Contains("sun", StringComparison.OrdinalIgnoreCase)
               || node.Name.Contains("daylight", StringComparison.OrdinalIgnoreCase);
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

    private void DetectMotionBlur(IINode node)
    {
        // Any renderable node with 3ds Max object/image motion blur enabled flips the scene-level
        // motion-blur flag; Blender's render-side motion blur is a single switch, so one enabled node
        // is enough. The shutter stays at the neutral default (0.5).
        if (m_summary.MotionBlur)
            return;

        try
        {
            if (node.GetMotBlurOnOff(m_coreInterface.Time))
                m_summary.MotionBlur = true;
        }
        catch
        {
            // Leave motion blur unset on failure — the render runs without motion blur.
        }
    }

    private MaxSceneColorSnapshotData ExtractVertexColor(IMesh mesh, int faceIndex, int vertexIndex)
    {
        // Vertex colours live in map channel 0; each map-vert is a Point3 whose X/Y/Z carry R/G/B. The
        // map-vert element is not a typed interface in the managed SDK, so the components are read
        // reflectively — the same approach ExtractUvFromChannel uses for non-primary UV channels.
        try
        {
            if (!mesh.MapSupport(0))
                return new MaxSceneColorSnapshotData();

            if (TryGetIndexedValue(mesh.MapFaces(0), faceIndex) is not ITVFace mapFace)
                return new MaxSceneColorSnapshotData();

            var colorVertIndex = (int)mapFace.GetTVert(vertexIndex);
            var vert = TryGetIndexedValue(mesh.MapVerts(0), colorVertIndex);
            if (vert is null)
                return new MaxSceneColorSnapshotData();

            var vertType = vert.GetType();
            var r = Convert.ToDouble(vertType.GetProperty("X")?.GetValue(vert) ?? 1d);
            var g = Convert.ToDouble(vertType.GetProperty("Y")?.GetValue(vert) ?? 1d);
            var b = Convert.ToDouble(vertType.GetProperty("Z")?.GetValue(vert) ?? 1d);
            return new MaxSceneColorSnapshotData { R = r, G = g, B = b, A = 1d };
        }
        catch
        {
            return new MaxSceneColorSnapshotData();
        }
    }

    private static double ReadDisplacementScale(IMtl material, int time)
    {
        // The displacement amount is renderer-specific (Standard/Physical/third-party each name it
        // differently) with no typed managed getter, so look it up by parameter internal name in the
        // material's parameter blocks. Default to the contract default (1.0) when not found.
        try
        {
            for (var blockIndex = 0; blockIndex < material.NumParamBlocks; blockIndex++)
            {
                if (material.GetParamBlock(blockIndex) is not IIParamBlock2 block)
                    continue;

                for (var paramIndex = 0u; paramIndex < block.NumParams; paramIndex++)
                {
                    var def = block.GetParamDefByIndex(paramIndex);
                    var name = def.IntName?.ToLowerInvariant();
                    if (name is null || !name.Contains("displac"))
                        continue;

                    if (name.Contains("amount") || name.Contains("amt") || name.Contains("scale")
                        || name.Contains("strength") || name.Contains("height") || name.Contains("depth"))
                    {
                        var value = block.GetFloat(def.Id, time, 0);
                        if (value > 0d)
                            return value;
                    }
                }
            }
        }
        catch
        {
        }

        return 1d;
    }

    private void SampleDeformationFrames(IINode node, MaxSceneMeshSnapshotData meshSnapshot)
    {
        // Bake per-frame object-space vertex positions (approach A). Re-evaluating the node's object at
        // each frame captures the result of any deformation (skin/morph/cloth/sim) as geometry. Object
        // space keeps deformation separate from the node's rigid transform (sampled as transform
        // keyframes). Only emitted when the mesh actually deforms and its topology stays constant over
        // the range — otherwise it cannot be represented as Blender shape keys.
        var frameStart = m_summary.FrameStart;
        var frameEnd = m_summary.FrameEnd;
        if (frameEnd <= frameStart || meshSnapshot.Positions.Count == 0)
            return;

        var ticksPerFrame = 4800 / Math.Max(m_summary.FrameRate, 1);
        var baseCornerCount = meshSnapshot.Positions.Count;
        var frames = new List<MaxSceneMeshDeformationFrameSnapshotData>();
        var anyDeformation = false;

        for (var frame = frameStart; frame <= frameEnd; frame++)
        {
            var positions = SampleMeshCornerPositionsAtTime(node, frame * ticksPerFrame, baseCornerCount);
            if (positions is null)
                return; // topology changed or read failed → skip deformation entirely

            frames.Add(new MaxSceneMeshDeformationFrameSnapshotData { Frame = frame, Positions = positions });

            if (!anyDeformation && CornersDiffer(positions, meshSnapshot.Positions))
                anyDeformation = true;
        }

        if (anyDeformation)
            meshSnapshot.DeformationFrames = frames;
    }

    private List<MaxSceneVector3SnapshotData>? SampleMeshCornerPositionsAtTime(IINode node, int time, int expectedCornerCount)
    {
        try
        {
            var sceneObject = node.ObjectRef.Eval(time).Obj;
            if (sceneObject is null || sceneObject.CanConvertToType(m_global.TriObjectClassID) != 1)
                return null;

            if (sceneObject.ConvertToType(time, m_global.TriObjectClassID) is not ITriObject triObject)
                return null;

            var mesh = triObject.Mesh;
            var positions = new List<MaxSceneVector3SnapshotData>(expectedCornerCount);

            for (var faceIndex = 0; faceIndex < mesh.NumFaces; faceIndex++)
            {
                var face = mesh.GetFace(faceIndex);
                for (var vertexIndex = 0; vertexIndex < 3; vertexIndex++)
                {
                    var point = mesh.GetVert((int)face.GetVert(vertexIndex));
                    positions.Add(new MaxSceneVector3SnapshotData { X = point.X, Y = point.Y, Z = point.Z });
                }
            }

            return positions.Count == expectedCornerCount ? positions : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool CornersDiffer(List<MaxSceneVector3SnapshotData> candidate, List<MaxSceneVector3SnapshotData> baseline)
    {
        const double epsilon = 1e-4;
        if (candidate.Count != baseline.Count)
            return true;

        for (var i = 0; i < candidate.Count; i++)
        {
            if (Math.Abs(candidate[i].X - baseline[i].X) > epsilon
                || Math.Abs(candidate[i].Y - baseline[i].Y) > epsilon
                || Math.Abs(candidate[i].Z - baseline[i].Z) > epsilon)
            {
                return true;
            }
        }

        return false;
    }

    private void SampleLightPropertyKeyframes(ILightObject lightObject, MaxSceneLightSnapshotData snapshot)
    {
        var frameStart = m_summary.FrameStart;
        var frameEnd = m_summary.FrameEnd;
        if (frameEnd <= frameStart)
            return;

        var ticksPerFrame = 4800 / Math.Max(m_summary.FrameRate, 1);

        snapshot.IntensityKeyframes = SampleScalarChannel(frameStart, frameEnd, ticksPerFrame, time => lightObject.GetIntensity(time));
        snapshot.RangeKeyframes = SampleScalarChannel(frameStart, frameEnd, ticksPerFrame, time => Math.Max(lightObject.GetTDist(time), 0.01d));
        snapshot.ColorKeyframes = SampleColorChannel(frameStart, frameEnd, ticksPerFrame, time =>
        {
            var color = lightObject.GetRGBColor(time);
            return new MaxSceneColorSnapshotData { R = color.X, G = color.Y, B = color.Z, A = 1d };
        });

        if (snapshot.Kind == DccLightKind.Spot)
        {
            snapshot.SpotAngleKeyframes = SampleScalarChannel(frameStart, frameEnd, ticksPerFrame,
                time => ResolveSpotAngleDegrees(DccLightKind.Spot, RadiansToDegrees(lightObject.GetHotspot(time))));
        }
    }

    private void SampleCameraPropertyKeyframes(ICameraObject cameraObject, MaxSceneCameraSnapshotData snapshot)
    {
        var frameStart = m_summary.FrameStart;
        var frameEnd = m_summary.FrameEnd;
        if (frameEnd <= frameStart)
            return;

        var ticksPerFrame = 4800 / Math.Max(m_summary.FrameRate, 1);

        snapshot.VerticalFovKeyframes = SampleScalarChannel(frameStart, frameEnd, ticksPerFrame, time => RadiansToDegrees(cameraObject.GetFOV(time)));
        snapshot.NearClipKeyframes = SampleScalarChannel(frameStart, frameEnd, ticksPerFrame, time => ResolveCameraClipDistance(cameraObject, time, 0, 0.1d));
        snapshot.FarClipKeyframes = SampleScalarChannel(frameStart, frameEnd, ticksPerFrame, time => ResolveCameraClipDistance(cameraObject, time, 1, 1000d));
    }

    private static List<MaxSceneScalarKeyframeSnapshotData> SampleScalarChannel(int frameStart, int frameEnd, int ticksPerFrame, Func<int, double> sampler)
    {
        // Sample the channel at every integer frame; emit keyframes only when the value actually varies
        // so static channels add nothing. A read failure on any frame drops the whole channel (no
        // partial/misaligned keyframes) and the static value is used instead.
        try
        {
            var samples = new List<MaxSceneScalarKeyframeSnapshotData>(frameEnd - frameStart + 1);
            for (var frame = frameStart; frame <= frameEnd; frame++)
                samples.Add(new MaxSceneScalarKeyframeSnapshotData { Frame = frame, Value = sampler(frame * ticksPerFrame) });

            return ScalarChannelVaries(samples) ? samples : [];
        }
        catch
        {
            return [];
        }
    }

    private static List<MaxSceneColorKeyframeSnapshotData> SampleColorChannel(int frameStart, int frameEnd, int ticksPerFrame, Func<int, MaxSceneColorSnapshotData> sampler)
    {
        try
        {
            var samples = new List<MaxSceneColorKeyframeSnapshotData>(frameEnd - frameStart + 1);
            for (var frame = frameStart; frame <= frameEnd; frame++)
                samples.Add(new MaxSceneColorKeyframeSnapshotData { Frame = frame, Color = sampler(frame * ticksPerFrame) });

            return ColorChannelVaries(samples) ? samples : [];
        }
        catch
        {
            return [];
        }
    }

    private static bool ScalarChannelVaries(List<MaxSceneScalarKeyframeSnapshotData> samples)
    {
        if (samples.Count < 2)
            return false;

        var first = samples[0].Value;
        return samples.Skip(1).Any(me => Math.Abs(me.Value - first) > 1e-4);
    }

    private static bool ColorChannelVaries(List<MaxSceneColorKeyframeSnapshotData> samples)
    {
        if (samples.Count < 2)
            return false;

        var first = samples[0].Color;
        return samples.Skip(1).Any(me => Math.Abs(me.Color.R - first.R) > 1e-4
                                         || Math.Abs(me.Color.G - first.G) > 1e-4
                                         || Math.Abs(me.Color.B - first.B) > 1e-4);
    }

    #endregion
}
