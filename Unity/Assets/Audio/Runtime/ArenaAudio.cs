using SdfLevel;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfAudio
{
    /// <summary>What the player's feet are on in an arena, which picks the footstep sounds.</summary>
    public enum FootstepSurface
    {
        Grass,
        Wood,
        Carpet,
    }

    /// <summary>Which of the two layered battle tracks an arena plays.</summary>
    public enum MusicSet
    {
        None,
        Orchestra,
        Electronic,
    }

    /// <summary>
    /// An arena's own sounds, on its scene's SceneInfo object (added by the level builder, <c>ArenaAudioSetup</c>, from the table in
    /// <see cref="PlanFor"/>): the surface its floor sounds like, and the clips of its music, the four layers of the battle track and, in
    /// the boss's arena, the boss theme. The music clips hang off the scene on purpose: the web build streams every arena after the first
    /// as its own download, and an asset only an arena's scene refers to goes into that arena's bundle, not the first download.
    /// <para>
    /// A scene built before this component existed has none: the surface then comes from the table by the level's name, and in the editor
    /// the music is loaded from the library by path, so a scene plays with sound before it is rebuilt. A web build needs the rebuilt scenes.
    /// </para>
    /// </summary>
    public sealed class ArenaAudio : MonoBehaviour
    {
        [Tooltip("What the floor sounds like under your feet")]
        public FootstepSurface surface = FootstepSurface.Grass;
        [Tooltip("The battle track's four layers, in sync: layer 1 always plays, 2 to 4 fade in as the fight heats up; empty: no battle music")]
        public AudioClip[] musicLayers = System.Array.Empty<AudioClip>();
        [Tooltip("The boss fight's theme (the boss arena only)")]
        public AudioClip bossTheme;

        /// <summary>Where the sound library is in the project.</summary>
        public const string LibraryFolder = "Assets/Audio/Library";

        /// <summary>The path of a layer (0 to 3) of a battle track.</summary>
        public static string MusicPath(MusicSet set, int layer) =>
            $"{LibraryFolder}/Music/music_lvl_02_{(set == MusicSet.Electronic ? "electronic" : "orchestra")}_layer_{layer + 1:00}.ogg";

        /// <summary>The path of the boss theme.</summary>
        public const string BossThemePath = LibraryFolder + "/Music/music_lvl_03_doma_east_theme_bpm_140.ogg";

        /// <summary>What a level has: the floor, the battle track, and whether its fight is the boss's.</summary>
        public readonly struct Plan
        {
            public Plan(FootstepSurface surface, MusicSet music, bool boss)
            {
                Surface = surface;
                Music = music;
                Boss = boss;
            }

            public FootstepSurface Surface { get; }
            public MusicSet Music { get; }
            public bool Boss { get; }
        }

        /// <summary>
        /// The arenas' sounds by level name (lower case, as level.json names it): grass in the meadow, the fungal level and the boss's arena;
        /// wood in the toy box, the kitchen and the chessboard; carpet in the bathroom and the computer. Orchestral music in the meadow, the
        /// fungal level, the chessboard and the boss's arena (where the boss theme plays in the fight), electronic in the toy box, the
        /// kitchen, the bathroom and the computer.
        /// </summary>
        public static Plan PlanFor(string levelName)
        {
            switch ((levelName ?? "").ToLowerInvariant())
            {
                case "meadow": return new Plan(FootstepSurface.Grass, MusicSet.Orchestra, false);
                case "fungal": return new Plan(FootstepSurface.Grass, MusicSet.Orchestra, false);
                case "arena": return new Plan(FootstepSurface.Grass, MusicSet.Orchestra, true);
                case "chessboard": return new Plan(FootstepSurface.Wood, MusicSet.Orchestra, false);
                case "toybox": return new Plan(FootstepSurface.Wood, MusicSet.Electronic, false);
                case "kitchen": return new Plan(FootstepSurface.Wood, MusicSet.Electronic, false);
                case "bathroom": return new Plan(FootstepSurface.Carpet, MusicSet.Electronic, false);
                case "computer": return new Plan(FootstepSurface.Carpet, MusicSet.Electronic, false);
                default: return new Plan(FootstepSurface.Grass, MusicSet.None, false);
            }
        }

        /// <summary>The footstep cue for a surface.</summary>
        public static string FootstepCue(FootstepSurface surface) =>
            surface == FootstepSurface.Wood ? Cues.FootstepWood : surface == FootstepSurface.Carpet ? Cues.FootstepCarpet : Cues.FootstepGrass;

        /// <summary>What a playing arena sounds like, resolved from its component or, failing that, the table.</summary>
        public sealed class Sounds
        {
            public FootstepSurface surface;
            public AudioClip[] layers = System.Array.Empty<AudioClip>();
            public AudioClip bossTheme;

            /// <summary>Whether the arena has a battle track of all four layers.</summary>
            public bool HasBattleMusic => layers != null && layers.Length >= MusicIntensity.Layers && layers[0] != null;
        }

        /// <summary>The sounds of the active battle scene: its component's, or the table's with (in the editor) the music loaded by path.</summary>
        public static Sounds Resolve()
        {
            var scene = IMMScene.Current;
            var component = scene != null ? scene.GetComponent<ArenaAudio>() : null;
            var name = scene != null && !string.IsNullOrEmpty(scene.levelName) ? scene.levelName : SceneManager.GetActiveScene().name;
            var plan = PlanFor(name);
            var sounds = new Sounds { surface = component != null ? component.surface : plan.Surface };
            if (component != null)
            {
                sounds.layers = component.musicLayers ?? sounds.layers;
                sounds.bossTheme = component.bossTheme;
            }
#if UNITY_EDITOR
            // A scene not rebuilt yet: the library's files by path (the web build does not have this, see the class's remarks)
            if (!sounds.HasBattleMusic && plan.Music != MusicSet.None)
            {
                sounds.layers = new AudioClip[MusicIntensity.Layers];
                for (var i = 0; i < sounds.layers.Length; i++)
                    sounds.layers[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath(plan.Music, i));
            }
            if (sounds.bossTheme == null && plan.Boss)
                sounds.bossTheme = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(BossThemePath);
#endif
            return sounds;
        }
    }
}
