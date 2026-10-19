using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfMushrooms;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SdfLevel.Editor
{
    /// <summary>
    /// Checks the sky's motion in Play mode, which batch snapshots never enter (<see cref="SkySettings"/> moves the moon and floaters
    /// only while playing; the spores and motes move on the shaders' _Time). It opens the level's scene, enters Play mode and holds the
    /// frame time at 1/60 s (<see cref="Time.captureDeltaTime"/>), so simulated time runs as fast as the frames render, then:
    /// <list type="bullet">
    /// <item>For the first 70 s: logs each blink (start, how far the lids closed at the fullest frame, its length), the iris's largest
    /// turn and largest step per frame, and the floaters' largest step per frame; at the fullest frame of the first blink it measures
    /// where the lids' meshes are in the moon's own frame, so they must meet (both are halves of one shell) and not cross. It saves
    /// the moon every frame of that blink side by side (moon_blink.png) and every 10 s (moon_iris.png).</item>
    /// <item>The far layer's culling: the moon seen from the spawn point, the playable area's corner and far outside the island, each
    /// shot logging whether the moon's renderers were drawn and how bright the frame's centre is (moon_from_*.png).</item>
    /// <item>The motes for 15 s with the camera still at the spawn point and 10 s walking at 4 m/s, then the abyss spores for 30 s from
    /// the island's edge, each drawn alone on black at 1080p every frame (the first saved, frame_*.png): a mote that wraps round the
    /// camera or a spore that starts over must not pop in or out. Each frame is compared with the one before: a pixel's jump is how
    /// much brighter (or darker) it got than the brightest pixel near it in the other frame (12 px, 40 px walking), so moving is no
    /// jump; the frame's edge, where they leave the view, is left out. Logs the largest jump and how many pixels jumped by more than a
    /// third, and saves the worst pair of frames, enlarged round the worst pixel (pops_*.png).</item>
    /// </list>
    /// Fails (exit code 1) if a blink does not close to 95 %, the lids cross or leave a gap when closed, or a pixel jumps by more than
    /// <see cref="PopLimit"/>. Without a window: <c>Unity -batchmode -projectPath Unity -executeMethod SdfLevel.Editor.SkyMotionCheck.Run
    /// -snapshotDir &lt;folder&gt; [-level meadow]</c>, without -quit (it exits once Play mode is over) and without -nographics. Domain
    /// and scene reload on entering Play mode are turned off for the run and restored after.
    /// </summary>
    public static class SkyMotionCheck
    {
        private const float FrameTime = 1f / 60;
        private const float BlinkUntil = 70;
        private const float MotesStillUntil = 85;
        private const float MotesWalkingUntil = 95;
        private const float SporesUntil = 125;

        /// <summary>The largest jump (0 to 1, the brightest channel) a pixel may make between frames, before it reads as a pop.</summary>
        private const float PopLimit = 0.5f;

        private const int PopWidth = 1920, PopHeight = 1080;
        private const int MoonTileSize = 160;

        private static string directory;
        private static EnterPlayModeOptions savedOptions;
        private static bool savedOptionsEnabled;
        private static bool savedAsync;
        private static int lastFrame = -1, lastFrameCounted, skippedFrames;
        private static float lastTime = -1;
        private static bool failed;
        private static readonly List<string> log = new List<string>();

        private static SkySettings sky;
        private static Camera camera;
        private static Vector3 eye;
        private static Vector3 edge;

        // The blinks
        private static float blinkStart = -1, blinkPeak;
        private static int blinks;
        private static bool firstBlinkSaved;
        private static readonly List<Texture2D> blinkTiles = new List<Texture2D>();
        private static readonly List<Texture2D> irisTiles = new List<Texture2D>();
        private static float maxClosed, maxIrisTurn, maxIrisStep, maxFloaterStep;
        private static Quaternion lastIris;
        private static Vector3[] lastFloaters;
        private static string lidGeometry;

        // The pops
        private static string popPhase;
        private static Renderer popRenderer;
        private static byte[] previous;
        private static Texture2D previousImage;
        private static float worstJump;
        private static int jumpedPixels, popFrames;
        private static int popNeighbourhood;

        public static void Run()
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
            directory = Argument("-snapshotDir", Path.Combine(Path.GetTempPath(), "sky-motion"));
            Directory.CreateDirectory(directory);
            EditorSceneManager.OpenScene(LevelBuilder.ScenePathOf($"{LevelBuilder.GeneratedFolder}/{Argument("-level", "meadow")}"), OpenSceneMode.Single);

            savedOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            savedOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            savedAsync = ShaderUtil.allowAsyncCompilation;
            // A shader still compiling draws as a placeholder for a frame or two, which would read as a pop
            ShaderUtil.allowAsyncCompilation = false;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void PlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
                return;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            EditorSettings.enterPlayModeOptions = savedOptions;
            EditorSettings.enterPlayModeOptionsEnabled = savedOptionsEnabled;
            ShaderUtil.allowAsyncCompilation = savedAsync;
            foreach (var line in log)
                Debug.Log($"SDF sky motion: {line}");
            Debug.Log($"SDF sky motion: {(failed ? "FAILED" : "passed")}; pictures in {directory}");
            EditorApplication.Exit(failed ? 1 : 0);
        }

        private static void Fail(string message)
        {
            failed = true;
            log.Add($"failed: {message}");
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || Time.frameCount == lastFrame)
                return;
            lastFrame = Time.frameCount;
            try
            {
                if (sky == null && !Begin())
                    return;
                Step(Time.time);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Fail(exception.Message);
                Finish();
            }
        }

        private static bool Begin()
        {
            sky = UnityEngine.Object.FindObjectsByType<SkySettings>(FindObjectsSortMode.None).FirstOrDefault(s => s.moonEye != null);
            if (sky == null)
            {
                Fail("the scene has no sky with a moon");
                Finish();
                return false;
            }
            Time.captureDeltaTime = FrameTime;
            var player = GameObject.Find(LevelBuilder.PlayerName);
            var playerCamera = player.GetComponentInChildren<Camera>();
            eye = playerCamera.transform.position;
            // Only this check's camera draws
            playerCamera.enabled = false;
            var cameraObject = new GameObject("Sky Motion Camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.CopyFrom(playerCamera);
            camera.enabled = false;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = playerCamera.GetUniversalAdditionalCameraData().renderPostProcessing;
            QualityPresetCamera.Apply(camera);

            var extent = 0f;
            while (extent < 1000 && Physics.Raycast(new Vector3(extent + 0.5f, 200, 0), Vector3.down, 400))
                extent += 0.5f;
            Physics.Raycast(new Vector3(extent - 2, 200, 0), Vector3.down, out var hit, 400);
            edge = hit.point + Vector3.up * LevelBuilder.EyeHeight;

            lastIris = sky.moonEye.localRotation;
            log.Add($"open: {LidGeometry(false)}");
            lastFloaters = sky.floaters.Select(f => f.localPosition).ToArray();
            log.Add($"{sky.floaters.Length} floaters; blinks every {sky.blinkEvery.x}-{sky.blinkEvery.y} s for {sky.blinkDuration} s, the lids turned by {sky.lidCloseAngle:0.0}°; " +
                    $"the iris turns up to {sky.turnAmount}° over about {sky.turnPeriod} s");
            return true;
        }

        private static void Step(float time)
        {
            if (lastTime >= 0 && Time.frameCount > lastFrameCounted + 1)
                skippedFrames += Time.frameCount - lastFrameCounted - 1;
            lastFrameCounted = Time.frameCount;
            if (time < BlinkUntil)
            {
                Motion(time);
                lastTime = time;
                return;
            }
            lastTime = time;
            if (popPhase == null)
            {
                SummariseMotion();
                Culling();
                StartPops("motes_still", "Motes", 12);
            }
            if (time < MotesStillUntil)
            {
                PopFrame(eye, Quaternion.Euler(0, 90, 0));
            }
            else if (time < MotesWalkingUntil)
            {
                if (popPhase == "motes_still")
                    StartPops("motes_walking", "Motes", 40);
                PopFrame(eye + Vector3.right * (4 * (time - MotesStillUntil)), Quaternion.Euler(0, 90, 0));
            }
            else if (time < SporesUntil)
            {
                if (popPhase == "motes_walking")
                    StartPops("spores", "Abyss Spores", 12);
                PopFrame(edge, Quaternion.Euler(25, 90, 0));
            }
            else
            {
                EndPops();
                Finish();
            }
        }

        /// <summary>The moon's blinks and iris, and the floaters, this frame (after <see cref="SkySettings"/>'s Update).</summary>
        private static void Motion(float time)
        {
            var closed = Quaternion.Angle(Quaternion.identity, sky.moonUpperLid.localRotation) / Mathf.Max(sky.lidCloseAngle, 1e-3f);
            maxClosed = Mathf.Max(maxClosed, closed);
            var iris = sky.moonEye.localRotation;
            maxIrisTurn = Mathf.Max(maxIrisTurn, Quaternion.Angle(Quaternion.identity, iris));
            // Quaternion.Angle reads steps this small as none: the chord between the gazes, in degrees
            maxIrisStep = Mathf.Max(maxIrisStep, (lastIris * Vector3.forward - iris * Vector3.forward).magnitude * Mathf.Rad2Deg);
            lastIris = iris;
            for (var i = 0; i < sky.floaters.Length; i++)
            {
                var place = sky.floaters[i].localPosition;
                maxFloaterStep = Mathf.Max(maxFloaterStep, Vector3.Angle(lastFloaters[i], place));
                lastFloaters[i] = place;
            }

            var toMoon = sky.moonEye.parent.localPosition.normalized;
            if (closed > 1e-4f)
            {
                if (blinkStart < 0)
                {
                    blinkStart = time;
                    blinkPeak = 0;
                }
                if (closed > blinkPeak)
                {
                    blinkPeak = closed;
                    if (blinks == 0)
                        lidGeometry = $"at the first blink's fullest frame: {LidGeometry(true)}";
                }
                if (!firstBlinkSaved)
                    blinkTiles.Add(MoonTile(toMoon));
            }
            else if (blinkStart >= 0)
            {
                blinks++;
                log.Add($"blink {blinks} at {blinkStart:0.00} s: the lids closed to {blinkPeak:P1} at the fullest frame, closed for {time - blinkStart:0.00} s");
                if (blinkPeak < 0.95f)
                    Fail($"blink {blinks} closed only to {blinkPeak:P1}");
                if (!firstBlinkSaved)
                {
                    blinkTiles.Add(MoonTile(toMoon));
                    SaveStrip(blinkTiles, "moon_blink.png");
                    firstBlinkSaved = true;
                }
                blinkStart = -1;
            }
            if (Mathf.Floor(time / 10) > Mathf.Floor(lastTime / 10))
                irisTiles.Add(MoonTile(toMoon));
        }

        private static void SummariseMotion()
        {
            if (blinks == 0)
                Fail($"no blink in {BlinkUntil} s");
            log.Add($"{blinks} blinks in {BlinkUntil} s; the lids never turned past {maxClosed:P1} of closed");
            log.Add($"the iris turned up to {maxIrisTurn:0.0}° from straight ahead, at most {maxIrisStep:0.000}° a frame");
            log.Add($"the floaters moved at most {maxFloaterStep:0.0000}° of the sky a frame ({maxFloaterStep * 60:0.000}°/s)");
            if (lidGeometry != null)
                log.Add(lidGeometry);
            if (irisTiles.Count > 0)
                SaveStrip(irisTiles, "moon_iris.png");
            log.Add($"{skippedFrames} frames passed without this check seeing them");
        }

        /// <summary>
        /// Where the lids are at this frame, in the moon's own frame (the model's metres): the lowest point of the upper lid and the
        /// highest of the lower, over the front half (towards the viewer). Closed, both are halves of one shell, each cut 4 mm off the
        /// middle and rounded by 4 mm, so their edges meet at the middle; turned too far they cross, not far enough they leave a gap
        /// (a few millimetres either way is the meshing; a pixel of the moon on screen is about 1.5 mm).
        /// </summary>
        private static string LidGeometry(bool check)
        {
            var moon = sky.moonEye.parent;
            float Extreme(Transform lid, bool lowest)
            {
                var mesh = lid.GetComponent<MeshFilter>().sharedMesh;
                var front = mesh.vertices.Select(v => moon.InverseTransformPoint(lid.TransformPoint(v))).Where(p => p.z > 0.02f).Select(p => p.y).ToArray();
                return lowest ? front.Min() : front.Max();
            }
            var closed = Quaternion.Angle(Quaternion.identity, sky.moonUpperLid.localRotation) / sky.lidCloseAngle;
            var upper = Extreme(sky.moonUpperLid, true);
            var lower = Extreme(sky.moonLowerLid, false);
            var overlap = lower - upper;
            // At the fullest frame the lids are a little short of closed: what is left of the turn, at the shell's radius
            var short_ = (1 - closed) * sky.lidCloseAngle * Mathf.Deg2Rad * 0.136f;
            if (check && overlap + 2 * short_ < -0.004f)
                Fail($"closed, the lids leave a gap: the upper lid's edge at {upper * 1000:0.0} mm, the lower's at {lower * 1000:0.0} mm");
            if (check && overlap > 0.01f)
                Fail($"closed, the lids cross: the upper lid's edge at {upper * 1000:0.0} mm, the lower's at {lower * 1000:0.0} mm");
            var edges = $"the lids {closed:P1} closed; in front, the upper lid's edge at y = {upper * 1000:0.00} mm, the lower's at {lower * 1000:0.00} mm " +
                        "(the eye's radius is 120 mm, the lids' 136 mm)";
            return check ? $"{edges}: they overlap by {overlap * 1000:0.00} mm, {(overlap + 2 * short_) * 1000:0.00} mm fully closed" : edges;
        }

        /// <summary>The moon, from the spawn point, its 9° across a tile.</summary>
        private static Texture2D MoonTile(Vector3 toMoon)
        {
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(toMoon));
            var fieldOfView = camera.fieldOfView;
            camera.fieldOfView = 14;
            var image = RenderImage(MoonTileSize, MoonTileSize);
            camera.fieldOfView = fieldOfView;
            return image;
        }

        /// <summary>The far layer is drawn round the camera; seen from anywhere, the moon must be drawn and bright.</summary>
        private static void Culling()
        {
            var toMoon = sky.moonEye.parent.localPosition.normalized;
            var corner = eye + new Vector3(60, 0, 40);
            var outside = new Vector3(-40, 50, -300);
            var moonRenderers = sky.moonEye.parent.GetComponentsInChildren<Renderer>();
            foreach (var (name, position) in new[] { ("spawn", eye), ("corner", corner), ("outside", outside) })
            {
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(toMoon));
                var image = RenderImage(320, 180);
                var centre = image.GetPixels32().Where((_, i) => Mathf.Abs(i % 320 - 160) < 6 && Mathf.Abs(i / 320 - 90) < 6).Average(c => (float)Mathf.Max(c.r, c.g, c.b)) / 255;
                File.WriteAllBytes(Path.Combine(directory, $"moon_from_{name}.png"), image.EncodeToPNG());
                UnityEngine.Object.Destroy(image);
                var drawn = moonRenderers.Count(r => r.isVisible);
                log.Add($"the moon from the {name} ({position.x:0}, {position.y:0}, {position.z:0}): {drawn} of {moonRenderers.Length} of its renderers drawn, the centre's brightness {centre:0.00}");
                if (drawn == 0 || centre < 0.2f)
                    Fail($"the moon is not drawn from the {name}");
            }
        }

        private static void StartPops(string phase, string rendererName, int neighbourhood)
        {
            EndPops();
            popPhase = phase;
            popNeighbourhood = neighbourhood;
            popRenderer = sky.GetComponentsInChildren<Renderer>(true).First(r => r.name == rendererName);
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                renderer.forceRenderingOff = renderer != popRenderer;
            foreach (var scatter in UnityEngine.Object.FindObjectsByType<InstancedScatterRenderer>(FindObjectsSortMode.None))
                scatter.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            (worstJump, jumpedPixels, popFrames) = (0, 0, 0);
        }

        /// <summary>One frame of the set alone, compared with the one before.</summary>
        private static void PopFrame(Vector3 position, Quaternion rotation)
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            var image = RenderImage(PopWidth, PopHeight);
            var pixels = image.GetPixels32();
            var current = new byte[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
                current[i] = Math.Max(pixels[i].r, Math.Max(pixels[i].g, pixels[i].b));
            if (popFrames++ == 0)
                File.WriteAllBytes(Path.Combine(directory, $"frame_{popPhase}.png"), image.EncodeToPNG());
            if (previous != null)
            {
                var (jump, worstAt, jumped) = Jumps(previous, current);
                var (drop, dropAt, dropped) = Jumps(current, previous);
                jumped += dropped;
                jumpedPixels += jumped;
                var largest = Mathf.Max(jump, drop);
                if (largest > worstJump)
                {
                    worstJump = largest;
                    SavePair(previousImage, image, jump >= drop ? worstAt : dropAt, $"pops_{popPhase}.png");
                }
            }
            if (previousImage != null)
                UnityEngine.Object.Destroy(previousImage);
            previous = current;
            previousImage = image;
        }

        /// <summary>How much brighter than anything near it in <paramref name="before"/> each pixel of <paramref name="after"/> is: the largest, where, and how many by more than a third.</summary>
        private static (float, int, int) Jumps(byte[] before, byte[] after)
        {
            var r = popNeighbourhood;
            var (largest, at, count) = (0, 0, 0);
            // Not near the frame's edge, where a spore leaving the view is no pop
            for (var y = 2 * r; y < PopHeight - 2 * r; y++)
            for (var x = 2 * r; x < PopWidth - 2 * r; x++)
            {
                var value = after[y * PopWidth + x];
                if (value <= largest && value < 85)
                    continue;
                var near = 0;
                for (var dy = -r; dy <= r && near < value; dy++)
                for (var dx = -r; dx <= r; dx++)
                    near = Math.Max(near, before[(y + dy) * PopWidth + x + dx]);
                var jump = value - near;
                if (jump > 85)
                    count++;
                if (jump > largest)
                    (largest, at) = (jump, y * PopWidth + x);
            }
            return (largest / 255f, at, count);
        }

        private static void EndPops()
        {
            if (popPhase == null)
                return;
            log.Add($"{popPhase}: {popFrames} frames; the largest jump of a pixel between frames {worstJump:0.00} (1 is black to white), " +
                    $"{jumpedPixels} pixel jumps of more than a third (within {popNeighbourhood} px)");
            if (worstJump > PopLimit)
                Fail($"{popPhase}: a pixel jumps by {worstJump:0.00} between frames");
            previous = null;
            if (previousImage != null)
                UnityEngine.Object.Destroy(previousImage);
            previousImage = null;
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            Time.captureDeltaTime = 0;
            EditorApplication.ExitPlaymode();
        }

        private static Texture2D RenderImage(int width, int height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = UniversalRenderPipeline.asset != null ? Mathf.Max(1, UniversalRenderPipeline.asset.msaaSampleCount) : 1,
            };
            camera.targetTexture = target;
            camera.Render();
            var active = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = active;
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.Destroy(target);
            return image;
        }

        /// <summary>The two frames round a pixel, 128 pixels square each, enlarged 3 times without smoothing, side by side.</summary>
        private static void SavePair(Texture2D before, Texture2D after, int at, string name)
        {
            const int crop = 128, scale = 3;
            var (cx, cy) = (Mathf.Clamp(at % PopWidth - crop / 2, 0, PopWidth - crop), Mathf.Clamp(at / PopWidth - crop / 2, 0, PopHeight - crop));
            var pair = new Texture2D(2 * crop * scale + 6, crop * scale, TextureFormat.RGBA32, false);
            pair.SetPixels32(Enumerable.Repeat(new Color32(60, 60, 60, 255), pair.width * pair.height).ToArray());
            for (var k = 0; k < 2; k++)
            {
                var source = k == 0 ? before : after;
                for (var y = 0; y < crop * scale; y++)
                for (var x = 0; x < crop * scale; x++)
                    pair.SetPixel(k * (crop * scale + 6) + x, y, source.GetPixel(cx + x / scale, cy + y / scale));
            }
            File.WriteAllBytes(Path.Combine(directory, name), pair.EncodeToPNG());
            UnityEngine.Object.Destroy(pair);
        }

        private static void SaveStrip(List<Texture2D> tiles, string name)
        {
            const int gap = 4;
            var tile = tiles[0].width;
            var strip = new Texture2D(tiles.Count * (tile + gap) - gap, tile, TextureFormat.RGBA32, false);
            strip.SetPixels32(Enumerable.Repeat(new Color32(20, 20, 20, 255), strip.width * strip.height).ToArray());
            for (var i = 0; i < tiles.Count; i++)
            {
                strip.SetPixels32(i * (tile + gap), 0, tile, tile, tiles[i].GetPixels32());
                UnityEngine.Object.Destroy(tiles[i]);
            }
            tiles.Clear();
            File.WriteAllBytes(Path.Combine(directory, name), strip.EncodeToPNG());
            UnityEngine.Object.Destroy(strip);
        }
    }
}
