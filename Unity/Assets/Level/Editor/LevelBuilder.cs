using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SdfLevel.Editor
{
    /// <summary>
    /// Turns each level the SDF pipeline bakes (sdf level, into Assets/Level/Generated/&lt;level&gt;/) into a scene of its own,
    /// Assets/Scenes/&lt;Level&gt;.unity (meadow becomes Meadow.unity), next to the model showcase Mushrooms.unity. The scene is
    /// regenerated from the baked data on every rebuild and never edited by hand.
    /// <para>
    /// The level is a static scene: everything generated is marked static for batching and occlusion (not for global illumination: the
    /// toon look uses no lightmaps), so nothing in it moves at runtime. It holds
    /// <list type="bullet">
    /// <item>a GameObject per ground chunk with a cross-fading LOD group over its levels (meshes Assets/Level/Meshes/&lt;level&gt;/), shadow
    /// casting and receiving, and a <see cref="MeshCollider"/> from LOD1;</item>
    /// <item>four invisible walls round the playable rectangle, tall enough that the hills cannot be climbed;</item>
    /// <item>the placed models (castles, big rocks; level.json "placements"): instances of the prefabs <see cref="MushroomBuilder"/>
    /// builds, static like the ground, with the colliders their prefabs bring; a placement whose prefab is missing logs a warning;</item>
    /// <item>the light, sky and post-processing (<see cref="MushroomScene.BuildLighting"/>) of the level's sky (level.json "sky"), and
    /// under Level/Sky its settings, far layer and spores (<see cref="LevelSky"/>); a level without one is lit like the mushroom scene;</item>
    /// <item>the player, the one thing that moves: a <see cref="FirstPersonController"/> with the camera at <see cref="EyeHeight"/>.</item>
    /// </list>
    /// The ground is drawn with SdfGround.shader, in the toon look of the models (the <see cref="MushroomStyle"/> settings), its material
    /// parameters from level.json (Assets/Level/Materials/&lt;level&gt; Ground.mat): grass, and over it the regions (dirt, path, tiles)
    /// where the level's region map, regions.png, puts them.
    /// </para>
    /// <para>
    /// When it rebuilds: after the bake changed the level's folder (the pipeline writes level.json last, and GeneratedFolderWatcher
    /// imports the folder as it changes); after <see cref="MushroomBuilder"/> rebuilt prefabs the level uses; on Tools > SDF Level >
    /// Rebuild; and when the editor loads, if the scene is missing or older than its level. A rebuild waits until the changes have been
    /// quiet for a moment and the models of the same pipeline run have their prefabs, so the level is built once, after them.
    /// Without a window: <c>Unity -batchmode -quit -projectPath Unity -executeMethod SdfLevel.Editor.LevelBuilder.BuildAllInBatch</c>.
    /// </para>
    /// Positions in level.json and the .glb files are the pipeline's; like <see cref="MushroomBuilder"/>, the builder mirrors x into
    /// Unity's coordinates.
    /// </summary>
    public sealed class LevelBuilder : AssetPostprocessor
    {
        public const string GeneratedFolder = "Assets/Level/Generated";
        public const string MeshFolder = "Assets/Level/Meshes";
        public const string MaterialFolder = "Assets/Level/Materials";
        public const string SceneFolder = "Assets/Scenes";
        public const string GroundShaderPath = "Assets/Level/Shaders/SdfGround.shader";
        public const string LevelJson = "level.json";

        /// <summary>
        /// Raise when the way level scenes are built changes, so every level scene is rebuilt when the editor loads. Version 2: ground
        /// regions from the region map, and placed models. Version 3: the sky.
        /// </summary>
        public const int BuilderVersion = 3;

        public const string LevelRootName = "Level";
        public const string PlacedName = "Placed";

        /// <summary>The region map of a level, regions.png, as the bake names it.</summary>
        public const string RegionMapFile = "regions.png";

        /// <summary>
        /// The width of a region's edge, in metres either side of it, and how far the shader's noise moves it: crisp, but not ruled.
        /// </summary>
        private const float RegionEdgeWidth = 0.05f;
        private const float RegionEdgeNoise = 0.06f;

        /// <summary>A convex collider round a placed model larger than this, in metres, would block walkable ground: it is made concave.</summary>
        private const float LargestConvexSize = 3;
        public const string PlayerName = "Player";

        /// <summary>The camera's height above the ground, in metres.</summary>
        public const float EyeHeight = 1.5f;

        /// <summary>The walls round the playable rectangle: this tall from below the ground, and this thick, outside it.</summary>
        private const float WallHeight = 20;
        private const float WallThickness = 1;
        private const float WallDepth = 5;

        /// <summary>
        /// How many pixels, at 1080 lines, a ground level's error may cover before the finer level is needed again. More than the models'
        /// one: the ground has no ink line whose silhouette would show the change, and its errors are mostly on the hills' smooth slopes.
        /// </summary>
        private const float LodPixelError = 2;

        /// <summary>How long the level's folder must stay unchanged before a rebuild, in seconds; a bake writes it within one.</summary>
        private const double QuietSeconds = 1;

        /// <summary>The longest a rebuild waits for models whose prefabs are still being built, in seconds.</summary>
        private const double MaxModelWaitSeconds = 60;

        private static readonly HashSet<string> pending = new HashSet<string>();
        private static double rebuildAt;
        private static double waitingSince = -1;
        private static bool building;

        /// <summary>
        /// What a level scene is stamped with: this code's version and a hash of the level.json it was built from, which names every
        /// chunk with its triangle counts and holds the level's parameters and material, so a bake that changes anything changes it.
        /// A bake that changes nothing writes the same bytes, and leaves the scene current.
        /// </summary>
        private static string VersionStamp(string levelJsonPath) =>
            $"sdf-level {BuilderVersion} {Hash128.Compute(File.ReadAllText(levelJsonPath))}";

        [InitializeOnLoadMethod]
        private static void Watch()
        {
            EditorApplication.update += RebuildWhenQuiet;
            MushroomBuilder.Built += prefabs =>
            {
                // The level places prefabs from ticket 02 on: rebuild the levels whose level.json names one of the rebuilt models
                var names = prefabs.Select(Path.GetFileNameWithoutExtension).ToHashSet();
                foreach (var level in Levels().Where(level => ModelsOf(level).Any(names.Contains)))
                    Schedule(level);
            };
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload)
        {
            // What a build imports itself is not a change of the level
            if (building)
                return;
            foreach (var path in imported.Concat(deleted).Concat(moved).Concat(movedFrom))
                if (LevelOf(path) is { } level)
                    Schedule(level);
        }

        /// <summary>
        /// The region map is data, not a picture: linear, uncompressed (the distances need every bit of their 8), clamped at the island's
        /// edge, filtered bilinearly with mip maps (filtered distances stay distances, so the edges stay crisp far away too).
        /// </summary>
        private void OnPreprocessTexture()
        {
            if (LevelOf(assetPath) == null || Path.GetFileName(assetPath) != RegionMapFile)
                return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.isReadable = false;
        }

        /// <summary>The level folder a generated file belongs to, Assets/Level/Generated/meadow for .../meadow/chunk_0_0.glb; else null.</summary>
        private static string LevelOf(string path)
        {
            path = path.Replace('\\', '/');
            if (!path.StartsWith(GeneratedFolder + "/", StringComparison.Ordinal))
                return null;
            var rest = path.Substring(GeneratedFolder.Length + 1);
            var slash = rest.IndexOf('/');
            return slash > 0 ? $"{GeneratedFolder}/{rest.Substring(0, slash)}" : null;
        }

        private static void Schedule(string level)
        {
            pending.Add(level);
            rebuildAt = EditorApplication.timeSinceStartup + QuietSeconds;
        }

        private static void RebuildWhenQuiet()
        {
            if (pending.Count == 0 || building || EditorApplication.timeSinceStartup < rebuildAt)
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            // Models of the same pipeline run first: they are meshed before the level, and the level's scene places their prefabs
            if (MushroomBuilder.StaleModels().Length > 0)
            {
                if (waitingSince < 0)
                    waitingSince = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - waitingSince < MaxModelWaitSeconds)
                    return;
            }
            waitingSince = -1;

            var levels = pending.OrderBy(l => l, StringComparer.Ordinal).ToArray();
            pending.Clear();
            foreach (var level in levels)
                if (File.Exists($"{level}/{LevelJson}"))
                    Build(level);
        }

        /// <summary>Every baked level: the folders of Assets/Level/Generated that hold a level.json, in name order.</summary>
        public static IEnumerable<string> Levels() =>
            Directory.Exists(GeneratedFolder)
                ? Directory.GetDirectories(GeneratedFolder).Select(d => d.Replace('\\', '/'))
                    .Where(d => File.Exists($"{d}/{LevelJson}")).OrderBy(d => d, StringComparer.Ordinal)
                : Enumerable.Empty<string>();

        /// <summary>meadow is built into Assets/Scenes/Meadow.unity.</summary>
        public static string ScenePathOf(string level)
        {
            var name = Path.GetFileName(level);
            return $"{SceneFolder}/{char.ToUpperInvariant(name[0])}{name.Substring(1)}.unity";
        }

        /// <summary>The models a level places (level.json "models").</summary>
        private static IEnumerable<string> ModelsOf(string level)
        {
            try
            {
                return JObject.Parse(File.ReadAllText($"{level}/{LevelJson}"))["models"]?.Values<string>().ToArray() ?? Array.Empty<string>();
            }
            catch (Exception exception) when (exception is IOException or Newtonsoft.Json.JsonException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Levels whose scene is missing, or was built from an older level.json or by an older version of this code.</summary>
        public static string[] StaleLevels() =>
            Levels().Where(level =>
            {
                var scene = ScenePathOf(level);
                return !File.Exists(scene) || AssetImporter.GetAtPath(scene)?.userData != VersionStamp($"{level}/{LevelJson}");
            }).ToArray();

        public static void BuildStale()
        {
            foreach (var level in StaleLevels())
                Build(level);
        }

        [MenuItem("Tools/SDF Level/Rebuild")]
        public static void BuildAll()
        {
            foreach (var level in Levels())
                Build(level);
        }

        /// <summary>For batch mode: the models' prefabs, then every level scene; exits with an error if a level could not be built.</summary>
        public static void BuildAllInBatch()
        {
            RenderPipelineSetup.Ensure();
            MushroomBuilder.BuildStale();
            var failed = Levels().Count(level => !Build(level));
            if (failed > 0)
                EditorApplication.Exit(1);
        }

        /// <summary>Builds the level's scene from its folder; returns whether it could.</summary>
        public static bool Build(string level)
        {
            building = true;
            try
            {
                BuildScene(level);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"SDF level: could not build {level}: {exception.Message}\n{exception.StackTrace}");
                return false;
            }
            finally
            {
                building = false;
            }
        }

        private static void BuildScene(string level)
        {
            var levelJsonPath = $"{level}/{LevelJson}";
            var data = JObject.Parse(File.ReadAllText(levelJsonPath));
            var name = data.Value<string>("name") ?? Path.GetFileName(level);
            var scenePath = ScenePathOf(level);
            var ground = (JObject)data["ground"];
            var material = GroundMaterial(name, level, data);

            // The scene is made anew each time: emptied where it is open, else created (next to what is open, if that is worth keeping)
            var existing = SceneManager.GetSceneByPath(scenePath);
            var openedHere = false;
            Scene scene;
            if (existing.IsValid() && existing.isLoaded)
            {
                scene = existing;
                foreach (var root in scene.GetRootGameObjects())
                    UnityEngine.Object.DestroyImmediate(root);
            }
            else
            {
                MushroomBuilder.EnsureFolder(SceneFolder);
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NothingWorthKeepingOpen() ? NewSceneMode.Single : NewSceneMode.Additive);
                openedHere = EditorSceneManager.sceneCount > 1;
            }

            MushroomScene.BuildLighting(scene, LevelSky.Lighting(name, data));
            var levelRoot = new GameObject(LevelRootName);
            SceneManager.MoveGameObjectToScene(levelRoot, scene);
            var groundRoot = new GameObject("Ground");
            groundRoot.transform.SetParent(levelRoot.transform, false);

            var colliderLod = ground.Value<int?>("colliderLod") ?? 1;
            var meshFolder = $"{MeshFolder}/{name}";
            var keptMeshes = new HashSet<string>();
            var triangles = 0;
            foreach (var chunk in (JArray)ground["chunks"])
            {
                var file = chunk.Value<string>("file");
                var glb = SdfGlb.Read($"{level}/{file}");
                var part = glb.Meshes.Single();
                var chunkObject = new GameObject(Path.GetFileNameWithoutExtension(file));
                chunkObject.transform.SetParent(groundRoot.transform, false);

                var renderers = new List<Renderer>();
                var meshes = new List<Mesh>();
                for (var lod = 0; lod < glb.LodCount; lod++)
                {
                    var meshData = part.AtLevel(lod);
                    var meshPath = $"{meshFolder}/{chunkObject.name}_{meshData.Name}.asset";
                    keptMeshes.Add(meshPath);
                    var mesh = MeshAsset(meshPath);
                    Fill(mesh, meshData, $"{chunkObject.name}_{meshData.Name}");
                    EditorUtility.SetDirty(mesh);
                    meshes.Add(mesh);
                    if (lod == 0)
                        triangles += mesh.triangles.Length / 3;

                    var levelObject = new GameObject(meshData.Name);
                    levelObject.transform.SetParent(chunkObject.transform, false);
                    levelObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = levelObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderers.Add(renderer);
                }
                AddLodGroup(chunkObject, part, glb.LodCount, renderers, meshes[0].bounds);
                chunkObject.AddComponent<MeshCollider>().sharedMesh = meshes[Mathf.Min(colliderLod, meshes.Count - 1)];
            }
            AssetDatabase.SaveAssets();
            DeleteMeshesExcept(meshFolder, keptMeshes);

            AddWalls(levelRoot.transform, data);
            var placed = AddPlacements(levelRoot.transform, scene, data);
            MarkStatic(levelRoot);
            AddPlayer(scene, data);
            LevelSky.Add(levelRoot.transform, name, data);

            EditorSceneManager.SaveScene(scene, scenePath);
            var importer = AssetImporter.GetAtPath(scenePath);
            var stamp = VersionStamp(levelJsonPath);
            if (importer != null && importer.userData != stamp)
            {
                importer.userData = stamp;
                importer.SaveAndReimport();
            }
            AddToBuildSettings(scenePath);
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"SDF level: built {scenePath} from {level}, {((JArray)ground["chunks"]).Count} chunks, {triangles:N0} ground triangles at LOD0, {placed} placed models");
        }

        private static bool NothingWorthKeepingOpen()
        {
            if (EditorSceneManager.sceneCount != 1)
                return false;
            var active = SceneManager.GetActiveScene();
            return string.IsNullOrEmpty(active.path) && !active.isDirty;
        }

        /// <summary>
        /// The level's ground material on SdfGround.shader: the toon settings of the models (<see cref="MushroomStyle"/>), then the
        /// materials' parameters from level.json, grass and each region's, and the region map with its mapping. The rim light is left off
        /// on the ground: from eye height nearly all of it is seen at a grazing angle, so the models' rim would light the whole field
        /// beyond a few metres and draw a ring round the player.
        /// </summary>
        private static Material GroundMaterial(string name, string level, JObject data)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(GroundShaderPath)
                         ?? throw new InvalidOperationException($"The shader {GroundShaderPath} is missing");
            var path = $"{MaterialFolder}/{name} Ground.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                MushroomBuilder.EnsureFolder(MaterialFolder);
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            MushroomStyle.Load().ApplyToSurface(material);
            material.SetVector("_ToonRim", Vector4.zero);
            var materials = (JObject)data["materials"];
            var grass = MaterialOf(materials, "grass")
                        ?? throw new InvalidDataException("level.json has no grass material (materials.grass.parameters); bake the level again");
            material.SetVector("_GroundBaseColor", Color(grass, "baseColor", 0));
            material.SetVector("_GroundVariation", Color(grass, "variationColor", grass.Number("variationScale")));
            material.SetVector("_GroundSurface", Surface(grass));
            material.SetVector("_GroundSpeckle", Color(grass, "speckleColor", grass.Number("speckleScale")));
            material.SetVector("_GroundSpeckleShape", new Vector4(grass.Number("speckleDensity"), grass.Number("speckleSize"), 0, 0));

            // The regions, in the region map's channel order; a level from before regions has none, and a white map: all far outside
            var regions = data["regions"] as JObject;
            var channels = regions?["channels"] as JArray ?? new JArray();
            for (var i = 0; i < 4; i++)
            {
                var channel = i < channels.Count ? (JObject)channels[i] : null;
                var region = channel == null ? null : MaterialOf(materials, channel.Value<string>("name"))
                    ?? throw new InvalidDataException($"level.json has no material for the region {channel.Value<string>("name")}; bake the level again");
                var stones = channel?.Value<string>("pattern") == "stones";
                material.SetVector($"_Region{i}Base", region == null ? Vector4.zero : Color(region, "baseColor", 0));
                material.SetVector($"_Region{i}Variation", region == null ? Vector4.zero : Color(region, "variationColor", region.Number("variationScale")));
                material.SetVector($"_Region{i}Surface", region == null ? new Vector4(0.9f, 0.3f, 0, 0.5f) : Surface(region));
                material.SetVector($"_Region{i}Speckle", region == null ? Vector4.zero : Color(region, "speckleColor", region.Number("speckleScale")));
                material.SetVector($"_Region{i}Pattern", region == null ? Vector4.zero
                    : new Vector4(region.Number("speckleDensity"), region.Number("speckleSize"), stones ? 1 : 0, channel.Value<float?>("stoneSize") ?? 0.6f));
                material.SetVector($"_Region{i}Grout", region == null ? Vector4.zero : Color(region, "edgeColor", channel.Value<float?>("groutWidth") ?? 0));
            }

            Texture2D map = null;
            if (regions != null)
            {
                // Imported with the bake's other files; only a map the database has not seen yet is imported here (importing it on
                // every build would schedule the next one)
                var mapPath = $"{level}/{regions.Value<string>("texture") ?? RegionMapFile}";
                map = AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath);
                if (map == null)
                {
                    AssetDatabase.ImportAsset(mapPath, ImportAssetOptions.ForceSynchronousImport);
                    map = AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath)
                          ?? throw new InvalidDataException($"The region map {mapPath} is missing; bake the level again");
                }
                // Pipeline x is Unity's -x: u = (-x - min.x) / width, v = (z - min.z) / length
                var min = regions["min"].ToObject<float[]>();
                var max = regions["max"].ToObject<float[]>();
                var (width, length) = (max[0] - min[0], max[1] - min[1]);
                material.SetVector("_GroundRegionMapping", new Vector4(-1 / width, 1 / length, -min[0] / width, -min[1] / length));
                material.SetVector("_GroundRegionShape", new Vector4(regions.Value<float?>("range") ?? 2, channels.Count, RegionEdgeWidth, RegionEdgeNoise));
            }
            else
            {
                material.SetVector("_GroundRegionShape", new Vector4(2, 0, RegionEdgeWidth, RegionEdgeNoise));
            }
            material.SetTexture("_GroundRegions", map);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static SdfGlb.Material MaterialOf(JObject materials, string role) =>
            materials?[role]?["parameters"] is JObject parameters ? new SdfGlb.Material { Name = role, Parameters = parameters } : null;

        private static Vector4 Color(SdfGlb.Material material, string colour, float w)
        {
            var c = material.Vector(colour);
            return new Vector4(c.x, c.y, c.z, w);
        }

        private static Vector4 Surface(SdfGlb.Material material) =>
            new Vector4(material.Number("roughness"), material.Number("specular"), material.Number("colorVariation"), material.Number("detailScale"));

        /// <summary>
        /// The placed models, under Level/Placed: an instance of each one's prefab with its base where level.json puts it, x mirrored and
        /// yaw turned round for Unity's coordinates, scaled. They bring the colliders their prefabs have; a convex one that the scale makes
        /// larger than <see cref="LargestConvexSize"/> is made concave, as the pipeline does for models that large. Returns how many.
        /// </summary>
        private static int AddPlacements(Transform levelRoot, Scene scene, JObject data)
        {
            var placements = data["placements"] as JArray;
            if (placements == null || placements.Count == 0)
                return 0;
            var root = new GameObject(PlacedName);
            root.transform.SetParent(levelRoot, false);
            var count = 0;
            foreach (var placement in placements)
            {
                var model = placement.Value<string>("model");
                var prefabPath = $"{MushroomBuilder.PrefabFolder}/{model}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"SDF level: there is no prefab {prefabPath} for the placed model {model}; mesh content/models (sdf unity) and let the prefabs build");
                    continue;
                }
                var position = placement["position"].ToObject<float[]>();
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetParent(root.transform, false);
                instance.transform.SetLocalPositionAndRotation(new Vector3(-position[0], position[1], position[2]),
                    Quaternion.Euler(0, -placement.Value<float>("yaw"), 0));
                var scale = placement.Value<float?>("scale") ?? 1;
                instance.transform.localScale = Vector3.one * scale;
                foreach (var collider in instance.GetComponentsInChildren<MeshCollider>())
                {
                    var size = collider.sharedMesh != null ? collider.sharedMesh.bounds.size : Vector3.zero;
                    if (collider.convex && Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * scale > LargestConvexSize)
                        collider.convex = false;
                }
                count++;
            }
            return count;
        }

        private static Mesh MeshAsset(string path)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null)
                return mesh;
            MushroomBuilder.EnsureFolder(Path.GetDirectoryName(path));
            mesh = new Mesh();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>The chunk's mesh in Unity's coordinates (x mirrored, so triangles are turned round): positions and normals only.</summary>
        private static void Fill(Mesh mesh, SdfGlb.MeshData data, string meshName)
        {
            var n = data.VertexCount;
            var position = data.Get("POSITION", 3);
            var normal = data.Get("NORMAL", 3) ?? throw new InvalidDataException($"{meshName} has no normals; bake the level again");
            var positions = new Vector3[n];
            var normals = new Vector3[n];
            for (var v = 0; v < n; v++)
            {
                positions[v] = new Vector3(-position[3 * v], position[3 * v + 1], position[3 * v + 2]);
                normals[v] = new Vector3(-normal[3 * v], normal[3 * v + 1], normal[3 * v + 2]);
            }
            var indices = data.Primitives.SelectMany(p => p.Indices).ToArray();
            var turned = new int[indices.Length];
            for (var t = 0; t < indices.Length; t += 3)
            {
                turned[t] = indices[t];
                turned[t + 1] = indices[t + 2];
                turned[t + 2] = indices[t + 1];
            }

            mesh.Clear();
            mesh.name = meshName;
            mesh.indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertexBufferParams(n,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3));
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.subMeshCount = 1;
            mesh.SetTriangles(turned, 0, calculateBounds: false);
            mesh.RecalculateBounds();
            // Kept readable: the collider is built from it, and static batching combines it
            mesh.UploadMeshData(false);
        }

        /// <summary>
        /// A cross-fading LOD group over the chunk's levels, with screen-relative heights from the bake's measured error of each level, as
        /// <see cref="MushroomBuilder"/> works them out for models: level k takes over once its error covers less than
        /// <see cref="LodPixelError"/> pixels at 1080 lines. The coarsest level is never culled, so the ground has no holes far away.
        /// </summary>
        private static void AddLodGroup(GameObject chunk, SdfGlb.MeshData part, int levels, IReadOnlyList<Renderer> renderers, Bounds bounds)
        {
            var group = chunk.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            group.localReferencePoint = bounds.center;
            group.size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));

            var heights = new float[levels];
            heights[levels - 1] = 0;
            for (var level = levels - 2; level >= 0; level--)
            {
                var error = part.AtLevel(level + 1).MaxError;
                var height = error > 0 ? group.size * LodPixelError / (error * 1080) : 0.5f / (1 << level);
                heights[level] = Mathf.Max(height, heights[level + 1] * 1.5f, 0.002f * (levels - 1 - level));
            }
            heights[0] = Mathf.Min(heights[0], 0.5f);
            for (var level = 1; level < levels; level++)
                heights[level] = Mathf.Min(heights[level], heights[level - 1] / 1.5f);
            heights[levels - 1] = 0;
            group.SetLODs(renderers.Select((r, level) => new LOD(heights[level], new[] { r })).ToArray());
        }

        /// <summary>Mesh assets of chunks the level no longer has.</summary>
        private static void DeleteMeshesExcept(string folder, HashSet<string> kept)
        {
            if (!AssetDatabase.IsValidFolder(folder))
                return;
            foreach (var path in AssetDatabase.FindAssets("t:Mesh", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).Distinct())
                if (!kept.Contains(path))
                    AssetDatabase.DeleteAsset(path);
        }

        /// <summary>Four invisible box colliders just outside the playable rectangle, from below the ground to well above the hills' crests.</summary>
        private static void AddWalls(Transform levelRoot, JObject data)
        {
            var walls = new GameObject("Walls");
            walls.transform.SetParent(levelRoot, false);
            var size = data["play"]["size"];
            var (width, length) = (size[0].Value<float>(), size[1].Value<float>());
            var centreY = (WallHeight - WallDepth) / 2;
            var height = WallHeight + WallDepth;
            void Wall(string name, Vector3 centre, Vector3 extent)
            {
                var wall = new GameObject(name);
                wall.transform.SetParent(walls.transform, false);
                wall.transform.localPosition = centre;
                wall.AddComponent<BoxCollider>().size = extent;
            }
            // The rectangle is centred on the origin, so mirroring x leaves the walls where they are
            Wall("Wall +X", new Vector3((width + WallThickness) / 2, centreY, 0), new Vector3(WallThickness, height, length + 2 * WallThickness));
            Wall("Wall -X", new Vector3(-(width + WallThickness) / 2, centreY, 0), new Vector3(WallThickness, height, length + 2 * WallThickness));
            Wall("Wall +Z", new Vector3(0, centreY, (length + WallThickness) / 2), new Vector3(width + 2 * WallThickness, height, WallThickness));
            Wall("Wall -Z", new Vector3(0, centreY, -(length + WallThickness) / 2), new Vector3(width + 2 * WallThickness, height, WallThickness));
        }

        /// <summary>Static for batching and occlusion, not for global illumination (no lightmaps in the toon look).</summary>
        private static void MarkStatic(GameObject root)
        {
            const StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(transform.gameObject, flags);
        }

        /// <summary>The player at the level's spawn point, looking along +x (across the long side), with the camera at eye height.</summary>
        private static void AddPlayer(Scene scene, JObject data)
        {
            var spawn = data["spawn"];
            var player = new GameObject(PlayerName);
            SceneManager.MoveGameObjectToScene(player, scene);
            player.transform.SetPositionAndRotation(
                new Vector3(-spawn[0].Value<float>(), spawn[1].Value<float>() + 0.05f, spawn[2].Value<float>()),
                Quaternion.Euler(0, 90, 0));
            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.75f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0, controller.height / 2, 0);
            controller.slopeLimit = 45;
            controller.stepOffset = 0.4f;
            player.AddComponent<FirstPersonController>();

            var camera = MushroomScene.CreateCamera(scene, "Main Camera", 60, player.transform);
            camera.transform.localPosition = new Vector3(0, EyeHeight, 0);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400;
        }

        private static void AddToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Any(s => s.path == scenePath))
                return;
            // After the mushroom scene, which stays the first
            EditorBuildSettings.scenes = scenes.Append(new EditorBuildSettingsScene(scenePath, true)).ToArray();
        }
    }
}
