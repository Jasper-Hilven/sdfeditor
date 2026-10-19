using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        /// </summary>
        public const int BuilderVersion = 10;

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
            var levels = model.LodCount;

            var root = new GameObject(name);
            try
            {
                var lods = new List<Renderer>[levels];
                var parts = new Dictionary<SdfGlb.MeshData, Renderer[]>();
                for (var level = 0; level < levels; level++)
                {
                    lods[level] = new List<Renderer>();
                    var coarsest = levels > 1 && level == levels - 1;
                    foreach (var meshData in model.Meshes.Select(m => m.AtLevel(level)))
                    {
                        // A part with fewer levels than the model shows its coarsest one at the levels it lacks
                        if (!parts.TryGetValue(meshData, out var renderers))
                            parts[meshData] = renderers = Part(root.transform, meshData, model, name, toon, coarsest ? null : outline);
                        lods[level].AddRange(renderers);
                    }
                }
                if (levels > 1)
                    AddLodGroup(root, model, lods);
                AddCollider(root, model, parts);

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
        /// One part at one level of detail: a child of the prefab named after the mesh (body, body_LOD1, ...), drawing its mesh asset,
        /// Assets/Mushrooms/Meshes/&lt;model&gt;_&lt;mesh&gt;.asset, plus the ink outline when <paramref name="outline"/> is given. Returns
        /// its renderers, for the level's LOD.
        /// </summary>
        private static Renderer[] Part(Transform root, SdfGlb.MeshData meshData, SdfGlb model, string name, Material material, Material outline)
        {
            var mesh = MeshAsset($"{MeshFolder}/{name}_{meshData.Name}.asset");
            Fill(mesh, meshData, model.Materials, name);
            EditorUtility.SetDirty(mesh);

            var part = new GameObject(meshData.Name);
            part.transform.SetParent(root, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
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
        /// A LOD group over the parts' levels, with Unity's screen-relative heights worked out from the pipeline's error limits, so the
        /// numbers follow the model's size and resolution instead of being guessed per model. A model of size s (the LOD group's, its
        /// largest side) at relative height h is h × 1080 pixels tall, so an error of e metres covers e / s × h × 1080 pixels; level k is
        /// good enough once that is under <see cref="LodPixelError"/>, which gives the height at which level k-1 hands over to it:
        /// h = s × pixels / (e × 1080), with e the largest error of level k over the parts (each part is meshed on its own grid). The
        /// coarsest level lasts down to <see cref="LodCullHeight"/>. Each height is kept at least half again the next one, so no level
        /// is squeezed out, and LOD0 is left at half the screen at the latest. Quality levels scale all of it with their LOD bias.
        /// <para>
        /// The levels cross-fade (URP dithers both for half a second, see SdfToon.shader): with only a few levels each change of
        /// triangles would otherwise pop.
        /// </para>
        /// </summary>
        private static void AddLodGroup(GameObject root, SdfGlb model, IReadOnlyList<List<Renderer>> renderers)
        {
            // What RecalculateBounds would give, taken from the meshes: the parts sit at the root's origin, and the renderers of an object
            // that was only just made may not have their bounds yet
            var bounds = renderers[0].Select(r => r.GetComponent<MeshFilter>().sharedMesh.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            var group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            group.localReferencePoint = bounds.center;
            group.size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));

            var levels = renderers.Count;
            var heights = new float[levels];
            heights[levels - 1] = LodCullHeight;
            for (var level = levels - 2; level >= 0; level--)
            {
                var error = model.Meshes.Max(m => m.AtLevel(level + 1).MaxError);
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
        /// </summary>
        private static void AddCollider(GameObject root, SdfGlb model, Dictionary<SdfGlb.MeshData, Renderer[]> parts)
        {
            var collider = model.Collider;
            switch (collider?.Value<string>("shape"))
            {
                case "convex":
                case "mesh":
                    var level = collider.Value<int?>("lod") ?? model.LodCount - 1;
                    foreach (var part in model.Meshes)
                    {
                        var mesh = parts[part.AtLevel(level)][0].GetComponent<MeshFilter>().sharedMesh;
                        var meshCollider = root.AddComponent<MeshCollider>();
                        meshCollider.sharedMesh = mesh;
                        meshCollider.convex = collider.Value<string>("shape") == "convex";
                    }
                    break;
                case "capsule":
                    var bottom = collider["base"].ToObject<float[]>();
                    var top = collider["top"].ToObject<float[]>();
                    var radius = collider.Value<float>("radius");
                    var capsule = root.AddComponent<CapsuleCollider>();
                    capsule.direction = 1;
                    capsule.radius = radius;
                    capsule.height = Mathf.Max(top[1] - bottom[1], 2 * radius);
                    capsule.center = new Vector3(-(bottom[0] + top[0]) / 2, (bottom[1] + top[1]) / 2, (bottom[2] + top[2]) / 2);
                    break;
            }
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
