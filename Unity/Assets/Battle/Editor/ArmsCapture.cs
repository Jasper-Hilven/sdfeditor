using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SdfBattle.Editor
{
    /// <summary>
    /// The first-person arms capture (ticket 53): in a batch editor with the GPU it opens the Meadow, plays it, and for each weapon (the single
    /// pistol, dual pistols, the minigun, sniper, electro-gun, flamethrower, poison-gun and blob-gun) and each team (red, blue) puts that weapon in
    /// your hands and renders the camera you play through to <c>&lt;out&gt;/&lt;team&gt;-&lt;weapon&gt;.png</c>. Nothing but the weapon view is drawn
    /// (the camera renders into a texture, so the HUD is not in it); you cannot be hurt. Run:
    /// <c>SDF_NO_QUIT=1 SDF_ARMS_OUT=&lt;folder&gt; tools/unity-batch.sh arms53 -executeMethod SdfBattle.Editor.ArmsCapture.Run</c>.
    /// It exits 0 when every picture is written, 1 on the first error.
    /// </summary>
    [InitializeOnLoad]
    public static class ArmsCapture
    {
        private const string ActiveKey = "SdfBattle.ArmsCapture.Active";
        private const string Scene = "Assets/Scenes/Meadow.unity";
        private const int Width = 1280, Height = 720;
        private static readonly string[] Weapons =
        {
            WeaponIds.Pistol, WeaponIds.DualPistols, WeaponIds.Minigun, WeaponIds.Sniper,
            WeaponIds.ElectroGun, WeaponIds.Flamethrower, WeaponIds.PoisonGun, WeaponIds.BlobGun,
        };

        private static bool active;
        private static bool started;
        private static int job;
        private static float jobStart = -1;
        private static string folder;
        private static Player you;
        private static Camera view;
        private static Arsenal arsenal;
        private static float playStarted = -1;
        private static int lastFrame = -1;

        static ArmsCapture()
        {
            active = SessionState.GetBool(ActiveKey, false);
            EditorApplication.update += Update;
        }

        public static void Run()
        {
            folder = Environment.GetEnvironmentVariable("SDF_ARMS_OUT");
            if (string.IsNullOrEmpty(folder))
            {
                Debug.LogError("ArmsCapture: set SDF_ARMS_OUT to the folder for the pictures");
                EditorApplication.Exit(1);
                return;
            }
            Directory.CreateDirectory(folder);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(ActiveKey + ".out", folder);
            active = true;
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void Fail(string why)
        {
            Debug.LogError("ArmsCapture: " + why);
            Finish(1);
        }

        private static void Finish(int code)
        {
            SessionState.SetBool(ActiveKey, false);
            active = false;
            Time.captureFramerate = 0;
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
            else
                EditorApplication.isPlaying = false;
        }

        private static void Update()
        {
            if (!active || !EditorApplication.isPlaying)
                return;
            try
            {
                Step();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Fail(exception.Message);
            }
        }

        private static void Step()
        {
            folder ??= SessionState.GetString(ActiveKey + ".out", "");
            if (playStarted < 0)
                playStarted = Time.realtimeSinceStartup;
            var match = Match.Current;
            if (match == null || match.State == null || !match.State.Started || match.You == null || !match.You.IsYou || Combat.Current == null)
            {
                if (Time.realtimeSinceStartup - playStarted > 90)
                    Fail("the Meadow never started a match");
                return;
            }
            if (Time.frameCount == lastFrame)
                return;
            lastFrame = Time.frameCount;
            if (Time.timeScale <= 0 || AudioListener.pause)
            {
                Time.timeScale = 1;
                AudioListener.pause = false;
            }
            if (!started)
            {
                started = true;
                you = match.You;
                view = you.GetComponentInChildren<Camera>();
                arsenal = Combat.Current.ArsenalOf(you);
                if (view == null || arsenal == null)
                {
                    Fail("no camera or arsenal on you");
                    return;
                }
                if (match.Hud != null)
                    foreach (var canvas in match.Hud.GetComponentsInChildren<Canvas>(true))
                        canvas.enabled = false;
                you.Controller.readsInput = false;
                arsenal.scripted = true;
                var previous = Combat.Current.State.DamageTakenOf;
                Combat.Current.State.DamageTakenOf = victim => victim == you.State ? 0 : (previous != null ? previous(victim) : 1f);
                match.State.Rules.capturesToWin = 999;
                var epic = Array.IndexOf(QualitySettings.names, "Epic");
                if (epic >= 0)
                    QualitySettings.SetQualityLevel(epic, true);
                Debug.Log("ArmsCapture: capturing " + Weapons.Length * 2 + " pictures into " + folder);
            }
            var total = Weapons.Length * 2;
            if (job >= total)
            {
                Debug.Log("ArmsCapture: done");
                Finish(0);
                return;
            }
            var team = job < Weapons.Length ? Team.Red : Team.Blue;
            var weapon = Weapons[job % Weapons.Length];
            if (jobStart < 0)
            {
                // This team and weapon in your hands
                var field = typeof(PlayerState).GetField("<Team>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field == null)
                {
                    Fail("PlayerState has no Team backing field");
                    return;
                }
                field.SetValue(you.State, team);
                var inventory = arsenal.Inventory;
                inventory.Reset();
                if (weapon == WeaponIds.DualPistols)
                    inventory.Take(WeaponIds.Pistol);
                else if (weapon != WeaponIds.Pistol)
                    inventory.Take(weapon);
                jobStart = Time.time;
                return;
            }
            // Drawn (0.25 s), settled and still, then the picture
            if (Time.time - jobStart < 1.2f)
                return;
            Shoot(team, weapon);
            job++;
            jobStart = -1;
        }

        private static void Shoot(Team team, string weapon)
        {
            var target = new RenderTexture(new RenderTextureDescriptor(Width, Height, RenderTextureFormat.ARGB32, 24) { sRGB = true, msaaSamples = 4 });
            var reader = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                view.targetTexture = target;
                view.Render();
                RenderTexture.active = target;
                reader.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                var path = Path.Combine(folder, $"{team.ToString().ToLowerInvariant()}-{weapon}.png");
                File.WriteAllBytes(path, reader.EncodeToPNG());
                Debug.Log("ArmsCapture: " + path);
            }
            finally
            {
                view.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(reader);
            }
        }
    }
}
