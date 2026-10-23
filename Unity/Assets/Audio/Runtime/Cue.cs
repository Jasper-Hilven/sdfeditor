using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SdfAudio
{
    /// <summary>
    /// One named sound of the game and how it is played, as data (kept in the <see cref="SoundBank"/>): the clips to pick from (a cue with
    /// no clip is simply silent, so a sound is added later by dropping a clip into its list), the volume, a random pitch range, whether it
    /// is a sound in the world (3D) or on the screen (2D), how far it carries, and how many may play at once (so a minigun's hundreds of
    /// shots never stack). The game plays cues by name (<see cref="Cues"/>) and knows nothing of the clips.
    /// </summary>
    [Serializable]
    public sealed class Cue
    {
        [Tooltip("The cue's name, one of the constants in Cues")]
        public string name = "";
        [Tooltip("The clips to pick from, one at random each time (never the same twice in a row); none: the cue is silent")]
        public AudioClip[] clips = Array.Empty<AudioClip>();
        [Tooltip("The volume the clip plays at; the game's settings volume applies on top")]
        [Range(0f, 1.5f)] public float volume = 1;
        [Tooltip("The pitch is picked at random between these two")]
        public Vector2 pitch = Vector2.one;
        [Tooltip("A sound in the world: loudest near its source, culled beyond Max Distance. Off: heard the same everywhere (the HUD, the music, your own weapon)")]
        public bool spatial;
        [Tooltip("Metres within which a 3D sound is at full volume")]
        public float minDistance = 2;
        [Tooltip("Metres beyond which a 3D sound is not played at all")]
        public float maxDistance = 40;
        [Tooltip("The most copies of this cue sounding at once; one more takes the place of the oldest, or is dropped if that is younger than a twentieth of a second")]
        public int maxVoices = 4;
        [Tooltip("Seconds that must pass before this cue plays again (0: no wait)")]
        public float cooldown;
        [Tooltip("The clip loops until it is stopped (heartbeat, engines, the minigun's burst)")]
        public bool loop;
        [Tooltip("0 (never dropped) to 256; footsteps and casings are lower than shots")]
        [Range(0, 256)] public int priority = 128;

        [NonSerialized] internal float lastPlayed = -1000;
        [NonSerialized] internal int lastClip = -1;
        [NonSerialized] internal int voices;

        /// <summary>Whether the cue has a clip to play.</summary>
        public bool Filled
        {
            get
            {
                if (clips == null)
                    return false;
                foreach (var clip in clips)
                    if (clip != null)
                        return true;
                return false;
            }
        }

        /// <summary>One of the clips at random, not the one played last when there is another; null for an empty cue.</summary>
        public AudioClip Pick()
        {
            if (!Filled)
                return null;
            var count = 0;
            foreach (var clip in clips)
                if (clip != null)
                    count++;
            var pick = Random.Range(0, count);
            if (count > 1 && pick == lastClip)
                pick = (pick + 1 + Random.Range(0, count - 1)) % count;
            lastClip = pick;
            foreach (var clip in clips)
            {
                if (clip == null)
                    continue;
                if (pick-- == 0)
                    return clip;
            }
            return null;
        }

        /// <summary>A pitch within the cue's range.</summary>
        public float PickPitch() => pitch.x >= pitch.y ? pitch.x : Random.Range(pitch.x, pitch.y);
    }
}
