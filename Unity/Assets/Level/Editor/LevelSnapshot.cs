using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using SdfMushrooms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SdfLevel.Editor
{
    /// <summary>
    /// Renders a level scene to PNGs for checking a change, through its player's camera: at eye height from the spawn point in four
    /// directions, from the playable area's corner towards the hills, from above, next to a tree and a mushroom (placed for the shot
    /// only), the busiest spot of the placed models from eye height and from above, and the first view again on the integrated GPU / web
    /// quality level. It logs what the ground and the placed models cost in each eye-height view: the triangles of the levels of detail
    /// their LOD groups pick, inside the view; and what the instanced foliage costs: the instances, draws and triangles
    /// <see cref="InstancedScatterRenderer"/> drew for it (outlines and shadow casters apart).
    /// <para>
    /// With scattered foliage it also shoots the grass close up and across the field, where the rings hand over, on both quality
    /// levels, and the same close view at two moments of the wind (wind_0, wind_1), logging how many pixels differ between them.
    /// </para>
    /// <para>
    /// With placed castles it also shoots the first castle (the one at +x, the pipeline's -x) from the far end of the field, the meadow,
    /// at its gate, from its courtyard, on its keep's stair and from its wall-walk, and close up where its parts join; it logs what the
    /// castles cost in each of those views (draws and triangles of their GameObjects' chosen levels, and of the instanced renderer's
    /// castle group). It shoots the ground regions' edges close up and from above, and checks the colliders by walking a character
    /// controller through the gate into the courtyard, up the keep's stair and along the wall-walk past the towers' rings, into a wall
    /// and a tower from outside, and straight at each rock, logging where each walk ends and how high it got.
    /// </para>
    /// <para>
    /// With a sky (<see cref="LevelSky"/>) it also looks up at the moon from the spawn point, out from a castle tower over the crest to
    /// the edge and the abyss, out and down from the island's edge, and at the whole island hanging over the abyss from outside; the moon
    /// and an eye view again on the integrated GPU / web quality level with bloom off. Every view logs what the sky draws in it.
    /// </para>
    /// <para>
    /// With trees (a trees draw group) it also looks up the hills and along them from their foot and into a corner, on both quality
    /// levels, out over the crest from high above a castle, down onto the island's edge, and at the underside from below the island,
    /// and walks a character controller into the tree that stands furthest into the playable area (<see cref="ShootTrees"/>).
    /// </para>
    /// <para>
    /// Without a window: <c>Unity -batchmode -quit -projectPath Unity -executeMethod SdfLevel.Editor.LevelSnapshot.Capture
    /// -snapshotDir &lt;folder&gt; [-level meadow]</c> (without -nographics, so the GPU renders). The level's scene is rebuilt first if it
    /// is stale. In the editor: Tools > SDF Level > Save Snapshots, for the open level scene. In a running editor, which batch mode cannot
    /// open: write the folder's path into Unity/Temp/sdf-level-snapshot.txt (a second line may name the level, meadow by default); the
    /// editor renders within a second, opening the level's scene next to the open ones for the shots if it is not open, and deletes
    /// the file.
    /// </para>
    /// </summary>
    public static class LevelSnapshot
    {
        public const string RequestPath = "Temp/sdf-level-snapshot.txt";

        /// <summary>Takes a request written to <see cref="RequestPath"/>, see above.</summary>
        public static void CaptureRequested()
        {
            var lines = File.ReadAllLines(RequestPath).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            File.Delete(RequestPath);
            if (lines.Length == 0)
                return;
            var scenePath = LevelBuilder.ScenePathOf($"{LevelBuilder.GeneratedFolder}/{(lines.Length > 1 ? lines[1] : "meadow")}");
            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            // The sky and ambient light are the active scene's; the other open scenes are kept from drawing meanwhile
            var active = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            var others = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => s != scene && s.isLoaded)
                .SelectMany(s => s.GetRootGameObjects()).SelectMany(g => g.GetComponentsInChildren<Renderer>()).Where(r => !r.forceRenderingOff).ToArray();
            foreach (var renderer in others)
                renderer.forceRenderingOff = true;
            try
            {
                Shoot(scene, lines[0]);
            }
            finally
            {
                foreach (var renderer in others)
                    renderer.forceRenderingOff = false;
                if (active.IsValid() && active != scene)
                    SceneManager.SetActiveScene(active);
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>The models placed next to the player for the shot that compares the ground with them.</summary>
        private static readonly string[] CompareModels = { "tree0", "amanita-scout", "rock1" };

        public static void Capture()
        {
            RenderPipelineSetup.Ensure();
            MushroomBuilder.BuildStale();
            LevelBuilder.BuildStale();
            var args = Environment.GetCommandLineArgs();
            string Argument(string name, string fallback)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
            }
            var level = Argument("-level", "meadow");
            var directory = Argument("-snapshotDir", Path.Combine(Path.GetTempPath(), "level-snapshots"));
            var scenePath = LevelBuilder.ScenePathOf($"{LevelBuilder.GeneratedFolder}/{level}");
            Shoot(EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single), directory);
        }

        [MenuItem("Tools/SDF Level/Save Snapshots")]
        private static void SaveSnapshotsFromMenu()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.GetRootGameObjects().All(g => g.name != LevelBuilder.PlayerName))
            {
                Debug.LogWarning("SDF level: open a level scene to take snapshots");
                return;
            }
            Shoot(scene, Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Temp", "Snapshots", scene.name));
        }

        private static void Shoot(Scene scene, string directory)
        {
            Directory.CreateDirectory(directory);
            var player = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.PlayerName);
            var camera = player.GetComponentInChildren<Camera>();
            var eye = camera.transform.position;
            Physics.SyncTransforms();

            void Shot(string name, Vector3 position, Quaternion rotation, int width = 1920, int height = 1080)
            {
                MushroomSnapshot.RenderView(camera, position, rotation, Path.Combine(directory, name + ".png"), width, height);
            }
            var scatter = scene.GetRootGameObjects().Select(g => g.GetComponent<InstancedScatterRenderer>()).FirstOrDefault(r => r != null);
            void EyeShot(string name, Vector3 position, Quaternion rotation)
            {
                Shot(name, position, rotation);
                Debug.Log($"SDF level: {name}: the ground draws {TrianglesInView(scene, "Ground", camera, position, rotation):N0} triangles, " +
                          $"the placed models {TrianglesInView(scene, LevelBuilder.PlacedName, camera, position, rotation):N0}{FoliageCost(scatter)}; " +
                          $"{SkyInView(scene, camera, position, rotation)}");
            }

            void CastleShot(string name, Vector3 position, Quaternion rotation)
            {
                Shot(name, position, rotation);
                Debug.Log($"SDF level: {name} ({QualitySettings.names[QualitySettings.GetQualityLevel()]}): {CastleCost(scene, camera, position, rotation, scatter)}");
            }

            var level = QualitySettings.GetQualityLevel();
            try
            {
                QualitySettings.SetQualityLevel(RenderPipelineSetup.DesktopLevel, true);
                if (scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find(LevelRoom.RoomName) != null)
                {
                    // A closed room has no outside: its own views (and none of the hills', edge's or sky's)
                    ShootRoom(scene, eye, Shot, EyeShot);
                    QualitySettings.SetQualityLevel(RenderPipelineSetup.WebLevel, true);
                    EyeShot("room_eye_web", eye, Quaternion.Euler(0, 90, 0));
                    Debug.Log($"SDF level: snapshots written to {directory}");
                    return;
                }
                foreach (var (name, yaw) in new[] { ("eye_+x", 90f), ("eye_-x", -90f), ("eye_+z", 0f), ("eye_-z", 180f) })
                    EyeShot(name, eye, Quaternion.Euler(0, yaw, 0));

                // From the playable area's corner, towards the corner of the hills
                var play = PlayHalfSize(scene);
                var corner = OnGround(new Vector3(play.x - 4, 0, play.y - 4)) + Vector3.up * LevelBuilder.EyeHeight;
                EyeShot("eye_corner", corner, Quaternion.LookRotation(new Vector3(1, 0, 1)));
                // On the chessboard the hills hold no pieces: look in across the board from the corner too
                if (scene.name == "Chessboard")
                    EyeShot("eye_corner_in", corner, Quaternion.LookRotation(new Vector3(-1, 0, -1)));
                // Up on a crest is beyond the walls; from there the island's edge and the chunk borders across the hills show
                Shot("above", new Vector3(0, 90, -160), Quaternion.Euler(28, 0, 0));
                Shot("top", new Vector3(0, 260, 0), Quaternion.Euler(90, 0, 0), 1600, 1200);

                CompareWithModels(scene, eye, directory, Shot);
                ShootCrowd(scene, Shot, EyeShot);
                ShootCastleAndRegions(scene, Shot, EyeShot, CastleShot);
                var moon = ShootSky(scene, eye, EyeShot);
                if (scatter != null)
                    ShootFoliage(scene, directory, "", Shot, EyeShot);
                if (HasGroup(scatter, TreesGroup))
                    ShootTrees(scene, "", Shot, EyeShot);
                if (scene.name == "Fungal")
                    ShootFungal(scene, Shot, EyeShot);
                if (scene.name == "Arena")
                    ShootBoss(scene, "", Shot, EyeShot);

                QualitySettings.SetQualityLevel(RenderPipelineSetup.WebLevel, true);
                EyeShot("eye_+x_web", eye, Quaternion.Euler(0, 90, 0));
                if (Placed(scene, "castle0").FirstOrDefault() is { } castleWeb)
                {
                    var up = Vector3.up * LevelBuilder.EyeHeight;
                    var far = OnGround(ModelPoint(castleWeb, 0, 0, 110)) + up;
                    CastleShot("castle_far_web", far, Quaternion.LookRotation(castleWeb.position + Vector3.up * 6 - far));
                    var courtyard = OnGround(ModelPoint(castleWeb, -6, 0, 4)) + up;
                    CastleShot("castle_courtyard_web", courtyard, Quaternion.LookRotation(ModelPoint(castleWeb, 1, 5, -5) - courtyard));
                }
                if (scatter != null)
                    ShootFoliage(scene, directory, "_web", Shot, EyeShot);
                if (scene.name == "Arena")
                    ShootBoss(scene, "_web", Shot, EyeShot);
                if (HasGroup(scatter, TreesGroup))
                    ShootTrees(scene, "_web", Shot, EyeShot);
                if (moon is { } toMoon)
                {
                    // Glow must read without bloom, which the low preset may drop
                    var bloom = scene.GetRootGameObjects().Select(g => g.GetComponent<Volume>()).FirstOrDefault(v => v != null)?.sharedProfile is { } profile
                                && profile.TryGet<Bloom>(out var found) ? found : null;
                    var wasActive = bloom != null && bloom.active;
                    try
                    {
                        if (bloom != null)
                            bloom.active = false;
                        EyeShot("eye_moon_web_nobloom", eye, Quaternion.LookRotation(toMoon));
                        EyeShot("eye_-x_web_nobloom", eye, Quaternion.Euler(0, -90, 0));
                    }
                    finally
                    {
                        if (bloom != null)
                            bloom.active = wasActive;
                    }
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(level, true);
            }
            Debug.Log($"SDF level: snapshots written to {directory}");
        }

        /// <summary>
        /// The views of a closed room (a chessboard cube or any room of walls and a lid): at eye height in the middle looking along each wall; at a corner looking along
        /// a wall, and from the opposite corner diagonally across and up; up at the ceiling, straight and slanted; close at the foot of a
        /// wall and at the vertical edge of two walls, where the checker must run on across the edge; from high up in the room down;
        /// and the busiest crowd of pieces. Unity's coordinates; the room is centred on x and z and stands on y = 0.
        /// </summary>
        private static void ShootRoom(Scene scene, Vector3 eye, Action<string, Vector3, Quaternion, int, int> shot, Action<string, Vector3, Quaternion> eyeShot)
        {
            foreach (var (name, yaw) in new[] { ("room_eye_+x", 90f), ("room_eye_-x", -90f), ("room_eye_+z", 0f), ("room_eye_-z", 180f) })
                eyeShot(name, eye, Quaternion.Euler(0, yaw, 0));
            // The room's own size (not necessarily a cube), from its ceiling's quad: half its width and depth, and its height
            var ceiling = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find(LevelRoom.RoomName + "/ceiling").GetComponent<Renderer>().bounds;
            var half = new Vector2(ceiling.extents.x, ceiling.extents.z);
            var height = ceiling.center.y;
            var up = Vector3.up * LevelBuilder.EyeHeight;
            eyeShot("room_corner_along", new Vector3(half.x - 8, 0, -(half.y - 8)) + up, Quaternion.LookRotation(new Vector3(0, 0.02f, 1)));
            eyeShot("room_corner_across", new Vector3(-(half.x - 8), 0, -(half.y - 8)) + up, Quaternion.LookRotation(new Vector3(1, 0.3f, 1)));
            eyeShot("room_ceiling", eye, Quaternion.Euler(-85, 0, 0));
            eyeShot("room_ceiling_slant", eye, Quaternion.Euler(-45, 30, 0));
            eyeShot("room_wall_foot", new Vector3(0, 0, half.y - 20) + up, Quaternion.Euler(20, 0, 0));
            eyeShot("room_wall_edge", new Vector3(Mathf.Max(half.x - 40, half.x * 0.4f), 0, Mathf.Max(half.y - 40, half.y * 0.4f)) + up, Quaternion.LookRotation(new Vector3(1, 0.1f, 1)));
            eyeShot("room_ceiling_edge", new Vector3(half.x - 20, 0, 0) + up, Quaternion.LookRotation(new Vector3(1, 1.4f, 0)));
            // A wall's upper part from the middle: a frieze band and the strip of light along the top of the +z wall, if the room has them
            eyeShot("room_frieze", eye, Quaternion.Euler(-12, 0, 0));
            eyeShot("room_glow", eye, Quaternion.Euler(-40, 0, 0));
            shot("room_above", new Vector3(-(half.x - 20), height - 10, -(half.y - 20)), Quaternion.LookRotation(new Vector3(1, -1.2f, 1)), 1920, 1080);
            ShootCrowd(scene, shot, eyeShot);
        }

        /// <summary>
        /// The sky's views: up at the moon from the spawn point (with one), from the top of the first castle's large +x tower out over
        /// the crest to the edge, from the island's edge out and down, and the island from outside. Returns the direction towards the
        /// moon, or null without one; nothing is shot for a level without a sky.
        /// </summary>
        private static Vector3? ShootSky(Scene scene, Vector3 eye, Action<string, Vector3, Quaternion> eyeShot)
        {
            var sky = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find(LevelSky.SkyName);
            if (sky == null)
                return null;
            var moon = sky.Find("Moon");
            Vector3? toMoon = moon != null ? moon.localPosition.normalized : (Vector3?)null;
            if (toMoon is { } direction)
                eyeShot("eye_moon", eye, Quaternion.LookRotation(direction));

            if (Placed(scene, "castle0").FirstOrDefault() is { } castle)
            {
                // Standing on top of the cap of the large tower at the oval's +x end (joint 4, its cap's top about 15.8 m up), looking
                // out of the island's end past the keep's flanking tower, a little down
                var above = ModelPoint(castle, 12.4f, 40, 0);
                var tower = (Physics.Raycast(above, Vector3.down, out var cap, 40) ? cap.point : ModelPoint(castle, 12.4f, 15.8f, 0))
                            + Vector3.up * LevelBuilder.EyeHeight;
                eyeShot("sky_tower", tower, Quaternion.LookRotation(ModelDirection(castle, 0, -0.2f, -1)));
            }
            // At the island's edge behind the +x hills (pipeline -x), looking out and down into the abyss: the last ground out along +x
            var extent = PlayHalfSize(scene).x;
            while (extent < 1000 && HasGround(new Vector3(extent + 0.5f, 0, 0)))
                extent += 0.5f;
            var edge = OnGround(new Vector3(extent - 2, 0, 0)) + Vector3.up * LevelBuilder.EyeHeight;
            eyeShot("sky_edge", edge, Quaternion.Euler(25, 90, 0));
            eyeShot("sky_edge_level", edge, Quaternion.Euler(0, 90, 0));
            // The island from outside, hanging over the abyss
            var outside = new Vector3(-40, 50, -300);
            eyeShot("sky_outside", outside, Quaternion.LookRotation(new Vector3(0, -15, 0) - outside));
            return toMoon;
        }

        /// <summary>
        /// What the sky draws in a view: its renderers inside the view (the far layer and the motes moved to the camera, as
        /// <see cref="SkySettings"/> does before each camera culls), their triangles, and the skybox.
        /// </summary>
        private static string SkyInView(Scene scene, Camera sceneCamera, Vector3 position, Quaternion rotation)
        {
            var sky = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find(LevelSky.SkyName);
            var settings = sky != null ? sky.GetComponent<SkySettings>() : null;
            if (settings == null)
                return "no sky";
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Count Camera", HideFlags.HideAndDontSave, typeof(Camera));
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.CopyFrom(sceneCamera);
                camera.aspect = 16f / 9f;
                camera.transform.SetPositionAndRotation(position, rotation);
                var planes = GeometryUtility.CalculateFrustumPlanes(camera);
                var (draws, triangles) = (1, 0);
                foreach (var renderer in sky.GetComponentsInChildren<Renderer>())
                {
                    var bounds = renderer.bounds;
                    if (settings.farRenderers.Contains(renderer))
                    {
                        renderer.ResetBounds();
                        bounds = renderer.bounds;
                        bounds.center += position;
                    }
                    if (!GeometryUtility.TestPlanesAABB(planes, bounds))
                        continue;
                    draws++;
                    triangles += (int)renderer.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3;
                }
                return $"the sky draws {draws} times (skybox included), {triangles:N0} triangles";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        /// <summary>What the foliage cost in the view just rendered, for the log; nothing without an instanced renderer.</summary>
        private static string FoliageCost(InstancedScatterRenderer scatter)
        {
            if (scatter == null)
                return "";
            var frame = scatter.LastFrame;
            var rings = frame.InstancesPerRing == null ? "" : string.Join(" / ", frame.InstancesPerRing.Take(3));
            // Each group's own share, outlines included: the trees' budget is the trees and clusters groups together
            var groups = frame.DrawsPerGroup == null ? "" : "; per group " + string.Join(", ", scatter.groups.Select((g, i) => i < frame.DrawsPerGroup.Length
                ? $"{g.name} {frame.DrawsPerGroup[i]} draws {frame.TrianglesPerGroup[i]:N0} triangles"
                : g.name));
            return $"; the foliage {frame.Instances:N0} instances (per ring {rings}), {frame.Draws + frame.OutlineDraws} draws ({frame.OutlineDraws} of them outlines), " +
                   $"{frame.Triangles + frame.OutlineTriangles:N0} triangles ({frame.OutlineTriangles:N0} of them outlines), {frame.ShadowTriangles:N0} into the shadow map{groups}";
        }

        /// <summary>The draw group of the level file's front-row trees (ticket 04), and that of its crest clusters.</summary>
        private const string TreesGroup = "trees";

        private static bool HasGroup(InstancedScatterRenderer scatter, string group) => scatter != null && scatter.groups.Any(g => g.name == group);

        /// <summary>
        /// The trees on the border hills and the island's edge: from eye height at the foot of the +x hills looking up the slope and
        /// along it, and into the corner; then (high preset only) from above the first castle's keep out over the crest to the edge and
        /// the underside, down onto the edge's lip from just inside it, and the underside from below the island. Each eye view logs what
        /// every draw group costs, so the trees' and clusters' draws and triangles can be read off. On the high preset it also walks a
        /// character controller straight at the tree that stands furthest into the playable area, logging how close it got to the trunk.
        /// </summary>
        private static void ShootTrees(Scene scene, string suffix, Action<string, Vector3, Quaternion, int, int> shot, Action<string, Vector3, Quaternion> eyeShot)
        {
            var up = Vector3.up * LevelBuilder.EyeHeight;
            var play = PlayHalfSize(scene);
            var foot = OnGround(new Vector3(play.x - 3, 0, 12)) + up;
            eyeShot("trees_slope" + suffix, foot, Quaternion.Euler(-8, 90, 0));
            eyeShot("trees_along" + suffix, foot, Quaternion.Euler(-3, 25, 0));
            var corner = OnGround(new Vector3(-(play.x - 6), 0, -(play.y - 6))) + up;
            eyeShot("trees_corner" + suffix, corner, Quaternion.LookRotation(new Vector3(-1, 0.05f, -1)));
            if (suffix != "")
                return;

            if (Placed(scene, "castle0").FirstOrDefault() is { } castle)
            {
                // Behind the castle at +x as high as its towers' caps, out over the crest to the edge and down into the abyss
                var tower = castle.position + new Vector3(19, 16, 0);
                eyeShot("edge_tower", tower, Quaternion.Euler(14, 90, 0));
                eyeShot("edge_tower_side", tower, Quaternion.Euler(12, 50, 0));
            }
            var extent = play.x;
            while (extent < 1000 && HasGround(new Vector3(extent + 0.5f, 0, 20)))
                extent += 0.5f;
            // From just outside the edge, back and down onto the lip where the grass ends on the underside's soil
            var lip = new Vector3(extent + 7, OnGround(new Vector3(extent - 0.5f, 0, 20)).y + 4, 20);
            eyeShot("edge_lip", lip, Quaternion.Euler(28, -70, 0));
            var below = new Vector3(170, -45, -230);
            eyeShot("island_below", below, Quaternion.LookRotation(new Vector3(0, -10, 0) - below));

            WalkIntoATree(scene, play);
        }

        /// <summary>
        /// The final boss's arena (ticket 33.3) in each phase's layout (its phase groups shown and hidden as the boss's arena does): from
        /// the red spawn looking east across the floor at the Royal Box (the busiest view: the crowd, the pieces and in phase 2 the
        /// copter's place), from the blue flag stand back west, from the middle of the floor up at the stands, and from above. Each logs
        /// what the placed models draw there (renderers in view of the level of detail each picks, a draw per material) beside the
        /// ground's and the foliage groups' costs; the scene is left in phase 1.
        /// </summary>
        private static void ShootBoss(Scene scene, string suffix, Action<string, Vector3, Quaternion, int, int> shot, Action<string, Vector3, Quaternion> eyeShot)
        {
            var root = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform;
            var arena = scene.GetRootGameObjects().Select(g => g.GetComponent<SdfBattle.BattleArena>()).FirstOrDefault(a => a != null);
            var camera = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.PlayerName).GetComponentInChildren<Camera>();
            var scatter = scene.GetRootGameObjects().Select(g => g.GetComponent<InstancedScatterRenderer>()).FirstOrDefault(r => r != null);
            var up = Vector3.up * LevelBuilder.EyeHeight;
            var spawn = OnGround(arena.west.spawn.position) + up;
            var stand = OnGround(arena.east.flagStand.transform.position) + up;
            try
            {
                for (var phase = 1; phase <= PhaseGroup.PhaseCount; phase++)
                {
                    PhaseGroup.ShowPhase(root, phase);
                    Physics.SyncTransforms();
                    var views = new (string Name, Vector3 Position, Quaternion Rotation)[]
                    {
                        ("spawn_east", spawn, Quaternion.Euler(0, -90, 0)),
                        ("blue_stand_west", stand, Quaternion.Euler(0, 90, 0)),
                        ("stands", OnGround(new Vector3(0, 0, 0)) + up, Quaternion.Euler(-4, 0, 0)),
                        ("above", new Vector3(0, 55, 62), Quaternion.Euler(38, 180, 0)),
                    };
                    foreach (var (name, position, rotation) in views)
                    {
                        var label = $"boss_p{phase}_{name}{suffix}";
                        eyeShot(label, position, rotation);
                        var (draws, triangles) = PlacedCost(scene, camera, position, rotation);
                        Debug.Log($"SDF level: {label} ({QualitySettings.names[QualitySettings.GetQualityLevel()]}): the placed models draw {draws} times, {triangles:N0} triangles");
                    }
                }
            }
            finally
            {
                PhaseGroup.ShowPhase(root, 1);
            }
        }

        /// <summary>
        /// What the placed models (under Level/Placed, active ones only) draw in a view: each LOD group inside it at the level of detail
        /// it picks, each renderer in view a draw per material (the ink outline is a material of its own), and its triangles.
        /// </summary>
        private static (int Draws, int Triangles) PlacedCost(Scene scene, Camera sceneCamera, Vector3 position, Quaternion rotation)
        {
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Count Camera", HideFlags.HideAndDontSave, typeof(Camera));
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.CopyFrom(sceneCamera);
                camera.aspect = 16f / 9f;
                camera.transform.SetPositionAndRotation(position, rotation);
                var planes = GeometryUtility.CalculateFrustumPlanes(camera);
                var halfAngle = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                int draws = 0, triangles = 0;
                var placed = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find(LevelBuilder.PlacedName);
                foreach (var group in placed.GetComponentsInChildren<LODGroup>())
                {
                    var lods = group.GetLODs();
                    var scale = group.transform.lossyScale;
                    var size = group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                    var distance = Vector3.Distance(position, group.transform.TransformPoint(group.localReferencePoint));
                    var height = size * 0.5f * QualitySettings.lodBias / (Mathf.Max(distance, 0.01f) * halfAngle);
                    var level = Array.FindIndex(lods, lod => height >= lod.screenRelativeTransitionHeight);
                    if (level < 0)
                        continue;
                    foreach (var renderer in lods[level].renderers)
                        if (renderer != null && GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                        {
                            draws += renderer.sharedMaterials.Length;
                            triangles += renderer.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
                        }
                }
                return (draws, triangles);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        /// <summary>
        /// The fungal level's own views (ticket 14.7): the fairy rings from outside and from inside (the placed ring models), the fallen
        /// log, a stump and a puffball from 6 m (found among the scatter colliders by model name), the crest across the field from the
        /// far side, the wood edge at the corner and the cyan glow among the trees at night; and a character controller walked at the
        /// log from the side, logging how high it climbed (it should step onto the log, which is about 0.5 m high).
        /// </summary>
        private static void ShootFungal(Scene scene, Action<string, Vector3, Quaternion, int, int> shot, Action<string, Vector3, Quaternion> eyeShot)
        {
            var up = Vector3.up * LevelBuilder.EyeHeight;
            var rings = Placed(scene, "fairy-ring0").Concat(Placed(scene, "fairy-ring1")).ToArray();
            for (var i = 0; i < rings.Length && i < 4; i++)
            {
                var ring = rings[i];
                var side = OnGround(ring.position + new Vector3(0, 0, -9)) + up;
                eyeShot($"fairy_ring{i}", side, Quaternion.LookRotation(ring.position + Vector3.up * 0.4f - side));
            }
            if (rings.Length > 0)
            {
                var inside = OnGround(rings[0].position) + up;
                eyeShot("fairy_ring_inside", inside, Quaternion.Euler(10, 40, 0));
            }
            var colliders = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find(LevelBuilder.ScatterCollidersName);
            if (colliders != null)
            {
                var play = PlayHalfSize(scene);
                foreach (var model in new[] { "log-fungi0", "stump-brackets0", "stump-brackets1", "puffball0" })
                {
                    var target = colliders.Cast<Transform>().Where(t => t.name.StartsWith(model + " ") && Mathf.Abs(t.position.x) < play.x - 20 && Mathf.Abs(t.position.z) < play.y - 15)
                        .OrderBy(t => t.position.sqrMagnitude).FirstOrDefault();
                    if (target == null)
                    {
                        Debug.Log($"SDF level: fungal view: no {model} among the scatter colliders inside the field");
                        continue;
                    }
                    var from = OnGround(target.position + new Vector3(0, 0, -6)) + up;
                    eyeShot($"fungal_{model}", from, Quaternion.LookRotation(target.position + Vector3.up * 0.5f - from));
                    if (model == "log-fungi0")
                        WalkAtLog(scene, target);
                }
            }
            // From the far end of the field the +x hills (and what the front row hides behind them), and the corner's wood
            var play2 = PlayHalfSize(scene);
            var across = OnGround(new Vector3(-(play2.x - 25), 0, 0)) + up;
            eyeShot("fungal_crest_across", across, Quaternion.Euler(-2, 90, 0));
            eyeShot("fungal_crest_across_up", across, Quaternion.Euler(-12, 90, 0));
            var wood = OnGround(new Vector3(play2.x - 18, 0, play2.y - 22)) + up;
            eyeShot("fungal_wood_edge", wood, Quaternion.LookRotation(new Vector3(1, 0.1f, 1)));
            eyeShot("fungal_glow", wood, Quaternion.LookRotation(new Vector3(0.3f, 0.2f, 1)));
        }

        /// <summary>Walks a character controller at the log from 4 m, across its length's middle, logging the greatest height it reached.</summary>
        private static void WalkAtLog(Scene scene, Transform log)
        {
            var walker = new GameObject("Log Check") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(walker, scene);
            try
            {
                var controller = walker.AddComponent<CharacterController>();
                controller.height = 1.75f;
                controller.radius = 0.3f;
                controller.center = new Vector3(0, controller.height / 2, 0);
                controller.slopeLimit = 45;
                controller.stepOffset = 0.4f;
                var bounds = log.GetComponentsInChildren<Collider>().Select(c => c.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                var side = Mathf.Abs(bounds.extents.x) > Mathf.Abs(bounds.extents.z) ? Vector3.forward : Vector3.right;
                var start = OnGround(bounds.center - side * (4 + Mathf.Min(bounds.extents.x, bounds.extents.z)));
                controller.enabled = false;
                walker.transform.position = start + Vector3.up * 0.05f;
                controller.enabled = true;
                Physics.SyncTransforms();
                var highest = start.y;
                for (var travelled = 0f; travelled < 9; travelled += 0.05f)
                {
                    controller.Move(side * 0.05f + Vector3.down * 0.1f);
                    highest = Mathf.Max(highest, walker.transform.position.y);
                }
                Debug.Log($"SDF level: log check: walking across {log.name} (collider {bounds.size.x:0.0} x {bounds.size.y:0.00} x {bounds.size.z:0.0} m) reached {highest - start.y:0.00} m above its start, ended {walker.transform.position.y - start.y:0.00} m above it");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(walker);
            }
        }

        /// <summary>
        /// Walks a character controller (the player's size) straight at the trunk of the scattered tree furthest inside the playable
        /// area, from 5 m away, and logs how close it came to the trunk's axis against the capsule's radius: it should stop at the radius
        /// plus its own 0.3 m. Logs the number of collider-only objects.
        /// </summary>
        private static void WalkIntoATree(Scene scene, Vector2 play)
        {
            var colliders = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find(LevelBuilder.ScatterCollidersName);
            if (colliders == null)
            {
                Debug.Log("SDF level: tree check: no scatter colliders");
                return;
            }
            var trees = colliders.Cast<Transform>().ToArray();
            var inside = trees.OrderBy(t => Mathf.Max(Mathf.Abs(t.position.x) - play.x, Mathf.Abs(t.position.z) - play.y)).First();
            var capsule = inside.GetComponent<CapsuleCollider>();
            var walker = new GameObject("Tree Check") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(walker, scene);
            try
            {
                var controller = walker.AddComponent<CharacterController>();
                controller.height = 1.75f;
                controller.radius = 0.3f;
                controller.center = new Vector3(0, controller.height / 2, 0);
                controller.slopeLimit = 45;
                controller.stepOffset = 0.4f;
                var axis = inside.TransformPoint(capsule.center);
                var towards = new Vector3(-axis.x, 0, -axis.z).normalized;
                var start = OnGround(axis + towards * 5);
                controller.enabled = false;
                walker.transform.position = start + Vector3.up * 0.05f;
                controller.enabled = true;
                Physics.SyncTransforms();
                var closest = float.PositiveInfinity;
                for (var travelled = 0f; travelled < 8; travelled += 0.05f)
                {
                    controller.Move(-towards * 0.05f + Vector3.down * 0.1f);
                    var p = walker.transform.position;
                    closest = Mathf.Min(closest, new Vector2(p.x - axis.x, p.z - axis.z).magnitude);
                }
                var radius = capsule.radius * inside.lossyScale.x;
                var edge = Mathf.Max(Mathf.Abs(inside.position.x) - play.x, Mathf.Abs(inside.position.z) - play.y);
                Debug.Log($"SDF level: tree check: {trees.Length} collider-only trees; walking straight at {inside.name} ({-edge:0.00} m inside the playable area, " +
                          $"trunk capsule {radius:0.00} m) comes no closer than {closest:0.00} m to its axis (blocked at {radius + controller.radius:0.00})");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(walker);
            }
        }

        /// <summary>
        /// The foliage from eye height in the field between the castles: looking down into the grass close by, across the field where the
        /// rings hand over (6, 15 and 35 m on the high preset), and the close view twice with the wind held at two moments, logging how much
        /// of the picture moved.
        /// </summary>
        private static void ShootFoliage(Scene scene, string directory, string suffix, Action<string, Vector3, Quaternion, int, int> shot,
            Action<string, Vector3, Quaternion> eyeShot)
        {
            var up = Vector3.up * LevelBuilder.EyeHeight;
            // The meadow has foliage across the field; the chessboard only on its hills, so its shots stand at the board's edge, looking out
            var hillsOnly = scene.name == "Chessboard";
            var half = PlayHalfSize(scene);
            var field = OnGround(hillsOnly ? new Vector3(half.x - 3, 0, 20) : new Vector3(-24, 0, 26)) + up;
            var closeLook = hillsOnly ? Quaternion.Euler(14, 90, 0) : Quaternion.Euler(28, 200, 0);
            eyeShot("foliage_close" + suffix, field, closeLook);
            eyeShot("foliage_rings" + suffix, field, hillsOnly ? Quaternion.Euler(4, 70, 0) : Quaternion.Euler(4, 160, 0));
            var other = OnGround(hillsOnly ? new Vector3(-half.x + 3, 0, -20) : new Vector3(30, 0, -20)) + up;
            eyeShot("foliage_field" + suffix, other, hillsOnly ? Quaternion.Euler(6, -80, 0) : Quaternion.Euler(6, 20, 0));

            var wind = scene.GetRootGameObjects().Select(g => g.GetComponent<Wind>()).FirstOrDefault(w => w != null);
            if (wind == null)
                return;
            var phase = wind.phase;
            try
            {
                var paths = new List<string>();
                foreach (var (name, at) in new[] { ("wind_0", 0f), ("wind_1", Mathf.PI) })
                {
                    wind.phase = at;
                    wind.Apply();
                    shot(name + suffix, field, closeLook, 1920, 1080);
                    paths.Add(Path.Combine(directory, name + suffix + ".png"));
                }
                var a = new Texture2D(2, 2);
                var b = new Texture2D(2, 2);
                a.LoadImage(File.ReadAllBytes(paths[0]));
                b.LoadImage(File.ReadAllBytes(paths[1]));
                var (pa, pb) = (a.GetPixels32(), b.GetPixels32());
                var moved = pa.Where((p, i) => Mathf.Abs(p.r - pb[i].r) + Mathf.Abs(p.g - pb[i].g) + Mathf.Abs(p.b - pb[i].b) > 24).Count();
                Debug.Log($"SDF level: wind{suffix}: {moved:N0} of {pa.Length:N0} pixels ({100f * moved / pa.Length:0.0} %) differ between two moments of the wind");
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
            finally
            {
                wind.phase = phase;
                wind.Apply();
            }
        }

        /// <summary>The placed instances of a model under Level/Placed, by position along x (Unity's).</summary>
        private static Transform[] Placed(Scene scene, string model)
        {
            var placed = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find(LevelBuilder.PlacedName);
            return placed == null
                ? Array.Empty<Transform>()
                : placed.Cast<Transform>().Where(t => t.name == model).OrderByDescending(t => t.position.x).ToArray();
        }

        /// <summary>
        /// The busiest spot of the placed models, where the most of them stand within 12 m of one: from eye height 18 m off on either side,
        /// and from above at a slant. Nothing without placements.
        /// </summary>
        private static void ShootCrowd(Scene scene, Action<string, Vector3, Quaternion, int, int> shot, Action<string, Vector3, Quaternion> eyeShot)
        {
            var placed = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find(LevelBuilder.PlacedName);
            var all = placed == null ? Array.Empty<Vector3>() : placed.Cast<Transform>().Select(t => t.position).ToArray();
            if (all.Length == 0)
                return;
            var busiest = all.OrderByDescending(p => all.Count(o => Vector3.Distance(o, p) < 12)).First();
            var around = all.Where(o => Vector3.Distance(o, busiest) < 12).ToArray();
            var centre = OnGround(around.Aggregate(Vector3.zero, (a, b) => a + b) / around.Length);
            var target = centre + Vector3.up * 2;
            var up = Vector3.up * LevelBuilder.EyeHeight;
            var near = OnGround(centre + new Vector3(0, 0, -18)) + up;
            eyeShot("crowd_eye", near, Quaternion.LookRotation(target - near));
            var other = OnGround(centre + new Vector3(14, 0, 11)) + up;
            eyeShot("crowd_eye_back", other, Quaternion.LookRotation(target - other));
            var above = centre + new Vector3(-10, 16, -20);
            shot("crowd_above", above, Quaternion.LookRotation(centre - above), 1920, 1080);
        }

        /// <summary>A point given in a placed model's own pipeline coordinates (as in its .sdf), in Unity's world.</summary>
        private static Vector3 ModelPoint(Transform model, float x, float y, float z) => model.TransformPoint(new Vector3(-x, y, z));

        /// <summary>A direction given in a placed model's own pipeline coordinates, in Unity's world.</summary>
        private static Vector3 ModelDirection(Transform model, float x, float y, float z) => model.TransformDirection(new Vector3(-x, y, z));

        private static void ShootCastleAndRegions(Scene scene, Action<string, Vector3, Quaternion, int, int> shot, Action<string, Vector3, Quaternion> eyeShot,
            Action<string, Vector3, Quaternion> castleShot)
        {
            var castle = Placed(scene, "castle0").FirstOrDefault();
            if (castle == null)
                return;
            var up = Vector3.up * LevelBuilder.EyeHeight;
            Quaternion Look(Vector3 from, Vector3 to) => Quaternion.LookRotation(to - from);

            // From the far end of the field, the meadow 28 m before the gate, at the gate, in the courtyard (towards the keep and towards
            // the gate), on the keep's stair, on the wall-walk, and close up where a wall bay meets a joint stem and a tower
            var far = OnGround(ModelPoint(castle, 0, 0, 110)) + up;
            castleShot("castle_far", far, Look(far, castle.position + Vector3.up * 6));
            var meadow = OnGround(ModelPoint(castle, 0, 0, 36)) + up;
            castleShot("castle_meadow", meadow, Look(meadow, castle.position + Vector3.up * 6));
            var gate = OnGround(ModelPoint(castle, 1.5f, 0, 14)) + up;
            castleShot("castle_gate", gate, Look(gate, ModelPoint(castle, 0, 2.5f, 7)));
            var courtyard = OnGround(ModelPoint(castle, -6, 0, 4)) + up;
            castleShot("castle_courtyard", courtyard, Look(courtyard, ModelPoint(castle, 1, 5, -5)));
            var courtyardGate = OnGround(ModelPoint(castle, 3, 0, 0.5f)) + up;
            castleShot("castle_courtyard_gate", courtyardGate, Look(courtyardGate, ModelPoint(castle, -2, 2.5f, 8)));
            var stair = ModelPoint(castle, 4.3f * Mathf.Sin(0.8f), 1.25f, -5 + 4.3f * Mathf.Cos(0.8f)) + up;
            castleShot("castle_stair", stair, Look(stair, ModelPoint(castle, 4.3f * Mathf.Sin(1.6f), 4f, -5 + 4.3f * Mathf.Cos(1.6f))));
            var wallWalk = ModelPoint(castle, -6.4f, 5, 6.2f) + up;
            castleShot("castle_wallwalk", wallWalk, Look(wallWalk, ModelPoint(castle, -11.6f, 6, 1.5f)));
            var wallWalkOut = ModelPoint(castle, 6.4f, 5, 6.2f) + up;
            castleShot("castle_wallwalk_out", wallWalkOut, Look(wallWalkOut, ModelPoint(castle, 14, 2, 40)));
            var joint = OnGround(ModelPoint(castle, 8, 0, 11)) + up;
            castleShot("castle_joint", joint, Look(joint, ModelPoint(castle, 9.2f, 3, 5.6f)));
            var towerFoot = OnGround(ModelPoint(castle, 17, 0, 4)) + up;
            castleShot("castle_tower_foot", towerFoot, Look(towerFoot, ModelPoint(castle, 12.4f, 4, 0)));

            // The regions' edges: the path, the dirt and the tiles round the castle, close up, at mid distance and from above
            var path = OnGround(new Vector3(36, 0, 6)) + up;
            shot("region_path_near", path, Quaternion.Euler(35, 120, 0), 1920, 1080);
            var apron = OnGround(ModelPoint(castle, 4, 0, 13)) + up;
            shot("region_tiles_near", apron, Quaternion.Euler(40, Look(apron, ModelPoint(castle, 0, 0, 10)).eulerAngles.y, 0), 1920, 1080);
            var mid = OnGround(ModelPoint(castle, 14, 0, 30)) + up;
            eyeShot("region_dirt_edge", mid, Look(mid, ModelPoint(castle, 14, 0, 0)));
            shot("region_top", castle.position + new Vector3(-8, 70, 0), Quaternion.Euler(90, 0, 0), 1600, 1200);

            CheckColliders(scene, castle);
        }

        /// <summary>
        /// Walks a character controller like the player's: from the foot of the castle's stair up it, from outside into its front wall
        /// beside the gate, and straight at each rock, and logs where each walk ends, so a missing or wrong collider shows in numbers.
        /// </summary>
        private static void CheckColliders(Scene scene, Transform castle)
        {
            var walker = new GameObject("Collider Check") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(walker, scene);
            try
            {
                var controller = walker.AddComponent<CharacterController>();
                controller.height = 1.75f;
                controller.radius = 0.3f;
                controller.center = new Vector3(0, controller.height / 2, 0);
                controller.slopeLimit = 45;
                controller.stepOffset = 0.4f;

                // Where the walk ends, how close along the way it came to a point (in plan), and the highest it climbed
                var highest = 0f;
                Vector3 Walk(Vector3 from, Vector3 direction, float metres, Vector3 near, out float closest)
                {
                    controller.enabled = false;
                    walker.transform.position = from + Vector3.up * 0.05f;
                    controller.enabled = true;
                    Physics.SyncTransforms();
                    const float step = 0.05f;
                    closest = float.PositiveInfinity;
                    highest = from.y;
                    for (var travelled = 0f; travelled < metres; travelled += step)
                    {
                        controller.Move(direction.normalized * step + Vector3.down * 0.1f);
                        var p = walker.transform.position;
                        closest = Mathf.Min(closest, new Vector2(p.x - near.x, p.z - near.z).magnitude);
                        highest = Mathf.Max(highest, p.y);
                    }
                    return walker.transform.position;
                }

                // Along waypoints, a little at a time, as a player steering by them would: the highest and lowest it was, and how close it
                // came to the last
                Vector3 Follow(Vector3 from, IReadOnlyList<Vector3> waypoints, out float lowest, out float top, out float missed, out int reached)
                {
                    controller.enabled = false;
                    walker.transform.position = from + Vector3.up * 0.05f;
                    controller.enabled = true;
                    Physics.SyncTransforms();
                    lowest = float.PositiveInfinity;
                    top = float.NegativeInfinity;
                    reached = 0;
                    foreach (var target in waypoints)
                    {
                        for (var n = 0; n < 2000; n++)
                        {
                            var p = walker.transform.position;
                            var flat = new Vector3(target.x - p.x, 0, target.z - p.z);
                            if (flat.magnitude < 0.15f)
                            {
                                reached++;
                                break;
                            }
                            controller.Move(flat.normalized * Mathf.Min(0.05f, flat.magnitude) + Vector3.down * 0.1f);
                            lowest = Mathf.Min(lowest, walker.transform.position.y);
                            top = Mathf.Max(top, walker.transform.position.y);
                        }
                    }
                    var end = walker.transform.position;
                    var last = waypoints[waypoints.Count - 1];
                    missed = new Vector2(end.x - last.x, end.z - last.z).magnitude;
                    return end;
                }

                var ground = castle.position.y;
                // What the walker stands against where a walk ended, by GameObject, for the log
                string Against(Vector3 p) => string.Join(", ", Physics.OverlapCapsule(p + Vector3.up * 0.35f, p + Vector3.up * 1.4f, 0.4f)
                    .Where(c => c.gameObject != walker).Select(c => c.name).Distinct());
                // Through the gate into the courtyard, towards the keep
                var before = OnGround(ModelPoint(castle, 0, 0, 16));
                var inside = Walk(before, ModelDirection(castle, 0, 0, -1), 16, before, out _);
                Debug.Log($"SDF level: collider check: walking in through the gate ends at z = {castle.InverseTransformPoint(inside).z:0.00} in the castle (the gate is at 8, the keep's foot at about -1), {inside.y - ground:0.00} m up, against {Against(inside)}");
                // Across the courtyard from side to side
                var west = OnGround(ModelPoint(castle, -7, 0, 2.5f));
                var east = Follow(west, new[] { ModelPoint(castle, 7, 0, 2.5f) }, out var low, out var high, out var missed, out _);
                Debug.Log($"SDF level: collider check: across the courtyard ends {missed:0.00} m from its goal 14 m away, between {low - ground:0.00} and {high - ground:0.00} m up");

                // Up the keep's stair (the one on the castle's +x side), then round onto the rear wall-walk and along it past the small
                // and the large tower, round their rings, to the front of the side wall
                var stairWay = new List<Vector3>();
                for (var i = 0; i < 20; i++)
                {
                    var angle = 0.42f + i * 0.078f;
                    // On the outer half of each shelf (they reach from the stem, 3.35 m out, to 5.25 m), clear of the keep's foot
                    stairWay.Add(ModelPoint(castle, 4.6f * Mathf.Sin(angle), 0, -5 + 4.6f * Mathf.Cos(angle)));
                }
                var stairFoot = OnGround(ModelPoint(castle, 4.6f * Mathf.Sin(0.15f), 0, -5 + 4.6f * Mathf.Cos(0.15f)));
                // and off the top step onto the ring beyond the opening
                stairWay.Add(ModelPoint(castle, 4.2f * Mathf.Sin(2.2f), 0, -5 + 4.2f * Mathf.Cos(2.2f)));
                var stairTop = Follow(stairFoot, stairWay, out low, out high, out missed, out var reached);
                Debug.Log($"SDF level: collider check: up the keep's stair onto its ring ends {stairTop.y - ground:0.00} m up (the ring is at 5 m), {missed:0.00} m from the last waypoint, {reached} of {stairWay.Count} reached, against {Against(stairTop)}");
                var walkWay = CastleWalk.WayAlongTheWall().Select(p => ModelPoint(castle, p.x, 0, p.y)).ToList();
                var walked = Follow(stairTop, walkWay, out low, out high, out missed, out reached);
                Debug.Log($"SDF level: collider check: along the wall-walk from the keep's ring past both towers' rings ends {missed:0.00} m from its goal, {reached} of {walkWay.Count} waypoints reached, between {low - ground:0.00} and {high - ground:0.00} m up (the walk is at 5 m), against {Against(walked)}");

                // Into a wall bay and into the large tower from outside
                var outsideWall = OnGround(ModelPoint(castle, 6.2f * 1.6f, 0, 6.6f * 1.6f));
                var atWall = Walk(outsideWall, ModelDirection(castle, -0.68f, 0, -0.73f), 10, outsideWall, out _);
                var wallLocal = castle.InverseTransformPoint(atWall);
                Debug.Log($"SDF level: collider check: walking into a wall bay from outside stops {new Vector2(wallLocal.x, wallLocal.z).magnitude:0.00} m from the castle's middle (the wall's outer face is about 10.5 m out there), {atWall.y - ground:0.00} m up");
                var outsideTower = OnGround(ModelPoint(castle, 20, 0, 0));
                var atTower = Walk(outsideTower, ModelDirection(castle, -1, 0, 0), 10, outsideTower, out _);
                Debug.Log($"SDF level: collider check: walking at the large tower from outside stops at x = {-castle.InverseTransformPoint(atTower).x:0.00} in the castle (its stem's face is at 14.7, its foot's at 15.6), {atTower.y - ground:0.00} m up");

                var rocks = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find(LevelBuilder.PlacedName)
                    .Cast<Transform>().Where(t => t.name.StartsWith("rock", StringComparison.Ordinal));
                foreach (var rock in rocks)
                {
                    var start = OnGround(rock.position + new Vector3(8, 0, 0));
                    // Straight at its middle: blocked, the walker slides round it rather than through it
                    Walk(start, Vector3.left, 16, rock.position, out var closest);
                    var colliders = rock.GetComponentsInChildren<Collider>();
                    var size = rock.GetComponentInChildren<Renderer>().bounds.size;
                    Debug.Log($"SDF level: collider check: walking straight at {rock.name} ({string.Join(", ", colliders.Select(c => c is MeshCollider m ? (m.convex ? "convex" : "mesh") : c.GetType().Name))}, " +
                              $"{size.x:0.0} × {size.z:0.0} × {size.y:0.0} m) comes no closer than {closest:0.00} m to its middle, climbing {highest - start.y:0.00} m");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(walker);
            }
        }

        /// <summary>A tree, a mushroom and a rock on the ground a few metres in front of the player, for one shot, then removed.</summary>
        private static void CompareWithModels(Scene scene, Vector3 eye, string directory, Action<string, Vector3, Quaternion, int, int> shot)
        {
            var placed = new List<GameObject>();
            try
            {
                var offsets = new[] { new Vector3(9, 0, 2.5f), new Vector3(4.5f, 0, -0.6f), new Vector3(6, 0, -2.6f) };
                for (var i = 0; i < CompareModels.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/{CompareModels[i]}.prefab");
                    if (prefab == null)
                        continue;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    instance.hideFlags = HideFlags.DontSave;
                    instance.transform.position = OnGround(new Vector3(eye.x, 0, eye.z) + offsets[i]);
                    instance.transform.rotation = Quaternion.Euler(0, -90, 0);
                    placed.Add(instance);
                }
                shot("eye_models", eye, Quaternion.Euler(8, 90, 0), 1920, 1080);
            }
            finally
            {
                foreach (var instance in placed)
                    UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Vector2 PlayHalfSize(Scene scene)
        {
            var walls = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find("Walls");
            var x = walls.Find("Wall +X").GetComponent<BoxCollider>();
            var z = walls.Find("Wall +Z").GetComponent<BoxCollider>();
            return new Vector2(x.transform.position.x - x.size.x / 2, z.transform.position.z - z.size.z / 2);
        }

        /// <summary>
        /// What the castles draw in a view: their GameObjects (the LOD groups under each placed castle0: the level each picks, each
        /// renderer in view a draw, its outline another) and the instanced renderer's castle group, as it drew the view just rendered.
        /// </summary>
        private static string CastleCost(Scene scene, Camera sceneCamera, Vector3 position, Quaternion rotation, InstancedScatterRenderer scatter)
        {
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Count Camera", HideFlags.HideAndDontSave, typeof(Camera));
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.CopyFrom(sceneCamera);
                camera.aspect = 16f / 9f;
                camera.transform.SetPositionAndRotation(position, rotation);
                var planes = GeometryUtility.CalculateFrustumPlanes(camera);
                var halfAngle = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                int draws = 0, triangles = 0;
                foreach (var castle in Placed(scene, "castle0"))
                foreach (var lodGroup in castle.GetComponentsInChildren<LODGroup>())
                {
                    var lods = lodGroup.GetLODs();
                    var scale = lodGroup.transform.lossyScale;
                    var size = lodGroup.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                    var distance = Vector3.Distance(position, lodGroup.transform.TransformPoint(lodGroup.localReferencePoint));
                    var height = size * 0.5f * QualitySettings.lodBias / (Mathf.Max(distance, 0.01f) * halfAngle);
                    var level = Array.FindIndex(lods, lod => height >= lod.screenRelativeTransitionHeight);
                    if (level < 0)
                        continue;
                    foreach (var renderer in lods[level].renderers)
                        if (renderer != null && GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                        {
                            draws++;
                            triangles += renderer.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
                        }
                }
                var instanced = "";
                var group = scatter == null ? -1 : Array.FindIndex(scatter.groups, g => g.name == LevelBuilder.PartsGroup);
                if (group >= 0 && scatter.LastFrame.DrawsPerGroup != null)
                    instanced = $", instanced {scatter.LastFrame.DrawsPerGroup[group]} draws and {scatter.LastFrame.TrianglesPerGroup[group]:N0} triangles";
                return $"the castles' GameObjects {draws} draws and {triangles:N0} triangles{instanced} (outlines included, shadow casters not)";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        /// <summary>
        /// The fly agaric castle's wall-walk as its layout (castle_kit.py, content/models/buildings/castle0.sdf) builds it: an oval of
        /// 12 × 8 m half axes in 18 bays of equal chord, bay 0 centred on the gate (+z), with towers at some of the joints.
        /// </summary>
        private static class CastleWalk
        {
            private const float A = 12, B = 8;
            private const int Bays = 18;

            /// <summary>The joints, where bays meet, in the castle's pipeline coordinates (x, z): joint k ends bay k.</summary>
            private static Vector2[] Joints()
            {
                const int samples = 20000;
                var points = new Vector2[samples + 1];
                var arc = new float[samples + 1];
                for (var i = 0; i <= samples; i++)
                {
                    var t = 2 * Mathf.PI * i / samples;
                    points[i] = new Vector2(A * Mathf.Sin(t), B * Mathf.Cos(t));
                    if (i > 0)
                        arc[i] = arc[i - 1] + Vector2.Distance(points[i], points[i - 1]);
                }
                var chord = arc[samples] / Bays;
                return Enumerable.Range(0, Bays).Select(k =>
                {
                    var s = (k + 0.5f) * chord;
                    var i = Array.FindIndex(arc, a => a >= s);
                    return points[Mathf.Max(i - 1, 0)];
                }).ToArray();
            }

            /// <summary>
            /// From beside the keep's ring at the back, along the +x side's walk to bay 2: on the walk's middle line, and round the
            /// inner side of each tower's ring (towers stand 0.4 m out from their joint) at 3.1 m from its axis.
            /// </summary>
            public static List<Vector2> WayAlongTheWall()
            {
                var joints = Joints();
                var towers = new HashSet<int> { 4, 6 };
                var way = new List<Vector2>();
                // Joint 9 is the middle of the rear wall's +x half; down the joints to 2
                for (var k = 8; k >= 2; k--)
                {
                    var a = joints[k];
                    var outward = new Vector2(a.x / (A * A), a.y / (B * B)).normalized;
                    if (!towers.Contains(k))
                    {
                        way.Add(a - outward * 0.3f);
                        continue;
                    }
                    var centre = a + outward * 0.4f;
                    var start = Mathf.Atan2(-outward.y, -outward.x);
                    var arc = Enumerable.Range(-2, 5).Select(step => centre + new Vector2(Mathf.Cos(start + step * 0.45f), Mathf.Sin(start + step * 0.45f)) * 3.1f).ToList();
                    // From the side of the joint behind (k + 1) round to the side of the one ahead
                    if (Vector2.Distance(arc[0], joints[k + 1]) > Vector2.Distance(arc[arc.Count - 1], joints[k + 1]))
                        arc.Reverse();
                    way.AddRange(arc);
                }
                return way;
            }
        }

        /// <summary>The point of the ground's colliders (the chunks', not the placed models') below or above a point.</summary>
        /// <summary>Whether a ground chunk is under (or over) the point; trees' and rocks' colliders do not count.</summary>
        private static bool HasGround(Vector3 point) =>
            Physics.RaycastAll(new Vector3(point.x, 200, point.z), Vector3.down, 400)
                .Any(hit => hit.collider is MeshCollider && hit.collider.transform.parent != null && hit.collider.transform.parent.name == "Ground");

        private static Vector3 OnGround(Vector3 point)
        {
            var from = new Vector3(point.x, 200, point.z);
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 400).OrderBy(h => h.distance))
                if (hit.collider is MeshCollider && hit.collider.transform.parent != null && hit.collider.transform.parent.name == "Ground")
                    return hit.point;
            return new Vector3(point.x, 0, point.z);
        }

        /// <summary>
        /// The triangles a view draws of what is under the level's child <paramref name="part"/> (the ground's chunks, or the placed
        /// models): for each LOD group inside the view, those of the level of detail it picks at that distance (Unity's relative height:
        /// the group's size over the height the view spans there, times the LOD bias).
        /// </summary>
        private static int TrianglesInView(Scene scene, string part, Camera sceneCamera, Vector3 position, Quaternion rotation)
        {
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Count Camera", HideFlags.HideAndDontSave, typeof(Camera));
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.CopyFrom(sceneCamera);
                camera.aspect = 16f / 9f;
                camera.transform.SetPositionAndRotation(position, rotation);
                var planes = GeometryUtility.CalculateFrustumPlanes(camera);
                var halfAngle = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                var total = 0;
                var root = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find(part);
                if (root == null)
                    return 0;
                foreach (var group in root.GetComponentsInChildren<LODGroup>())
                {
                    var lods = group.GetLODs();
                    var distance = Vector3.Distance(position, group.transform.TransformPoint(group.localReferencePoint));
                    var height = group.size * 0.5f * QualitySettings.lodBias / (Mathf.Max(distance, 0.01f) * halfAngle);
                    var level = Array.FindIndex(lods, lod => height >= lod.screenRelativeTransitionHeight);
                    if (level < 0)
                        continue;
                    foreach (var renderer in lods[level].renderers)
                        if (GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                            total += renderer.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
                }
                return total;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
