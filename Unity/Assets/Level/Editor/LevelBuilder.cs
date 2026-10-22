using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SdfBattle;
using SdfMushrooms.Editor;
using SdfPickups;
using SdfPickups.Editor;
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
    /// <item>four invisible walls round the playable rectangle, tall enough that the hills cannot be climbed; or, for a level with a
    /// room (level.json "room", the chessboard cube), Level/Room (<see cref="LevelRoom"/>): drawn walls and ceiling and colliders on all
    /// six faces, and then neither those walls nor any sky;</item>
    /// <item>the placed models (castles, big rocks; level.json "placements"): instances of the prefabs <see cref="MushroomBuilder"/>
    /// builds, static like the ground, with the colliders their prefabs bring; a placement whose prefab is missing logs a warning. When
    /// the level has a <c>draw castle</c> section, a placed model's part instances (the children of its prefab with a LOD group of their
    /// own, a castle's repeated parts) are drawn by the instanced renderer in that group instead, and keep only their colliders here;</item>
    /// <item>the scattered models (grass, flowers; level.json "instances", the bake's instances.bin): no GameObject each, but one
    /// <see cref="InstancedScatterRenderer"/> that draws them all instanced, in the draw groups of level.json "drawGroups", reading the
    /// prototypes' meshes from their prefabs; it is the one object besides the player that is not static, and has no colliders. Next to
    /// it, the level's <see cref="Wind"/>. The trees and crest clusters (the level file's trees sections) are drawn the same way, in
    /// their own draw groups; a scattered model whose prefab has a collider (a tree's trunk capsule) gets a static, collider-only object
    /// per instance under Level/Scatter Colliders, so trunks block the player while no tree is a rendered GameObject;</item>
    /// <item>the island's rocky underside (level.json "edge"."underside"), a placed model at the origin, its top meeting the ground's
    /// rim; it has no outline or shadow (its model's settings);</item>
    /// <item>the light, sky and post-processing (<see cref="MushroomScene.BuildLighting"/>) of the level's sky (level.json "sky"), and
    /// under Level/Sky its settings, far layer and spores (<see cref="LevelSky"/>); a level without one is lit like the mushroom scene,
    /// unless it has a room, which is lit by <see cref="RoomLight"/> and has no sky object, skybox or sky material at all;</item>
    /// <item>the player, the one thing that moves: a <see cref="FirstPersonController"/> with the camera at <see cref="EyeHeight"/>;</item>
    /// <item>Battle, the level's battle layout (level.json "battle", the level file's sides and neutral pods): a <see cref="BattleArena"/>
    /// with Battle/West and Battle/East, each a <see cref="SdfBattle.BattleSide"/> over its spawn marker, flag stand
    /// (Assets/Pickups/Flags) and weapon and health pods (Assets/Pickups/Pods), and Battle/Neutral with the two neutral pods;
    /// not static, as the pods' pickups turn and hide; on it the <see cref="Match"/> (ticket 20), with the player as you, the two scout
    /// prefabs for the bots' bodies and the GUI prefab for the HUD: it makes the eight players at runtime, none are in the scene;</item>
    /// <item>SceneInfo, an <see cref="IMMScene"/> with what the game needs to know of the scene: its play area and extent, the spawn,
    /// the player and camera, the placed models, the battle sides;</item>
    /// <item>Sun Shadow, the level's fixed sun shadow (<see cref="StaticSunShadow"/>): one shadow map over the whole level, rendered
    /// once, in place of the light's cascaded shadows, which are off;</item>
    /// <item>Game, an instance of the hand-made <see cref="IMMGame.PrefabPath"/>, the same in every level but for its reference to
    /// SceneInfo.</item>
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
        /// regions from the region map, and placed models. Version 3: the scattered models through the instanced renderer, and wind.
        /// Version 4: the placed models' repeated parts (a castle's) through the instanced renderer, in the draw group <see cref="PartsGroup"/>.
        /// Version 5: the sky. Version 6: the island's underside, and collider-only objects for scattered models with colliders (trees).
        /// Version 9: SceneInfo, the scene's <see cref="IMMScene"/>. Version 10: Game, an instance of <see cref="IMMGame.PrefabPath"/>.
        /// Version 11: the ground's LOD distances doubled.
        /// Version 12: the fixed sun shadow (<see cref="StaticSunShadow"/>) instead of the light's cascades. Version 13: the ground's LOD
        /// distances back down, to a quarter. Version 14: Battle, the battle sides and neutral pods (<see cref="BattleArena"/>).
        /// Version 15: the match on Battle (<see cref="Match"/>), the sides' spawn zones, every player's capsule from
        /// <see cref="FirstPersonController.Configure"/>.
        /// Version 16: the bots' navmesh (ticket 23, <see cref="SdfBattle.Editor.BattleNavmesh"/>), Level/Navigation Scout and Tank.
        /// Version 17: the scouts' agent 5 cm wider than the capsule.
        /// Version 18: the sides' turrets and machine zones (ticket 24.2), the turrets obstacles in the navmesh.
        /// Version 19: the machines' prefabs on Battle (ticket 24.3, <see cref="SdfBattle.Machines"/>); the match without a countdown.
        /// Version 20: the level's machine set and a room's helicopter ceiling (ticket 27.2).
        /// Version 21: the level's movers (ticket 27.3, <see cref="PathMover"/>), under Movers at the scene's root.
        /// Version 22: the movers' behaviour (ticket 27.10): push, damage, kill feed name, puffs, crossing distance.
        /// Version 23: a room as a backdrop model with open edges (ticket 28.2, <see cref="LevelBackdrop"/>), no edge walls.
        /// </summary>
        public const int BuilderVersion = 23;

        /// <summary>
        /// The draw group that a placed model's part instances go to when the level has a <c>draw</c> section for it (the castles' walls,
        /// towers, merlons, ...): their renderers leave the scene, and only each use's colliders stay as a GameObject.
        /// </summary>
        public const string PartsGroup = "castle";

        /// <summary>Where the mirrored copies of part meshes go, below the level's meshes (see PartInstances).</summary>
        public const string MirroredFolder = "Mirrored";

        /// <summary>
        /// How a room (a closed cube with no sky) is lit, decided in ticket 14.2: light only, no lamps in the room and no dome. One
        /// directional light hangs overhead, towards (0.3, 1, 0.35): near straight down, tilted a little so that the pieces' shadows fall
        /// across the floor's squares and read as shapes; warm white (the sun's 5600 K tint, a lamp rather than daylight), the toon
        /// shaders taking only its direction, hue and shadows; the ambient is the showcase's flat, dim one. The walls and the ceiling
        /// cast no shadows of that light (the ceiling would shade the whole room) and receive none; pieces cast and receive them on the
        /// floor. A wall only grazes an overhead light, so SdfRoomFace.shader leans the light toward each face's own normal: walls in the
        /// lit and mid bands, the ceiling in the mid band, the floor in the lit band with the shadows in the shadow band. There is no
        /// distance fade (no SkySettings fade amount): a far wall keeps its own colours, no fog, and the camera clears to a flat colour
        /// it never shows.
        /// </summary>
        private static readonly MushroomScene.IndoorLighting RoomLight = new MushroomScene.IndoorLighting
        {
            lightDirection = new Vector3(0.3f, 1, 0.35f).normalized,
            lightColor = new Color(1, 0.95f, 0.84f),
            ambient = new Color(0.17f, 0.14f, 0.1f),
        };

        /// <summary>
        /// A room's lamp as its level file has it (ticket 27.1: the room block's <c>light</c>: the direction towards the lamp, its colour and
        /// the ambient, each optional), the rest from <see cref="RoomLight"/>. x is mirrored, as everywhere.
        /// </summary>
        private static MushroomScene.IndoorLighting RoomLightOf(JObject data)
        {
            var light = data["room"]?["light"] as JObject;
            if (light == null)
                return RoomLight;
            Vector3 Direction(JToken t) => new Vector3(-t[0].Value<float>(), t[1].Value<float>(), t[2].Value<float>()).normalized;
            UnityEngine.Color Colour(JToken t) => new UnityEngine.Color(t[0].Value<float>(), t[1].Value<float>(), t[2].Value<float>());
            return new MushroomScene.IndoorLighting
            {
                lightDirection = light["direction"] != null ? Direction(light["direction"]) : RoomLight.lightDirection,
                lightColor = light["color"] != null ? Colour(light["color"]) : RoomLight.lightColor,
                ambient = light["ambient"] != null ? Colour(light["ambient"]) : RoomLight.ambient,
            };
        }

        /// <summary>
        /// The far plane reaches across a room's diagonal with room to spare: at least <see cref="RoomFarPlane"/> (160 m square: 226 m on
        /// the floor, 277 m to the opposite top corner), else the room's own diagonal (width, height, depth) times 1.25.
        /// </summary>
        private static float RoomFarPlaneOf(JObject data)
        {
            var size = data["room"]?["size"];
            if (size == null)
                return RoomFarPlane;
            var diagonal = new Vector3(size[0].Value<float>(), size[1].Value<float>(), size[2].Value<float>()).magnitude;
            return Mathf.Max(RoomFarPlane, diagonal * 1.25f);
        }

        /// <summary>The least far plane of a room's camera.</summary>
        private const float RoomFarPlane = 450;

        public const string LevelRootName = "Level";
        public const string PlacedName = "Placed";
        public const string UndersideName = "Underside";
        public const string ScatterCollidersName = "Scatter Colliders";
        public const string ScatterName = "Scatter";
        public const string WindName = "Wind";
        public const string NeutralName = "Neutral";
        public const string SpawnName = "Spawn";
        public const string MachineSpawnName = "Machine Spawn";

        /// <summary>The bake's instances, instances.bin, copied next to the level's meshes as instances.bytes, which Unity reads as a TextAsset.</summary>
        public const string InstancesFile = "instances.bin";
        public const string InstancesAsset = "instances.bytes";

        /// <summary>The region map of a level, regions.png, as the bake names it.</summary>
        public const string RegionMapFile = "regions.png";

        /// <summary>
        /// The width of a region's edge, in metres either side of it. How far the shader's noise moves it is per region (the level
        /// file's wobble, 0.06 m unless given: crisp, but not ruled; 0 for straight squares).
        /// </summary>
        private const float RegionEdgeWidth = 0.05f;
        private const float DefaultRegionWobble = 0.06f;

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
        /// Four: the ground's finer levels showed no difference over its coarser ones, so it changes level at a quarter of the distance
        /// one pixel would give.
        /// </summary>
        private const float LodPixelError = 4;

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
            // A scene whose assets are gone (see HasMissingReferences) is built anew once the editor has loaded
            if (!Application.isBatchMode)
                EditorApplication.delayCall += () =>
                {
                    foreach (var level in StaleLevels())
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

        /// <summary>Levels whose scene is missing, was built from an older level.json or by an older version of this code, or has lost its ground meshes.</summary>
        public static string[] StaleLevels() =>
            Levels().Where(level =>
            {
                var scene = ScenePathOf(level);
                return !File.Exists(scene) || AssetImporter.GetAtPath(scene)?.userData != VersionStamp($"{level}/{LevelJson}")
                    || HasMissingReferences(level, scene);
            }).ToArray();

        /// <summary>
        /// Whether the scene does not point at the ground chunk meshes that are in this project. The scene is committed but the level's
        /// meshes (Assets/Level/Meshes, .asset files) are generated and ignored by git, so on a checkout where they were made anew they
        /// have other GUIDs: the ground's MeshColliders then have no mesh, and the player falls through the ground. The version stamp
        /// cannot see that (level.json is the same), so the chunks' GUIDs are looked for in the scene as well.
        /// </summary>
        private static bool HasMissingReferences(string level, string scenePath)
        {
            try
            {
                var data = JObject.Parse(File.ReadAllText($"{level}/{LevelJson}"));
                var chunks = data["ground"]?["chunks"] as JArray;
                if (chunks == null)
                    return false;
                var sceneText = File.ReadAllText(scenePath);
                var meshFolder = $"{MeshFolder}/{data.Value<string>("name") ?? Path.GetFileName(level)}";
                foreach (var chunk in chunks)
                {
                    var guid = AssetDatabase.AssetPathToGUID($"{meshFolder}/{Path.GetFileNameWithoutExtension(chunk.Value<string>("file"))}_ground.asset");
                    if (string.IsNullOrEmpty(guid) || !sceneText.Contains(guid, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }
            catch (Exception exception) when (exception is IOException or Newtonsoft.Json.JsonException)
            {
                return false;
            }
        }

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
            // The weapon pickup pods are built over the weapons' and the pod's prefabs
            SdfPickups.Editor.WeaponPodBuilder.BuildStale();
            // The flag stands, over the stand's and the flags' prefabs
            SdfPickups.Editor.FlagStandBuilder.BuildStale();
            // The machines (ticket 24), over their pieces' prefabs: the turrets stand in the battle levels
            SdfMachines.Editor.MachineBuilder.BuildStale();
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

            var hasRoom = LevelRoom.HasRoom(data);
            // A backdrop (ticket 28.2) is a room with open edges: the room's lamp and no sky, but no walls and not "indoor"
            var hasBackdrop = LevelBackdrop.IsBackdrop(data);
            var sun = MushroomScene.BuildLighting(scene, LevelSky.Lighting(name, data), (hasRoom || hasBackdrop) && !(data["sky"] is JObject) ? RoomLightOf(data) : null);
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

            if (hasRoom)
                LevelRoom.Add(levelRoot.transform, name, meshFolder, data);
            else if (hasBackdrop)
                LevelBackdrop.Add(levelRoot.transform, scene, data);
            else
                AddWalls(levelRoot.transform, data);
            var parts = new PartInstances(((data["drawGroups"] as JArray) ?? new JArray()).Any(g => g.Value<string>("name") == PartsGroup),
                $"{meshFolder}/{MirroredFolder}");
            var placements = new List<IMMScene.Placement>();
            var placed = AddPlacements(levelRoot.transform, scene, data, parts, placements);
            AddUnderside(levelRoot.transform, scene, data);
            MarkStatic(levelRoot);
            var scattered = AddScatter(scene, level, meshFolder, data, parts);
            var scatterColliders = AddScatterColliders(levelRoot.transform, meshFolder, data);
            parts.DeleteUnused();
            AssetDatabase.SaveAssets();
            var player = AddPlayer(scene, data, (hasRoom || hasBackdrop) && !(data["sky"] is JObject));
            var battle = AddBattle(scene, data, player);
            // The movers (ticket 27.3) stand outside the Level root, so the bots' navmesh below leaves them out
            var movers = AddMovers(scene, data);
            // The bots' navmesh (ticket 23), from the level's colliders, without the player and the battle objects
            var navmeshes = SdfBattle.Editor.BattleNavmesh.Build(levelRoot.transform, PlayCorner(data, "min"), PlayCorner(data, "max"), battle, meshFolder,
                player, battle != null ? battle.gameObject : null);
            LevelSky.Add(levelRoot.transform, name, data);
            AddStaticShadow(scene, sun);
            var sceneInfo = AddSceneInfo(scene, name, hasRoom, data, levelRoot.transform, player, placements, battle);
            AddGame(scene, sceneInfo);

            EditorSceneManager.SaveScene(scene, scenePath);
            var importer = AssetImporter.GetAtPath(scenePath);
            var stamp = VersionStamp(levelJsonPath);
            if (importer != null && importer.userData != stamp)
            {
                importer.userData = stamp;
                importer.SaveAndReimport();
            }
            AddToBuildSettings(scenePath);
            if (hasRoom && LevelRoom.CheckEdges(level) > 0)
                throw new InvalidDataException("the room's squares or edges are wrong, see above");
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"SDF level: built {scenePath} from {level}, {((JArray)ground["chunks"]).Count} chunks, {triangles:N0} ground triangles at LOD0, {placed} placed models, {movers} movers, {scattered:N0} scattered ({scatterColliders} with colliders), {navmeshes} navmeshes");
        }

        /// <summary>A corner of the play area (level.json "play" min or max) in Unity's coordinates, x mirrored.</summary>
        private static Vector2 PlayCorner(JObject data, string corner)
        {
            var play = data["play"];
            var mirrored = corner == "min" ? "max" : "min";
            if (play?[mirrored] is JArray x && play[corner] is JArray z)
                return new Vector2(-x[0].Value<float>(), z[1].Value<float>());
            var size = play?["size"];
            var half = size != null ? new Vector2(size[0].Value<float>(), size[1].Value<float>()) / 2 : new Vector2(75, 50);
            return corner == "min" ? -half : half;
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
            var wobble = new Vector4(DefaultRegionWobble, DefaultRegionWobble, DefaultRegionWobble, DefaultRegionWobble);
            for (var i = 0; i < 4 && i < channels.Count; i++)
                wobble[i] = ((JObject)channels[i]).Value<float?>("wobble") ?? DefaultRegionWobble;
            material.SetVector("_GroundRegionWobble", wobble);
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
                material.SetVector("_GroundRegionShape", new Vector4(regions.Value<float?>("range") ?? 2, channels.Count, RegionEdgeWidth, 0));
            }
            else
            {
                material.SetVector("_GroundRegionShape", new Vector4(2, 0, RegionEdgeWidth, 0));
            }
            material.SetTexture("_GroundRegions", map);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static SdfGlb.Material MaterialOf(JObject materials, string role) =>
            materials?[role]?["parameters"] is JObject parameters ? new SdfGlb.Material { Name = role, Parameters = parameters } : null;

        internal static Vector4 Color(SdfGlb.Material material, string colour, float w)
        {
            var c = material.Vector(colour);
            return new Vector4(c.x, c.y, c.z, w);
        }

        internal static Vector4 Surface(SdfGlb.Material material) =>
            new Vector4(material.Number("roughness"), material.Number("specular"), material.Number("colorVariation"), material.Number("detailScale"));

        /// <summary>
        /// The placed models, under Level/Placed: an instance of each one's prefab with its base where level.json puts it, x mirrored and
        /// yaw turned round for Unity's coordinates, scaled. They bring the colliders their prefabs have; a convex one that the scale makes
        /// larger than <see cref="LargestConvexSize"/> is made concave, as the pipeline does for models that large. Adds each to
        /// <paramref name="placed"/> and returns how many.
        /// </summary>
        private static int AddPlacements(Transform levelRoot, Scene scene, JObject data, PartInstances parts, List<IMMScene.Placement> placed)
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
                parts.Take(model, instance);
                placed.Add(new IMMScene.Placement { model = model, transform = instance.transform });
                count++;
            }
            return count;
        }

        public const string MoversName = "Movers";

        /// <summary>
        /// The level's movers (level.json "movers", ticket 27.3), under <see cref="MoversName"/> at the scene's root: one object per mover
        /// with a <see cref="PathMover"/> and, below it, the cars in train order, each an empty object with a kinematic
        /// <see cref="Rigidbody"/> and a <see cref="PathMoverCar"/> over an instance of its model's prefab with convex mesh colliders (a model without a collider gets a box round its
        /// renderers). The path is in Unity's coordinates, x mirrored as for placements. The cars start where the timetable has them at clock 0.
        /// A mover whose model has no prefab is left out, with a warning. Returns how many movers were built.
        /// </summary>
        private static int AddMovers(Scene scene, JObject data)
        {
            if (!(data["movers"] is JArray movers) || movers.Count == 0)
                return 0;
            var root = new GameObject(MoversName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var count = 0;
            foreach (var entry in movers)
            {
                var name = entry.Value<string>("name");
                var cars = (JArray)entry["cars"];
                var prefabs = cars.Select(c => AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/{c.Value<string>("model")}.prefab")).ToArray();
                if (prefabs.Any(p => p == null))
                {
                    Debug.LogWarning($"SDF level: the mover {name} is left out: there is no prefab for its car {cars[Array.FindIndex(prefabs, p => p == null)].Value<string>("model")}; mesh content/models (sdf unity) and let the prefabs build");
                    continue;
                }
                var mover = new GameObject($"Mover {name}");
                mover.transform.SetParent(root.transform, false);
                var component = mover.AddComponent<PathMover>();
                component.path = ((JArray)entry["path"]).Select(p => new Vector2(-p[0].Value<float>(), p[1].Value<float>())).ToArray();
                component.heights = (entry["heights"] as JArray)?.Select(h => new Vector2(h[0].Value<float>(), h[1].Value<float>())).ToArray() ?? new Vector2[0];
                component.carLengths = cars.Select(c => c.Value<float>("length")).ToArray();
                component.speed = entry.Value<float>("speed");
                component.gap = entry.Value<float>("gap");
                component.phase = entry.Value<float>("phase");
                // What the cars do (ticket 27.10); a level.json from before has none of it, and the mover stays harmless
                component.pushes = entry.Value<float?>("pushes") ?? 0;
                component.damage = entry.Value<float?>("damage") ?? 0;
                component.hazardName = entry.Value<string>("hazard") ?? name;
                component.cooldown = entry.Value<float?>("cooldown") ?? 1;
                component.puffEvery = entry.Value<float?>("puff") ?? 0;
                if (entry["puffAt"] is JArray puffAt && puffAt.Count == 3)
                    component.puffAt = new Vector3(puffAt[0].Value<float>(), puffAt[1].Value<float>(), puffAt[2].Value<float>());
                component.whistleLead = entry.Value<float?>("whistle") ?? 0;
                component.crossingWarn = entry.Value<float?>("crossing") ?? 0;
                for (var i = 0; i < prefabs.Length; i++)
                {
                    // The car's own object holds the body and the mover's parts, so the model's prefab instance stays unmodified
                    // (components added to a prefab instance do not always survive the scene's save)
                    var car = new GameObject($"{name} car {i + 1} ({cars[i].Value<string>("model")})");
                    car.transform.SetParent(mover.transform, false);
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], scene);
                    model.transform.SetParent(car.transform, false);
                    MakeCarCollide(car);
                    var body = car.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.interpolation = RigidbodyInterpolation.None;
                    car.AddComponent<PathMoverCar>();
                }
                component.Place(0);
                count++;
            }
            return count;
        }

        /// <summary>A car's colliders: every mesh collider convex (a moving body needs hulls); a car without one gets a box round its renderers.</summary>
        private static void MakeCarCollide(GameObject car)
        {
            var meshColliders = car.GetComponentsInChildren<MeshCollider>();
            foreach (var collider in meshColliders)
                collider.convex = true;
            if (meshColliders.Length > 0 || car.GetComponentInChildren<Collider>() != null)
                return;
            var renderers = car.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            var box = car.AddComponent<BoxCollider>();
            box.center = car.transform.InverseTransformPoint(bounds.center);
            box.size = bounds.size;
            Debug.LogWarning($"SDF level: the mover car {car.name} has no collider; it gets a box round its renderers (give its model a convex collider)");
        }

        /// <summary>
        /// The island's underside (level.json "edge"."underside"), under Level/<see cref="UndersideName"/>: an instance of its model's
        /// prefab at the origin, unturned, where its top follows the island's edge at the ground's rim. Its prefab casts no shadow and
        /// has no outline (the model's own settings). A missing prefab logs a warning.
        /// </summary>
        private static void AddUnderside(Transform levelRoot, Scene scene, JObject data)
        {
            var model = data["edge"]?.Value<string>("underside");
            if (string.IsNullOrEmpty(model))
                return;
            var prefabPath = $"{MushroomBuilder.PrefabFolder}/{model}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"SDF level: there is no prefab {prefabPath} for the island's underside {model}; mesh content/models (sdf unity) and let the prefabs build");
                return;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = UndersideName;
            instance.transform.SetParent(levelRoot, false);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        /// <summary>
        /// Collider-only objects for the scattered models whose prefab has colliders on its root (the trees' and crest clusters' trunk
        /// capsules, from their models' <c>collider capsule</c>): under Level/<see cref="ScatterCollidersName"/>, one static object per
        /// instance of instances.bin, where the renderer draws it (x mirrored, yaw turned round, scaled), with a copy of each of those
        /// colliders and no renderer. The canopies get nothing: the capsule is the trunk's. All instances get one, whether or not the
        /// player can reach them, so physics objects bounce off the trees on the hills too. Returns how many.
        /// </summary>
        private static int AddScatterColliders(Transform levelRoot, string meshFolder, JObject data)
        {
            var bytes = data["instances"] == null ? null : AssetDatabase.LoadAssetAtPath<TextAsset>($"{meshFolder}/{InstancesAsset}");
            if (bytes == null)
                return 0;
            var file = ScatterInstances.Read(bytes.bytes);
            var colliders = file.Prototypes.Select(model =>
                AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/{model}.prefab")?.GetComponents<Collider>()
                ?? Array.Empty<Collider>()).ToArray();
            if (colliders.All(c => c.Length == 0))
                return 0;

            var root = new GameObject(ScatterCollidersName);
            root.transform.SetParent(levelRoot, false);
            var count = 0;
            foreach (var record in file.Records)
            {
                var own = colliders[record.Prototype];
                if (own.Length == 0)
                    continue;
                var instance = new GameObject($"{file.Prototypes[record.Prototype]} {count}");
                instance.transform.SetParent(root.transform, false);
                instance.transform.SetLocalPositionAndRotation(record.Position, record.Rotation);
                instance.transform.localScale = Vector3.one * record.Scale;
                foreach (var collider in own)
                    EditorUtility.CopySerialized(collider, instance.AddComponent(collider.GetType()));
                count++;
            }
            MarkStatic(root);
            return count;
        }

        /// <summary>
        /// The scattered models: a root object <see cref="ScatterName"/> with the <see cref="InstancedScatterRenderer"/>, pointed at the
        /// level's instances (instances.bin, copied to instances.bytes beside the level's meshes, written only when it changed), at each
        /// prototype's prefab and at the shared toon and outline materials, with each draw group's settings from level.json. Not static:
        /// static batching would merge the meshes it draws. The level's <see cref="Wind"/> is its own root object. Returns how many
        /// instances there are; a level without any gets neither the renderer nor the copy.
        /// </summary>
        private static int AddScatter(Scene scene, string level, string meshFolder, JObject data, PartInstances parts)
        {
            var assetPath = $"{meshFolder}/{InstancesAsset}";
            var instances = data["instances"] as JObject;
            var groups = data["drawGroups"] as JArray ?? new JArray();
            if (instances == null)
            {
                if (File.Exists(assetPath))
                    AssetDatabase.DeleteAsset(assetPath);
                if (groups.Count == 0)
                    return 0;
            }

            TextAsset bytes = null;
            if (instances != null)
            {
                var source = $"{level}/{instances.Value<string>("file") ?? InstancesFile}";
                var content = File.ReadAllBytes(source);
                if (!File.Exists(assetPath) || !content.AsSpan().SequenceEqual(File.ReadAllBytes(assetPath)))
                {
                    MushroomBuilder.EnsureFolder(meshFolder);
                    File.WriteAllBytes(assetPath, content);
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                }
                bytes = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath)
                        ?? throw new InvalidDataException($"Could not import {assetPath}");
            }

            var root = new GameObject(ScatterName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var renderer = root.AddComponent<InstancedScatterRenderer>();
            renderer.instances = bytes;
            renderer.toonMaterial = AssetDatabase.LoadAssetAtPath<Material>(MushroomBuilder.ToonMaterialPath);
            renderer.outlineMaterial = AssetDatabase.LoadAssetAtPath<Material>(MushroomBuilder.ToonOutlineMaterialPath);
            renderer.lowQualityLevel = RenderPipelineSetup.Web.Name;
            renderer.groups = groups.Select(g => new ScatterDrawGroup
            {
                name = g.Value<string>("name"),
                rings = g["rings"].ToObject<float[]>(),
                outlineRings = g.Value<int?>("outline") ?? 0,
                shadowRings = g.Value<int?>("shadows") ?? 0,
                lowShadowRings = g.Value<int?>("lowShadows") ?? 0,
                blend = g.Value<float?>("blend") ?? 1,
                fade = g.Value<float?>("fade") ?? 0,
                wind = g.Value<float?>("wind") ?? 0,
                windHeight = g.Value<float?>("windHeight") ?? 1,
                tintHeight = g.Value<float?>("tintHeight") ?? 0,
                lowDensity = g.Value<float?>("lowDensity") ?? 1,
                lowDistance = g.Value<float?>("lowDistance") ?? 1,
            }).ToArray();
            renderer.prototypes = (instances?["prototypes"] as JArray ?? new JArray()).Select(p =>
            {
                var model = p.Value<string>("model");
                var prefabPath = $"{MushroomBuilder.PrefabFolder}/{model}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                    Debug.LogWarning($"SDF level: there is no prefab {prefabPath} for the scattered model {model}; mesh content/models (sdf unity) and let the prefabs build");
                return new ScatterPrototype { name = model, group = p.Value<string>("group"), prefab = prefab };
            }).ToArray();
            // The placed models' part instances after the scattered ones
            var offset = renderer.prototypes.Length;
            renderer.prototypes = renderer.prototypes.Concat(parts.Prototypes).ToArray();
            renderer.placements = parts.Placements.Select(p => { p.prototype += offset; return p; }).ToArray();

            var wind = new GameObject(WindName);
            SceneManager.MoveGameObjectToScene(wind, scene);
            wind.AddComponent<Wind>();
            return instances?.Value<int?>("count") ?? 0;
        }

        /// <summary>
        /// The part instances of the placed models, for the instanced renderer: a prototype per model and part (castle0/castle_merlon), its
        /// levels read from a use's LOD group (each level's surface renderers, with their outline and shadow settings as MushroomBuilder
        /// left them, so a part's <c>outline off</c> and <c>shadows near</c> carry over), and a placement per use. What is taken leaves
        /// the scene: the placed prefab is unpacked, and each use keeps its GameObject with its colliders only. A part with a
        /// <c>cull</c> setting keeps its distance (worked out back from its LOD group's last height). Nothing is taken when the level has
        /// no <see cref="PartsGroup"/> group.
        /// <para>
        /// A mirrored use (a negative x scale under its castle) gets a prototype of its own with mirrored copies of the part's meshes
        /// (Assets/Level/Meshes/&lt;level&gt;/Mirrored/), placed with the turn and scale only: Graphics.RenderMeshInstanced turns no
        /// triangles round for an instance whose matrix mirrors, so drawn from the part's own meshes it would show its back faces (seen
        /// on the keep's mirrored stair: dark and inside out).
        /// </para>
        /// </summary>
        private sealed class PartInstances
        {
            private readonly bool enabled;
            private readonly string mirroredFolder;
            private readonly Dictionary<string, int> index = new Dictionary<string, int>();
            private readonly Dictionary<Mesh, Mesh> mirroredMeshes = new Dictionary<Mesh, Mesh>();
            public readonly List<ScatterPrototype> Prototypes = new List<ScatterPrototype>();
            public readonly List<ScatterPlacement> Placements = new List<ScatterPlacement>();

            public PartInstances(bool enabled, string mirroredFolder)
            {
                this.enabled = enabled;
                this.mirroredFolder = mirroredFolder;
            }

            public void Take(string model, GameObject instance)
            {
                if (!enabled)
                    return;
                var uses = instance.transform.Cast<Transform>().Where(t => t.GetComponent<LODGroup>() != null).ToArray();
                if (uses.Length == 0)
                    return;
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var use in uses)
                {
                    var group = use.GetComponent<LODGroup>();
                    var lods = group.GetLODs();
                    var surfaces = lods.Select(lod => lod.renderers.Where(r => r != null && r.name != MushroomBuilder.OutlineObjectName).ToArray()).ToArray();
                    if (surfaces.Length == 0 || surfaces[0].Length == 0)
                        continue;
                    var world = use.localToWorldMatrix;
                    var mirrored = world.determinant < 0;
                    if (surfaces.Any(level => level.Any(r => r.GetComponent<MeshFilter>()?.sharedMesh == null)))
                    {
                        Debug.LogError($"SDF level: {model} part '{use.name}' has a renderer with a missing mesh (its prefab references meshes that are not in this checkout); re-mesh {model} and rebuild its prefab.");
                        continue;
                    }
                    var key = $"{model}/{surfaces[0][0].GetComponent<MeshFilter>().sharedMesh.name}{(mirrored ? " (mirrored)" : "")}";
                    if (!index.TryGetValue(key, out var prototype))
                    {
                        index[key] = prototype = Prototypes.Count;
                        var toUse = use.worldToLocalMatrix;
                        var flip = Matrix4x4.Scale(new Vector3(-1, 1, 1));
                        Prototypes.Add(new ScatterPrototype
                        {
                            name = key,
                            group = PartsGroup,
                            levels = surfaces.Select(renderers => new ScatterLevel
                            {
                                parts = renderers.Select(r =>
                                {
                                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                                    var transform = toUse * r.transform.localToWorldMatrix;
                                    return new ScatterPart
                                    {
                                        mesh = mirrored ? Mirrored(mesh) : mesh,
                                        transform = mirrored ? flip * transform * flip : transform,
                                        outline = r.transform.Find(MushroomBuilder.OutlineObjectName) != null,
                                        shadows = r.shadowCastingMode != ShadowCastingMode.Off,
                                    };
                                }).ToArray(),
                            }).ToArray(),
                            cull = CullDistance(group, lods),
                        });
                    }
                    Placements.Add(Placement(prototype, mirrored ? world * Matrix4x4.Scale(new Vector3(-1, 1, 1)) : world));

                    // The use keeps its colliders; its renderers and LOD group go
                    UnityEngine.Object.DestroyImmediate(group);
                    foreach (var child in use.Cast<Transform>().ToArray())
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            /// <summary>A use's world matrix (a turn and a uniform scale; a mirror is in the mesh) as a placement.</summary>
            private static ScatterPlacement Placement(int prototype, Matrix4x4 world)
            {
                Vector3 y = world.GetColumn(1), z = world.GetColumn(2);
                var scale = y.magnitude;
                return new ScatterPlacement
                {
                    prototype = prototype,
                    position = world.GetColumn(3),
                    rotation = Quaternion.LookRotation(z / scale, y / scale),
                    scale = scale,
                };
            }

            /// <summary>
            /// A copy of the mesh mirrored in x, with its triangles turned round so they face out again, saved next to the level's meshes
            /// (updated in place on a rebuild, so its GUID stays).
            /// </summary>
            private Mesh Mirrored(Mesh mesh)
            {
                if (mirroredMeshes.TryGetValue(mesh, out var copy))
                    return copy;
                var path = $"{mirroredFolder}/{mesh.name}.asset";
                copy = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (copy == null)
                {
                    MushroomBuilder.EnsureFolder(mirroredFolder);
                    copy = new Mesh();
                    AssetDatabase.CreateAsset(copy, path);
                }
                // Every attribute as it is, in the same layout, but x and the normals' x negated and each triangle turned round
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                for (var v = 0; v < vertices.Length; v++)
                {
                    vertices[v].x = -vertices[v].x;
                    normals[v].x = -normals[v].x;
                }
                var triangles = mesh.triangles;
                for (var t = 0; t < triangles.Length; t += 3)
                    (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
                copy.Clear();
                copy.name = mesh.name;
                copy.indexFormat = mesh.indexFormat;
                copy.SetVertexBufferParams(mesh.vertexCount, mesh.GetVertexAttributes());
                copy.SetVertices(vertices);
                copy.SetNormals(normals);
                if (mesh.HasVertexAttribute(VertexAttribute.Color))
                    copy.SetColors(mesh.colors);
                for (var channel = 0; channel < 8; channel++)
                {
                    var attribute = VertexAttribute.TexCoord0 + channel;
                    if (!mesh.HasVertexAttribute(attribute))
                        continue;
                    switch (mesh.GetVertexAttributeDimension(attribute))
                    {
                        case 2:
                            var uv2 = new List<Vector2>();
                            mesh.GetUVs(channel, uv2);
                            copy.SetUVs(channel, uv2);
                            break;
                        case 3:
                            var uv3 = new List<Vector3>();
                            mesh.GetUVs(channel, uv3);
                            copy.SetUVs(channel, uv3);
                            break;
                        default:
                            var uv4 = new List<Vector4>();
                            mesh.GetUVs(channel, uv4);
                            copy.SetUVs(channel, uv4);
                            break;
                    }
                }
                copy.subMeshCount = 1;
                copy.SetTriangles(triangles, 0, calculateBounds: true);
                copy.RecalculateBounds();
                EditorUtility.SetDirty(copy);
                return mirroredMeshes[mesh] = copy;
            }

            /// <summary>Deletes the mirrored copies this build made no use of.</summary>
            public void DeleteUnused()
            {
                if (!AssetDatabase.IsValidFolder(mirroredFolder))
                    return;
                var kept = new HashSet<string>(mirroredMeshes.Values.Select(AssetDatabase.GetAssetPath));
                foreach (var path in AssetDatabase.FindAssets("t:Mesh", new[] { mirroredFolder }).Select(AssetDatabase.GUIDToAssetPath))
                    if (!kept.Contains(path))
                        AssetDatabase.DeleteAsset(path);
            }

            /// <summary>
            /// The distance at which a LOD group culls, in metres, for the first-person camera's 60° field of view: its world size over
            /// its last level's relative height. 0 (no cull of its own) beyond the furthest a level can be seen from.
            /// </summary>
            private static float CullDistance(LODGroup group, LOD[] lods)
            {
                var height = lods[lods.Length - 1].screenRelativeTransitionHeight;
                var scale = group.transform.lossyScale;
                var size = group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                var distance = size / (2 * height * Mathf.Tan(30 * Mathf.Deg2Rad));
                return distance > 400 ? 0 : distance;
            }
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
            // The mirrored copies of placed models' parts have their own clean-up (PartInstances)
            foreach (var path in AssetDatabase.FindAssets("t:Mesh", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).Distinct())
                if (!kept.Contains(path) && !path.StartsWith($"{folder}/{MirroredFolder}/", StringComparison.Ordinal))
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
        private static GameObject AddPlayer(Scene scene, JObject data, bool noSky)
        {
            var spawn = data["spawn"];
            var player = new GameObject(PlayerName);
            SceneManager.MoveGameObjectToScene(player, scene);
            player.transform.SetPositionAndRotation(
                new Vector3(-spawn[0].Value<float>(), spawn[1].Value<float>() + 0.05f, spawn[2].Value<float>()),
                Quaternion.Euler(0, 90, 0));
            FirstPersonController.Configure(player.AddComponent<CharacterController>());
            player.AddComponent<FirstPersonController>();

            var camera = MushroomScene.CreateCamera(scene, "Main Camera", 60, player.transform);
            camera.transform.localPosition = new Vector3(0, EyeHeight, 0);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400;
            if (noSky)
            {
                // Surrounded by walls: no skybox to clear to, a flat colour the room never shows; far enough for the whole cube
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = UnityEngine.Color.black;
                camera.farClipPlane = RoomFarPlaneOf(data);
                // A backdrop (ticket 28.2) clears to its walls' colour, which shows beyond it, and sets its own far plane
                if (LevelBackdrop.IsBackdrop(data))
                {
                    camera.backgroundColor = LevelBackdrop.Clear(data);
                    camera.farClipPlane = LevelBackdrop.FarPlane(data);
                }
            }
            return player;
        }

        /// <summary>
        /// Battle, at the scene's root (level.json "battle"): a <see cref="BattleArena"/> over Battle/West and Battle/East, each a
        /// <see cref="SdfBattle.BattleSide"/> whose children are its Spawn (an empty marker, the side's gizmo draws it), its flag stand
        /// (Assets/Pickups/Flags/flag-stand-&lt;side&gt;.prefab), its weapon pods (whichever the level file gives
        /// it: the minigun and the pistol on most levels) and its health pods (Assets/Pickups/Pods/&lt;pod&gt;-pod.prefab), and Battle/Neutral with the neutral pods. On a level with machines (ticket 24)
        /// each side also gets its turrets, Turret 1-4 (Assets/Machines/Prefabs/turret-&lt;team&gt;.prefab, red west, blue east; static
        /// scene objects, where they respawn), and its Machine Spawn, an empty marker in the middle of the zone where its helicopter and
        /// tanks start and respawn (they are made at runtime, ticket 24.3). Each stands where level.json puts it,
        /// x mirrored and yaw turned round, as the placements. A missing prefab logs a warning and leaves its reference empty; a level
        /// without sides gets no Battle and returns null.
        /// </summary>
        private static BattleArena AddBattle(Scene scene, JObject data, GameObject player)
        {
            if (!(data["battle"] is JObject battle))
                return null;
            var root = new GameObject(BattleArena.ObjectName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var arena = root.AddComponent<BattleArena>();
            // Ticket 27.2: the set of machine models (turret-toys-red, ...; mushroom when level.json names none)
            var machineSet = MachineSets.Of(battle.Value<string>("machineSet"));
            if (!MachineSets.IsDefault(machineSet) && !SdfMachines.Editor.MachineBuilder.Sets().Contains(machineSet))
                Debug.Log($"SDF level: the machine set {machineSet} has no prefabs yet (mesh content/models/machines, models like {MachineSets.PieceName("turret", machineSet, "red", "foot")}); its machines are left out until it has");

            void Place(Transform item, JToken entry)
            {
                var position = entry["position"].ToObject<float[]>();
                item.SetLocalPositionAndRotation(new Vector3(-position[0], position[1], position[2]), Quaternion.Euler(0, -entry.Value<float>("yaw"), 0));
            }
            T Instance<T>(Transform parent, string prefabPath, string objectName, JToken entry) where T : Component
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"SDF level: there is no prefab {prefabPath} for the battle's {objectName}; let the pickups and machines build (Tools > SDF Pickups, Tools > SDF Machines)");
                    return null;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = objectName;
                instance.transform.SetParent(parent, false);
                Place(instance.transform, entry);
                return instance.GetComponent<T>();
            }
            WeaponPickup Pod(Transform parent, JToken entry)
            {
                var pod = entry.Value<string>("pod");
                return Instance<WeaponPickup>(parent, WeaponPodBuilder.PodPrefabOf(pod), $"{Capitalized(pod)} Pod", entry);
            }

            foreach (var entry in (JArray)battle["sides"])
            {
                var id = entry.Value<string>("side") == FlagStand.SideNameOf(BattleSideId.East) ? BattleSideId.East : BattleSideId.West;
                var sideObject = new GameObject(Capitalized(FlagStand.SideNameOf(id)));
                sideObject.transform.SetParent(root.transform, false);
                var side = sideObject.AddComponent<SdfBattle.BattleSide>();
                side.side = id;
                var spawn = new GameObject(SpawnName).transform;
                spawn.SetParent(sideObject.transform, false);
                Place(spawn, entry["spawn"]);
                side.spawn = spawn;
                side.spawnRadius = entry["spawn"].Value<float?>("radius") ?? side.spawnRadius;
                side.flagStand = Instance<FlagStand>(sideObject.transform, FlagStandBuilder.StandPrefabOf(id), "Flag Stand", entry["flag"]);
                var moreHealthPods = new List<HealthPickup>();
                var weaponPods = new List<WeaponPickup>();
                foreach (var podEntry in (JArray)entry["pods"])
                    switch (podEntry.Value<string>("pod"))
                    {
                        // The first health pod, then any more (Health Pod 2, ...)
                        case "health" when side.healthPod == null:
                            side.healthPod = Instance<HealthPickup>(sideObject.transform, HealthPodBuilder.PodPrefab, "Health Pod", podEntry);
                            break;
                        case "health":
                            moreHealthPods.Add(Instance<HealthPickup>(sideObject.transform, HealthPodBuilder.PodPrefab, $"Health Pod {moreHealthPods.Count + 2}", podEntry));
                            break;
                        // Every other pod is one of the side's weapons, whichever the level file gives it (Minigun Pod, Electro-gun Pod)
                        default:
                            weaponPods.Add(Pod(sideObject.transform, podEntry));
                            break;
                    }
                side.weaponPods = weaponPods.Where(p => p != null).ToArray();
                side.moreHealthPods = moreHealthPods.Where(p => p != null).ToArray();
                // Ticket 24: the turrets, in the side's team colour, and the machine zone
                if (entry["turrets"] is JArray turrets)
                {
                    var team = id == BattleSideId.West ? "red" : "blue";
                    side.turrets = turrets.Select((turret, index) => Instance<SdfMachines.MachineRig>(sideObject.transform,
                            SdfMachines.Editor.MachineBuilder.MachinePrefabOf("turret", team, machineSet), $"Turret {index + 1}", turret))
                        .Where(t => t != null).ToArray();
                }
                if (entry["machines"] is JObject machines)
                {
                    var machineSpawn = new GameObject(MachineSpawnName).transform;
                    machineSpawn.SetParent(sideObject.transform, false);
                    Place(machineSpawn, machines);
                    side.machineSpawn = machineSpawn;
                    side.machineSpawnRadius = machines.Value<float?>("radius") ?? side.machineSpawnRadius;
                }
                if (id == BattleSideId.West)
                    arena.west = side;
                else
                    arena.east = side;
            }

            var neutral = new GameObject(NeutralName);
            neutral.transform.SetParent(root.transform, false);
            arena.neutralPods = ((JArray)battle["neutral"]).Select(entry => Pod(neutral.transform, entry)).Where(p => p != null).ToArray();
            AddMatch(root, player);
            // Ticket 24.3: the helicopters' and tanks' prefabs, which the match makes in the machine zones at its start
            if (SdfBattle.Machines.HasMachines(arena))
                AddMachines(root, machineSet, battle.Value<float?>("flightCeiling") ?? 0);
            return arena;
        }

        /// <summary>
        /// The match (ticket 20) on Battle: the scene's player is you, the scouts are the bots' bodies (amanita red, chanterelle blue),
        /// and an instance of the GUI prefab at the scene's root its HUD, there in the editor as it is in play (the HUD showing, its
        /// test panel off). A missing prefab logs a warning; the match then plays without those bodies or without a HUD.
        /// </summary>
        private static void AddMatch(GameObject battle, GameObject player)
        {
            var match = battle.AddComponent<Match>();
            match.you = player.GetComponent<FirstPersonController>();
            GameObject Load(string path, string what)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    Debug.LogWarning($"SDF level: there is no {path} for the match's {what}; build it (Tools > SDF Mushrooms, Tools > SDF GUI) and rebuild the level");
                return prefab;
            }
            match.redScout = Load($"{MushroomBuilder.PrefabFolder}/{RedScout}.prefab", "red bodies");
            match.blueScout = Load($"{MushroomBuilder.PrefabFolder}/{BlueScout}.prefab", "blue bodies");
            var gui = Load(SdfGui.Editor.GuiScene.GuiPrefabPath, "HUD");
            if (gui != null)
            {
                match.gui = (GameObject)PrefabUtility.InstantiatePrefab(gui, battle.scene);
                if (match.gui.TryGetComponent<SdfGui.MatchView>(out var view))
                {
                    MatchHud.ShowRoundStart(view, match.rules);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(view);
                }
                if (match.gui.TryGetComponent<SdfGui.GuiFlow>(out var flow))
                {
                    flow.screen = SdfGui.GuiScreen.Hud;
                    flow.testPanel = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(flow);
                }
            }
        }

        /// <summary>
        /// The machines (ticket 24.3) on Battle: the red and blue tank and helicopter prefabs (Assets/Machines/Prefabs), which the match
        /// makes in each side's machine zone at its start; the turrets are the sides' own scene objects. A missing prefab logs a warning.
        /// </summary>
        private static void AddMachines(GameObject battle, string machineSet, float flightCeiling)
        {
            var machines = battle.AddComponent<SdfBattle.Machines>();
            machines.machineSet = machineSet;
            machines.flightCeiling = flightCeiling;
            GameObject Load(SdfBattle.MachineKind kind, SdfBattle.Team team)
            {
                var path = SdfBattle.Machines.PrefabPath(kind, team, machineSet);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    Debug.LogWarning($"SDF level: there is no {path} for the match's machines; build it (Tools > SDF Machines > Rebuild Machines) and rebuild the level");
                return prefab;
            }
            machines.redTank = Load(SdfBattle.MachineKind.Tank, SdfBattle.Team.Red);
            machines.blueTank = Load(SdfBattle.MachineKind.Tank, SdfBattle.Team.Blue);
            machines.redHelicopter = Load(SdfBattle.MachineKind.Helicopter, SdfBattle.Team.Red);
            machines.blueHelicopter = Load(SdfBattle.MachineKind.Helicopter, SdfBattle.Team.Blue);
        }

        /// <summary>The scouts the bots are drawn as: red (west) the amanita scout, blue (east) the chanterelle scout.</summary>
        public const string RedScout = "amanita-scout", BlueScout = "chanterelle-scout";

        /// <summary>minigun is Minigun, electro-gun Electro-gun.</summary>
        private static string Capitalized(string name) => char.ToUpperInvariant(name[0]) + name.Substring(1);

        public const string StaticShadowName = "Sun Shadow";

        /// <summary>
        /// The level's fixed sun shadow (<see cref="StaticSunShadow"/>), at the scene's root, and the light's own (cascaded) shadows off:
        /// one map over the whole level, rendered once, so no shadow changes with where the camera is.
        /// </summary>
        private static void AddStaticShadow(Scene scene, Light sun)
        {
            sun.shadows = LightShadows.None;
            var shadow = new GameObject(StaticShadowName);
            SceneManager.MoveGameObjectToScene(shadow, scene);
            var component = shadow.AddComponent<StaticSunShadow>();
            component.sun = sun;
            component.scatter = scene.GetRootGameObjects().Select(o => o.GetComponent<InstancedScatterRenderer>()).FirstOrDefault(r => r != null);
            component.casterMaterial = AssetDatabase.LoadAssetAtPath<Material>(MushroomBuilder.ToonMaterialPath);
            component.lowQualityLevel = RenderPipelineSetup.Web.Name;
        }

        /// <summary>
        /// SceneInfo, at the scene's root: the <see cref="IMMScene"/> filled from level.json, in Unity's coordinates. The play area and
        /// the extent are mirrored in x, so a corner's x is the negated other one's.
        /// </summary>
        private static IMMScene AddSceneInfo(Scene scene, string name, bool indoor, JObject data, Transform levelRoot, GameObject player,
            List<IMMScene.Placement> placements, BattleArena battle)
        {
            var info = new GameObject(IMMScene.ObjectName);
            SceneManager.MoveGameObjectToScene(info, scene);
            var sceneInfo = info.AddComponent<IMMScene>();
            (Vector2 min, Vector2 max) Mirrored(JToken area) =>
                (new Vector2(-area["max"][0].Value<float>(), area["min"][1].Value<float>()),
                    new Vector2(-area["min"][0].Value<float>(), area["max"][1].Value<float>()));
            sceneInfo.levelName = name;
            sceneInfo.indoor = indoor;
            (sceneInfo.playAreaMin, sceneInfo.playAreaMax) = Mirrored(data["play"]);
            (sceneInfo.extentMin, sceneInfo.extentMax) = data["extent"] != null ? Mirrored(data["extent"]) : (sceneInfo.playAreaMin, sceneInfo.playAreaMax);
            var spawn = data["spawn"];
            sceneInfo.spawnPoint = new Vector3(-spawn[0].Value<float>(), spawn[1].Value<float>(), spawn[2].Value<float>());
            sceneInfo.spawnRotation = player.transform.rotation;
            sceneInfo.eyeHeight = EyeHeight;
            sceneInfo.player = player.transform;
            sceneInfo.playerCamera = player.GetComponentInChildren<Camera>();
            sceneInfo.levelRoot = levelRoot;
            sceneInfo.placements = placements.ToArray();
            sceneInfo.battleSides = IMMScene.NewBattleSides();
            sceneInfo.battle = battle;
            // Team 0 is the west side, team 1 the east, each with its spawn, flag stand and pods
            if (battle != null)
                for (var team = 0; team < IMMScene.SideCount; team++)
                {
                    var side = battle.Side(team == 0 ? BattleSideId.West : BattleSideId.East);
                    if (side == null)
                        continue;
                    sceneInfo.battleSides[team].name = side.name;
                    sceneInfo.battleSides[team].elements = side.Objects.ToList();
                }
            return sceneInfo;
        }

        /// <summary>
        /// Game, at the scene's root: an instance of <see cref="IMMGame.PrefabPath"/> whose one override is its reference to the scene's
        /// SceneInfo. The prefab is made, holding only an <see cref="IMMGame"/>, when it is missing; else it is left as it is.
        /// </summary>
        private static void AddGame(Scene scene, IMMScene sceneInfo)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(IMMGame.PrefabPath);
            if (prefab == null)
            {
                MushroomBuilder.EnsureFolder(Path.GetDirectoryName(IMMGame.PrefabPath));
                var template = new GameObject(IMMGame.ObjectName, typeof(IMMGame));
                prefab = PrefabUtility.SaveAsPrefabAsset(template, IMMGame.PrefabPath);
                UnityEngine.Object.DestroyImmediate(template);
            }
            var game = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            game.name = IMMGame.ObjectName;
            var component = game.GetComponent<IMMGame>();
            component.scene = sceneInfo;
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
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
