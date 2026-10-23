using UnityEditor;
using UnityEngine;

namespace SdfAudio.Editor
{
    /// <summary>
    /// Gives an arena's scene its sounds (<see cref="ArenaAudio"/>): the level builder calls <see cref="Attach"/> on the scene's SceneInfo
    /// object with the level's name, and the component gets the floor and the music clips <see cref="ArenaAudio.PlanFor"/> lists for it.
    /// The clips are references from the scene, so the web build's streamed arenas carry exactly the music they play: an asset only an
    /// arena's scene uses goes into that arena's bundle (ticket 38), and one several arenas use into a shared bundle.
    /// </summary>
    public static class ArenaAudioSetup
    {
        /// <summary>Adds (or refreshes) the <see cref="ArenaAudio"/> on <paramref name="host"/> for the level called <paramref name="levelName"/>.</summary>
        public static ArenaAudio Attach(GameObject host, string levelName)
        {
            var plan = ArenaAudio.PlanFor(levelName);
            var audio = host.GetComponent<ArenaAudio>();
            if (audio == null)
                audio = host.AddComponent<ArenaAudio>();
            audio.surface = plan.Surface;
            audio.musicLayers = new AudioClip[0];
            audio.bossTheme = null;
            if (plan.Music != MusicSet.None)
            {
                audio.musicLayers = new AudioClip[MusicIntensity.Layers];
                for (var layer = 0; layer < audio.musicLayers.Length; layer++)
                {
                    var path = ArenaAudio.MusicPath(plan.Music, layer);
                    audio.musicLayers[layer] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (audio.musicLayers[layer] == null)
                        Debug.LogWarning($"SDF audio: {levelName} has no music layer {layer + 1} ({path} is not imported yet); rebuild the level once the library has imported");
                }
            }
            if (plan.Boss)
            {
                audio.bossTheme = AssetDatabase.LoadAssetAtPath<AudioClip>(ArenaAudio.BossThemePath);
                if (audio.bossTheme == null)
                    Debug.LogWarning($"SDF audio: {levelName} has no boss theme ({ArenaAudio.BossThemePath} is not imported yet)");
            }
            return audio;
        }
    }
}
