using SdfGui;
using UnityEngine;

namespace SdfAudio
{
    /// <summary>
    /// The game's sound, in one place. The first thing the game does (before any scene) is make one persistent object, the audio host,
    /// holding the <see cref="AudioPlayer"/> (the pool of sound-effect sources) and the <see cref="MusicDirector"/>, and load the
    /// <see cref="SoundBank"/>. Everything else plays a sound by cue name: <c>Audio.Play(Cues.Casing, position)</c> or
    /// <c>Audio.Play2D(Cues.UiOkay)</c>; a name the bank has no clip for is silent and costs nothing. The master volume is
    /// <c>AudioListener.volume</c>, which the settings' Volume slider sets (<see cref="GuiSettings.Volume"/>, applied here at the start),
    /// and the pause menu pauses the listener, which stops everything here with it.
    /// <para>
    /// Silent in batch mode and while a video is recorded (<c>Time.captureFramerate</c> is set by the preview recorder), so recording and
    /// the tools never open an audio device or play over a capture.
    /// </para>
    /// </summary>
    public static class Audio
    {
        /// <summary>The name of the persistent host object.</summary>
        public const string HostName = "Audio";

        private static AudioPlayer player;
        private static MusicDirector music;
        private static SoundBank bank;
        private static bool made;

        /// <summary>Nothing is played: batch mode, or a recording is running.</summary>
        public static bool Muted => Application.isBatchMode || !Application.isPlaying || Time.captureFramerate > 0;

        public static SoundBank Bank => bank;

        public static AudioPlayer Player => player;

        public static MusicDirector Music => music;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            player = null;
            music = null;
            bank = null;
            made = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Start() => Ensure();

        /// <summary>Makes the audio host if it is not there yet; true when there is one to play on.</summary>
        public static bool Ensure()
        {
            if (made && player != null)
                return true;
            if (Application.isBatchMode || !Application.isPlaying)
                return false;
            made = true;
            bank = SoundBank.Load();
            if (bank == null)
                Debug.LogWarning("Audio: there is no sound bank (Assets/Audio/Resources/SoundBank.asset); the game is silent. Run Tools > SDF Audio > Update Sound Bank");
            else
                bank.ResetPlayState();
            var host = new GameObject(HostName);
            Object.DontDestroyOnLoad(host);
            player = host.AddComponent<AudioPlayer>();
            player.Build();
            music = host.AddComponent<MusicDirector>();
            // The saved volume: the settings only set the listener's volume when the slider moves
            AudioListener.volume = GuiSettings.Volume;
            return true;
        }

        /// <summary>Plays the cue called <paramref name="cue"/> at <paramref name="position"/>; <paramref name="twoD"/> as a sound on the screen. See <see cref="AudioPlayer.Play"/>.</summary>
        public static AudioPlayer.Handle Play(string cue, Vector3 position, float volume = 1, float delay = 0, bool twoD = false, Transform follow = null, float pitch = 1)
        {
            if (Muted || !Ensure())
                return default;
            return player.Play(CueOf(cue), position, volume, delay, twoD, follow, pitch);
        }

        /// <summary>Plays a cue on the screen (the HUD, the menus, your own weapon): heard the same everywhere.</summary>
        public static AudioPlayer.Handle Play2D(string cue, float volume = 1, float delay = 0, float pitch = 1)
        {
            if (Muted || !Ensure())
                return default;
            return player.Play(CueOf(cue), Vector3.zero, volume, delay, true, null, pitch);
        }

        /// <summary>The cue called <paramref name="name"/>; null when the bank is missing or has none.</summary>
        public static Cue CueOf(string name) => bank != null ? bank.Get(name) : null;

        /// <summary>Whether the cue has a clip (an empty cue is wired but silent).</summary>
        public static bool Has(string name) => CueOf(name) is { } cue && cue.Filled;

        /// <summary>Where sounds are heard from.</summary>
        public static Vector3 ListenerPosition => player != null ? player.ListenerPosition : Vector3.zero;
    }
}
