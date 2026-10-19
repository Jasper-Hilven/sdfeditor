using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// Turns each .glb the SDF pipeline writes into Assets/Mushrooms/Generated into a classic prefab, Assets/Mushrooms/&lt;name&gt;.prefab,
    /// drawn in the toon look, and keeps the default scene showing the best variant of each model.
    /// <para>
    /// The toon look takes everything from the vertices: colour, roughness, wetness, subsurface glow, detail, variation and speckles
    /// come per vertex, mixed between materials as the preview mixes them (see SdfSurface.hlsl for how the vertex data is packed), plus
    /// the edge colour (UV6). So every model shares one material on SdfToon.shader, Assets/Mushrooms/Materials/Toon/Mushroom.mat, each
    /// mesh is one submesh (one draw), and each part gets a child renderer with the ink outline (SdfToonOutline.shader).
    /// <see cref="MushroomStyle"/> (Assets/Mushrooms/Mushroom Style.asset) holds the toon settings.
    /// </para>
    /// <para>
    /// Levels of detail: a model meshed with levels (sdf mesh --lods n) gets a LOD group on the prefab's root, cross-fading between them.
    /// LOD0 is the parts as above; each coarser level adds a child per part named after its mesh (body_LOD1, ...) with its own mesh asset,
    /// and the group is culled below a small share of the screen, so the many trees, rocks and props of a scene cost little far away.
    /// The coarsest level is also the cheapest to draw: no ink outline. A plain LOD group with baked levels rather than Unity's Mesh
    /// LOD, as the browser renderer draws the same levels. Models without levels are built as before, without a LOD group.
    /// </para>
    /// <para>
    /// Colliders: a model whose file asks for one (a <c>collider</c> line in the .sdf, handed on in the .glb's extras) gets it on the
    /// prefab's root: a <see cref="MeshCollider"/> per part from the level of detail the extras name, convex or not, or an upright
    /// <see cref="CapsuleCollider"/>. Without one the prefab has no collider.
    /// </para>
    /// <para>
    /// Part instances: a part the pipeline meshed once for several uses (the .glb has a node with a matrix per use) is a child of the
    /// root per use, named after the use's node (eye.0, eye.1), placed, turned, scaled and mirrored as the use puts it, with its own LOD
    /// group sized to the part, so each use switches level by its own size and distance. Its levels are children of it, drawing the
    /// part's mesh assets, which all uses share. The prefab then reads, from the root down:
    /// <code>
    /// castle0                 LOD group over the model's own meshes (if it has any), the model's colliders
    ///   body                  LOD0 of the body, with an Outline child; and each part meshed in the model's space, as before
    ///   body_LOD1, ...        the coarser levels, one child each
    ///   tower.0               a use of a part meshed once: its placement, LOD group, and collider
    ///     tower               LOD0, mesh asset castle0_tower, with an Outline child
    ///     tower_LOD1, ...     the coarser levels, mesh assets castle0_tower_LOD1, ...
    ///   tower.1, ...
    /// </code>
    /// So a renderer's mesh per part and level is found the same way under the root and under each use: a child named after the mesh.
    /// A model without repeated parts has no use children and is built as before. Static flags come from the level, which marks the
    /// whole placed hierarchy.
    /// </para>
    /// <para>
    /// Settings (the model's and the parts' own, from the .glb's extras, see <see cref="SdfGlb"/>): <c>shadows off</c> casts none,
    /// <c>near</c> only from LOD0; <c>outline off</c> leaves out the ink line; <c>cull</c> drops the part (or the model) from its LOD
    /// group beyond that distance; and a part's own <c>collider</c> replaces the model's for its meshes, on the root for a part in the
    /// model's space and on each use's child for an instanced one. Without a <c>cull</c>, a use's LOD group culls where the model
    /// would, so an eye does not vanish before the mushroom it floats by.
    /// </para>
    /// Rebuilding keeps every asset's GUID: meshes and materials are updated in place and prefabs are saved over the old ones, so the
    /// scene's instances follow along.
    /// </summary>
    public sealed class MushroomBuilder : AssetPostprocessor
    {
        public const string GeneratedFolder = "Assets/Mushrooms/Generated";
        public const string PrefabFolder = "Assets/Mushrooms";
        public const string MeshFolder = "Assets/Mushrooms/Meshes";
        public const string MaterialFolder = "Assets/Mushrooms/Materials";
        public const string ToonShaderPath = "Assets/Mushrooms/Shaders/SdfToon.shader";
        public const string ToonOutlineShaderPath = "Assets/Mushrooms/Shaders/SdfToonOutline.shader";
        public const string ToonMaterialFolder = "Assets/Mushrooms/Materials/Toon";
        public const string ToonMaterialPath = "Assets/Mushrooms/Materials/Toon/Mushroom.mat";
        public const string ToonOutlineMaterialPath = "Assets/Mushrooms/Materials/Toon/Outline.mat";
        public const string OutlineObjectName = "Outline";
        public const string ScenePath = "Assets/Scenes/Mushrooms.unity";

        /// <summary>
        /// Raise when the way prefabs are built changes: a prefab stamped with an older version is rebuilt when the editor loads, even
        /// if its model did not change. Version 2: speckles (UV4, UV5) and post-scatter diffusion profiles. Version 3: lower sun.
        /// Version 4: looks (the stamp also names the look, so changing it rebuilds every prefab). Version 5: one toon material for
        /// everything, the edge colour per vertex (UV6) and toon meshes as a single submesh. Version 6: levels of detail, a cross-fading
        /// LOD group over the parts' levels with a mesh asset per level, no outline and a cheaper realistic material on the coarsest.
        /// Version 7: diffusion profiles per snapped look instead of per material group, so they fit HDRP's list. Version 8: the toon
        /// look only, the realistic one gone. Version 9: URP instead of HDRP. Version 10: colliders from the model's collider setting.
        /// Version 11: part instances, a child with its own LOD group per use, and the shadows, outline, cull and collider settings per part.
        /// </summary>
        public const int BuilderVersion = 11;

        private static string VersionStamp
        {
            get
            {
                var look = MushroomStyle.Load().outlineWidth > 0 ? "toon+outline" : "toon";
                return $"sdf-builder {BuilderVersion} {look}";
            }
        }

        /// <summary>Raised after prefabs were (re)built, with their paths.</summary>
        public static event Action<IReadOnlyList<string>> Built;

        private static bool building;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload)
        {
            var models = imported.Concat(moved).Where(IsGenerated).ToArray();
            if (models.Length > 0 && !building)
                Build(models);
            // The style asset changed on disk (e.g. edited outside Unity): rebuild what was built with or without the outline
            else if (!building && imported.Contains(MushroomStyle.AssetPath))
                EditorApplication.delayCall += () =>
                {
                    ApplyToonSettings();
                    BuildStale();
                };
        }

        public static bool IsGenerated(string path) =>
            path.Replace('\\', '/').StartsWith(GeneratedFolder + "/", StringComparison.Ordinal)
            && path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

        public static string PrefabPathOf(string glbPath) => $"{PrefabFolder}/{Path.GetFileNameWithoutExtension(glbPath)}.prefab";

        /// <summary>Every .glb in the Generated folder and its subfolders; sdf mesh --into keeps the content folder's subfolders.</summary>
        public static IEnumerable<string> GeneratedModels() =>
            Directory.Exists(GeneratedFolder)
                ? Directory.GetFiles(GeneratedFolder, "*.glb", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal)
                : Enumerable.Empty<string>();

        /// <summary>The group a model belongs to: its subfolder of the Generated folder (characters, trees, weapons), or "" at the top.</summary>
        public static string GroupOf(string glbPath)
        {
            var folder = Path.GetDirectoryName(glbPath.Replace('\\', '/'))!.Replace('\\', '/');
            return folder.Length > GeneratedFolder.Length ? folder.Substring(GeneratedFolder.Length + 1) : "";
        }

        /// <summary>
        /// The model a file is a variant of, and how good the variant is: minigun_512 is minigun at 512 cells, minigun_fast minigun at
        /// the default resolution (sdf mesh --suffix). Variants with a number rank by it, others below any number.
        /// </summary>
        private static (string model, int rank) VariantOf(string glbPath)
        {
            var name = Path.GetFileNameWithoutExtension(glbPath);
            var underscore = name.LastIndexOf('_');
            if (underscore <= 0)
                return (name, 0);
            return (name.Substring(0, underscore), int.TryParse(name.Substring(underscore + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var cells) ? cells : 0);
        }

        /// <summary>What the default scene shows: per group, the best built variant of each model, in name order.</summary>
        public static IReadOnlyList<(string group, string prefab)> SceneModels() =>
            GeneratedModels()
                .Where(glb => File.Exists(PrefabPathOf(glb)))
                .GroupBy(glb => (GroupOf(glb), VariantOf(glb).model))
                .Select(variants => (variants.Key.Item1, PrefabPathOf(variants.OrderByDescending(v => VariantOf(v).rank).First())))
                .OrderBy(m => m.Item1, StringComparer.Ordinal).ThenBy(m => m.Item2, StringComparer.Ordinal)
                .ToArray();

        /// <summary>
        /// Models whose prefab is missing, older than the model or built by an older version of this code: what an import missed, e.g.
        /// before these scripts compiled.
        /// </summary>
        public static string[] StaleModels()
        {
            var stamp = VersionStamp;
            return GeneratedModels()
                .Where(glb => !File.Exists(PrefabPathOf(glb))
                              || File.GetLastWriteTimeUtc(PrefabPathOf(glb)) < File.GetLastWriteTimeUtc(glb)
                              || AssetImporter.GetAtPath(PrefabPathOf(glb))?.userData != stamp)
                .ToArray();
        }

        [MenuItem("Tools/SDF Mushrooms/Rebuild All")]
        public static void BuildAll() => Build(GeneratedModels().ToArray());

        public static void BuildStale()
        {
            var stale = StaleModels();
            if (stale.Length > 0 || !File.Exists(ScenePath))
                Build(stale);
        }

        public static void Build(IReadOnlyList<string> glbPaths)
        {
            building = true;
            var prefabs = new List<string>();
            try
            {
                RenderPipelineSetup.Ensure();
                foreach (var glb in glbPaths)
                {
                    try
                    {
                        prefabs.Add(BuildOne(glb));
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError($"SDF mushrooms: could not build {glb}: {exception.Message}\n{exception.StackTrace}");
                    }
                }
                AssetDatabase.SaveAssets();
                DeleteOldToonMaterials();
                MushroomScene.Ensure(SceneModels());
            }
            finally
            {
                building = false;
            }
            if (prefabs.Count > 0)
            {
                Debug.Log($"SDF mushrooms: built {string.Join(", ", prefabs)}");
                Built?.Invoke(prefabs);
            }
        }

        private static string BuildOne(string glbPath)
        {
            var model = SdfGlb.Read(glbPath);
            var name = Path.GetFileNameWithoutExtension(glbPath);
            var style = MushroomStyle.Load();
            var toon = ToonMaterial(style);
            var outline = style.outlineWidth > 0 ? ToonOutlineMaterial(style) : null;

            var root = new GameObject(name);
            try
            {
                var meshes = new Dictionary<SdfGlb.MeshData, Mesh>();
                Mesh MeshOf(SdfGlb.MeshData data)
                {
                    if (!meshes.TryGetValue(data, out var mesh))
                    {
                        meshes[data] = mesh = MeshAsset($"{MeshFolder}/{name}_{data.Name}.asset");
                        Fill(mesh, data, model.Materials, name);
                        EditorUtility.SetDirty(mesh);
                    }
                    return mesh;
                }

                // The model's own meshes, in its space, under the root's LOD group
                var placed = model.Meshes.Where(m => m.Instances.Count == 0).ToList();
                var levels = 1 + placed.Select(m => m.Lods.Count).DefaultIfEmpty(0).Max();
                var lods = Levels(root.transform, placed, levels, model, MeshOf, toon, outline);

                // Each use of a part meshed once: a child placed as the use puts it, with the part's levels below it
                var uses = new List<(GameObject Use, SdfGlb.MeshData Part, List<Renderer>[] Lods)>();
                foreach (var part in model.Meshes.Where(m => m.Instances.Count > 0))
                foreach (var (useName, matrix) in part.Instances)
                {
                    var use = new GameObject(useName);
                    use.transform.SetParent(root.transform, false);
                    Place(use.transform, matrix);
                    uses.Add((use, part, Levels(use.transform, new[] { part }, 1 + part.Lods.Count, model, MeshOf, toon, outline)));
                }

                var modelSize = Largest(BoundsInRoot(root));
                if (levels > 1 && placed.Count > 0)
                    AddLodGroup(root, placed, lods, CullHeight(model.Cull, modelSize, LodCullHeight));
                foreach (var (use, part, partLods) in uses)
                {
                    if (partLods.Length < 2)
                        continue;
                    // Culled where the whole model would be, unless the part says how far it shows
                    var size = Largest(BoundsInRoot(use));
                    var cull = part.Settings != null ? part.Settings.Value<float?>("cull") : model.Cull;
                    AddLodGroup(use, new[] { part }, partLods, CullHeight(cull, size, LodCullHeight * size / Mathf.Max(modelSize, 1e-4f)));
                }
                AddCollider(root, model, MeshOf, uses.Select(u => (u.Use, u.Part)));

                var prefabPath = PrefabPathOf(glbPath);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out var saved);
                if (!saved)
                    throw new InvalidOperationException($"Could not save {prefabPath}");
                var importer = AssetImporter.GetAtPath(prefabPath);
                if (importer != null && importer.userData != VersionStamp)
                {
                    importer.userData = VersionStamp;
                    importer.SaveAndReimport();
                }
                return prefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The levels of detail of <paramref name="parts"/> as children of <paramref name="parent"/> (the root, or a part's use), the
        /// renderers of each level in a list for its LOD. The coarsest level of several has no outline, nor has a part whose settings
        /// turn it off.
        /// </summary>
        private static List<Renderer>[] Levels(Transform parent, IReadOnlyList<SdfGlb.MeshData> parts, int levels, SdfGlb model,
            Func<SdfGlb.MeshData, Mesh> meshOf, Material material, Material outline)
        {
            var lods = new List<Renderer>[levels];
            var made = new Dictionary<SdfGlb.MeshData, Renderer[]>();
            for (var level = 0; level < levels; level++)
            {
                lods[level] = new List<Renderer>();
                var coarsest = levels > 1 && level == levels - 1;
                foreach (var part in parts)
                {
                    // A part with fewer levels than the model shows its coarsest one at the levels it lacks
                    var meshData = part.AtLevel(level);
                    var shadows = part.Settings?.Value<string>("shadows") ?? model.Shadows;
                    var hasOutline = part.Settings?.Value<bool?>("outline") ?? model.Outline;
                    if (!made.TryGetValue(meshData, out var renderers))
                        made[meshData] = renderers = Part(parent, meshData, meshOf(meshData), material, coarsest || !hasOutline ? null : outline,
                            shadows == "on" || shadows == "near" && meshData.Lod == 0);
                    lods[level].AddRange(renderers);
                }
            }
            return lods;
        }

        /// <summary>
        /// One part at one level of detail: a child of <paramref name="parent"/> named after the mesh (body, body_LOD1, ...), drawing its
        /// mesh asset, Assets/Mushrooms/Meshes/&lt;model&gt;_&lt;mesh&gt;.asset, plus the ink outline when <paramref name="outline"/> is
        /// given. Returns its renderers, for the level's LOD.
        /// </summary>
        private static Renderer[] Part(Transform parent, SdfGlb.MeshData meshData, Mesh mesh, Material material, Material outline, bool castShadows)
        {
            var part = new GameObject(meshData.Name);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (outline == null)
                return new Renderer[] { renderer };

            // The inverted hull: the same mesh again, back faces only, pushed out by the outline shader
            var hull = new GameObject(OutlineObjectName);
            hull.transform.SetParent(part.transform, false);
            hull.AddComponent<MeshFilter>().sharedMesh = mesh;
            var hullRenderer = hull.AddComponent<MeshRenderer>();
            hullRenderer.sharedMaterial = outline;
            hullRenderer.shadowCastingMode = ShadowCastingMode.Off;
            hullRenderer.receiveShadows = false;
            hullRenderer.lightProbeUsage = LightProbeUsage.Off;
            hullRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return new Renderer[] { renderer, hullRenderer };
        }

        /// <summary>
        /// Puts a use of a part where its glTF matrix does, in Unity's coordinates (x mirrored, so the matrix is mirrored on both sides):
        /// a turn, a uniform scale and, for a mirrored use, a negative x scale, for which Unity turns the triangles round itself.
        /// </summary>
        private static void Place(Transform use, Matrix4x4 gltf)
        {
            var flip = Matrix4x4.Scale(new Vector3(-1, 1, 1));
            var m = flip * gltf * flip;
            Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
            var scale = y.magnitude;
            var mirrored = Vector3.Dot(Vector3.Cross(x, y), z) < 0;
            use.localPosition = m.GetColumn(3);
            use.localRotation = Quaternion.LookRotation(z / scale, y / scale);
            use.localScale = new Vector3(mirrored ? -scale : scale, scale, scale);
        }

        /// <summary>The bounds of every surface renderer at or below the object, in the prefab root's space (the root is not moved).</summary>
        private static Bounds BoundsInRoot(GameObject owner)
        {
            var bounds = new Bounds();
            var first = true;
            foreach (var filter in owner.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || filter.name == OutlineObjectName)
                    continue;
                var local = filter.sharedMesh.bounds;
                var matrix = filter.transform.localToWorldMatrix;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = matrix.MultiplyPoint3x4(new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x, (corner & 2) == 0 ? local.min.y : local.max.y, (corner & 4) == 0 ? local.min.z : local.max.z));
                    if (first)
                        bounds = new Bounds(point, Vector3.zero);
                    else
                        bounds.Encapsulate(point);
                    first = false;
                }
            }
            return bounds;
        }

        private static float Largest(Bounds bounds) => Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));

        /// <summary>The first-person camera's vertical field of view, which a <c>cull</c> distance is turned into a screen height with.</summary>
        private const float CullFieldOfView = 60;

        /// <summary>
        /// The share of the screen's height below which a LOD group of <paramref name="size"/> metres is culled: at a <c>cull</c> distance
        /// d it is size / (2 d tan(fov / 2)), never less than <paramref name="fallback"/>; without one, the fallback.
        /// </summary>
        private static float CullHeight(float? cull, float size, float fallback) =>
            cull is float metres && metres > 0
                ? Mathf.Max(fallback, size / (2 * metres * Mathf.Tan(CullFieldOfView * 0.5f * Mathf.Deg2Rad)))
                : fallback;

        /// <summary>
        /// How many pixels, at 1080 lines, a level's error may cover before the finer level is needed again. One pixel keeps the change
        /// of silhouette under the ink line's width (4 pixels by default), so the toon look shows no pop even before the cross-fade.
        /// </summary>
        private const float LodPixelError = 1;

        /// <summary>
        /// Below this share of the screen's height the model is culled: about 11 pixels at 1080p, where a mushroom or a small prop is a
        /// few pixels of outline and colour that cost a draw each (two with the outline). Large things such as trees reach it only far
        /// away, since the height is relative to their own size.
        /// </summary>
        private const float LodCullHeight = 0.01f;

        /// <summary>
        /// A LOD group over the parts' levels (the model's own, on the root, or one part's, on a use of it), with Unity's screen-relative heights worked out from the pipeline's error limits, so the
        /// numbers follow the model's size and resolution instead of being guessed per model. A model of size s (the LOD group's, its
        /// largest side) at relative height h is h × 1080 pixels tall, so an error of e metres covers e / s × h × 1080 pixels; level k is
        /// good enough once that is under <see cref="LodPixelError"/>, which gives the height at which level k-1 hands over to it:
        /// h = s × pixels / (e × 1080), with e the largest error of level k over the parts (each part is meshed on its own grid). The
        /// coarsest level lasts down to <paramref name="cullHeight"/>, <see cref="LodCullHeight"/> unless a cull setting says otherwise. Each height is kept at least half again the next one, so no level
        /// is squeezed out, and LOD0 is left at half the screen at the latest. Quality levels scale all of it with their LOD bias.
        /// <para>
        /// The levels cross-fade (URP dithers both for half a second, see SdfToon.shader): with only a few levels each change of
        /// triangles would otherwise pop.
        /// </para>
        /// </summary>
        private static void AddLodGroup(GameObject owner, IReadOnlyList<SdfGlb.MeshData> parts, IReadOnlyList<List<Renderer>> renderers, float cullHeight)
        {
            // What RecalculateBounds would give, taken from the meshes: the parts sit at the owner's origin, and the renderers of an object
            // that was only just made may not have their bounds yet
            var bounds = renderers[0].Select(r => r.GetComponent<MeshFilter>().sharedMesh.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            var group = owner.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            group.localReferencePoint = bounds.center;
            group.size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));

            var levels = renderers.Count;
            var heights = new float[levels];
            heights[levels - 1] = cullHeight;
            for (var level = levels - 2; level >= 0; level--)
            {
                var error = parts.Max(m => m.AtLevel(level + 1).MaxError);
                // Files that carry no error limit: halve the height with each level
                var height = error > 0 ? group.size * LodPixelError / (error * 1080) : 0.5f / (1 << level);
                heights[level] = Mathf.Max(height, heights[level + 1] * 1.5f);
            }
            heights[0] = Mathf.Min(heights[0], 0.5f);
            for (var level = 1; level < levels; level++)
                heights[level] = Mathf.Min(heights[level], heights[level - 1] / 1.5f);

            group.SetLODs(renderers.Select((r, level) => new LOD(heights[level], r.ToArray())).ToArray());
        }

        /// <summary>
        /// The collider the model's extras ask for, on the root: for convex and mesh a <see cref="MeshCollider"/> per part, from the mesh
        /// asset of the part at the level the extras name (the pipeline has already chosen it, and made a convex one round a large
        /// outcrop a mesh); for capsule an upright <see cref="CapsuleCollider"/> (x mirrored like the meshes). None without the extras.
        /// A part with a collider setting of its own has that instead (none included); a use of a part meshed once has its collider on
        /// its own child, in the part's space, where a model's capsule round everything needs nothing more.
        /// </summary>
        private static void AddCollider(GameObject root, SdfGlb model, Func<SdfGlb.MeshData, Mesh> meshOf,
            IEnumerable<(GameObject Use, SdfGlb.MeshData Part)> uses)
        {
            foreach (var part in model.Meshes.Where(m => m.Instances.Count == 0))
            {
                var own = part.Settings?["collider"] as JObject;
                var collider = own ?? model.Collider;
                if (own != null && own.Value<string>("shape") == "capsule")
                    AddCapsule(root, own);
                else if (collider?.Value<string>("shape") is "convex" or "mesh")
                    AddMeshCollider(root, part, collider, model, meshOf);
            }
            if (model.Collider?.Value<string>("shape") == "capsule")
                AddCapsule(root, model.Collider);

            foreach (var (use, part) in uses)
            {
                var collider = part.Settings?["collider"] as JObject ?? model.Collider;
                if (collider == null || collider == model.Collider && model.Collider.Value<string>("shape") == "capsule")
                    continue;
                if (collider.Value<string>("shape") == "capsule")
                    AddCapsule(use, collider);
                else if (collider.Value<string>("shape") is "convex" or "mesh")
                    AddMeshCollider(use, part, collider, model, meshOf);
            }
        }

        private static void AddMeshCollider(GameObject owner, SdfGlb.MeshData part, JObject collider, SdfGlb model, Func<SdfGlb.MeshData, Mesh> meshOf)
        {
            var level = collider.Value<int?>("lod") ?? model.LodCount - 1;
            var meshCollider = owner.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshOf(part.AtLevel(level));
            meshCollider.convex = collider.Value<string>("shape") == "convex";
        }

        private static void AddCapsule(GameObject owner, JObject collider)
        {
            var bottom = collider["base"].ToObject<float[]>();
            var top = collider["top"].ToObject<float[]>();
            var radius = collider.Value<float>("radius");
            var capsule = owner.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = radius;
            capsule.height = Mathf.Max(top[1] - bottom[1], 2 * radius);
            capsule.center = new Vector3(-(bottom[0] + top[0]) / 2, (bottom[1] + top[1]) / 2, (bottom[2] + top[2]) / 2);
        }

        private static Mesh MeshAsset(string path)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null)
                return mesh;
            EnsureFolder(Path.GetDirectoryName(path));
            mesh = new Mesh();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>
        /// The mesh in Unity's coordinates (x mirrored, so triangles are turned round), with the vertex data packed as SdfSurface.hlsl
        /// expects it, plus the edge colour per vertex (UV6, see <see cref="EdgeColor"/>). The toon look takes nothing from the material,
        /// so the material regions are drawn as one submesh.
        /// </summary>
        private static void Fill(Mesh mesh, SdfGlb.MeshData data, IReadOnlyList<SdfGlb.Material> materials, string modelName)
        {
            var n = data.VertexCount;
            var position = data.Get("POSITION", 3);
            var normal = data.Get("NORMAL", 3);
            var baseColor = data.Get("_BASE_COLOR", 3) ?? data.Get("COLOR_0", 3);
            var surface = Require(data.Get("_SURFACE", 4), "_SURFACE", modelName);
            var detail = Require(data.Get("_DETAIL", 4), "_DETAIL", modelName);
            var variation = Require(data.Get("_VARIATION", 4), "_VARIATION", modelName);
            var local = Require(data.Get("_LOCAL_POSITION", 3), "_LOCAL_POSITION", modelName);
            var thickness = Require(data.Get("_THICKNESS", 1), "_THICKNESS", modelName);
            var occlusion = Require(data.Get("_OCCLUSION", 1), "_OCCLUSION", modelName);
            // Files from before speckles existed have none
            var speckle = data.Get("_SPECKLE", 4) ?? new float[4 * n];
            var speckleShape = data.Get("_SPECKLE_SHAPE", 2) ?? new float[2 * n];
            var materialIds = data.Get("_MATERIAL_IDS", 2);
            var materialBlend = data.Get("_MATERIAL_BLEND", 1);

            var positions = new Vector3[n];
            var normals = new Vector3[n];
            var colors = new Color[n];
            var uv0 = new Vector4[n];
            var uv1 = new Vector4[n];
            var uv2 = new Vector4[n];
            var uv3 = new Vector4[n];
            var uv4 = new Vector4[n];
            var uv5 = new Vector4[n];
            var uv6 = new Vector4[n];
            for (var v = 0; v < n; v++)
            {
                positions[v] = new Vector3(-position[3 * v], position[3 * v + 1], position[3 * v + 2]);
                normals[v] = new Vector3(-normal[3 * v], normal[3 * v + 1], normal[3 * v + 2]);
                colors[v] = new Color(baseColor[3 * v], baseColor[3 * v + 1], baseColor[3 * v + 2], surface[4 * v + 2]);
                uv0[v] = new Vector4(surface[4 * v], surface[4 * v + 1], surface[4 * v + 3], occlusion[v]);
                uv1[v] = new Vector4(local[3 * v], local[3 * v + 1], local[3 * v + 2], thickness[v]);
                uv2[v] = new Vector4(detail[4 * v], detail[4 * v + 1], detail[4 * v + 2], detail[4 * v + 3]);
                uv3[v] = new Vector4(variation[4 * v], variation[4 * v + 1], variation[4 * v + 2], variation[4 * v + 3]);
                uv4[v] = new Vector4(speckle[4 * v], speckle[4 * v + 1], speckle[4 * v + 2], speckle[4 * v + 3]);
                uv5[v] = new Vector4(speckleShape[2 * v], speckleShape[2 * v + 1], 0, 0);
                uv6[v] = materialIds == null || materialBlend == null
                    ? Vector4.zero
                    : EdgeColor(materials, (int)materialIds[2 * v], (int)materialIds[2 * v + 1], materialBlend[v], thickness[v]);
            }

            mesh.Clear();
            mesh.name = $"{modelName}_{data.Name}";
            mesh.indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertexBufferParams(n,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord5, VertexAttributeFormat.Float32, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 4));
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetUVs(3, uv3);
            mesh.SetUVs(4, uv4);
            mesh.SetUVs(5, uv5.Select(u => new Vector2(u.x, u.y)).ToArray());
            mesh.SetUVs(6, uv6);
            // The regions' triangles, turned round for the mirrored x
            var indices = data.Primitives.SelectMany(p => p.Indices).ToArray();
            var turned = new int[indices.Length];
            for (var t = 0; t < indices.Length; t += 3)
            {
                turned[t] = indices[t];
                turned[t + 1] = indices[t + 2];
                turned[t + 2] = indices[t + 1];
            }
            mesh.subMeshCount = 1;
            mesh.SetTriangles(turned, 0, calculateBounds: false);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
        }

        /// <summary>
        /// The vertex's edge colour for the toon look: rgb = the colour thin material turns to, a = how far it turns. Each of the two
        /// materials at the vertex turns its share towards its own edgeColor as the material is thinner than its edgeThickness, as the
        /// preview does, so a = the summed turn and rgb = the colours weighted by it.
        /// </summary>
        private static Vector4 EdgeColor(IReadOnlyList<SdfGlb.Material> materials, int first, int second, float blend, float thickness)
        {
            var sum = Vector3.zero;
            var amount = 0f;
            void Add(int id, float share)
            {
                if (share <= 0 || id < 0 || id >= materials.Count)
                    return;
                var edgeThickness = materials[id].Number("edgeThickness");
                if (edgeThickness <= 0)
                    return;
                var turn = share * Mathf.Clamp01(1 - thickness / edgeThickness);
                var color = materials[id].Color("edgeColor");
                sum += new Vector3(color.r, color.g, color.b) * turn;
                amount += turn;
            }
            Add(first, 1 - blend);
            Add(second, first == second ? 0 : blend);
            return amount > 0 ? new Vector4(sum.x / amount, sum.y / amount, sum.z / amount, amount) : Vector4.zero;
        }

        private static float[] Require(float[] values, string attribute, string model) =>
            values ?? throw new InvalidDataException($"{model} has no {attribute}; mesh it again with the current pipeline");

        /// <summary>The one toon material every mushroom shares (SdfToon.shader), created on first use and updated in place after that.</summary>
        private static Material ToonMaterial(MushroomStyle style)
        {
            var material = MaterialAsset(ToonMaterialPath, ToonShaderPath);
            style.ApplyToSurface(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>The one outline material all toon mushrooms share; the line takes its colour from each vertex.</summary>
        private static Material ToonOutlineMaterial(MushroomStyle style)
        {
            var material = MaterialAsset(ToonOutlineMaterialPath, ToonOutlineShaderPath);
            style.ApplyToOutline(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material MaterialAsset(string path, string shaderPath)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath)
                         ?? throw new InvalidOperationException($"The shader {shaderPath} is missing");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                EnsureFolder(Path.GetDirectoryName(path));
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            return material;
        }

        /// <summary>Puts the style's toon settings on every toon material that exists, without rebuilding any prefab.</summary>
        public static void ApplyToonSettings()
        {
            var style = MushroomStyle.Load();
            if (!AssetDatabase.IsValidFolder(ToonMaterialFolder))
                return;
            foreach (var path in AssetDatabase.FindAssets("t:Material", new[] { ToonMaterialFolder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                    continue;
                if (path == ToonOutlineMaterialPath)
                    style.ApplyToOutline(material);
                else
                    style.ApplyToSurface(material);
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Toon materials other than the shared one and the outline, e.g. the per-region ones builder version 4 made.</summary>
        private static void DeleteOldToonMaterials()
        {
            if (!AssetDatabase.IsValidFolder(ToonMaterialFolder))
                return;
            foreach (var path in AssetDatabase.FindAssets("t:Material", new[] { ToonMaterialFolder }).Select(AssetDatabase.GUIDToAssetPath))
                if (path != ToonMaterialPath && path != ToonOutlineMaterialPath)
                    AssetDatabase.DeleteAsset(path);
        }

        public static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder))
                return;
            var parent = Path.GetDirectoryName(folder)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
