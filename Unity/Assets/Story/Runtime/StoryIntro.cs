using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SdfStory
{
    /// <summary>
    /// The comic intro (ticket 31.8): the twelve panels full screen, one at a time, letterboxed to 16:9, with music, Next and Skip.
    /// <see cref="Begin"/> is the only way in and the only way out: it loads the viewer prefab from Resources (so the panels, which the
    /// prefab holds, are not loaded until then), and when the player plays on or skips it calls <c>onFinished</c> (by default the
    /// meadow loads) and lets the panels go. The prefab is built by Tools > SDF Story > Build Intro Viewer.
    /// </summary>
    public sealed class StoryIntro : MonoBehaviour
    {
        public const string ResourceName = "StoryIntro";
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
                Debug.LogWarning("StoryIntro: the viewer holds no panels; skipping the intro");
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
