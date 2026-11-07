using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SdfBattle;
using SdfGui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SdfMedia.Editor
{
    /// <summary>
    /// Records the CrazyGames preview videos: four clips of four seconds of real battle, one each from the Fungal, Chessboard, Toybox and
    /// Kitchen arenas, each frame rendered three times from the same camera pose (landscape, portrait and square), and then stitched
    /// into <c>Media/preview-landscape.mp4</c>, <c>preview-portrait.mp4</c> and <c>preview-square.mp4</c> by
    /// <see cref="PreviewVideoEncoder"/>. Tools > SDF Media > Record Preview Videos (Regenerate Promo Videos does the same, see
    /// <see cref="PreviewPromo"/>; Encode Preview Videos only re-encodes the frames there are; the covers in Media/Covers are
    /// hand-retouched art and are never rendered again). Start it from edit mode; it opens each arena scene, enters Play mode
    /// itself, and leaves it when the clip is done, so leave the editor alone until the log says the videos are written (Tools > SDF Media >
    /// Cancel Preview Recording stops it). The scene open before is opened again at the end.
    /// <list type="number">
    /// <item><b>The match.</b> The arena's scene plays as it does on its own: 4 against 4, you and seven bots, the scene's own rules (no
    /// campaign setup). The recorder makes you play: a <see cref="Bot"/> brain of the hunting style goes on your player (your own keyboard
    /// and mouse are switched off, <see cref="Arsenal.scripted"/>), so you walk the arena, find fights and fire the way a bot does, and the
    /// picture is the real first-person view with your weapon in the corner. You take no damage (infinite life, so no take is lost to your
    /// death), the number of captures that ends the match is raised out of reach so it never ends, and the quality level is Epic,
    /// held every frame (the HUD's automatic quality change is also silenced for the run).</item>
    /// <item><b>The hidden HUD.</b> The GUI's flow is switched off and all its canvases hidden: no clock, scores, kill feed, banners, quality
    /// countdown or other text, and no mouse cursor. The pictures are rendered from the camera into render textures, which the GUI (a screen
    /// overlay) is not part of anyway; what the frames show of the HUD is drawn here: the crosshair (four arms and a dot in the HUD's cream,
    /// opening with the weapon's spread as the HUD's does, on a thin dark outline for contrast) and the hit marker (an X, red for a kill,
    /// fading as in the HUD), both read from the HUD's own <see cref="MatchView"/>, and sized like the HUD at that picture size.</item>
    /// <item><b>Fixed frame rate.</b> <see cref="Time.captureFramerate"/> is 30 from the first frame, so every frame of the game is
    /// exactly 1/30 s long whatever it cost to draw, and the clips never stutter. The editor takes as long as it needs per frame.</item>
    /// <item><b>When to roll.</b> After <see cref="WarmSeconds"/> of play (so the teams have met), <see cref="PreviewAction"/> scores
    /// the view five times a second (opponents in view within 25 m, kills, hits, hurt, firing, flag carriers, machines). Rolling starts when
    /// the score holds at or above the bar twice in a row. The bar starts at <see cref="StartBar"/> (a real fight) and sinks to
    /// <see cref="EndBar"/> over <see cref="TimeoutSeconds"/>; after that any moment you are alive starts it.</item>
    /// <item><b>Takes round a kill.</b> While rolling, every frame is rendered into Media/Video/roll, the last four seconds kept. A take
    /// is made only round a kill of yours of an opponent close by and on screen (<see cref="PreviewAction.CloseKills"/>, so the splatter is
    /// seen): <see cref="PostKillFrames"/> after it, four seconds in all, so the kill lands about 2.5 s in. Rolling stops after
    /// <see cref="QuietSeconds"/> without an opponent in view or a shot of yours, or when you die (that buffer is thrown away). Each take
    /// is scored (<see cref="TakeScore"/>: close kills first, then hits, firing, opponents in view, less for a camera that turns fast).
    /// An arena keeps going until it has <see cref="TakesPerArena"/> takes (or <see cref="ArenaSeconds"/> of searching with at least
    /// <see cref="KeptTakes"/>); its best <see cref="KeptTakes"/> are kept in Media/Video/takes/&lt;arena&gt;/take1..3 (take1 the best,
    /// a landscape take1..3.mp4 beside them to review, scores.txt with the numbers) and take1 is the arena's clip in the videos.</item>
    /// <item><b>The sniper clip.</b> The Fungal clip is a sniper headshot, the sniper given to you though the level has none: zoom in,
    /// kill, the head's pieces bursting through the scope, zoom out. The autopilot aims at the head (<see cref="Bot.aimHead"/>) with a
    /// fraction of a bot's error (<see cref="SniperAimError"/>), stands still while zoomed (<see cref="Bot.stillWhileZoomed"/>; walking
    /// makes the shot's trail look odd), zooms slowly enough to be seen (<see cref="ZoomSpeed"/>), fires only fully zoomed and dead on,
    /// and stays zoomed <see cref="ZoomLinger"/> seconds after. Its takes are built round a headshot kill on screen within
    /// <see cref="PreviewAction.HeadshotKill"/> metres instead of a close kill, <see cref="SniperPostKillFrames"/> after it, and score
    /// more for showing the whole zoom, in and out.</item>
    /// <item><b>A calm camera.</b> The autopilot turns at most <see cref="TurnLimit"/> degrees a second, eased over <see cref="AimEase"/>
    /// seconds (<see cref="Bot.turnLimit"/>, <see cref="Bot.aimEase"/>): a bot's own snap turns are hard to watch.</item>
    /// <item><b>The camera.</b> The camera in your player is rendered three times a frame with a render texture as its target and then
    /// put back: the landscape (1920x1080, 16:9) keeps the game's vertical field of view; the portrait (1080x1620, 2:3) and the square
    /// (1080x1080) cannot keep both directions, so they take a narrower horizontal field of view than the landscape's and a wider vertical
    /// one: the horizontal field of view is <see cref="Shape.hFovFactor"/> times the landscape's (0.74 for the portrait, 0.86 for the
    /// square; for a 60 degree vertical field of view the landscape is 91 degrees wide, the portrait 68 wide and 90 high, the square 79 by
    /// 79), capped at 110 degrees vertically. What matters in the action is therefore judged in the narrowest of the cuts. The
    /// pose is the same for the three, they only differ in field of view and aspect.</item>
    /// <item><b>Frames.</b> PNGs, <c>Media/Video/&lt;width&gt;x&lt;height&gt;/clipN/0000.png</c> on (120 per clip), written on worker
    /// threads. The frame folders are emptied when a recording starts.</item>
    /// </list>
    /// </summary>
    [InitializeOnLoad]
    public static class PreviewVideoRecorder
    {
        /// <summary>One of the three video shapes.</summary>
        public readonly struct Shape
        {
            public Shape(string name, int width, int height, float hFovFactor)
            {
                this.name = name;
                this.width = width;
                this.height = height;
                this.hFovFactor = hFovFactor;
            }

            /// <summary>landscape, portrait or square: the name in the output file and the cover's.</summary>
            public readonly string name;
            public readonly int width, height;
            /// <summary>The horizontal field of view as a share of the landscape's; 1 keeps the game's vertical field of view.</summary>
            public readonly float hFovFactor;

            public float Aspect => (float)width / height;

            /// <summary>The vertical field of view (degrees) this shape renders with when the landscape's is <paramref name="landscape"/>.</summary>
            public float VerticalFov(float landscape)
            {
                if (hFovFactor >= 1)
                    return landscape;
                var wide = 2 * Mathf.Atan(Mathf.Tan(landscape * Mathf.Deg2Rad / 2) * (16f / 9)) * hFovFactor;
                return Mathf.Min(MaxVerticalFov, 2 * Mathf.Atan(Mathf.Tan(wide / 2) / Aspect) * Mathf.Rad2Deg);
            }

            /// <summary>The horizontal field of view (degrees) at the vertical <paramref name="vertical"/>.</summary>
            public float HorizontalFov(float vertical) => 2 * Mathf.Atan(Mathf.Tan(vertical * Mathf.Deg2Rad / 2) * Aspect) * Mathf.Rad2Deg;
        }

        public static readonly Shape[] Shapes =
        {
            new Shape("landscape", 1920, 1080, 1),
            new Shape("portrait", 1080, 1620, 0.74f),
            new Shape("square", 1080, 1080, 0.86f),
        };

        /// <summary>The arenas, in clip order: clip N is arena N-1.</summary>
        public static readonly string[] Arenas = { "Fungal", "Chessboard", "Toybox", "Kitchen" };

        /// <summary>
        /// The weapon you hold in each arena's clip (never the single pistol), in the order of <see cref="Arenas"/>; the Fungal clip's sniper
        /// is hardcoded for the video, the level itself has none.
        /// </summary>
        public static readonly string[] Weapons = { WeaponIds.Sniper, WeaponIds.Flamethrower, WeaponIds.BlobGun, WeaponIds.DualPistols };

        public const string ScenesFolder = "Assets/Scenes/";
        public const int Fps = PreviewVideoEncoder.Fps;
        public const int ClipFrames = 4 * Fps;
        public const float MaxVerticalFov = 110;

        /// <summary>Seconds of play before the search for a clip begins.</summary>
        public const float WarmSeconds = 8;

        /// <summary>The action score a clip needs at first, and at last before anything goes.</summary>
        public const float StartBar = 8, EndBar = 2;

        /// <summary>Seconds of searching over which the bar sinks; then the next living moment is taken.</summary>
        public const float TimeoutSeconds = 100;

        /// <summary>Frames of a take after its kill: the splatter's time.</summary>
        public const int PostKillFrames = 45;

        /// <summary>Seconds without an opponent in view or a shot of yours that stop rolling.</summary>
        public const float QuietSeconds = 3;

        /// <summary>Takes an arena collects before it moves on, and how many of them, the best, are kept.</summary>
        public const int TakesPerArena = 6, KeptTakes = 3;

        /// <summary>Seconds of searching after which an arena with <see cref="KeptTakes"/> takes moves on; with one, after twice this.</summary>
        public const float ArenaSeconds = 240;

        /// <summary>Seconds of searching after which an arena without a take gives up (its clip is left out of the videos).</summary>
        public const float GiveUpSeconds = 900;

        /// <summary>The autopilot's turn: at most this many degrees a second, eased over <see cref="AimEase"/> seconds.</summary>
        public const float TurnLimit = 90, AimEase = 0.22f;

        /// <summary>The sniper clip: the share of a bot's aim error left, degrees a second of the zoom, seconds it stays zoomed after.</summary>
        public const float SniperAimError = 0.12f, ZoomSpeed = 80, ZoomLinger = 1.3f;

        /// <summary>
        /// The sniper's frames of a take after its kill: the zoom held while the head bursts (<see cref="ZoomLinger"/>), the zoom out
        /// (half a second) and a moment after; the zoom in and the aim before it get the 1.8 s left.
        /// </summary>
        public const int SniperPostKillFrames = 66;

        /// <summary>The take score's bonus for showing the sniper's whole zoom: in before the kill, out again by the end.</summary>
        public const float ZoomInBonus = 8;

        /// <summary>Frames of a take after its kill, for this arena's weapon.</summary>
        private static int PostKill => Sniping ? SniperPostKillFrames : PostKillFrames;

        /// <summary>The share of the damage you take.</summary>
        public const float YourDamage = 0.4f;

        /// <summary>The arenas whose clips keep you on the main road (the first region of the level's region map), where the view is clear.</summary>
        public static readonly string[] RoadArenas = { "Meadow", "Fungal" };

        /// <summary>How far ahead along the road you walk at a time, in metres: short enough that the way there stays on it.</summary>
        public const float RoadLead = 6;

        /// <summary>What the damage you deal is multiplied by, for more kills in the clips.</summary>
        public const float YourHits = 2;

        private const string ActiveKey = "SdfMedia.Preview.Active";
        private const string ArenaKey = "SdfMedia.Preview.Arena";
        private const string PhaseKey = "SdfMedia.Preview.Phase";
        private const string ReturnKey = "SdfMedia.Preview.Return";
        private const string ReopenKey = "SdfMedia.Preview.Reopen";
        private const string QualityKey = "SdfMedia.Preview.Quality";
        private const string HintsKey = "SdfMedia.Preview.Hints";
        private const string GoreKey = "SdfMedia.Preview.Gore";
        private const string CaptureKey = "SdfMedia.Preview.Capture";

        private enum Stage { Warm, Search, Record }

        private static bool active;
        private static bool configured;
        private static bool leaving;
        private static float playStarted = -1;
        private static int lastFrame = -1;
        private static bool timeReleased;
        private static Stage stage;
        private static float sceneStart;
        private static float searchStart;
        private static float nextSample;
        private static int streak;
        private static int frame;
        private static int aborts;

        // The rolling buffer: frames rollStart..frame-1 are on disk; per frame what happened in it
        private struct FrameNote
        {
            public bool firing, hit, closeKill, zoomed;
            public int opponents;
            public float turn;
        }

        private sealed class Take
        {
            public string folder;
            public float score;
            public int closeKills, hits;
            public float firing, opponents, turn;
            public bool zoomIn;
        }

        private static int rollStart;
        private static readonly List<FrameNote> notes = new List<FrameNote>();
        private static int killFrame = -1;
        private static int lastFrameWithAction;
        private static int closeKillsBefore, hitsBefore;
        private static Vector2 lastAim;
        private static readonly List<Take> takes = new List<Take>();
        private static bool immortal;
        private static float landscapeFov;
        private static Bot autopilot;

        /// <summary>Whether this arena's clip is the sniper's: the take is built round a headshot.</summary>
        private static bool Sniping => Weapons[SessionState.GetInt(ArenaKey, 0)] == WeaponIds.Sniper;

        /// <summary>The kills a take is built round: close headshots for the sniper, close kills for the rest.</summary>
        private static int TakeKills => Sniping ? action.CloseHeadshots : action.CloseKills;

        private static Match match;
        private static Player you;
        private static Camera view;
        private static PreviewAction action;
        private static readonly RenderTexture[] targets = new RenderTexture[Shapes.Length];
        private static readonly Texture2D[] readers = new Texture2D[Shapes.Length];
        private static readonly List<Task> writing = new List<Task>();
        private static Material overlay;
        private static readonly List<Vector3> road = new List<Vector3>();

        /// <summary>Whether a recording is under way (across the domain reloads of entering and leaving Play mode).</summary>
        public static bool Active => active;

        static PreviewVideoRecorder()
        {
            active = SessionState.GetBool(ActiveKey, false);
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        // ---------------------------------------------------------------- Menu

        [MenuItem("Tools/SDF Media/Record Preview Videos")]
        public static void Record() => Start();

        /// <summary>
        /// Records the videos in a batch editor with the GPU and exits it (0 when the videos are written, 1 on the first error):
        /// <c>SDF_NO_QUIT=1 tools/unity-batch.sh previews -executeMethod SdfMedia.Editor.PreviewVideoRecorder.RecordInBatch</c> (no -quit, no -nographics).
        /// </summary>
        public static void RecordInBatch()
        {
            if (!Start())
                EditorApplication.Exit(1);
        }

        private static void ExitIfBatch(bool ok)
        {
            if (Application.isBatchMode)
                EditorApplication.Exit(ok ? 0 : 1);
        }

        [MenuItem("Tools/SDF Media/Cancel Preview Recording")]
        public static void CancelFromMenu()
        {
            if (active)
                End("Cancelled from the menu.");
        }

        /// <summary>Starts a recording from edit mode; false when it could not (already running, playing, scenes unsaved).</summary>
        public static bool Start()
        {
            if (active)
            {
                Debug.LogWarning("SDF Media: a preview recording is already running (Tools > SDF Media > Cancel Preview Recording).");
                return false;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("Record Preview Videos", "Stop Play mode and wait for the compile to finish first.", "OK");
                return false;
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            var videos = Path.Combine(PreviewVideoEncoder.MediaFolder, "Video");
            foreach (var shape in Shapes)
            {
                var folder = Path.Combine(videos, $"{shape.width}x{shape.height}");
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
            // The takes and the rolling buffer of the last run are thrown away (a locked clip's arena keeps its takes)
            foreach (var old in new[] { "roll" }.Concat(Arenas.Where((_, i) => !Locked(i + 1)).Select(a => Path.Combine("takes", a))))
            {
                var folder = Path.Combine(videos, old);
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
            // The locked clips' frames go in as they are; their arenas are not played again
            for (var clip = 1; clip <= Arenas.Length; clip++)
                if (Locked(clip))
                {
                    foreach (var shape in Shapes)
                    {
                        var folder = PreviewVideoEncoder.ClipFolder(shape, clip);
                        Directory.CreateDirectory(folder);
                        foreach (var file in Directory.GetFiles(LockedFolder(shape, clip), "*.png"))
                            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
                    }
                    Debug.Log($"SDF Media: clip {clip} ({Arenas[clip - 1]}) is locked; its frames are taken from {Path.Combine(videos, "locked")}.");
                }
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetInt(ArenaKey, 0);
            SessionState.SetInt(PhaseKey, 0);
            SessionState.SetString(ReturnKey, ActiveScenePath());
            SessionState.SetString(ReopenKey, "");
            SessionState.SetInt(QualityKey, QualitySettings.GetQualityLevel());
            SessionState.SetInt(HintsKey, PlayerPrefs.HasKey(HintsPref) ? PlayerPrefs.GetInt(HintsPref) : -1);
            SessionState.SetInt(GoreKey, GuiSettings.Gore);
            active = true;
            Debug.Log("SDF Media: recording the preview videos; the editor opens each arena and plays it by itself.");
            return true;
        }

        private const string HintsPref = "gui.qualityHints.dismissed";

        /// <summary>
        /// Where a locked clip's frames are kept: Media/Video/locked/&lt;size&gt;/clipN, a clip copied there whole for every shape. A locked
        /// clip is not recorded again; delete its folder to record it anew. It may be cut shorter than four seconds by hand (at least
        /// <see cref="LockedMinFrames"/>): the Fungal clip is the sniper take from 0.5 s to 3.5 s, 90 frames.
        /// </summary>
        public static string LockedFolder(Shape shape, int clip) =>
            Path.Combine(PreviewVideoEncoder.MediaFolder, "Video", "locked", $"{shape.width}x{shape.height}", "clip" + clip);

        private static bool Locked(int clip) =>
            Shapes.All(shape => Directory.Exists(LockedFolder(shape, clip)) && Directory.GetFiles(LockedFolder(shape, clip), "*.png").Length >= LockedMinFrames);

        /// <summary>The fewest frames a locked clip may have: one cut shorter by hand (the Fungal sniper clip, 3 s) still counts.</summary>
        public const int LockedMinFrames = 3 * Fps;

        private static string ActiveScenePath() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;

        // ---------------------------------------------------------------- Edit mode: one arena after the other

        private static void Update()
        {
            if (!active)
                return;
            try
            {
                if (EditorApplication.isPlaying)
                    Play();
                else
                    Edit();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                End("Stopped by an error: " + exception.Message);
            }
        }

        private static void Edit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (SessionState.GetInt(PhaseKey, 0) == 1)
            {
                End("Play mode ended before the clip was done.");
                return;
            }
            var arena = SessionState.GetInt(ArenaKey, 0);
            while (arena < Arenas.Length && Locked(arena + 1))
                arena++;
            SessionState.SetInt(ArenaKey, arena);
            if (arena >= Arenas.Length)
            {
                Finish();
                return;
            }
            var path = ScenesFolder + Arenas[arena] + ".unity";
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, path)))
            {
                End($"No scene {path}.");
                return;
            }
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            SessionState.SetInt(PhaseKey, 1);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                if (active || SessionState.GetBool(CaptureKey, false))
                    Time.captureFramerate = 0;
                SessionState.SetBool(CaptureKey, false);
                ReleaseTargets();
            }
            else if (change == PlayModeStateChange.EnteredPlayMode)
            {
                configured = false;
                leaving = false;
                playStarted = -1;
                lastFrame = -1;
                stage = Stage.Warm;
                streak = 0;
                aborts = 0;
                takes.Clear();
                autopilot = null;
                // Infinite life from the start: no take is lost to your death, so an arena is done sooner
                immortal = true;
                timeReleased = false;
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                var reopen = SessionState.GetString(ReopenKey, "");
                if (!string.IsNullOrEmpty(reopen))
                {
                    SessionState.SetString(ReopenKey, "");
                    EditorApplication.delayCall += () => EditorSceneManager.OpenScene(reopen, OpenSceneMode.Single);
                }
            }
        }

        // ---------------------------------------------------------------- Play mode: find the action, record it

        private static void Play()
        {
            if (leaving)
                return;
            if (playStarted < 0)
                playStarted = Time.realtimeSinceStartup;
            match = Match.Current;
            if (match == null || match.State == null || !match.State.Started || match.You == null || !match.You.IsYou || Combat.Current == null || Bots.Current == null)
            {
                if (Time.realtimeSinceStartup - playStarted > 90)
                    End("The arena scene never started a match (is it a battle scene?).");
                return;
            }
            if (!configured)
            {
                Configure();
                if (!active)
                    return;
            }
            // One step per game frame: the editor may tick more often than the player loop runs
            if (Time.frameCount == lastFrame)
                return;
            lastFrame = Time.frameCount;
            KeepEpic();
            KeepTime();
            KeepWeapon();
            // Zoomed in, the turn slows with the view, so it looks as calm on screen
            if (autopilot != null && landscapeFov > 0)
                autopilot.turnLimit = TurnLimit * Mathf.Clamp01(view.fieldOfView / landscapeFov);
            if (Time.captureFramerate != Fps)
                Time.captureFramerate = Fps;

            switch (stage)
            {
                case Stage.Warm:
                    if (Time.time - sceneStart >= WarmSeconds && you.State.Alive)
                    {
                        stage = Stage.Search;
                        if (searchStart <= 0)
                            searchStart = Time.time;
                    }
                    break;
                case Stage.Search:
                    Search();
                    break;
                case Stage.Record:
                    RecordFrame();
                    break;
            }
        }

        private static void Configure()
        {
            configured = true;
            you = match.You;
            view = you.GetComponentInChildren<Camera>();
            var arsenal = Combat.Current.ArsenalOf(you);
            if (view == null || arsenal == null)
            {
                End("The arena's player has no camera or no arsenal.");
                return;
            }
            var names = QualitySettings.names;
            if (Array.IndexOf(names, "Epic") < 0)
            {
                End("There is no quality level named Epic.");
                return;
            }
            sceneStart = Time.time;
            searchStart = 0;
            nextSample = 0;
            Time.captureFramerate = Fps;
            SessionState.SetBool(CaptureKey, true);
            Cursor.visible = false;
            PlayerPrefs.SetInt(HintsPref, 1);
            GuiSettings.Gore = GuiSettings.GoreLevels.Length - 1;
            KeepEpic();

            // No HUD: the flow (pause, end sequence, quality countdown) off and every canvas hidden; the views still run, the MatchView still fills
            if (match.Hud != null)
            {
                var flow = match.Hud.GetComponent<GuiFlow>();
                if (flow != null)
                    flow.enabled = false;
                foreach (var canvas in match.Hud.GetComponentsInChildren<Canvas>(true))
                    canvas.enabled = false;
            }

            // A match that never ends, and you who survive more often than not and hit twice as hard (the other damage rules stay)
            match.State.Rules.capturesToWin = 999;
            var combat = Combat.Current.State;
            var previous = combat.DamageTakenOf;
            var yourState = you.State;
            combat.DamageTakenOf = victim => (previous != null ? previous(victim) : 1f) * (victim == yourState ? (immortal ? 0 : YourDamage) : 1f);
            combat.DamageDealtOf = attacker => attacker == yourState ? YourHits : 1f;

            // The autopilot: a hunter's brain on you, the input off
            you.Controller.readsInput = false;
            arsenal.scripted = true;
            var random = new System.Random(2025 + SessionState.GetInt(ArenaKey, 0));
            var bot = you.gameObject.AddComponent<Bot>();
            bot.keepWeapon = true;
            bot.turnLimit = TurnLimit;
            bot.aimEase = AimEase;
            autopilot = bot;
            if (Sniping)
            {
                bot.aimHead = true;
                bot.aimErrorScale = SniperAimError;
                bot.zoomLinger = ZoomLinger;
                bot.stillWhileZoomed = true;
                arsenal.zoomSpeed = ZoomSpeed;
            }
            LoadRoad(Arenas[SessionState.GetInt(ArenaKey, 0)]);
            if (road.Count > 1)
                bot.route = AlongRoad;
            bot.Bind(Bots.Current, you, BotStyle.Hunter, Temperament.Varied(BotStyle.Hunter, random), 7 + SessionState.GetInt(ArenaKey, 0));

            landscapeFov = view.fieldOfView;
            // The part of the picture all three shapes show, well inside: the narrowest horizontal cut, the shortest vertical one
            var halfH = Shapes.Min(s => s.HorizontalFov(s.VerticalFov(landscapeFov))) / 2 * 0.85f;
            var halfV = landscapeFov / 2 * 0.85f;
            action = new PreviewAction();
            action.Attach(match, view, halfH, halfV);
            Debug.Log($"SDF Media: {Arenas[SessionState.GetInt(ArenaKey, 0)]}: autopilot on, field of view {landscapeFov:0.#} (portrait {Shapes[1].VerticalFov(landscapeFov):0.#}, square {Shapes[2].VerticalFov(landscapeFov):0.#}), looking for action");
        }

        // The game's time keeps going: a pause or a loading hold (time scale 0, the sound paused) would leave the clip a still picture
        private static void KeepTime()
        {
            if (Time.timeScale > 0 && !AudioListener.pause)
                return;
            if (!timeReleased)
                Debug.LogWarning($"SDF Media: the game's time was held (time scale {Time.timeScale}, sound paused {AudioListener.pause}); letting it run.");
            timeReleased = true;
            Time.timeScale = 1;
            AudioListener.pause = false;
        }

        // The main road's middle, along Unity's x upwards, a point a metre: the deepest texel of each column of the region map's first channel
        // (a signed distance, negative inside; the map in the pipeline's coordinates, x mirrored in Unity's); the road runs along x, one
        // stretch a column
        private static void LoadRoad(string arena)
        {
            road.Clear();
            if (!RoadArenas.Contains(arena))
                return;
            var folder = Path.Combine(Application.dataPath, "Level", "Generated", arena.ToLowerInvariant());
            var regions = JObject.Parse(File.ReadAllText(Path.Combine(folder, "level.json")))["regions"];
            var texel = (float)regions["texelSize"];
            var range = (float)regions["range"];
            var minX = (float)regions["min"][0];
            var maxZ = (float)regions["max"][1];
            var map = new Texture2D(2, 2);
            try
            {
                if (!map.LoadImage(File.ReadAllBytes(Path.Combine(folder, (string)regions["texture"]))))
                    return;
                var pixels = map.GetPixels32();
                var step = Mathf.Max(1, Mathf.RoundToInt(1 / texel));
                for (var x = 0; x < map.width; x += step)
                {
                    var best = 0f;
                    var bestRow = -1;
                    for (var y = 0; y < map.height; y++)
                    {
                        var d = (pixels[y * map.width + x].r / 255f * 2 - 1) * range;
                        if (d < best)
                        {
                            best = d;
                            bestRow = y;
                        }
                    }
                    // Rows bottom up here, from the highest z down in the file
                    if (bestRow >= 0)
                        road.Add(new Vector3(-(minX + (x + 0.5f) * texel), 0, maxZ - (map.height - 1 - bestRow + 0.5f) * texel));
                }
                road.Reverse();
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
            Debug.Log(road.Count > 1
                ? $"SDF Media: {arena}: keeping to the main road, x {road[0].x:0} to {road[^1].x:0}"
                : $"SDF Media: {arena}: no main road in the region map; walking anywhere");
        }

        // Where the autopilot walks: up to RoadLead metres on along the road from where you are, towards the goal's place along it
        private static Vector3 AlongRoad(Vector3 feet, Vector3 goal)
        {
            var spacing = (road[^1].x - road[0].x) / (road.Count - 1);
            int Index(float x) => Mathf.Clamp(Mathf.RoundToInt((x - road[0].x) / spacing), 0, road.Count - 1);
            var here = Index(feet.x);
            var lead = Mathf.Max(1, Mathf.RoundToInt(RoadLead / spacing));
            var point = road[here + Mathf.Clamp(Index(goal.x) - here, -lead, lead)];
            return new Vector3(point.x, feet.y, point.z);
        }

        // The clip's weapon stays in hand: every life starts with the single pistol alone, and the autopilot would switch by distance
        private static void KeepWeapon()
        {
            var arsenal = Combat.Current.ArsenalOf(you);
            var inventory = arsenal != null ? arsenal.Inventory : null;
            if (inventory == null)
                return;
            var id = Weapons[SessionState.GetInt(ArenaKey, 0)];
            if (!inventory.Holds(id))
                inventory.Take(WeaponIds.ModelOf(id));
            arsenal.Select(WeaponIds.SlotOf(id).Value);
        }

        // The quality level stays Epic: the HUD and the start-up measurement would change it
        private static void KeepEpic()
        {
            var epic = Array.IndexOf(QualitySettings.names, "Epic");
            if (epic >= 0 && QualitySettings.GetQualityLevel() != epic)
                QualitySettings.SetQualityLevel(epic, true);
        }

        private static void Search()
        {
            if (Time.time < nextSample)
                return;
            nextSample = Time.time + 0.2f;
            var alive = you.State.Alive;
            var score = action.Score();
            var elapsed = Time.time - searchStart;
            var bar = Mathf.Lerp(StartBar, EndBar, Mathf.Clamp01(elapsed / TimeoutSeconds));
            var due = elapsed > TimeoutSeconds;
            streak = alive && score >= bar ? streak + 1 : 0;
            if (!alive || (streak < 2 && !due))
                return;
            var arena = Arenas[SessionState.GetInt(ArenaKey, 0)];
            Debug.Log($"SDF Media: {arena}: rolling at {Time.time:0.0} s of play: action {score:0.0} (bar {bar:0.0}{(due ? ", timed out" : "")}), {action.OpponentsInView} opponents in view");
            ClearRoll();
            foreach (var shape in Shapes)
                Directory.CreateDirectory(RollFolder(shape));
            frame = 0;
            rollStart = 0;
            notes.Clear();
            killFrame = -1;
            lastFrameWithAction = 0;
            closeKillsBefore = TakeKills;
            hitsBefore = action.YourHits;
            lastAim = new Vector2(view.transform.eulerAngles.y, view.transform.eulerAngles.x);
            stage = Stage.Record;
        }

        /// <summary>Media/Video/roll/&lt;size&gt;: the rolling buffer's frames, named by their frame number.</summary>
        private static string RollFolder(Shape shape) =>
            Path.Combine(PreviewVideoEncoder.MediaFolder, "Video", "roll", $"{shape.width}x{shape.height}");

        /// <summary>Media/Video/takes/&lt;arena&gt;: an arena's kept takes.</summary>
        public static string TakesFolder(string arena) => Path.Combine(PreviewVideoEncoder.MediaFolder, "Video", "takes", arena);

        private static void ClearRoll()
        {
            WaitForWrites();
            var roll = Path.Combine(PreviewVideoEncoder.MediaFolder, "Video", "roll");
            if (Directory.Exists(roll))
                Directory.Delete(roll, true);
        }

        private static void RecordFrame()
        {
            if (!you.State.Alive)
            {
                AbortRoll();
                return;
            }
            CaptureFrame();

            // What happened in this frame
            var aim = new Vector2(view.transform.eulerAngles.y, view.transform.eulerAngles.x);
            var turn = Mathf.Sqrt(Mathf.Pow(Mathf.DeltaAngle(lastAim.x, aim.x), 2) + Mathf.Pow(Mathf.DeltaAngle(lastAim.y, aim.y), 2));
            lastAim = aim;
            action.Score();
            var note = new FrameNote
            {
                firing = action.Firing,
                hit = action.YourHits > hitsBefore,
                closeKill = TakeKills > closeKillsBefore,
                zoomed = view.fieldOfView < landscapeFov - 1,
                opponents = action.OpponentsInView,
                turn = turn,
            };
            hitsBefore = action.YourHits;
            closeKillsBefore = TakeKills;
            notes.Add(note);
            if (note.closeKill && killFrame < 0)
            {
                killFrame = frame;
                Debug.Log($"SDF Media: a close {(Sniping ? "headshot " : "")}kill at frame {frame} of the roll.");
            }
            if (note.firing || note.hit || note.opponents > 0)
                lastFrameWithAction = frame;
            frame++;

            // The buffer keeps four seconds, unless a take is waiting for its end
            var keepFrom = killFrame >= 0 ? Mathf.Min(frame - ClipFrames, killFrame + PostKill - ClipFrames) : frame - ClipFrames;
            for (var old = rollStart; old < keepFrom; old++)
                foreach (var shape in Shapes)
                    File.Delete(Path.Combine(RollFolder(shape), old.ToString("00000") + ".png"));
            rollStart = Mathf.Max(rollStart, keepFrom);

            if (killFrame >= 0)
            {
                var takeEnd = Mathf.Max(killFrame + PostKill, rollStart + ClipFrames);
                if (frame >= takeEnd)
                    MakeTake(takeEnd - ClipFrames);
            }
            else if ((frame - lastFrameWithAction) > QuietSeconds * Fps)
            {
                Debug.Log("SDF Media: the fight went quiet; rolling stops.");
                ClearRoll();
                streak = 0;
                stage = Stage.Search;
            }
            if (stage != Stage.Record)
                NextOrMore();
        }

        // Frames start..start+ClipFrames-1 of the roll become a take; the roll goes on from here with an empty buffer
        private static void MakeTake(int start)
        {
            WaitForWrites();
            var arena = Arenas[SessionState.GetInt(ArenaKey, 0)];
            var folder = Path.Combine(TakesFolder(arena), "new" + (takes.Count + 1));
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            foreach (var shape in Shapes)
            {
                var target = Path.Combine(folder, $"{shape.width}x{shape.height}");
                Directory.CreateDirectory(target);
                for (var i = 0; i < ClipFrames; i++)
                    File.Move(Path.Combine(RollFolder(shape), (start + i).ToString("00000") + ".png"), Path.Combine(target, i.ToString("0000") + ".png"));
            }
            var window = notes.GetRange(start, ClipFrames);
            var take = new Take
            {
                folder = folder,
                closeKills = window.Count(n => n.closeKill),
                hits = window.Count(n => n.hit),
                firing = window.Count(n => n.firing) / (float)Fps,
                opponents = (float)window.Average(n => n.opponents),
                turn = window.Average(n => n.turn) * Fps,
                zoomIn = ZoomsIn(window),
            };
            take.score = TakeScore(take.closeKills, take.hits, take.firing, take.opponents, take.turn) + (take.zoomIn ? ZoomInBonus : 0);
            takes.Add(take);
            Debug.Log($"SDF Media: {arena}: take {takes.Count}: score {take.score:0.0} ({Describe(take)}).");

            // Go on rolling after it: the frames from here on only
            for (var old = rollStart; old < frame; old++)
                foreach (var shape in Shapes)
                    File.Delete(Path.Combine(RollFolder(shape), old.ToString("00000") + ".png"));
            rollStart = frame;
            killFrame = -1;
            lastFrameWithAction = frame;
            if (Enough())
            {
                ClearRoll();
                stage = Stage.Search;
            }
        }

        // Whether the take shows the whole zoom: unzoomed at first, zoomed in at the kill, out again at the end
        private static bool ZoomsIn(List<FrameNote> window)
        {
            var kill = window.FindIndex(n => n.closeKill);
            return kill > 0 && !window[0].zoomed && window[kill].zoomed && !window[^1].zoomed;
        }

        private static string Describe(Take take) =>
            $"{take.closeKills} close {(Sniping ? "headshot kills" : "kills")}, {take.hits} hits, {take.firing:0.0} s firing, {take.opponents:0.0} opponents in view, turning {take.turn:0} deg/s{(take.zoomIn ? ", zooms in and out" : "")}";

        /// <summary>
        /// A take's score: 10 a close kill, half a point a hit (at most 6), a point a second of firing, two an opponent in view on average,
        /// less a point for every 30 degrees a second the camera turns on average.
        /// </summary>
        public static float TakeScore(int closeKills, int hits, float firingSeconds, float opponents, float turnDegreesPerSecond) =>
            10 * closeKills + Mathf.Min(6, 0.5f * hits) + firingSeconds + 2 * opponents - turnDegreesPerSecond / 30;

        private static bool Enough()
        {
            var searched = Time.time - searchStart;
            return takes.Count >= TakesPerArena
                || (takes.Count >= KeptTakes && searched > ArenaSeconds)
                || (takes.Count > 0 && searched > 2 * ArenaSeconds);
        }

        // Out of rolling: the arena is done when it has enough takes (or gives up), else the search goes on
        private static void NextOrMore()
        {
            var searched = Time.time - searchStart;
            if (!Enough() && !(takes.Count == 0 && searched > GiveUpSeconds))
                return;
            var clip = SessionState.GetInt(ArenaKey, 0) + 1;
            var arena = Arenas[clip - 1];
            if (takes.Count == 0)
                Debug.LogWarning($"SDF Media: {arena}: no take with a close {(Sniping ? "headshot " : "")}kill in {GiveUpSeconds} s; its clip is left out.");
            else
                KeepBest(clip, arena);
            action.Detach();
            SessionState.SetInt(ArenaKey, clip);
            SessionState.SetInt(PhaseKey, 0);
            leaving = true;
            Time.captureFramerate = 0;
            ReleaseTargets();
            EditorApplication.isPlaying = false;
        }

        // The best takes become take1..KeptTakes (the rest are deleted) and take1 is the arena's clip
        private static void KeepBest(int clip, string arena)
        {
            WaitForWrites();
            var ranked = takes.OrderByDescending(t => t.score).ToList();
            var folder = TakesFolder(arena);
            foreach (var old in Directory.GetDirectories(folder, "take*"))
                Directory.Delete(old, true);
            foreach (var old in Directory.GetFiles(folder, "take*.mp4"))
                File.Delete(old);
            var lines = new List<string>();
            for (var i = 0; i < ranked.Count; i++)
            {
                var take = ranked[i];
                if (i >= KeptTakes)
                {
                    Directory.Delete(take.folder, true);
                    continue;
                }
                var kept = Path.Combine(folder, "take" + (i + 1));
                Directory.Move(take.folder, kept);
                take.folder = kept;
                lines.Add($"take{i + 1}: score {take.score:0.0}, {Describe(take)}");
            }
            File.WriteAllLines(Path.Combine(folder, "scores.txt"), lines);
            foreach (var shape in Shapes)
            {
                var target = PreviewVideoEncoder.ClipFolder(shape, clip);
                if (Directory.Exists(target))
                    Directory.Delete(target, true);
                Directory.CreateDirectory(target);
                foreach (var file in Directory.GetFiles(Path.Combine(ranked[0].folder, $"{shape.width}x{shape.height}"), "*.png"))
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            }
            Debug.Log($"SDF Media: {arena}: kept the best {Mathf.Min(KeptTakes, ranked.Count)} of {ranked.Count} takes; clip {clip} is take1 (score {ranked[0].score:0.0}).");
        }

        // You died while rolling: the death camera pulls back, so the buffer is thrown away and the search goes on
        private static void AbortRoll()
        {
            ClearRoll();
            aborts++;
            // Twice is enough: from then on nothing can hurt you
            if (aborts >= 2)
                immortal = true;
            streak = 0;
            stage = Stage.Warm;
            sceneStart = Time.time - WarmSeconds;
            Debug.Log($"SDF Media: you died while rolling, at frame {frame}; trying again{(immortal ? " (you cannot be hurt now)" : "")}.");
            NextOrMore();
        }

        // ---------------------------------------------------------------- Rendering

        private static void CaptureFrame()
        {
            var original = view.fieldOfView;
            var previousActive = RenderTexture.active;
            var hud = match.Hud != null ? match.Hud.view : null;
            var spread = hud != null ? hud.spread : 0;
            var hit = hud != null ? hud.hitMarker : 0;
            var kill = hud != null && hud.hitMarkerKill;
            try
            {
                for (var i = 0; i < Shapes.Length; i++)
                {
                    var shape = Shapes[i];
                    if (targets[i] == null)
                    {
                        // URP leaves render scale out when drawing into a texture, so the edges are smoothed here: the quality level's MSAA,
                        // but no more than the GPU allows (Apple GPUs stop at 4; asking for Epic's 8 leaves the picture black)
                        var descriptor = new RenderTextureDescriptor(shape.width, shape.height, RenderTextureFormat.ARGB32, 24) { sRGB = true };
                        var asked = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset != null
                            ? Mathf.Max(1, UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset.msaaSampleCount) : 1;
                        descriptor.msaaSamples = Mathf.Clamp(SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor), 1, asked);
                        targets[i] = new RenderTexture(descriptor) { name = "Preview " + shape.name };
                        readers[i] = new Texture2D(shape.width, shape.height, TextureFormat.RGB24, false);
                    }
                    view.targetTexture = targets[i];
                    view.fieldOfView = shape.VerticalFov(original);
                    view.Render();
                    DrawCrosshair(targets[i], spread, hit, kill);
                    RenderTexture.active = targets[i];
                    readers[i].ReadPixels(new Rect(0, 0, shape.width, shape.height), 0, 0, false);
                    var bytes = readers[i].EncodeToPNG();
                    var path = Path.Combine(RollFolder(shape), frame.ToString("00000") + ".png");
                    writing.Add(Task.Run(() => File.WriteAllBytes(path, bytes)));
                }
            }
            finally
            {
                view.targetTexture = null;
                view.fieldOfView = original;
                RenderTexture.active = previousActive;
            }
            if (writing.Count >= 18)
                WaitForWrites();
        }

        private static void WaitForWrites()
        {
            try
            {
                Task.WaitAll(writing.ToArray());
            }
            catch (AggregateException exception)
            {
                Debug.LogError("SDF Media: writing a frame failed: " + exception.InnerException?.Message);
            }
            writing.Clear();
        }

        private static void ReleaseTargets()
        {
            for (var i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].Release();
                    Object.DestroyImmediate(targets[i]);
                    targets[i] = null;
                }
                if (readers[i] != null)
                {
                    Object.DestroyImmediate(readers[i]);
                    readers[i] = null;
                }
            }
            if (overlay != null)
            {
                Object.DestroyImmediate(overlay);
                overlay = null;
            }
        }

        /// <summary>
        /// The HUD's crosshair and hit marker, drawn into a finished picture: four arms 8x22 pixels (at the HUD's 1920x1080 reference size)
        /// whose middles are 12 + 26 x spread pixels from the centre, a dot of 8, in cream with a dark outline of 2; the hit marker an X of
        /// 48 pixels, red for a kill, fading with <paramref name="hit"/> and swelling by 35 % as the HUD's does. The HUD scales with the
        /// square root of width x height against 1920x1080 (its canvas matches width and height half and half); so does this.
        /// </summary>
        private static void DrawCrosshair(RenderTexture target, float spread, float hit, bool kill)
        {
            if (overlay == null)
            {
                overlay = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                overlay.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                overlay.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                overlay.SetInt("_Cull", (int)CullMode.Off);
                overlay.SetInt("_ZWrite", 0);
                overlay.SetInt("_ZTest", (int)CompareFunction.Always);
            }
            var scale = Mathf.Sqrt(target.width / 1920f * (target.height / 1080f));
            var cx = target.width / 2f;
            var cy = target.height / 2f;
            var cream = GuiPalette.Cream.linear;
            var outline = new Color(GuiPalette.Ink.r, GuiPalette.Ink.g, GuiPalette.Ink.b, 0.55f).linear;
            outline.a = 0.55f;
            var gap = 12 + 26 * Mathf.Clamp01(spread);

            RenderTexture.active = target;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, target.width, 0, target.height);
            overlay.SetPass(0);
            GL.Begin(GL.QUADS);
            void Box(float x, float y, float halfWidth, float halfHeight, Color color)
            {
                GL.Color(color);
                GL.Vertex3(x - halfWidth, y - halfHeight, 0);
                GL.Vertex3(x + halfWidth, y - halfHeight, 0);
                GL.Vertex3(x + halfWidth, y + halfHeight, 0);
                GL.Vertex3(x - halfWidth, y + halfHeight, 0);
            }
            void Bar(float x, float y, float length, float halfWidth, float degrees, Color color)
            {
                var along = new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * length;
                var across = new Vector2(-along.y, along.x).normalized * halfWidth;
                GL.Color(color);
                GL.Vertex3(x - along.x - across.x, y - along.y - across.y, 0);
                GL.Vertex3(x + along.x - across.x, y + along.y - across.y, 0);
                GL.Vertex3(x + along.x + across.x, y + along.y + across.y, 0);
                GL.Vertex3(x - along.x + across.x, y - along.y + across.y, 0);
            }
            // Outline pass, then the cream
            for (var pass = 0; pass < 2; pass++)
            {
                var grow = pass == 0 ? 2f : 0f;
                var color = pass == 0 ? outline : cream;
                for (var arm = 0; arm < 4; arm++)
                {
                    var vertical = arm % 2 == 0;
                    var direction = arm switch { 0 => Vector2.up, 1 => Vector2.right, 2 => Vector2.down, _ => Vector2.left };
                    var at = new Vector2(cx, cy) + direction * (gap * scale);
                    Box(at.x, at.y, ((vertical ? 4 : 11) + grow) * scale, ((vertical ? 11 : 4) + grow) * scale, color);
                }
                Box(cx, cy, (4 + grow) * scale, (4 + grow) * scale, color);
            }
            if (hit > 0.01f)
            {
                var marker = kill ? GuiPalette.Red.linear : cream;
                marker.a = Mathf.Clamp01(hit);
                var size = scale * (1 + 0.35f * hit);
                for (var cross = 0; cross < 2; cross++)
                    Bar(cx, cy, 24 * size, 3 * size, 45 + 90 * cross, marker);
            }
            GL.End();
            GL.PopMatrix();
        }

        // ---------------------------------------------------------------- The end

        // All four clips done, in edit mode: the videos
        private static void Finish()
        {
            var back = SessionState.GetString(ReturnKey, "");
            Deactivate();
            Debug.Log("SDF Media: all clips recorded; encoding the videos with ffmpeg.");
            var ok = PreviewVideoEncoder.EncodeAll();
            // Each arena's kept takes as landscape videos, to review them
            foreach (var arena in Arenas)
                for (var take = 1; take <= KeptTakes; take++)
                {
                    var frames = Path.Combine(TakesFolder(arena), "take" + take, $"{Shapes[0].width}x{Shapes[0].height}");
                    if (Directory.Exists(frames))
                        ok &= PreviewVideoEncoder.EncodeTake(frames, Path.Combine(TakesFolder(arena), $"take{take}.mp4"));
                }
            Reopen(back);
            Debug.Log(ok
                ? $"SDF Media: preview videos written to {PreviewVideoEncoder.MediaFolder} (preview-landscape.mp4, preview-portrait.mp4, preview-square.mp4)."
                : "SDF Media: encoding failed, see the errors above; the frames are still in Media/Video.");
            PreviewPromo.RecorderFinished(ok);
            ExitIfBatch(ok);
        }

        /// <summary>Stops the recording where it is (Play mode left, settings put back, the scene from before opened again), with a message.</summary>
        private static void End(string message)
        {
            Debug.LogWarning("SDF Media: preview recording stopped. " + message);
            var back = SessionState.GetString(ReturnKey, "");
            var playing = EditorApplication.isPlaying;
            if (action != null)
                action.Detach();
            WaitForWrites();
            Deactivate();
            leaving = true;
            if (playing)
            {
                SessionState.SetString(ReopenKey, back);
                Time.captureFramerate = 0;
                EditorApplication.isPlaying = false;
            }
            else
                Reopen(back);
            PreviewPromo.RecorderFinished(false);
            ExitIfBatch(false);
        }

        private static void Deactivate()
        {
            active = false;
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetInt(PhaseKey, 0);
            var quality = SessionState.GetInt(QualityKey, -1);
            if (quality >= 0 && quality < QualitySettings.names.Length)
                QualitySettings.SetQualityLevel(quality, true);
            var hints = SessionState.GetInt(HintsKey, -1);
            if (hints < 0)
                PlayerPrefs.DeleteKey(HintsPref);
            else
                PlayerPrefs.SetInt(HintsPref, hints);
            GuiSettings.Gore = SessionState.GetInt(GoreKey, GuiSettings.Gore);
            PlayerPrefs.Save();
            ReleaseTargets();
        }

        private static void Reopen(string scenePath)
        {
            if (!string.IsNullOrEmpty(scenePath) && File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, scenePath)))
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }
    }
}
