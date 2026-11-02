using System;
using UnityEngine;

namespace SdfHeads
{
    /// <summary>The small SDF parts a monster explosion throws (content/models/effects/monster/, ticket 46); <see cref="DebrisKinds.ModelOf"/> is each one's model name.</summary>
    public enum DebrisKind
    {
        IceShard, Snowball, Bat, Shamrock, WeedTangle, Eyeball, Tentacle, BandageStrip, FurTuft, Fang, PumpkinSeed, Bolt, CandyCane, ConfettiRed,
    }

    /// <summary>How a piece of debris moves: scripted, never physics (the web has none to spare).</summary>
    public enum DebrisMotion
    {
        /// <summary>Thrown, falls under gravity, tumbles, bounces off the ground and lies.</summary>
        Ballistic,
        /// <summary>Light and flat (confetti, shamrocks, bandages, fur): falls slowly, swaying and tumbling, and lies.</summary>
        Flutter,
        /// <summary>A bat: flies outward and up on a curving path, flapping, then shrinks away. No gravity.</summary>
        Fly,
        /// <summary>The Cyclops' eye: heavy, bounces high, rolls, its gaze wandering (dazed), settles.</summary>
        Bounce,
        /// <summary>A tentacle: thrown, lands, wriggles and deflates (shrinks along its length) to nothing.</summary>
        Deflate,
    }

    /// <summary>The scripted extra a head's explosion has beyond its debris (what it is called in the ticket's table).</summary>
    public enum ExplosionSpecial
    {
        None,
        /// <summary>One giant dazed eyeball bounces away (Raging Cyclops).</summary>
        Eyeball,
        /// <summary>Tiny cartoon bats fly outward (Vampire Count).</summary>
        Bats,
        /// <summary>Writhing tentacles that deflate (Deep-Sea Cthulhu).</summary>
        Tentacles,
        /// <summary>Cracking electric-blue bolts, the electro-gun's lightning (Frankenstein's Monster).</summary>
        ElectricSparks,
    }

    /// <summary>The sound family of an explosion: one cue each in the sound bank (ticket 40).</summary>
    public enum ExplosionSound { Ice, Mist, Splash, Sparks, Pop }

    /// <summary>One kind of debris a head throws: how many (at Splatty), how fast, how it spins and moves, how long it lives.</summary>
    [Serializable]
    public struct DebrisSpec
    {
        public DebrisKind kind;
        public DebrisMotion motion;
        [Tooltip("How many at Splatty; the gore levels above throw more (MonsterExplosions.DebrisScale) unless Fixed")]
        public int count;
        [Tooltip("A special's pieces (the eyeball, the bats, the tentacles) are the same number at every gore level")]
        public bool fixedCount;
        [Tooltip("Metres a second at the start")]
        public Vector2 speed;
        [Tooltip("Degrees a second, at most")]
        public float spin;
        [Tooltip("Scale of the model, a random one in this range")]
        public Vector2 size;
        [Tooltip("Seconds before it shrinks away, from the start")]
        public float life;

        public static DebrisSpec Of(DebrisKind kind, DebrisMotion motion, int count, float speedMin, float speedMax, float spin, float sizeMin, float sizeMax, float life,
            bool fixedCount = false) =>
            new DebrisSpec
            {
                kind = kind, motion = motion, count = count, speed = new Vector2(speedMin, speedMax), spin = spin, size = new Vector2(sizeMin, sizeMax), life = life,
                fixedCount = fixedCount,
            };
    }

    /// <summary>
    /// One head's explosion (ticket 46), the data of its burst on top of the generic one (51's pieces cut from the head, its gobs and splats): a
    /// palette that starts from the head's blood colour (ticket 51), the debris thrown (<see cref="DebrisSpec"/>), the mist or smoke, the
    /// glints, sparks and drops, the special, and the sound family. Made in code by <see cref="MonsterExplosions"/>, one per head id; nothing in
    /// it is allocated per death.
    /// </summary>
    public sealed class MonsterExplosion : ScriptableObject
    {
        public string headId;

        [Header("Palette (starts from the head's blood colour)")]
        [Tooltip("The gobs the burst throws")]
        public Color goop = Color.white;
        [Tooltip("What lands on the ground")]
        public Color splat = Color.white;
        [Tooltip("The mist or smoke")]
        public Color mist = Color.white;

        [Header("Debris")]
        public DebrisSpec[] debris = new DebrisSpec[0];

        [Header("Mist or smoke")]
        [Tooltip("Puffs at Splatty")]
        public int mistPuffs;
        public Vector2 mistSize = new Vector2(0.15f, 0.3f);
        public float mistLife = 1.2f;
        [Tooltip("Metres a second squared upward (a puff rises when positive)")]
        public float mistRise = 0.5f;
        [Tooltip("Metres a second the puffs fly out at")]
        public float mistSpeed = 1.5f;

        [Header("Gobs and splats")]
        [Tooltip("Gobs on top of the generic burst's, in the goop colour, at Splatty")]
        public int extraGobs;
        [Tooltip("Splats on the ground round the head, in the splat colour, at Splatty")]
        public int splats;

        [Header("Glints, sparks, drops")]
        public Color glint = Color.white;
        [Tooltip("Glowing motes that drift up and dissolve (frost, gold dust, acid)")]
        public int glints;
        public Color spark = Color.white;
        [Tooltip("Streaks that fly out and die at once (sparks)")]
        public int sparks;
        public float sparkSpeed = 8;
        public Color drop = Color.white;
        [Tooltip("Droplets that fall (pond water)")]
        public int drops;

        [Header("Special and sound")]
        public ExplosionSpecial special;
        [Tooltip("Lightning bolts (ElectricSparks)")]
        public int bolts;
        public ExplosionSound sound = ExplosionSound.Pop;
    }

    /// <summary>The file names of the debris models.</summary>
    public static class DebrisKinds
    {
        public static readonly int Count = Enum.GetValues(typeof(DebrisKind)).Length;

        /// <summary>The model's name: <c>content/models/effects/monster/&lt;name&gt;.sdf</c>, and its prefab Assets/Mushrooms/&lt;name&gt;.prefab.</summary>
        public static string ModelOf(DebrisKind kind) => kind switch
        {
            DebrisKind.IceShard => "ice-shard",
            DebrisKind.Snowball => "snowball",
            DebrisKind.Bat => "bat",
            DebrisKind.Shamrock => "shamrock",
            DebrisKind.WeedTangle => "weed-tangle",
            DebrisKind.Eyeball => "eyeball",
            DebrisKind.Tentacle => "tentacle",
            DebrisKind.BandageStrip => "bandage-strip",
            DebrisKind.FurTuft => "fur-tuft",
            DebrisKind.Fang => "fang",
            DebrisKind.PumpkinSeed => "pumpkin-seed",
            DebrisKind.Bolt => "bolt",
            DebrisKind.CandyCane => "candy-cane",
            _ => "confetti-red",
        };
    }
}
