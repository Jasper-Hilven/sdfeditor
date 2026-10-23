using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SdfStory
{
    /// <summary>
    /// The comic viewer (tickets 31.8, 34): a comic's panels full screen, one at a time, letterboxed to 16:9, with music, Next and Skip.
    /// <see cref="Begin"/> (the intro) and <see cref="Play"/> (any comic by name) are the only ways in, and <c>onFinished</c> the only way
    /// out: when the player plays on or skips it runs, and the panels are let go. The intro's twelve panels are held by its prefab
    /// (Resources/StoryIntro); every other comic, such as the story beat <c>beat-fungal</c>, is a folder of panels,
    /// Resources/Comics/&lt;comic&gt;/panel-01 ..., shown in the same screen without panels of its own (Resources/StoryViewer), so a comic is
    /// data only: rendered panels in its folder. Nothing is loaded until a comic starts. Both prefabs are built by Tools > SDF Story >
    /// Build Intro Viewer.
    /// </summary>
    public sealed class StoryIntro : MonoBehaviour
    {
        public const string ResourceName = "StoryIntro";
        /// <summary>The same screen with no panels, for every comic but the intro.</summary>
        public const string ViewerResourceName = "StoryViewer";
        /// <summary>The Resources folder of the comics' panel folders.</summary>
        public const string ComicResources = "Comics";
        public const string Intro = "intro";
        /// <summary>A comic has at most this many panels.</summary>
        public const int MaxPanels = 99;
        public const string DefaultLevel = "Meadow";
        public const float CrossFade = 0.25f;
        public const float MusicFade = 1f;

        [Header("Built by the editor script")]
        public Texture2D[] panels = Array.Empty<Texture2D>();
        public AudioClip music;
        public AudioSource source;
        public GameObject canvasRoot;
        public RectTransform frame;
        public RawImage current;
        public RawImage incoming;
        public Button next;
        public Button skip;
        public Button panelClick;
        public TMP_Text nextLabel;

        private static StoryIntro active;

        private StoryIntroLogic logic;
        private readonly AdvanceCooldown cooldown = new AdvanceCooldown();
        private Action onFinished;
        private int startFrame;
        private float fade = 1;
        // Whether the incoming panel has not yet taken the current one's place (true from a Next until SettleFade)
        private bool crossing;
        private int lastWidth, lastHeight;
        private bool ending;
        private bool musicFading;
        private float musicFadeStart, musicFadeFrom, musicFadeTo;

        /// <summary>
        /// Plays the intro. <paramref name="onFinished"/> runs once, when the player has pressed Play on the last panel or Skip; the
        /// default loads the meadow. A second call while one is playing is ignored.
        /// </summary>
        public static void Begin(Action onFinished = null)
        {
            if (active != null)
                return;
            var prefab = Resources.Load<GameObject>(ResourceName);
            if (prefab == null)
            {
                Debug.LogWarning($"StoryIntro: no viewer at Resources/{ResourceName} (run Tools > SDF Story > Build Intro Viewer); skipping the intro");
                (onFinished ?? LoadDefaultLevel)();
                return;
            }
            var instance = Instantiate(prefab);
            instance.name = "Story Intro";
            active = instance.GetComponent<StoryIntro>();
            active.Run(onFinished ?? LoadDefaultLevel);
        }

        public static void LoadDefaultLevel() => SceneManager.LoadScene(DefaultLevel);

        /// <summary>panel-01: a panel's name in its comic's folder, from 1.</summary>
        public static string PanelName(int number) => $"panel-{number:00}";

        /// <summary>Where a comic's panel is in Resources: Comics/beat-fungal/panel-01.</summary>
        public static string PanelResource(string comic, int number) => $"{ComicResources}/{comic}/{PanelName(number)}";

        private static readonly Dictionary<string, bool> known = new Dictionary<string, bool>();

        /// <summary>
        /// Whether a comic can be played as panels: the intro when its viewer is built, any other when its first panel is in its folder. The
        /// answer is kept (a build's Resources do not change), and the panel looked at is let go again at once.
        /// </summary>
        public static bool HasComic(string comic)
        {
            if (string.IsNullOrEmpty(comic))
                return false;
            if (comic == Intro)
                return Resources.Load<GameObject>(ResourceName) != null;
            if (known.TryGetValue(comic, out var has))
                return has;
            var first = Resources.Load<Texture2D>(PanelResource(comic, 1));
            has = first != null;
            if (has)
                Resources.UnloadAsset(first);
            known[comic] = has;
            return has;
        }

        /// <summary>
        /// Plays a comic by name (ticket 34): the intro as <see cref="Begin"/> does, any other from its folder of panels, in order until a
        /// number is missing. <paramref name="onFinished"/> runs once, after Play on the last panel or Skip, or at once when the comic has no
        /// panels or there is no viewer. A second call while one is playing is ignored.
        /// </summary>
        public static void Play(string comic, Action onFinished)
        {
            if (comic == Intro)
            {
                Begin(onFinished);
                return;
            }
            if (active != null)
                return;
            var loaded = new List<Texture2D>();
            for (var number = 1; number <= MaxPanels; number++)
            {
                var panel = Resources.Load<Texture2D>(PanelResource(comic, number));
                if (panel == null)
                    break;
                loaded.Add(panel);
            }
            // The panels' own viewer; a project whose viewer was built before ticket 34 has only the intro's, which then loads its panels too
            var prefab = Resources.Load<GameObject>(ViewerResourceName);
            if (prefab == null)
            {
                prefab = Resources.Load<GameObject>(ResourceName);
                if (prefab != null)
                    Debug.LogWarning($"StoryIntro: no Resources/{ViewerResourceName} yet (run Tools > SDF Story > Build Intro Viewer); showing {comic} in the intro's viewer");
            }
            if (loaded.Count == 0 || prefab == null)
            {
                Debug.LogWarning(loaded.Count == 0
                    ? $"StoryIntro: no panels at Resources/{ComicResources}/{comic}/ (render them with Tools > SDF Story > Render Comic...); skipping {comic}"
                    : $"StoryIntro: no viewer at Resources/{ViewerResourceName} (run Tools > SDF Story > Build Intro Viewer); skipping {comic}");
                onFinished?.Invoke();
                return;
            }
            var instance = Instantiate(prefab);
            instance.name = "Story " + comic;
            active = instance.GetComponent<StoryIntro>();
            active.panels = loaded.ToArray();
            active.Run(onFinished ?? (() => { }));
        }

        /// <summary>The comic on show, if any (for the checks).</summary>
        public static StoryIntro Active => active;

        private void Run(Action finished)
        {
            onFinished = finished;
            startFrame = Time.frameCount;
            var count = 0;
            foreach (var panel in panels)
                if (panel != null)
                    count++;
            logic = new StoryIntroLogic(count);
            if (count == 0)
            {
                Debug.LogWarning("StoryIntro: the viewer holds no panels; skipping the comic");
                Finish();
                return;
            }
            EnsureEventSystem();
            next.onClick.AddListener(OnNext);
            panelClick.onClick.AddListener(OnNext);
            skip.onClick.AddListener(OnSkip);
            current.texture = panels[0];
            incoming.color = new Color(1, 1, 1, 0);
            Layout();
            nextLabel.text = logic.NextLabel;
            StartMusic();
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;
            // The legacy input module, as the GUI scene uses; under the canvas root, so it goes with the screen
            var events = new GameObject("Event System", typeof(EventSystem), typeof(StandaloneInputModule));
            events.transform.SetParent(canvasRoot.transform, false);
        }

        private void StartMusic()
        {
            if (music == null)
            {
                Debug.LogWarning("StoryIntro: Assets/Story/Audio/intro-music.ogg is missing, so the intro plays without music");
                return;
            }
            source.clip = music;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.volume = 0;
            source.Play();
            FadeMusic(0, 1);
        }

        private void FadeMusic(float from, float to)
        {
            musicFadeFrom = from;
            musicFadeTo = to;
            musicFadeStart = Time.unscaledTime;
            musicFading = true;
        }

        private void Update()
        {
            if (logic == null)
                return;
            UpdateMusic();
            if (ending)
                return;
            if (Screen.width != lastWidth || Screen.height != lastHeight)
                Layout();
            if (fade < 1)
            {
                fade = Mathf.Min(1, fade + Time.unscaledDeltaTime / CrossFade);
                incoming.color = new Color(1, 1, 1, fade);
                if (fade >= 1)
                    SettleFade();
            }
            // Keys pressed on the frame the intro opened belong to whatever opened it
            if (Time.frameCount <= startFrame)
                return;
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
                Input.GetKeyDown(KeyCode.RightArrow))
                OnNext();
            else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton7))
                OnSkip();
        }

        private void OnNext()
        {
            Console.WriteLine("OnNext");
            if (ending || logic == null)
                return;
            ClearSelection();
            // One press must count once, whichever of the button, the screen click and the keys reported it
            if (!cooldown.TryAdvance(Time.unscaledTime))
                return;
            // A press during the cross-fade lands the panel that is arriving at once
            SettleFade();
            if (logic.Next())
            {
                incoming.texture = panels[logic.Index];
                incoming.color = new Color(1, 1, 1, 0);
                fade = 0;
                crossing = true;
                nextLabel.text = logic.NextLabel;
            }
            else if (logic.Finished)
                Finish();
        }

        private void OnSkip()
        {
            if (ending || logic == null)
                return;
            ClearSelection();
            logic.Skip();
            Finish();
        }

        // So Space and Enter, which submit the selected button, do not act twice
        private static void ClearSelection()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        private void SettleFade()
        {
            // Not on fade: the fade reaches 1 before it settles, and the incoming panel must still become the current one then
            if (!crossing)
                return;
            crossing = false;
            current.texture = incoming.texture;
            incoming.color = new Color(1, 1, 1, 0);
            fade = 1;
        }

        /// <summary>Fits the panel to the screen's shape; the bars are the backdrop behind it.</summary>
        private void Layout()
        {
            lastWidth = Screen.width;
            lastHeight = Screen.height;
            var box = StoryIntroLogic.Letterbox(lastWidth, lastHeight);
            frame.anchorMin = new Vector2(box.X, box.Y);
            frame.anchorMax = new Vector2(box.X + box.Width, box.Y + box.Height);
            frame.offsetMin = frame.offsetMax = Vector2.zero;
        }

        private void Finish()
        {
            if (ending)
                return;
            ending = true;
            // The panels and the screen go now; the music alone stays to fade out over whatever comes next
            panels = Array.Empty<Texture2D>();
            if (canvasRoot != null)
                Destroy(canvasRoot);
            var fadeMusic = source != null && source.isPlaying;
            if (fadeMusic)
            {
                FadeMusic(source.volume, 0);
                DontDestroyOnLoad(gameObject);
            }
            var finished = onFinished;
            onFinished = null;
            active = null;
            finished?.Invoke();
            if (!fadeMusic)
                Destroy(gameObject);
            Resources.UnloadUnusedAssets();
        }

        private void UpdateMusic()
        {
            if (!musicFading || source == null)
                return;
            var t = Mathf.Clamp01((Time.unscaledTime - musicFadeStart) / MusicFade);
            source.volume = Mathf.Lerp(musicFadeFrom, musicFadeTo, t);
            if (t < 1)
                return;
            musicFading = false;
            if (!ending)
                return;
            source.Stop();
            source.clip = null;
            music = null;
            Destroy(gameObject);
            Resources.UnloadUnusedAssets();
        }

        private void OnDestroy()
        {
            if (active == this)
                active = null;
        }
    }
}
