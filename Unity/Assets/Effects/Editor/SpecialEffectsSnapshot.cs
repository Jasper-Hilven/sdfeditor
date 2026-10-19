using System;
using System.IO;
using System.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SdfEffects.Editor
{
    /// <summary>
    /// Renders the special effects mid-action to PNGs, stepping the bench's clock in the editor (no Play mode), at 60 frames a second
    /// from the bench's seed, so each run shows the same frames:
    /// <list type="bullet">
    /// <item>overview: the whole bench with the loop running; overview_all: every effect at once, mid-action;</item>
    /// <item>lightning_a/b: two flickers of a bolt, a twentieth of a second apart;</item>
    /// <item>tracers_flash, tracers_flight, tracers_impact: the pistol, minigun and sniper firing, their tracers on the way, and
    /// landing on the backstop;</item>
    /// <item>goop_flight, goop_splat, goop_stuck: gobs in their arc, squashing as they land, and the puddles a while later;</item>
    /// <item>explode_intact and explode_frame0: the amanita scout and its pieces put together in its place, the burst's first frame
    /// without the puff over it (the log gives how many pixels differ), then explode_0.05 to explode_2.7 as it bursts and the pieces
    /// shrink away; explode_reset after it is back.</item>
    /// </list>
    /// Without a window: <c>Unity -batchmode -quit -projectPath Unity -executeMethod SdfEffects.Editor.SpecialEffectsSnapshot.Capture
    /// -snapshotDir &lt;folder&gt;</c> (without -nographics). It builds the scene first.
    /// </summary>
    public static class SpecialEffectsSnapshot
    {
        private const float Frame = 1 / 60f;

        public static void Capture()
        {
            SpecialEffectsScene.Build();
            var scene = EditorSceneManager.OpenScene(SpecialEffectsScene.ScenePath, OpenSceneMode.Single);
            var directory = DirectoryArgument();
            Directory.CreateDirectory(directory);
            var bench = scene.GetRootGameObjects().Select(g => g.GetComponent<SpecialEffectsBench>()).First(b => b != null);
            var camera = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Camera>()).First(c => c != null);
            Physics.SyncTransforms();
            var effects = bench.effects;

            void Run(float seconds)
            {
                for (var t = 0f; t < seconds - 1e-4f; t += Frame)
                    bench.Step(Frame);
            }

            void Shoot(string name, Vector3 position, Vector3 target, int width = 1600, int height = 900) =>
                MushroomSnapshot.RenderView(camera, position, Quaternion.LookRotation(target - position), Path.Combine(directory, name + ".png"), width, height);

            T Effect<T>(Func<T, bool> which = null) where T : FxEffect => effects.OfType<T>().First(e => which == null || which(e));

            // Overview, the loop running for a while
            bench.loop = true;
            bench.Restart();
            Run(3.35f);
            Shoot("overview", camera.transform.position, camera.transform.position + camera.transform.forward, 1920, 1080);
            bench.loop = false;

            // Everything at once, timed so each is mid-action in the same frame
            bench.Restart();
            foreach (var (at, effect) in new (float, FxEffect)[]
                     {
                         (0, Effect<GoopGun>()), (0.3f, Effect<TracerGun>(g => g.label == "Minigun")), (0.45f, Effect<ExplodingMushroom>()),
                         (0.5f, Effect<LightningGun>()), (0.53f, Effect<TracerGun>(g => g.label == "Pistol")), (0.56f, Effect<TracerGun>(g => g.label == "Sniper")),
                     })
            {
                Run(at - bench.Clock);
                effect.Fire();
            }
            Run(0.62f - bench.Clock);
            Shoot("overview_all", camera.transform.position, camera.transform.position + camera.transform.forward, 1920, 1080);

            // Lightning, seen from the side of its lane
            var lightning = Effect<LightningGun>();
            var lane = (lightning.muzzle.position + lightning.target.bounds.center) / 2;
            bench.Restart();
            lightning.Fire();
            Run(0.1f);
            Shoot("lightning_a", lane + new Vector3(-4.6f, 1.3f, -0.8f), lane);
            Run(0.05f);
            Shoot("lightning_b", lane + new Vector3(-4.6f, 1.3f, -0.8f), lane);
            Shoot("lightning_impact", lightning.target.bounds.center + new Vector3(-1.6f, 0.4f, -2f), lightning.target.bounds.center);

            // Tracers: the three bullet guns at once
            bench.Restart();
            foreach (var gun in effects.OfType<TracerGun>())
                gun.Fire();
            var guns = Effect<TracerGun>(g => g.label == "Minigun").muzzle.position;
            Run(Frame);
            Shoot("tracers_flash", guns + new Vector3(-2.2f, 0.6f, 1.6f), guns + new Vector3(0.3f, -0.1f, 0.6f));
            Run(0.05f);
            Shoot("tracers_flight", guns + new Vector3(-4.5f, 1.6f, 2.5f), guns + new Vector3(0, -0.2f, 6));
            Run(0.12f);
            var backstop = guns + new Vector3(0, -0.3f, 11.6f);
            Shoot("tracers_impact", backstop + new Vector3(-3.2f, 1.2f, -4.5f), backstop);

            // Goop: a burst of three gobs in their arc, landing, and stuck
            var goop = Effect<GoopGun>();
            bench.Restart();
            goop.Fire();
            var arc = goop.muzzle.position + new Vector3(0.4f, -0.4f, 2.6f);
            Run(0.4f);
            Shoot("goop_flight", arc + new Vector3(-3.6f, 0.9f, -0.6f), arc);
            Run(0.42f);
            var landing = goop.muzzle.position + new Vector3(0.5f, -0.9f, 4.6f);
            Shoot("goop_splat", landing + new Vector3(-1.9f, 1.3f, -1.6f), landing);
            Run(1.2f);
            Shoot("goop_stuck", landing + new Vector3(-1.9f, 1.3f, -1.6f), landing);

            // The burst: the intact scout, its pieces in its place, then flying apart
            var exploding = Effect<ExplodingMushroom>();
            var holder = exploding.piecesRoot.parent;
            var middle = holder.TransformPoint(new Vector3(0, 0.9f, 0));
            var view = middle + holder.TransformDirection(new Vector3(-0.35f, 0.4f, 1).normalized * 4.2f);
            var wide = middle + holder.TransformDirection(new Vector3(-0.6f, 0.55f, 1).normalized * 8.5f);
            bench.Restart();
            Shoot("explode_intact", view, middle);
            exploding.ShowPieces();
            Shoot("explode_frame0", view, middle);
            LogDifference(directory, "explode_intact", "explode_frame0");
            bench.Restart();
            exploding.Fire();
            foreach (var (seconds, name) in new[] { (Frame, "explode_0.02"), (0.05f, "explode_0.05"), (0.25f, "explode_0.25"), (0.6f, "explode_0.6"),
                         (1.4f, "explode_1.4"), (2.7f, "explode_2.7") })
            {
                Run(seconds - bench.Clock);
                Shoot(name, seconds < 0.5f ? view : wide, middle);
            }
            Run(exploding.resetAfter + 0.5f - bench.Clock);
            Shoot("explode_reset", view, middle);

            Debug.Log($"SDF effects: snapshots written to {directory}");
        }

        /// <summary>Logs how many pixels of two snapshots differ, and by how much at most.</summary>
        private static void LogDifference(string directory, string a, string b)
        {
            var first = Load(Path.Combine(directory, a + ".png"));
            var second = Load(Path.Combine(directory, b + ".png"));
            var pixelsA = first.GetPixels32();
            var pixelsB = second.GetPixels32();
            int differ = 0, clearly = 0, most = 0;
            for (var i = 0; i < pixelsA.Length; i++)
            {
                var d = Mathf.Max(Mathf.Abs(pixelsA[i].r - pixelsB[i].r), Mathf.Max(Mathf.Abs(pixelsA[i].g - pixelsB[i].g), Mathf.Abs(pixelsA[i].b - pixelsB[i].b)));
                differ += d > 2 ? 1 : 0;
                clearly += d > 24 ? 1 : 0;
                most = Mathf.Max(most, d);
            }
            Debug.Log($"SDF effects: {b} against {a}: {differ} of {pixelsA.Length} pixels differ by more than 2 levels, {clearly} by more than 24, at most {most}");
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }

        private static Texture2D Load(string path)
        {
            var texture = new Texture2D(2, 2);
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }

        private static string DirectoryArgument()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-snapshotDir");
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Path.GetTempPath(), "effects-snapshots");
        }
    }
}
