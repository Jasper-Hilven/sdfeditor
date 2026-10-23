using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SdfAudio.Editor
{
    /// <summary>
    /// Makes and keeps up to date the game's <see cref="SoundBank"/> (Assets/Audio/Resources/SoundBank.asset): one <see cref="Cue"/> for
    /// every name in <see cref="Cues"/>, with the library clips and the numbers of the table in <see cref="Defaults"/>. It runs when the
    /// editor loads, when anything in Assets/Audio/Library is imported, and from Tools > SDF Audio > Update Sound Bank. It only fills in:
    /// a cue that is missing is added, a cue with no clip gets the table's clips; a cue that already has clips, and every volume, pitch and
    /// distance someone tuned in the inspector, stays as it is. Adding a sound later is putting a clip in the cue (or its files in the table).
    /// <para>
    /// The bank sits in Resources, so it and the clips it refers to are in the player: they are the sounds every arena shares (about a
    /// megabyte). The arenas' music is not in it, see <see cref="ArenaAudio"/>; the menu cue's clip is the CC0 menu music (Assets/Audio/CC0), the effects' clips there too.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public sealed class SoundBankSetup : AssetPostprocessor
    {
        /// <summary>Where the CC0 sounds are (see SOURCES.md there).</summary>
        public const string CC0Folder = "Assets/Audio/CC0";
        public const string AssetPath = "Assets/Audio/Resources/SoundBank.asset";

        static SoundBankSetup() => EditorApplication.delayCall += () => Ensure(false);

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(path => path.StartsWith(ArenaAudio.LibraryFolder) || path.StartsWith(CC0Folder)))
                EditorApplication.delayCall += () => Ensure(false);
        }

        [MenuItem("Tools/SDF Audio/Update Sound Bank")]
        public static void Update() => Ensure(true);

        /// <summary>One cue of the table: its name, library files (relative to the library folder; none: an empty cue to fill later) and numbers.</summary>
        public sealed class Def
        {
            public string name;
            public string[] files = new string[0];
            public float volume = 1;
            public Vector2 pitch = Vector2.one;
            public bool spatial;
            public float minDistance = 2;
            public float maxDistance = 40;
            public int maxVoices = 4;
            public float cooldown;
            public bool loop;
            public int priority = 128;
            /// <summary>A library file an older table gave this cue; a cue holding only that clip gets this table's clips instead.</summary>
            public string supersedes;
        }

        private static Def Entry(string name, string[] files = null, float volume = 1, float pitchMin = 1, float pitchMax = 1, bool spatial = false,
            float minDistance = 2, float maxDistance = 40, int voices = 4, float cooldown = 0, bool loop = false, int priority = 128, string supersedes = null) =>
            new Def
            {
                name = name, files = files ?? new string[0], volume = volume, pitch = new Vector2(pitchMin, pitchMax), spatial = spatial,
                minDistance = minDistance, maxDistance = maxDistance, maxVoices = voices, cooldown = cooldown, loop = loop, priority = priority, supersedes = supersedes,
            };

        /// <summary>The asset path of a table file: "CC0/..." files are in Assets/Audio/CC0, the others in the library folder.</summary>
        public static string PathOf(string file) => file.StartsWith("CC0/") ? $"{CC0Folder}/{file.Substring(4)}" : $"{ArenaAudio.LibraryFolder}/{file}";

        private static string[] Numbered(string prefix, int count, string extension = ".ogg") =>
            Enumerable.Range(1, count).Select(i => $"{prefix}{i:00}{extension}").ToArray();

        /// <summary>
        /// The table: every cue of <see cref="Cues"/>, with what the library gives it. Cues with no file are empty on purpose (their events
        /// are wired; a separate set of sounds is being fetched for them). Footsteps and shots are quiet-ish individually; the loud ones
        /// (explosions, the sniper) carry far.
        /// </summary>
        public static List<Def> Defaults() => new List<Def>
        {
            // Weapons
            Entry(Cues.Pistol, new[] { "Weapons/SFX_PistolShoot.ogg" }, 0.55f, 0.95f, 1.05f, true, 3, 80, 6, priority: 100),
            Entry(Cues.DualPistols, new[] { "Weapons/SFX_PistolShoot.ogg" }, 0.5f, 0.9f, 1.1f, true, 3, 80, 6, 0.02f, priority: 100),
            Entry(Cues.Sniper, new[] { "Weapons/LongShot/EFX EXT .50 Cal Pistol Shots 02 A.wav" }, 1f, 0.97f, 1.03f, true, 5, 200, 3, priority: 90),
            Entry(Cues.MinigunSpinUp, null, 0.8f, 1, 1, true, 3, 50, 4),
            Entry(Cues.MinigunHead, new[] { "Weapons/M4/weapons_m4_head_01.wav", "Weapons/M4/weapons_m4_head_02.wav" }, 0.8f, 0.98f, 1.02f, true, 3, 70, 4, priority: 100),
            Entry(Cues.MinigunLoop, Numbered("Weapons/M4/weapons_m4_auto_", 3), 0.7f, 1, 1, true, 3, 70, 4, loop: true, priority: 100),
            Entry(Cues.MinigunTail, new[] { "Weapons/M4/weapons_m4_tail_01.ogg", "Weapons/M4/weapons_m4_tail_02.ogg" }, 0.8f, 0.98f, 1.02f, true, 3, 70, 4, priority: 100),
            Entry(Cues.FlamethrowerStart, new[] { "CC0/weapons/flamethrower_start.ogg" }, 0.6f, 0.97f, 1.03f, true, 2, 40, 3, priority: 100),
            Entry(Cues.FlamethrowerLoop, new[] { "CC0/weapons/flamethrower_loop.ogg" }, 0.55f, 1, 1, true, 2, 40, 3, loop: true, priority: 100),
            Entry(Cues.FlamethrowerEnd, new[] { "CC0/weapons/flamethrower_stop.ogg" }, 0.55f, 1, 1, true, 2, 40, 3),
            Entry(Cues.ElectroGun, new[] { "CC0/weapons/electro_lightning_shot.wav", "CC0/weapons/electro_lightning_shot_b.wav" }, 1f, 0.95f, 1.05f, true, 3, 70, 4),
            Entry(Cues.ElectroChain, new[] { "CC0/weapons/electro_lightning_chain.wav" }, 0.7f, 0.95f, 1.05f, true, 3, 50, 3, 0.04f),
            Entry(Cues.PoisonGun, new[] { "CC0/weapons/poison_spray.ogg", "CC0/weapons/poison_spray_b.ogg" }, 0.6f, 0.95f, 1.05f, true, 3, 60, 4),
            Entry(Cues.PoisonImpact, new[] { "CC0/weapons/blob_splat_impact_b.ogg", "CC0/weapons/poison_spray_b.ogg" }, 0.55f, 0.85f, 0.95f, true, 3, 60, 4),
            Entry(Cues.BlobGun, new[] { "CC0/weapons/blob_shot.ogg", "CC0/weapons/blob_shot_b.ogg" }, 0.65f, 0.95f, 1.05f, true, 3, 60, 4),
            Entry(Cues.BlobImpact, new[] { "CC0/weapons/blob_splat_impact.ogg", "CC0/weapons/blob_splat_impact_b.ogg" }, 0.65f, 0.95f, 1.05f, true, 3, 60, 4),
            Entry(Cues.Casing, Numbered("Weapons/Bullet Casings/weapons_bullet_casing_", 4), 0.35f, 0.9f, 1.1f, true, 1, 15, 3, 0.06f, priority: 200),
            Entry(Cues.WeaponPickup, new[] { "CC0/flag/flag_pickup.wav" }, 0.6f, 1.2f, 1.3f, true, 3, 40, 2),
            Entry(Cues.HealthPickup, new[] { "CC0/flag/flag_pickup.wav" }, 0.6f, 1.05f, 1.15f, true, 3, 40, 2),

            // Hits, hurt and death
            Entry(Cues.HitMarker, new[] { "CC0/player/hit_marker_tick.ogg" }, 0.45f, 0.95f, 1.05f, false, voices: 2, cooldown: 0.03f, priority: 60),
            Entry(Cues.HitMarkerHead, new[] { "CC0/player/hit_marker_tick.ogg" }, 0.55f, 1.45f, 1.55f, false, voices: 2, cooldown: 0.03f, priority: 60),
            Entry(Cues.HitMarkerKill, new[] { "CC0/player/hit_marker_tick.ogg" }, 0.75f, 0.6f, 0.65f, false, voices: 2, cooldown: 0.03f, priority: 60),
            Entry(Cues.Hurt, new[] { "CC0/player/hurt.ogg" }, 0.7f, 0.95f, 1.05f, false, voices: 2, cooldown: 0.25f, priority: 50),
            Entry(Cues.DeathYou, new[] { "Weapons/die.wav" }, 0.8f, 1, 1, false, voices: 1, priority: 40),
            Entry(Cues.DeathGore, new[] { "Weapons/RIPS AND TEARS DESIGNED Humanoid Guts 04.wav" }, 0.9f, 0.9f, 1.1f, true, 4, 60, 3, priority: 110),
            Entry(Cues.Respawn, new[] { "CC0/player/respawn.ogg" }, 0.6f, 1, 1, false, voices: 1),
            Entry(Cues.Heartbeat, new[] { "Character/heartbeat.wav" }, 0.9f, 1, 1, false, voices: 1, loop: true, priority: 20),

            // Moving
            Entry(Cues.FootstepGrass, Numbered("Character/Footsteps/footsteps_player_grass_", 3), 0.5f, 0.93f, 1.07f, false, voices: 3, cooldown: 0.12f, priority: 150),
            Entry(Cues.FootstepWood, Numbered("Character/Footsteps/footsteps_player_wood_", 4), 0.5f, 0.93f, 1.07f, false, voices: 3, cooldown: 0.12f, priority: 150),
            Entry(Cues.FootstepCarpet, Numbered("Character/Footsteps/footsteps_player_carpet_", 3), 0.55f, 0.93f, 1.07f, false, voices: 3, cooldown: 0.12f, priority: 150),
            Entry(Cues.FootstepEnemy, Numbered("Character/Footsteps/footsteps_enemy_", 3), 0.35f, 0.9f, 1.1f, true, 2, 22, 6, 0.05f, priority: 180),
            // Taken out by ear: no jump sound, and no engine hum or rotor bubbling under the music (the cues stay, silent)
            Entry(Cues.Jump, null, 0.5f, 0.95f, 1.05f, false, voices: 1, priority: 140),
            Entry(Cues.Land, new[] { "CC0/player/land.ogg" }, 0.5f, 0.95f, 1.05f, false, voices: 1, priority: 140),

            // Explosions
            Entry(Cues.ExplosionSmall, new[] { "Weapons/Explosion/weapons_explosion_small.ogg" }, 0.9f, 0.92f, 1.08f, true, 8, 90, 3, priority: 80),
            Entry(Cues.ExplosionMedium, new[] { "Weapons/Explosion/weapons_explosion_medium.ogg" }, 1f, 0.92f, 1.08f, true, 10, 130, 3, priority: 80),
            Entry(Cues.ExplosionLarge, new[] { "Weapons/Explosion/weapons_explosion_large.ogg" }, 1f, 0.92f, 1.08f, true, 14, 180, 3, priority: 70),
            Entry(Cues.ExplosionDistant, new[] { "Weapons/Explosion/weapons_explosion_distant.ogg" }, 0.7f, 0.92f, 1.08f, true, 60, 500, 2, priority: 120),
            Entry(Cues.DebrisSmall, new[] { "Weapons/Explosion/weapons_explosion_debris_small.ogg" }, 0.6f, 0.92f, 1.08f, true, 6, 80, 3, priority: 130),
            Entry(Cues.DebrisMedium, new[] { "Weapons/Explosion/weapons_explosion_debris_medium.ogg" }, 0.6f, 0.92f, 1.08f, true, 6, 100, 3, priority: 130),
            Entry(Cues.DebrisLarge, new[] { "Weapons/Explosion/weapons_explosion_debris_large.ogg" }, 0.6f, 0.92f, 1.08f, true, 6, 120, 3, priority: 130),

            // Machines
            Entry(Cues.TurretFire, new[] { "CC0/vehicles/turret_shot.ogg" }, 0.55f, 0.95f, 1.05f, true, 3, 70, 4),
            Entry(Cues.TankFire, new[] { "CC0/vehicles/tank_cannon_shot.ogg" }, 0.9f, 0.95f, 1.05f, true, 5, 100, 3),
            Entry(Cues.HelicopterFire, new[] { "CC0/vehicles/turret_shot.ogg" }, 0.5f, 0.8f, 0.9f, true, 4, 80, 3),
            // Taken out by ear: no jump sound, and no engine hum or rotor bubbling under the music (the cues stay, silent)
            Entry(Cues.TankEngine, null, 0.5f, 1, 1, true, 5, 60, 3, loop: true, priority: 160),
            Entry(Cues.HelicopterRotor, null, 0.55f, 1, 1, true, 8, 120, 3, loop: true, priority: 150),

            // Capture the flag and the match
            Entry(Cues.FlagPickup, new[] { "CC0/flag/flag_pickup.wav" }, 0.7f, 1, 1, false, voices: 1),
            Entry(Cues.FlagDrop, new[] { "CC0/flag/flag_dropped.ogg" }, 0.7f, 1, 1, false, voices: 1),
            Entry(Cues.FlagReturn, new[] { "CC0/flag/flag_returned.ogg" }, 0.7f, 1, 1, false, voices: 1),
            Entry(Cues.FlagCapture, new[] { "CC0/flag/flag_capture_jingle.ogg" }, 0.8f, 1, 1, false, voices: 1),
            Entry(Cues.FlagLost, new[] { "CC0/flag/flag_dropped.ogg" }, 0.7f, 0.8f, 0.8f, false, voices: 1),
            Entry(Cues.CaptureSting, new[] { "Music/music_lvl_03_sting_high_02.ogg" }, 0.7f, 1, 1, false, voices: 1, priority: 30),
            Entry(Cues.Applause, null, 0.8f, 1, 1, false, voices: 2, priority: 40),
            Entry(Cues.VictorySting, new[] { "Music/music_lvl_03_sting_high_01.ogg" }, 0.8f, 1, 1, false, voices: 1, priority: 30),
            Entry(Cues.Defeat, new[] { "CC0/flag/flag_dropped.ogg" }, 0.7f, 0.65f, 0.65f, false, voices: 1),
            Entry(Cues.SuddenDeath, new[] { "Music/music_lvl_03_sting_high_02.ogg" }, 0.6f, 0.7f, 0.7f, false, voices: 1),

            // Spores
            Entry(Cues.SporeReady, new[] { "CC0/spores/spore_meter_ready.ogg" }, 0.6f, 1, 1, true, 4, 50, 2),
            Entry(Cues.SporeRush, new[] { "CC0/spores/mycelium_rush_whoosh.ogg" }, 0.7f, 1, 1, true, 4, 50, 2),
            Entry(Cues.SporeRegrowth, new[] { "CC0/spores/regrowth_heal_chime.ogg" }, 0.7f, 1, 1, true, 4, 50, 2),
            Entry(Cues.SporeArmour, new[] { "CC0/spores/puffball_armour_puff.ogg" }, 0.7f, 1, 1, true, 4, 50, 2),

            // The boss
            Entry(Cues.BossPhase, new[] { "Weapons/Explosion/weapons_explosion_medium.ogg" }, 0.6f, 0.65f, 0.7f, false, voices: 1),
            Entry(Cues.BossShieldDown, new[] { "CC0/spores/puffball_armour_puff.ogg" }, 0.9f, 0.55f, 0.55f, false, voices: 1),
            Entry(Cues.BossCrash, new[] { "Weapons/Bluezone-BC0212-impact-007.wav" }, 1f, 1, 1, true, 20, 300, 1, priority: 60),

            // Menus and music
            Entry(Cues.UiOkay, new[] { "UI/ui_okay.ogg" }, 0.8f, 1, 1, false, voices: 3, priority: 20),
            Entry(Cues.UiCancel, new[] { "UI/ui_cancel.ogg" }, 0.8f, 1, 1, false, voices: 3, priority: 20),
            Entry(Cues.ComicPanel, null, 0.6f, 0.95f, 1.05f, false, voices: 2, priority: 20),
            Entry(Cues.MusicMenu, new[] { "Music/music_lvl_02_orchestra_layer_01.ogg" }, 1f, 1, 1, false, voices: 1, loop: true, priority: 0, supersedes: "CC0/music/menu_music_flowerbed_fields.ogg"),
        };

        /// <summary>
        /// Makes the bank if it is missing and fills in what the table has that it lacks. <paramref name="log"/> says what was done even when
        /// nothing changed. Does nothing while the editor is compiling or importing, or playing.
        /// </summary>
        public static void Ensure(bool log)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(AssetPath);
            var created = bank == null;
            if (created)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
                bank = ScriptableObject.CreateInstance<SoundBank>();
                AssetDatabase.CreateAsset(bank, AssetPath);
            }
            var cues = bank.cues.Where(c => c != null).ToList();
            var changed = created || cues.Count != bank.cues.Length;
            var filled = 0;
            foreach (var def in Defaults())
            {
                var cue = cues.FirstOrDefault(c => c.name == def.name);
                if (cue == null)
                {
                    cue = new Cue
                    {
                        name = def.name, volume = def.volume, pitch = def.pitch, spatial = def.spatial, minDistance = def.minDistance,
                        maxDistance = def.maxDistance, maxVoices = def.maxVoices, cooldown = def.cooldown, loop = def.loop, priority = def.priority,
                    };
                    cues.Add(cue);
                    changed = true;
                }
                if (def.files.Length == 0)
                    continue;
                // A cue that holds only the clip an older table gave it (for a while the menu music was the CC0 "Flowerbed Fields") moves on to the new clips
                var old = def.supersedes != null && cue.clips != null && cue.clips.Length == 1 && cue.clips[0] != null
                    && AssetDatabase.GetAssetPath(cue.clips[0]) == PathOf(def.supersedes);
                if (cue.Filled && !old)
                    continue;
                var clips = def.files.Select(file => AssetDatabase.LoadAssetAtPath<AudioClip>(PathOf(file))).Where(c => c != null).ToArray();
                if (clips.Length == 0)
                    continue;
                // A cue that was empty (or held the old default) takes the table's numbers with its clips: nobody tuned a silent cue
                cue.volume = def.volume;
                cue.pitch = def.pitch;
                cue.spatial = def.spatial;
                cue.minDistance = def.minDistance;
                cue.maxDistance = def.maxDistance;
                cue.maxVoices = def.maxVoices;
                cue.cooldown = def.cooldown;
                cue.loop = def.loop;
                cue.priority = def.priority;
                cue.clips = clips;
                filled++;
                changed = true;
            }
            if (changed)
            {
                bank.cues = cues.ToArray();
                EditorUtility.SetDirty(bank);
                AssetDatabase.SaveAssets();
            }
            if (log || created || filled > 0)
            {
                var empty = bank.cues.Count(c => !c.Filled);
                Debug.Log($"SDF audio: sound bank {(created ? "made" : "updated")}: {bank.cues.Length} cues, {bank.cues.Length - empty} with clips, {empty} empty (to fill later)");
            }
        }
    }

    /// <summary>A build (the web build's batch run too) never goes out without the sound bank being complete.</summary>
    public sealed class SoundBankBuildStep : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report) => SoundBankSetup.Ensure(false);
    }
}
