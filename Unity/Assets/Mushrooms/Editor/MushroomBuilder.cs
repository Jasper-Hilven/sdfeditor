using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// Turns each .glb the SDF pipeline writes into Assets/Mushrooms/Generated into a classic prefab, Assets/Mushrooms/&lt;name&gt;.prefab,
    /// with HDRP materials built from the file's material table, and keeps the default scene showing every one of them.
    /// <para>
    /// Regions and materials: the pipeline splits each mesh into one primitive per material region (the material that weighs most at
    /// the triangle). Each region becomes a submesh with its own Unity material, Assets/Mushrooms/Materials/&lt;material&gt;.mat, on the
    /// SdfSurface Shader Graph (HDRP Lit, subsurface scattering with transmission and clear coat).
    /// Everything that blends between materials (colour, roughness, wetness, subsurface strength, detail, variation) comes per vertex,
    /// mixed as the preview mixes it, so region borders stay smooth; see SdfSurface.hlsl for how the vertex data is packed.
    /// The diffusion profile cannot come per vertex, and HDRP does not scatter light across a change of profile, which would show every
    /// region border as a hard seam. So materials that blend into each other in the model (cap, ridges and stem flesh) share one
    /// profile, made from all of them, and each vertex scatters with its own share of it (subsurface mask = its radius / the group's).
    /// </para>
    /// <para>
    /// Looks: <see cref="MushroomStyle"/> (Assets/Mushrooms/Mushroom Style.asset, or Tools > SDF Mushrooms > Look) picks how the prefabs
    /// are drawn. Realistic uses the materials above. Toon takes everything from the vertices, so every mushroom shares one material
    /// on SdfToon.shader, Assets/Mushrooms/Materials/Toon/Mushroom.mat, each mesh is one submesh (one draw), and each part gets a child
    /// renderer with the ink outline (SdfToonOutline.shader). Both looks' materials are always kept up to date, so switching loses nothing.
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
        public const string ProfileFolder = "Assets/Mushrooms/Materials/DiffusionProfiles";
        public const string ShaderPath = "Assets/Mushrooms/Shaders/SdfSurface.shadergraph";
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
        /// everything, the edge colour per vertex (UV6) and toon meshes as a single submesh.
        /// </summary>
        public const int BuilderVersion = 5;

        private static string VersionStamp
        {
            get
            {
                var style = MushroomStyle.Load();
                var look = style.look == MushroomLook.Toon && style.outlineWidth > 0 ? "toon+outline" : style.look.ToString().ToLowerInvariant();
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
            // The style asset changed on disk (e.g. edited outside Unity): rebuild what was built with another look
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

        public static IEnumerable<string> GeneratedModels() =>
            Directory.Exists(GeneratedFolder)
                ? Directory.GetFiles(GeneratedFolder, "*.glb").Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal)
                : Enumerable.Empty<string>();

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
                HdrpProjectSetup.Ensure();
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
                DeleteUnusedProfiles();
                DeleteOldToonMaterials();
                MushroomScene.Ensure(GeneratedModels().Select(PrefabPathOf).Where(File.Exists).ToArray());
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
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath)
                         ?? throw new InvalidOperationException($"The shader {ShaderPath} is missing");

            var maxThickness = Math.Max(0.01f, model.Meshes.Select(m => m.Get("_THICKNESS", 1)).Where(t => t != null).SelectMany(t => t).DefaultIfEmpty(0.25f).Max());
            var groups = BlendGroups(model);
            var realistic = model.Materials.Select((m, i) => MaterialFor(m, shader, maxThickness, groups[i])).ToArray();

            // The toon material is kept up to date whichever look is on, so switching to it never shows a stale one
            var style = MushroomStyle.Load();
            var toon = ToonMaterial(style);
            var isToon = style.look == MushroomLook.Toon;
            var outline = isToon && style.outlineWidth > 0 ? ToonOutlineMaterial(style) : null;

            var root = new GameObject(name);
            try
            {
                foreach (var meshData in model.Meshes)
                {
                    var mesh = MeshAsset($"{MeshFolder}/{name}_{meshData.Name}.asset");
                    Fill(mesh, meshData, model.Materials, name, singleSubmesh: isToon);
                    EditorUtility.SetDirty(mesh);

                    var part = new GameObject(meshData.Name);
                    part.transform.SetParent(root.transform, false);
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = part.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = isToon ? new[] { toon } : meshData.Primitives.Select(p => realistic[p.Material]).ToArray();
                    renderer.shadowCastingMode = ShadowCastingMode.On;

                    if (outline != null)
                    {
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
                    }
                }

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
        /// expects it, plus the edge colour per vertex for the toon look (UV6, see <see cref="EdgeColor"/>). The realistic look needs one
        /// submesh per material region; the toon look, which takes nothing from the material, draws the whole mesh as one.
        /// </summary>
        private static void Fill(Mesh mesh, SdfGlb.MeshData data, IReadOnlyList<SdfGlb.Material> materials, string modelName, bool singleSubmesh)
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
            var submeshes = singleSubmesh
                ? new[] { data.Primitives.SelectMany(p => p.Indices).ToArray() }
                : data.Primitives.Select(p => p.Indices).ToArray();
            mesh.subMeshCount = submeshes.Length;
            for (var s = 0; s < submeshes.Length; s++)
            {
                var indices = submeshes[s];
                var turned = new int[indices.Length];
                for (var t = 0; t < indices.Length; t += 3)
                {
                    turned[t] = indices[t];
                    turned[t + 1] = indices[t + 2];
                    turned[t + 2] = indices[t + 1];
                }
                mesh.SetTriangles(turned, s, calculateBounds: false);
            }
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
        }

        /// <summary>
        /// The vertex's edge colour for the toon look: rgb = the colour thin material turns to, a = how far it turns. Each of the two
        /// materials at the vertex turns its share towards its own edgeColor as the material is thinner than its edgeThickness, as the
        /// realistic look does per region, so a = the summed turn and rgb = the colours weighted by it.
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

        /// <summary>Materials that blend into each other somewhere in the model, which therefore share a diffusion profile.</summary>
        private sealed class BlendGroup
        {
            public readonly List<SdfGlb.Material> Members = new List<SdfGlb.Material>();
            public DiffusionProfileSettings Profile;

            /// <summary>The widest scattering of its materials, which the profile scatters at; each vertex takes its share of it.</summary>
            public float Radius => Members.Max(m => m.Number("subsurfaceRadius"));
        }

        /// <summary>The group of each material of the table: materials are grouped where a vertex has a share of both.</summary>
        private static BlendGroup[] BlendGroups(SdfGlb model)
        {
            var parent = Enumerable.Range(0, model.Materials.Count).ToArray();
            int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
            foreach (var mesh in model.Meshes)
            {
                var ids = mesh.Get("_MATERIAL_IDS", 2);
                var blend = mesh.Get("_MATERIAL_BLEND", 1);
                if (ids == null || blend == null)
                    continue;
                for (var v = 0; v < mesh.VertexCount; v++)
                {
                    int a = (int)ids[2 * v], b = (int)ids[2 * v + 1];
                    if (a != b && blend[v] > 0.02f && a < parent.Length && b < parent.Length)
                        parent[Find(a)] = Find(b);
                }
            }

            var groups = new Dictionary<int, BlendGroup>();
            var result = new BlendGroup[parent.Length];
            for (var i = 0; i < parent.Length; i++)
            {
                if (!groups.TryGetValue(Find(i), out var group))
                    groups[Find(i)] = group = new BlendGroup();
                group.Members.Add(model.Materials[i]);
                result[i] = group;
            }
            foreach (var group in groups.Values)
                group.Profile = ProfileFor(group);
            return result;
        }

        /// <summary>The region's material, created on first use and updated in place after that.</summary>
        private static Material MaterialFor(SdfGlb.Material source, Shader shader, float maxThickness, BlendGroup group)
        {
            var path = $"{MaterialFolder}/{SafeName(source.Name)}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                EnsureFolder(MaterialFolder);
                material = new Material(shader) { name = SafeName(source.Name) };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            var edge = source.Color("edgeColor");
            material.SetVector("_Subsurface", new Vector4(group.Radius, maxThickness, 0, 0));
            material.SetVector("_DetailAxis", source.Vector("detailAxis", Vector3.up));
            material.SetVector("_EdgeColor", new Vector4(edge.r, edge.g, edge.b, source.Number("edgeThickness")));
            material.SetVector("_Look", new Vector4(1, 0.2f, 0, 0));
            ConfigureProfile(group.Profile, group, maxThickness);
            HDMaterial.SetDiffusionProfileShaderGraph(material, group.Profile, "_DiffusionProfile");
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

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

        /// <summary>
        /// The group's diffusion profile, named after its scattering materials; groups that do not scatter share one per specular level,
        /// because a profile also sets the specular reflectance (F0) of every pixel that uses it.
        /// </summary>
        private static DiffusionProfileSettings ProfileFor(BlendGroup group)
        {
            var scattering = group.Members.Where(m => m.Number("subsurfaceRadius") > 0).Select(m => SafeName(m.Name)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var name = scattering.Length > 0
                ? string.Join("+", scattering)
                : string.Format(CultureInfo.InvariantCulture, "dielectric_ior_{0:0.00}", IndexOfRefraction(group));
            var path = $"{ProfileFolder}/{name}.asset";

            var profile = AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>(path);
            if (profile == null)
            {
                EnsureFolder(ProfileFolder);
                profile = ScriptableObject.CreateInstance<DiffusionProfileSettings>();
                AssetDatabase.CreateAsset(profile, path);
                UpdateProfileHash(profile);
            }
            HdrpProjectSetup.RegisterDiffusionProfile(profile);
            return profile;
        }

        /// <summary>The preview's F0 is 0.08 × specular; HDRP takes it from the profile as an index of refraction (averaged over the group).</summary>
        private static float IndexOfRefraction(BlendGroup group)
        {
            var f0 = Mathf.Sqrt(Mathf.Clamp(0.08f * group.Members.Average(m => m.Number("specular", 0.5f)), 0.0001f, 0.25f));
            return Mathf.Clamp((1 + f0) / (1 - f0), 1, 2);
        }

        private static void ConfigureProfile(DiffusionProfileSettings profile, BlendGroup group, float maxThickness)
        {
            // Scattering distance per channel in millimetres (HDRP's unit, at 1 m per world unit): the group's radius, coloured by the
            // average subsurfaceColor so red light travels furthest in an orange material. Light through thin parts is tinted the same.
            var scattering = group.Members.Where(m => m.Number("subsurfaceRadius") > 0).ToArray();
            var radius = group.Radius;
            var tint = scattering.Length == 0
                ? Color.black
                : scattering.Select(m => m.Color("subsurfaceColor")).Aggregate((a, b) => a + b) / scattering.Length;
            tint.a = 1;
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("profile.thicknessRemap").vector2Value = new Vector2(0, maxThickness * 1000);
            serialized.FindProperty("profile.transmissionMode").intValue = 0; // Thick object: thickness from shadow maps and the baked thickness
            // Post-scatter: the albedo is applied after the light is blurred, not half before, so the colour detail (variation, speckles,
            // edge colour) stays as sharp as the preview draws it instead of being smeared over the scattering radius
            serialized.FindProperty("profile.texturingMode").intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            profile.scatteringDistance = radius > 0 ? tint * (radius * 1000) : new Color(0.5f, 0.5f, 0.5f);
            profile.transmissionTint = radius > 0 ? tint : Color.black;
            profile.indexOfRefraction = IndexOfRefraction(group);
            profile.worldScale = 1;
            EditorUtility.SetDirty(profile);
        }

        /// <summary>Profiles no material uses any more, e.g. after materials were regrouped, are deleted so they do not fill HDRP's list.</summary>
        private static void DeleteUnusedProfiles()
        {
            if (!AssetDatabase.IsValidFolder(ProfileFolder))
                return;
            var used = AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(m => m != null && m.HasProperty("_DiffusionProfile"))
                .Select(m => AssetDatabase.GetAssetPath(HDMaterial.GetDiffusionProfileShaderGraph(m, "_DiffusionProfile")))
                .ToHashSet();
            foreach (var profile in AssetDatabase.FindAssets("t:DiffusionProfileSettings", new[] { ProfileFolder }).Select(AssetDatabase.GUIDToAssetPath))
                if (!used.Contains(profile))
                    AssetDatabase.DeleteAsset(profile);
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

        /// <summary>HDRP tells profiles apart by a hash of the asset's GUID, made when the asset is created from its menu; do the same.</summary>
        private static void UpdateProfileHash(DiffusionProfileSettings profile)
        {
            var table = typeof(DiffusionProfileSettings).Assembly.GetType("UnityEditor.Rendering.HighDefinition.DiffusionProfileHashTable");
            var update = table?.GetMethod("UpdateDiffusionProfileHashNow", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (update == null)
                Debug.LogWarning("SDF mushrooms: HDRP's diffusion profile hash table was not found; open the profile in the inspector to fix its hash");
            else
                update.Invoke(null, new object[] { profile });
        }

        private static string SafeName(string name) =>
            string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_'));

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
