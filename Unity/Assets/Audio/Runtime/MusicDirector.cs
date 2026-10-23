using System.Collections.Generic;
using SdfCampaign;
using SdfStory;
using UnityEngine;

namespace SdfAudio
{
    /// <summary>
    /// The music, 2D and through the listener's volume (so the settings' Volume slider and the pause menu act on it like on the effects).
    /// It plays one <em>track</em> at a time, a set of layers that start together on the same audio clock and loop, so they stay in
    /// sync however their volumes move:
    /// <list type="bullet">
    /// <item>In a battle, the match's <see cref="GameAudio"/> says what plays: the arena's four layers, whose volumes follow the fight's
    /// intensity (layer 1 always, 2 to 4 fading in and out, see <see cref="MusicIntensity"/>), or in the boss fight the boss theme.</item>
    /// <item>In the menus and between battles (while the campaign runs and no match is on): the menu cue's clip (<see cref="Cues.MusicMenu"/>),
    /// quietly. That clip is the in-game music's own loop, the orchestral layer 1, so going into an orchestral arena (the Meadow first)
    /// carries on the same clip with the other layers joining in; an electronic arena or the boss crossfades (below).</item>
    /// <item>A different track is faded in as the old one fades out (1.5 s). While the comic viewer plays music of its own, the menu music
    /// stays out of its way.</item>
    /// </list>
    /// Silent in batch mode and while recording a video.
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        /// <summary>The menu music's level (of the clip's own volume), 0 to 1.</summary>
        public const float MenuLevel = 0.3f;
        /// <summary>A battle track's level.</summary>
        public const float BattleLevel = 0.55f;
        /// <summary>The boss theme's level.</summary>
        public const float BossLevel = 0.65f;
        /// <summary>Seconds a track takes to fade in or out.</summary>
        public const float TrackFade = 1.5f;
        /// <summary>Seconds a layer takes to fade in or out as the intensity moves.</summary>
        public const float LayerFade = 2f;

        private sealed class Track
        {
            public AudioClip[] clips;
            public AudioSource[] sources;
            public readonly List<float> volume = new List<float>();
            public readonly List<float> target = new List<float>();
            public float master;
            public float masterTarget;
            public float masterRate = 1 / TrackFade;
            public GameObject root;
            public bool retiring;
        }

        private Track current;
        private readonly List<Track> retired = new List<Track>();
        private float duckCheck;
        private bool ducked;

        /// <summary>The clips playing now (the first track's), for tests and the debug view; null when silent.</summary>
        public AudioClip[] Playing => current != null ? current.clips : null;

        private void Update()
        {
            if (Audio.Muted)
            {
                Silence();
                return;
            }
            var dt = Time.unscaledDeltaTime;
            Decide(out var clips, out var level, out var shares);
            if (clips == null || clips.Length == 0 || clips[0] == null || level <= 0.001f)
                Retire();
            else
                Follow(clips, level, shares);

            Step(current, dt);
            for (var i = retired.Count - 1; i >= 0; i--)
            {
                Step(retired[i], dt);
                if (retired[i].master <= 0)
                {
                    Destroy(retired[i].root);
                    retired.RemoveAt(i);
                }
            }
        }

        // What should play: the match's track, else the menu's while the campaign runs, else nothing
        private void Decide(out AudioClip[] clips, out float level, out IReadOnlyList<float> shares)
        {
            shares = null;
            var game = GameAudio.Current;
            if (game != null && game.MusicClips != null && game.MusicClips.Length > 0)
            {
                clips = game.MusicClips;
                level = game.MusicLevel;
                shares = game.LayerShares;
                return;
            }
            clips = null;
            level = 0;
            if (CampaignRunner.Instance == null)
                return;
            var menu = Audio.CueOf(Cues.MusicMenu);
            if (menu == null || !menu.Filled)
                return;
            clips = new[] { menu.clips[0] };
            level = MenuLevel * menu.volume * (MenuDucked() ? 0 : 1);
        }

        // The comic viewer, when it has music of its own, plays alone
        private bool MenuDucked()
        {
            if (Time.unscaledTime >= duckCheck)
            {
                duckCheck = Time.unscaledTime + 0.5f;
                var intro = FindFirstObjectByType<StoryIntro>();
                ducked = intro != null && intro.music != null;
            }
            return ducked;
        }

        private void Follow(AudioClip[] clips, float level, IReadOnlyList<float> shares)
        {
            if (current == null || current.retiring || current.clips[0] != clips[0])
            {
                Retire();
                current = BeginTrack(clips);
            }
            else if (clips.Length > current.sources.Length)
                Extend(current, clips);
            var t = current;
            t.masterTarget = level;
            for (var i = 0; i < t.sources.Length; i++)
                t.target[i] = shares != null && i < shares.Count ? shares[i] : i == 0 ? 1 : 0;
        }

        private void Retire()
        {
            if (current == null)
                return;
            current.retiring = true;
            current.masterTarget = 0;
            retired.Add(current);
            current = null;
        }

        private void Silence()
        {
            Retire();
            foreach (var track in retired)
                Destroy(track.root);
            retired.Clear();
        }

        // A new track: its sources start together, silent until the master rises
        private Track BeginTrack(AudioClip[] clips)
        {
            var track = new Track { clips = clips, root = new GameObject("Music"), master = 0 };
            track.root.transform.SetParent(transform, false);
            track.sources = new AudioSource[clips.Length];
            // On the audio clock a moment ahead, so every layer starts on the same sample; the web build starts them in one frame instead
            // (its mixer runs in blocks, so they land in the same one), as scheduling is not dependable there
            var start = AudioSettings.dspTime + 0.2;
            var web = Application.platform == RuntimePlatform.WebGLPlayer;
            for (var i = 0; i < clips.Length; i++)
            {
                track.sources[i] = Source(track, clips[i], i);
                if (web)
                    track.sources[i].Play();
                else
                    track.sources[i].PlayScheduled(start);
                track.volume.Add(0);
                track.target.Add(0);
            }
            return track;
        }

        // Layers added to a track already playing (a track whose first clip stays while more join): in step with the first
        private void Extend(Track track, AudioClip[] clips)
        {
            var reference = track.sources[0];
            var sources = new AudioSource[clips.Length];
            track.sources.CopyTo(sources, 0);
            for (var i = track.sources.Length; i < clips.Length; i++)
            {
                sources[i] = Source(track, clips[i], i);
                sources[i].Play();
                sources[i].timeSamples = Mathf.Min(reference.timeSamples, clips[i].samples - 1);
                track.volume.Add(0);
                track.target.Add(0);
            }
            track.sources = sources;
            track.clips = clips;
        }

        private static AudioSource Source(Track track, AudioClip clip, int layer)
        {
            var child = new GameObject("Layer " + (layer + 1));
            child.transform.SetParent(track.root.transform, false);
            var source = child.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.volume = 0;
            source.priority = 0;
            return source;
        }

        private static void Step(Track track, float dt)
        {
            if (track == null)
                return;
            track.master = Mathf.MoveTowards(track.master, track.masterTarget, track.masterRate * dt);
            for (var i = 0; i < track.sources.Length; i++)
            {
                track.volume[i] = Mathf.MoveTowards(track.volume[i], track.target[i], dt / LayerFade);
                track.sources[i].volume = track.volume[i] * track.master * SdfGui.GuiSettings.MusicVolume;
            }
        }
    }
}
