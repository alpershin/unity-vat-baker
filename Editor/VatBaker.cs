using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Alpershin.Vat.EditorTools
{
    /// <summary>
    /// Fills a <see cref="VatAnimationSet"/> from the skinned prefab and clips it was authored with:
    /// mesh and animation maps go inside the asset as sub-objects, materials and the spawnable
    /// prefab next to it.
    /// </summary>
    internal static class VatBaker
    {
        private const string DefaultShaderName = "VAT/Lit";
        private const int MaxMapWidth = 16384;
        private const int MaxMapHeight = 16384;
        private const TextureFormat NormalMapFormat = TextureFormat.RG16;

        // Positions are written normalised to the pose bounds either way, so the choice of format
        // never reaches the shader — it decodes with one lerp regardless.
        //
        // RGB9e5Float halves the map and passes every support query, but its shared exponent has to
        // be unpacked on every fetch and a VAT map is fetched per vertex per pass. RGBA32 is the
        // other four-byte option and needs no unpacking at all: eight bits a channel across the
        // bounds, which on a 1.5 m character lands every 6 mm.
        private const TextureFormat CompactPositionFormat = TextureFormat.RGBA32;
        private const TextureFormat FallbackPositionFormat = TextureFormat.RGBAHalf;
        private const string PerInstanceKeyword = "_VAT_PER_INSTANCE";

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int PositionMapId = Shader.PropertyToID("_PositionMap");
        private static readonly int NormalMapId = Shader.PropertyToID("_NormalMap");
        private static readonly int VatParamsId = Shader.PropertyToID("_VatParams");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int PerInstanceId = Shader.PropertyToID("_PerInstance");
        private static readonly int BoundsMinId = Shader.PropertyToID("_VatBoundsMin");
        private static readonly int BoundsMaxId = Shader.PropertyToID("_VatBoundsMax");

        public static VatBakeResult Bake(VatAnimationSet set)
        {
            var clips = CollectClips(set);
            if (!TryValidate(set, clips, out var problem))
            {
                return VatBakeResult.Failed(problem);
            }

            var shader = set.Shader != null ? set.Shader : Shader.Find(DefaultShaderName);
            if (shader == null)
            {
                return VatBakeResult.Failed($"Shader '{DefaultShaderName}' was not found. Assign a shader on the set.");
            }

            var instances = new List<GameObject>();
            try
            {
                var levels = CollectLevels(set, instances, out problem);
                if (levels == null)
                {
                    return VatBakeResult.Failed(problem);
                }

                var baseName = set.SourcePrefab.name;
                for (var i = 0; i < levels.Count; i++)
                {
                    var level = levels[i];
                    if (!TryValidateLevel(level, baseName, out problem))
                    {
                        return VatBakeResult.Failed(problem);
                    }

                    PrepareAnimator(level.Instance);
                    level.Buffer = new VatSampler(level.Instance, level.Renderers, set.Fps, set.BakeNormals).Sample(clips);
                }

                return WriteAssets(set, shader, levels);
            }
            finally
            {
                for (var i = 0; i < instances.Count; i++)
                {
                    Object.DestroyImmediate(instances[i]);
                }
            }
        }

        /// <summary>
        /// Levels come from an explicit prefab per level, from a LODGroup on the source prefab, or —
        /// when there is neither — from the source prefab as a single level.
        /// </summary>
        private static List<VatLevel> CollectLevels(VatAnimationSet set, List<GameObject> instances, out string problem)
        {
            problem = string.Empty;
            var levels = new List<VatLevel>();
            var lodPrefabs = set.LodPrefabs;

            if (lodPrefabs != null && lodPrefabs.Length > 0)
            {
                for (var i = 0; i < lodPrefabs.Length; i++)
                {
                    if (lodPrefabs[i] == null)
                    {
                        problem = $"LOD prefab {i} is empty.";
                        return null;
                    }

                    var levelInstance = Spawn(lodPrefabs[i], instances);
                    levels.Add(new VatLevel(
                        levelInstance,
                        levelInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                        ResolveTransition(set, i),
                        Suffix(i, lodPrefabs.Length)));
                }

                return levels;
            }

            var instance = Spawn(set.SourcePrefab, instances);
            var group = instance.GetComponentInChildren<LODGroup>();
            if (group == null || group.lodCount <= 1)
            {
                levels.Add(new VatLevel(
                    instance,
                    instance.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                    0f,
                    string.Empty));
                return levels;
            }

            // The group would switch renderers off while we sample; the levels are read out of it,
            // so it has no job left to do on this throwaway instance.
            var lods = group.GetLODs();
            group.enabled = false;

            for (var i = 0; i < lods.Length; i++)
            {
                levels.Add(new VatLevel(
                    instance,
                    FilterSkinned(lods[i].renderers),
                    lods[i].screenRelativeTransitionHeight,
                    Suffix(i, lods.Length)));
            }

            return levels;
        }

        private static GameObject Spawn(GameObject prefab, List<GameObject> instances)
        {
            var instance = Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instances.Add(instance);
            return instance;
        }

        private static SkinnedMeshRenderer[] FilterSkinned(Renderer[] renderers)
        {
            var skinned = new List<SkinnedMeshRenderer>();
            for (var i = 0; renderers != null && i < renderers.Length; i++)
            {
                if (renderers[i] is SkinnedMeshRenderer candidate)
                {
                    skinned.Add(candidate);
                }
            }

            return skinned.ToArray();
        }

        private static string Suffix(int index, int count)
        {
            return count > 1 ? $"-LOD{index}" : string.Empty;
        }

        private static float ResolveTransition(VatAnimationSet set, int index)
        {
            var transitions = set.LodTransitions;
            if (transitions != null && index < transitions.Length)
            {
                return transitions[index];
            }

            // Halving the screen height per level is the usual shape when nothing was authored.
            return 0.4f / (1 << index);
        }

        private static List<VatSourceClip> CollectClips(VatAnimationSet set)
        {
            var clips = new List<VatSourceClip>();
            var source = set.SourceClips;
            if (source == null)
            {
                return clips;
            }

            for (var i = 0; i < source.Length; i++)
            {
                if (source[i] != null && source[i].Clip != null)
                {
                    clips.Add(source[i]);
                }
            }

            return clips;
        }

        private static bool TryValidate(VatAnimationSet set, List<VatSourceClip> clips, out string problem)
        {
            if (!AssetDatabase.Contains(set))
            {
                problem = "Save the animation set as an asset before baking into it.";
                return false;
            }

            if (set.SourcePrefab == null)
            {
                problem = "No source prefab assigned.";
                return false;
            }

            if (clips.Count == 0)
            {
                problem = "No animation clips assigned.";
                return false;
            }

            if (set.Fps <= 0f)
            {
                problem = "FPS must be greater than zero.";
                return false;
            }

            // An unsupported texture format does not throw — the graphics backend asserts and takes
            // the editor down with it, so it gets checked here rather than discovered at upload.
            if (set.BakeNormals && !IsMapFormatUsable(NormalMapFormat))
            {
                problem = $"This machine's graphics backend cannot both sample and CPU-fill " +
                          $"{NormalMapFormat}, which the normal map needs. Switch Bake Normals off.";
                return false;
            }

            // Every clip stacks into the same map, so it is the total that hits the texture limit,
            // not any one clip. Caught here rather than inside the bake: Texture2D would throw
            // after the sampling had already run, with nothing pointing at the clip list.
            var frameCount = VatSampler.CountFrames(clips, set.Fps);
            if (frameCount > MaxMapHeight)
            {
                problem = $"{clips.Count} clips at {set.Fps} FPS come to {frameCount} frames, over the " +
                          $"{MaxMapHeight} texture height limit. Lower the FPS, shorten the clips, or " +
                          "split them across two sets.";
                return false;
            }

            problem = string.Empty;
            return true;
        }

        private static bool TryValidateLevel(VatLevel level, string baseName, out string problem)
        {
            if (level.Renderers.Length == 0)
            {
                problem = $"'{baseName}' level {level.Suffix} has no SkinnedMeshRenderer to bake.";
                return false;
            }

            var vertexCount = VatSampler.CountVertices(level.Renderers);
            if (vertexCount == 0)
            {
                problem = $"'{baseName}' level {level.Suffix} has no vertices to bake.";
                return false;
            }

            if (vertexCount > MaxMapWidth)
            {
                problem = $"{vertexCount} vertices exceed the {MaxMapWidth} texture width limit. Split the mesh or decimate it.";
                return false;
            }

            problem = string.Empty;
            return true;
        }

        private static void PrepareAnimator(GameObject instance)
        {
            var animator = instance.GetComponent<Animator>();
            if (animator == null)
            {
                return;
            }

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private static VatBakeResult WriteAssets(VatAnimationSet set, Shader shader, List<VatLevel> levels)
        {
            var setPath = AssetDatabase.GetAssetPath(set);
            var folder = System.IO.Path.GetDirectoryName(setPath).Replace('\\', '/');
            var baseName = set.SourcePrefab.name;
            var kept = new List<Object>();
            var totalVertices = 0;

            for (var i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                var buffer = level.Buffer;
                totalVertices += buffer.VertexCount;

                var mesh = AcquireSubAsset<Mesh>(set, setPath, $"{baseName}-VatMesh{level.Suffix}");
                level.MeshResult = VatMeshBuilder.Build(level.Renderers, buffer, $"{baseName}-VatMesh{level.Suffix}", mesh);
                Keep(set, setPath, level.MeshResult.Mesh, kept);

                var positionMap = WritePositionMap(set, setPath, $"{baseName}-VatPositions{level.Suffix}", buffer, kept);
                var normalMap = buffer.HasNormals
                    ? WriteNormalMap(set, setPath, $"{baseName}-VatNormals{level.Suffix}", buffer, kept)
                    : null;

                var firstClip = buffer.Clips[0];
                var vatParams = new Vector4(firstClip.StartFrame, firstClip.FrameCount, firstClip.Fps, buffer.FrameCount);
                level.Materials = CreateMaterials(set, shader, level, folder, positionMap, normalMap, vatParams);

                // The clip table is identical across levels, so one record describes the whole set.
                if (i == 0)
                {
                    set.Configure(level.MeshResult.Mesh, positionMap, normalMap, buffer.Clips, buffer.FrameCount, buffer.VertexCount, levels.Count, DecodeBounds(set, buffer));
                }
            }

            RemoveStaleSubAssets(setPath, kept);

            if (set.CreatePrefab)
            {
                set.SetPrefab(CreatePrefab(set, folder, levels));
            }

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var head = levels[0].Buffer;
            var message = $"Baked '{baseName}': {levels.Count} LOD level(s), {totalVertices} vertices total, " +
                          $"{head.FrameCount} frames, {head.Clips.Length} clip(s) -> {setPath}";
            return VatBakeResult.Succeeded(set, message);
        }

        private static Texture2D WritePositionMap(VatAnimationSet set, string setPath, string name, VatSampleBuffer buffer, List<Object> kept)
        {
            var existing = AcquireSubAsset<Texture2D>(set, setPath, name);
            var map = CreatePositionMap(set, name, buffer, existing);
            Keep(set, setPath, map, kept);
            return map;
        }

        /// <summary>
        /// A map format has to survive both halves of its life: filled from the CPU here, sampled
        /// in a vertex shader later. A format can pass one and fail the other — RGB10A2 on Metal
        /// takes SetPixels and refuses Sample — and an unsupported format does not raise anything
        /// catchable, it asserts inside the graphics backend and takes the editor down.
        /// </summary>
        /// <summary>
        /// The box the shader has to undo. A raw bake gets a zero-sized one on purpose: that is the
        /// signal to pass map samples through, and it means a Shader Graph can wire these inputs
        /// once and stay correct whichever way the set was baked.
        /// </summary>
        private static Bounds DecodeBounds(VatAnimationSet set, VatSampleBuffer buffer)
        {
            return set.CompactPositionMap ? buffer.Bounds : new Bounds(Vector3.zero, Vector3.zero);
        }

        private static bool IsMapFormatUsable(TextureFormat format)
        {
            var graphics = GraphicsFormatUtility.GetGraphicsFormat(format, false);
            return SystemInfo.IsFormatSupported(graphics, GraphicsFormatUsage.Sample)
                && SystemInfo.IsFormatSupported(graphics, GraphicsFormatUsage.SetPixels);
        }

        private static TextureFormat ResolvePositionFormat(VatAnimationSet set)
        {
            if (!set.CompactPositionMap)
            {
                return FallbackPositionFormat;
            }

            if (IsMapFormatUsable(CompactPositionFormat))
            {
                return CompactPositionFormat;
            }

            Debug.LogWarning(
                $"This machine cannot both sample and CPU-fill {CompactPositionFormat}, so the position " +
                $"map falls back to {FallbackPositionFormat} at twice the size. The bake is otherwise " +
                "unaffected — positions are normalised either way and the shader does not know the " +
                "difference.", set);
            return FallbackPositionFormat;
        }

        private static Texture2D WriteNormalMap(VatAnimationSet set, string setPath, string name, VatSampleBuffer buffer, List<Object> kept)
        {
            var existing = AcquireSubAsset<Texture2D>(set, setPath, name);
            var map = CreateNormalMap(name, buffer, existing);
            Keep(set, setPath, map, kept);
            return map;
        }

        private static T AcquireSubAsset<T>(VatAnimationSet set, string setPath, string name) where T : Object
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(setPath);
            for (var i = 0; i < assets.Length; i++)
            {
                if (assets[i] is T typed && assets[i] != set && typed.name == name)
                {
                    return typed;
                }
            }

            return null;
        }

        private static void Keep(VatAnimationSet set, string setPath, Object asset, List<Object> kept)
        {
            if (!AssetDatabase.Contains(asset))
            {
                AssetDatabase.AddObjectToAsset(asset, set);
            }

            kept.Add(asset);
        }

        /// <summary>Drops sub-objects left over from a previous bake — a level that no longer exists,
        /// or normal maps after normals were switched off.</summary>
        private static void RemoveStaleSubAssets(string setPath, List<Object> kept)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(setPath);
            for (var i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                if (asset is VatAnimationSet || kept.Contains(asset))
                {
                    continue;
                }

                if (asset is Mesh || asset is Texture2D)
                {
                    AssetDatabase.RemoveObjectFromAsset(asset);
                    Object.DestroyImmediate(asset);
                }
            }
        }

        private static Material[] CreateMaterials(VatAnimationSet set, Shader shader, VatLevel level, string folder, Texture2D positionMap, Texture2D normalMap, Vector4 vatParams)
        {
            var baseName = set.SourcePrefab.name;
            var template = set.MaterialTemplate;
            var sources = level.MeshResult.SourceMaterials;
            var materials = new Material[sources.Length];

            for (var i = 0; i < sources.Length; i++)
            {
                var source = sources[i];

                // A template carries the look you already tuned — any shader, third-party included.
                // Its shader must sample the VAT maps, otherwise the crowd renders in bind pose.
                var material = template != null ? new Material(template) : new Material(shader);
                material.name = source != null
                    ? $"{source.name}-VAT{level.Suffix}"
                    : $"{baseName}-{i}-VAT{level.Suffix}";
                material.enableInstancing = true;

                CopyBaseSurface(source, material);
                WarnIfNotAnimated(material, set);

                if (material.HasProperty(PositionMapId))
                {
                    material.SetTexture(PositionMapId, positionMap);
                }

                if (normalMap != null && material.HasProperty(NormalMapId))
                {
                    material.SetTexture(NormalMapId, normalMap);
                }

                if (material.HasProperty(VatParamsId))
                {
                    material.SetVector(VatParamsId, vatParams);
                }

                // Without these the map decodes against a zero-sized box and the mesh collapses
                // onto a point — loud rather than subtly wrong, which is what we want if a
                // material template's shader forgot to declare them.
                if (material.HasProperty(BoundsMinId) && material.HasProperty(BoundsMaxId))
                {
                    var bounds = DecodeBounds(set, level.Buffer);
                    material.SetVector(BoundsMinId, bounds.min);
                    material.SetVector(BoundsMaxId, bounds.max);
                }

                ApplyPerUnitMode(material, set.PerUnitAnimation);

                AssetDatabase.CreateAsset(material, $"{folder}/{material.name}.mat");
                materials[i] = material;
            }

            return materials;
        }

        // A [Toggle] property drives a keyword, and setting it from code has to move both.
        private static void ApplyPerUnitMode(Material material, bool perUnit)
        {
            if (!material.HasProperty(PerInstanceId))
            {
                return;
            }

            material.SetFloat(PerInstanceId, perUnit ? 1f : 0f);
            if (perUnit)
            {
                material.EnableKeyword(PerInstanceKeyword);
                return;
            }

            material.DisableKeyword(PerInstanceKeyword);
        }

        private static void WarnIfNotAnimated(Material material, VatAnimationSet set)
        {
            if (material.HasProperty(PositionMapId))
            {
                return;
            }

            Debug.LogWarning(
                $"Shader '{material.shader.name}' has no _PositionMap property, so '{material.name}' will render " +
                "the bind pose. Add the VAT sampling to that shader (see VatShaderGraph.hlsl) or clear the material template.",
                set);
        }

        private static void CopyBaseSurface(Material source, Material material)
        {
            if (source == null)
            {
                return;
            }

            if (source.HasProperty(BaseMapId) && material.HasProperty(BaseMapId))
            {
                material.SetTexture(BaseMapId, source.GetTexture(BaseMapId));
            }

            if (source.HasProperty(BaseColorId) && material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, source.GetColor(BaseColorId));
            }

            CopyFloat(source, material, MetallicId);
            CopyFloat(source, material, SmoothnessId);
        }

        private static void CopyFloat(Material source, Material material, int propertyId)
        {
            if (source.HasProperty(propertyId) && material.HasProperty(propertyId))
            {
                material.SetFloat(propertyId, source.GetFloat(propertyId));
            }
        }

        /// <summary>
        /// Positions go in normalised to the pose bounds, so every value is in [0, 1] and the
        /// shader decodes with a single lerp. Normalising is what makes a compact format viable at
        /// all: raw world-space positions would need the range a half float carries.
        /// </summary>
        private static Texture2D CreatePositionMap(VatAnimationSet set, string name, VatSampleBuffer buffer, Texture2D target)
        {
            var width = buffer.VertexCount;
            var height = buffer.FrameCount;
            var compact = set.CompactPositionMap;
            var format = ResolvePositionFormat(set);

            var texture = target;
            if (texture == null || texture.format != format)
            {
                texture = new Texture2D(width, height, format, false, true);
            }
            else
            {
                texture.Reinitialize(width, height, format, false);
            }

            texture.name = name;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;

            var positions = buffer.Positions;
            var pixels = new Color[width * height];

            if (compact)
            {
                var bounds = buffer.Bounds;
                var min = bounds.min;
                var size = bounds.size;

                // A character that never moves along an axis gives that axis zero extent.
                // Everything there collapses to zero and decodes back to the value bounds.min holds.
                var inverse = new Vector3(
                    size.x > 0f ? 1f / size.x : 0f,
                    size.y > 0f ? 1f / size.y : 0f,
                    size.z > 0f ? 1f / size.z : 0f);

                for (var i = 0; i < pixels.Length; i++)
                {
                    var value = positions[i];
                    pixels[i] = new Color(
                        Mathf.Clamp01((value.x - min.x) * inverse.x),
                        Mathf.Clamp01((value.y - min.y) * inverse.y),
                        Mathf.Clamp01((value.z - min.z) * inverse.z),
                        1f);
                }
            }
            else
            {
                for (var i = 0; i < pixels.Length; i++)
                {
                    var value = positions[i];
                    pixels[i] = new Color(value.x, value.y, value.z, 1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D CreateNormalMap(string name, VatSampleBuffer buffer, Texture2D target)
        {
            var width = buffer.VertexCount;
            var height = buffer.FrameCount;

            var texture = target;
            if (texture == null)
            {
                texture = new Texture2D(width, height, NormalMapFormat, false, true);
            }
            else
            {
                texture.Reinitialize(width, height, NormalMapFormat, false);
            }

            texture.name = name;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;

            var normals = buffer.Normals;
            var encoded = new byte[width * height * 2];
            for (var i = 0; i < normals.Length; i++)
            {
                var oct = OctEncode(normals[i]);
                encoded[i * 2] = Quantise(oct.x);
                encoded[i * 2 + 1] = Quantise(oct.y);
            }

            texture.SetPixelData(encoded, 0);
            texture.Apply(false, false);
            return texture;
        }

        private static byte Quantise(float normalised)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(normalised * 255f), 0, 255);
        }

        /// <summary>Folds a unit vector onto a square so it fits in two channels instead of three.</summary>
        private static Vector2 OctEncode(Vector3 normal)
        {
            var length = Mathf.Abs(normal.x) + Mathf.Abs(normal.y) + Mathf.Abs(normal.z);
            if (length <= 0f)
            {
                return new Vector2(0.5f, 0.5f);
            }

            var folded = normal / length;
            var oct = new Vector2(folded.x, folded.y);
            if (folded.z < 0f)
            {
                oct = new Vector2(
                    (1f - Mathf.Abs(folded.y)) * (folded.x >= 0f ? 1f : -1f),
                    (1f - Mathf.Abs(folded.x)) * (folded.y >= 0f ? 1f : -1f));
            }

            return oct * 0.5f + new Vector2(0.5f, 0.5f);
        }

        private static GameObject CreatePrefab(VatAnimationSet set, string folder, List<VatLevel> levels)
        {
            var name = $"{set.SourcePrefab.name}-VAT";
            var instance = new GameObject(name);

            try
            {
                if (levels.Count == 1)
                {
                    var level = levels[0];
                    instance.AddComponent<MeshFilter>().sharedMesh = level.MeshResult.Mesh;
                    ConfigureRenderer(instance.AddComponent<MeshRenderer>(), level.Materials, set);
                }
                else
                {
                    BuildLodGroup(instance, levels, set);
                }

                if (set.PerUnitAnimation)
                {
                    instance.AddComponent<VatUnitAnimator>().Configure(set, 0);
                }

                return PrefabUtility.SaveAsPrefabAsset(instance, $"{folder}/{name}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // A LODGroup swaps whole renderers on the CPU, so it works without the GPU-driven path.
        // The price is a separate mesh, maps and materials for every level.
        private static void BuildLodGroup(GameObject root, List<VatLevel> levels, VatAnimationSet set)
        {
            var lods = new LOD[levels.Count];

            for (var i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                var child = new GameObject($"LOD{i}");
                child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = level.MeshResult.Mesh;

                var renderer = child.AddComponent<MeshRenderer>();
                ConfigureRenderer(renderer, level.Materials, set);
                lods[i] = new LOD(Mathf.Clamp01(level.Transition), new Renderer[] { renderer });
            }

            var group = root.AddComponent<LODGroup>();
            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        // Per-renderer probe data is per-object data: it buys a flat-shaded crowd nothing and gives
        // the batcher one more reason to split. The shadow pass re-runs the whole vertex animation,
        // so whether the crowd casts shadows is the single biggest knob on this prefab.
        private static void ConfigureRenderer(MeshRenderer renderer, Material[] materials, VatAnimationSet set)
        {
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = set.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
        }
    }
}
