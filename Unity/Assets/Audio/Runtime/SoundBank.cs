using System.Collections.Generic;
using UnityEngine;

namespace SdfAudio
{
    /// <summary>
    /// The game's table of sounds: every <see cref="Cue"/> by name, in one asset (Assets/Audio/Resources/SoundBank.asset, made and kept up
    /// to date by the editor script SoundBankSetup from the library in Assets/Audio/Library). Cues with no clip are there on purpose, named
    /// and wired to their events, so a sound is added by putting a clip in the cue's list in the inspector (or in SoundBankSetup's table).
    /// The bank is in the player (it is loaded from Resources) so it holds only the sounds every arena shares; the arenas' music hangs off
    /// the arena scenes (<see cref="ArenaAudio"/>) and goes into the arena's own download.
    /// </summary>
    [CreateAssetMenu(fileName = "SoundBank", menuName = "SDF/Sound Bank")]
    public sealed class SoundBank : ScriptableObject
    {
        /// <summary>Where the bank is, inside Resources.</summary>
        public const string ResourceName = "SoundBank";

        public Cue[] cues = System.Array.Empty<Cue>();

        private Dictionary<string, Cue> byName;

        /// <summary>The cue called <paramref name="name"/>; null when the bank has none (a name the game uses that the bank lacks is silent).</summary>
        public Cue Get(string name)
        {
            if (byName == null || byName.Count != cues.Length)
            {
                byName = new Dictionary<string, Cue>(cues.Length);
                foreach (var cue in cues)
                    if (cue != null && !string.IsNullOrEmpty(cue.name))
                        byName[cue.name] = cue;
            }
            return name != null && byName.TryGetValue(name, out var found) ? found : null;
        }

        /// <summary>Forgets the cues' play counts and times (a play session starts).</summary>
        public void ResetPlayState()
        {
            byName = null;
            foreach (var cue in cues)
            {
                if (cue == null)
                    continue;
                cue.voices = 0;
                cue.lastPlayed = -1000;
                cue.lastClip = -1;
            }
        }

        /// <summary>The bank of the game, from Resources; null when it is not made yet (everything is then silent).</summary>
        public static SoundBank Load() => Resources.Load<SoundBank>(ResourceName);
    }
}
