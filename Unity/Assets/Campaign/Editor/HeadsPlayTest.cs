using System;
using System.IO;
using System.Linq;
using SdfBattle;
using SdfGui;
using SdfHeads;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// Ticket 47's play test. <c>Run</c> (clean save, the clock set to 15 October 2026): Boot, the first match live, you win 3-0, which must unlock
    /// November's head (Frankenstein, whose model is not built, so it shows as a stand-in); Settings then lists two heads, ▶ picks Frankenstein, and
    /// a screenshot of Settings is written to <c>Media/heads-47</c>; the next match must wear the pick (as its stand-in) with every player's head
    /// drawn once. <c>CheckReload</c> (a new process, same clock) checks that the save came back with the head and that the next match wears it.
    /// Batch: <c>SDF_NO_QUIT=1 tools/unity-batch.sh heads47 -executeMethod SdfCampaign.Editor.HeadsPlayTest.Run</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class HeadsPlayTest
    {
        private const string ActiveKey = "SdfCampaign.HeadsPlayTest", ModeKey = "SdfCampaign.HeadsPlayTest.Mode";
        private static readonly DateTime Today = new DateTime(2026, 10, 15);

        private static bool checking, finished;
        private static int phase;
        private static float since, started = -1;
        private static Match first;
        private static readonly System.Collections.Generic.List<string> log = new System.Collections.Generic.List<string>();
        private static readonly System.Collections.Generic.List<string> errors = new System.Collections.Generic.List<string>();

        static HeadsPlayTest()
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;
            checking = SessionState.GetString(ModeKey, "run") == "check";
            GameLoop.Clock = () => Today;
            SceneLoader.AutoPlay = true;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if ((type == LogType.Exception || type == LogType.Error) && !stack.Contains("UnityEditor.Search"))
                    errors.Add(message + "\n" + stack);
            };
            EditorApplication.update += Update;
        }

        public static void Run()
        {
            PlayerPrefs.DeleteKey(GameSave.Key);
            Begin("run");
        }

        public static void CheckReload() => Begin("check");

        private static void Begin(string mode)
        {
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(ModeKey, mode);
            EditorSceneManager.OpenScene(BootBuilder.ScenePath);
            EditorApplication.EnterPlaymode();
        }

        private static void Expect(bool ok, string what)
        {
            if (!ok)
                throw new Exception("Expected: " + what);
        }

        private static void Update()
        {
            if (finished || !EditorApplication.isPlaying)
                return;
            var now = Time.realtimeSinceStartup;
            if (started < 0)
                started = since = now;
            var loop = UnityEngine.Object.FindAnyObjectByType<GameLoop>();
            var flow = UnityEngine.Object.FindAnyObjectByType<GuiFlow>();
            if (loop == null || flow == null)
                return;
            try
            {
                Step(loop, flow, now);
            }
            catch (Exception exception)
            {
                errors.Add(exception.ToString());
                Finish();
                return;
            }
            if (!finished && now - started > 300)
            {
                errors.Add("timed out in phase " + phase);
                Finish();
            }
        }

        private static bool Live(GameLoop loop)
        {
            var loader = UnityEngine.Object.FindAnyObjectByType<SceneLoader>();
            if (loader != null && loader.Busy)
                return false;
            var m = Match.Current;
            if (m == null || m.State == null || loop.Current != UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
                return false;
            var warm = UnityEngine.Object.FindAnyObjectByType<WarmUpPlayer>();
            if (warm != null)
            {
                warm.Stop();
                return false;
            }
            return !m.Held && m.State.Phase == RoundPhase.Playing;
        }

        private static string WornBy(Match m, Func<Player, bool> who)
        {
            var player = m.Players.First(who);
            return player.WornHead != null ? player.WornHead.Id : "none";
        }

        private static void CheckCast(Match m, string wantedForYou)
        {
            Expect(HeadSelection.Yours == wantedForYou, $"the campaign hands you {wantedForYou} (was {HeadSelection.Yours})");
            var requested = HeadSelection.Cast.Used.ToList();
            Expect(requested.Count == m.Players.Count, $"every player drew a head ({requested.Count} of {m.Players.Count})");
            Expect(requested.Distinct().Count() == requested.Count, "each player's head is different: " + string.Join(",", requested));
            Expect(requested[0] == wantedForYou || requested.Contains(wantedForYou), "yours is among them");
            var wear = WornBy(m, p => p.IsYou);
            Expect(wear == HeadStandIn.Of(wantedForYou), $"you wear {HeadStandIn.Of(wantedForYou)} for {wantedForYou} (wore {wear})");
            log.Add($"match: you ask for {wantedForYou} and wear {wear}; cast {string.Join(",", requested)}");
        }

        private static void Step(GameLoop loop, GuiFlow flow, float now)
        {
            switch (phase)
            {
                case 0:
                    if (!Live(loop))
                        return;
                    first = Match.Current;
                    if (checking)
                    {
                        var save = GameSave.Load(new GameStorage());
                        Expect(save.version == GameSave.CurrentVersion, "the save is the current version");
                        Expect(save.heads.current == "frankenstein", $"the pick came back ({save.heads.current})");
                        CollectionAssert(save.heads.unlocked, "frankenstein");
                        CheckCast(first, "frankenstein");
                        Finish();
                        return;
                    }
                    Expect(loop.CurrentHead == "pumpkin", "a new player has the head of October");
                    Expect(loop.AvailableHeads.Count == 1, "and only that");
                    CheckCast(first, "pumpkin");
                    for (var i = 0; i < 3; i++)
                        first.State.ReportCapture(first.yourTeam);
                    phase = 1;
                    since = now;
                    return;
                case 1:
                    if (!first.State.Over || (flow.screen != GuiScreen.Victory && flow.screen != GuiScreen.Defeat) || flow.Ending)
                        return;
                    Expect(loop.LastUnlockedHead == "frankenstein", $"the win unlocks November's head ({loop.LastUnlockedHead})");
                    CollectionAssert(GameSave.Load(new GameStorage()).heads.unlocked, "frankenstein");
                    // Settings: two heads in the cycle, the second a placeholder, and the pick through the selector
                    flow.Show(GuiScreen.Settings);
                    phase = 2;
                    since = now;
                    return;
                case 2:
                    if (now - since < 1.5f)
                        return;
                    var menu = UnityEngine.Object.FindAnyObjectByType<GuiSettingsMenu>();
                    Expect(menu != null && menu.head != null, "the settings menu has the head row");
                    Expect(menu.head.options.Length == 2, "two heads in the cycle");
                    // Ticket 45 builds the heads one pair at a time: a head without its model says so, a real one does not
                    if (!MonsterHeads.ById("frankenstein").Value.Built)
                        StringEndsWith(menu.head.options[1], "(placeholder)");
                    Expect(menu.headPreview != null && menu.headPreview.Worn == HeadStandIn.Of(loop.CurrentHead), "the preview wears the current head (the stand-in while it is unbuilt)");
                    menu.head.Set(Array.FindIndex(menu.head.options, o => o.StartsWith("Frankenstein")), true);
                    Expect(loop.CurrentHead == "frankenstein", "the arrow picks Frankenstein");
                    Expect(menu.headPreview.Worn == HeadStandIn.Of("frankenstein"), "its preview shows the head (the stand-in while it is unbuilt)");
                    var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Media/heads-47"));
                    Directory.CreateDirectory(dir);
                    SavePreview(menu.headPreview.GetComponent<UnityEngine.UI.RawImage>().texture as RenderTexture, Path.Combine(dir, "settings-head-preview.png"));
                    log.Add("settings: " + string.Join(" | ", menu.head.options) + "; preview Media/heads-47/settings-head-preview.png");
                    phase = 3;
                    since = now;
                    return;
                case 3:
                    if (now - since < 1.5f)
                        return;
                    flow.Execute(GuiCommandId.Next, GuiScreen.Hud);
                    first = Match.Current;
                    phase = 4;
                    since = now;
                    return;
                case 4:
                    if (Match.Current == first || !Live(loop))
                        return;
                    CheckCast(Match.Current, "frankenstein");
                    Finish();
                    return;
            }
        }

        private static void SavePreview(RenderTexture texture, string path)
        {
            Expect(texture != null, "the preview has a texture");
            UnityEngine.Object.FindAnyObjectByType<GuiHeadPreview>().RenderNow();
            var active = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            var pixels = image.GetPixels32();
            for (var i = 0; i < pixels.Length; i++)
            {
                // Over a mid-grey, so the transparent corners show
                var a = pixels[i].a / 255f;
                pixels[i] = new Color32((byte)(pixels[i].r * a + 90 * (1 - a)), (byte)(pixels[i].g * a + 110 * (1 - a)), (byte)(pixels[i].b * a + 130 * (1 - a)), 255);
            }
            image.SetPixels32(pixels);
            image.Apply();
            RenderTexture.active = active;
            File.WriteAllBytes(path, image.EncodeToPNG());
        }

        private static void CollectionAssert(string[] list, string wanted) => Expect(list.Contains(wanted), $"{wanted} is in {string.Join(",", list)}");

        private static void StringEndsWith(string s, string end) => Expect(s.EndsWith(end), $"'{s}' ends with {end}");

        private static void Finish()
        {
            if (finished)
                return;
            finished = true;
            EditorApplication.update -= Update;
            SessionState.EraseBool(ActiveKey);
            foreach (var line in log)
                Debug.Log("Heads play test: " + line);
            foreach (var error in errors)
                Debug.Log("Heads play test error: " + error);
            Debug.Log($"Heads play test ({(checking ? "reload" : "run")}): {errors.Count} errors");
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }
    }
}
