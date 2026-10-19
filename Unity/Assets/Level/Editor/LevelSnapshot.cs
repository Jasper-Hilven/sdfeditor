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
    /// only), and the first view again on the integrated GPU / web quality level. It logs what the ground costs in each eye-height view:
    /// the triangles of the levels of detail the chunks' LOD groups pick, inside the view.
    /// <para>
    /// With placed castles it also shoots the first castle (the one at +x, the pipeline's -x) from the meadow, at its gate, from its
    /// courtyard and from its wall-walk, the ground regions' edges close up and from above, and checks the colliders by walking a
    /// character controller up the castle's stair, into its wall and straight at each rock, logging where each walk ends.
    /// </para>
    /// <para>
    /// With a sky (<see cref="LevelSky"/>) it also looks up at the moon from the spawn point, out from a castle tower over the crest to
    /// the edge and the abyss, out and down from the island's edge, and at the whole island hanging over the abyss from outside; the moon
    /// and an eye view again on the integrated GPU / web quality level with bloom off. Every view logs what the sky draws in it.
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
            void EyeShot(string name, Vector3 position, Quaternion rotation)
            {
                Shot(name, position, rotation);
                Debug.Log($"SDF level: {name}: the ground draws {GroundTrianglesInView(scene, camera, position, rotation):N0} triangles; {SkyInView(scene, camera, position, rotation)}");
            }

            var level = QualitySettings.GetQualityLevel();
            try
            {
                QualitySettings.SetQualityLevel(0, true);
                foreach (var (name, yaw) in new[] { ("eye_+x", 90f), ("eye_-x", -90f), ("eye_+z", 0f), ("eye_-z", 180f) })
                    EyeShot(name, eye, Quaternion.Euler(0, yaw, 0));

                // From the playable area's corner, towards the corner of the hills
                var play = PlayHalfSize(scene);
                var corner = OnGround(new Vector3(play.x - 4, 0, play.y - 4)) + Vector3.up * LevelBuilder.EyeHeight;
                EyeShot("eye_corner", corner, Quaternion.LookRotation(new Vector3(1, 0, 1)));
                // Up on a crest is beyond the walls; from there the island's edge and the chunk borders across the hills show
                Shot("above", new Vector3(0, 90, -160), Quaternion.Euler(28, 0, 0));
                Shot("top", new Vector3(0, 260, 0), Quaternion.Euler(90, 0, 0), 1600, 1200);

                CompareWithModels(scene, eye, directory, Shot);
                ShootCastleAndRegions(scene, Shot, EyeShot);
                var moon = ShootSky(scene, eye, EyeShot);

                QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);
                EyeShot("eye_+x_web", eye, Quaternion.Euler(0, 90, 0));
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
        /// The sky's views: up at the moon from the spawn point (with one), from the top of the first castle's outer back tower out over
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
                // On top of the tower's roof (its apex is 12.5 m up), looking out of the island's end, a little down
                var tower = ModelPoint(castle, 13, 14, -8);
                eyeShot("sky_tower", tower, Quaternion.LookRotation(ModelDirection(castle, 0, -0.2f, -1)));
            }
            // At the island's edge behind the +x hills (pipeline -x), looking out and down into the abyss: the last ground out along +x
            var extent = PlayHalfSize(scene).x;
            while (extent < 1000 && Physics.Raycast(new Vector3(extent + 0.5f, 200, 0), Vector3.down, 400))
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

        /// <summary>The placed instances of a model under Level/Placed, by position along x (Unity's).</summary>
        private static Transform[] Placed(Scene scene, string model)
        {
            var placed = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find(LevelBuilder.PlacedName);
            return placed == null
                ? Array.Empty<Transform>()
                : placed.Cast<Transform>().Where(t => t.name == model).OrderByDescending(t => t.position.x).ToArray();
        }

        /// <summary>A point given in a placed model's own pipeline coordinates (as in its .sdf), in Unity's world.</summary>
        private static Vector3 ModelPoint(Transform model, float x, float y, float z) => model.TransformPoint(new Vector3(-x, y, z));

        /// <summary>A direction given in a placed model's own pipeline coordinates, in Unity's world.</summary>
        private static Vector3 ModelDirection(Transform model, float x, float y, float z) => model.TransformDirection(new Vector3(-x, y, z));

        private static void ShootCastleAndRegions(Scene scene, Action<string, Vector3, Quaternion, int, int> shot, Action<string, Vector3, Quaternion> eyeShot)
        {
            var castle = Placed(scene, "castle0").FirstOrDefault();
            if (castle == null)
                return;
            var up = Vector3.up * LevelBuilder.EyeHeight;
            Quaternion Look(Vector3 from, Vector3 to) => Quaternion.LookRotation(to - from);

            // From the meadow 25 m before the gate, at the gate, in the courtyard towards the gate and stairs, and from the wall-walk out
            var meadow = OnGround(ModelPoint(castle, 0, 0, 36)) + up;
            eyeShot("castle_meadow", meadow, Look(meadow, castle.position + Vector3.up * 4));
            var gate = OnGround(ModelPoint(castle, 1.5f, 0, 16)) + up;
            eyeShot("castle_gate", gate, Look(gate, ModelPoint(castle, 0, 2, 9)));
            var courtyard = OnGround(ModelPoint(castle, -4, 0, -1)) + up;
            eyeShot("castle_courtyard", courtyard, Look(courtyard, ModelPoint(castle, 4, 2.5f, 8)));
            var wallWalk = ModelPoint(castle, -6.6f, 5, 8.7f) + up;
            shot("castle_wallwalk", wallWalk, Look(wallWalk, ModelPoint(castle, -6.6f, 2, 40)), 1920, 1080);

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

                // The stair runs along the front wall's inside from x = 3.2 (its foot) to 10.2, up to the wall-walk at 5 m
                var foot = OnGround(ModelPoint(castle, 2.4f, 0, 7.6f));
                var climbed = Walk(foot, ModelDirection(castle, 1, 0, 0), 9, foot, out _);
                Debug.Log($"SDF level: collider check: walking up the castle's stair ends {climbed.y - foot.y:0.00} m above its foot (the wall-walk is at 5 m)");

                var outside = OnGround(ModelPoint(castle, 7, 0, 15));
                var stopped = Walk(outside, ModelDirection(castle, 0, 0, -1), 10, outside, out _);
                var local = castle.InverseTransformPoint(stopped);
                Debug.Log($"SDF level: collider check: walking into the castle's front wall stops at z = {local.z:0.00} in the castle (its face is at 10, plus the walker's 0.3 m)");

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

        /// <summary>The point of the ground's colliders (the chunks', not the placed models') below or above a point.</summary>
        private static Vector3 OnGround(Vector3 point)
        {
            var from = new Vector3(point.x, 200, point.z);
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 400).OrderBy(h => h.distance))
                if (hit.collider is MeshCollider && hit.collider.transform.parent != null && hit.collider.transform.parent.name == "Ground")
                    return hit.point;
            return new Vector3(point.x, 0, point.z);
        }

        /// <summary>
        /// The triangles of the ground a view draws: for each chunk inside the view, those of the level of detail its LOD group picks
        /// at that distance (Unity's relative height: the group's size over the height the view spans there, times the LOD bias).
        /// </summary>
        private static int GroundTrianglesInView(Scene scene, Camera sceneCamera, Vector3 position, Quaternion rotation)
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
                var root = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName);
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
