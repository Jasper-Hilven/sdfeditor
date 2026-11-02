using System.Collections.Generic;
using UnityEngine;

namespace SdfHeads
{
    /// <summary>
    /// The twelve explosions (ticket 46), one <see cref="MonsterExplosion"/> per head of the <see cref="MonsterHeads"/> table, and the numbers
    /// by which the gore levels (ticket 51: Off, Splatty, Extra Splatty, Ultra Gore) scale them. Each palette starts from the head's blood
    /// colour (ticket 51's table) and is turned toward the ticket's colour only where that is a mist or smoke (the Yeti's frost, the
    /// Vampire's purple mist, the Mummy's sand cloud, the pumpkin's dark candle smoke). Off has no explosion: the head puffs only (51).
    /// <code>var profile = MonsterExplosions.Of("cyclops");   var n = MonsterExplosions.CountOf(profile.debris[0], GoreRules.ExtraSplatty);</code>
    /// </summary>
    public static class MonsterExplosions
    {
        private static Dictionary<string, MonsterExplosion> byId;
        private static MonsterExplosion[] all;

        /// <summary>Every explosion, in the heads' calendar order.</summary>
        public static IReadOnlyList<MonsterExplosion> All
        {
            get
            {
                Ensure();
                return all;
            }
        }

        /// <summary>The explosion of a head by id, or null.</summary>
        public static MonsterExplosion Of(string headId)
        {
            Ensure();
            return headId != null && byId.TryGetValue(headId, out var explosion) ? explosion : null;
        }

        // ---------------------------------------------------------------- The gore levels

        /// <summary>How much more debris a gore level throws than Splatty: none at Off (no explosion), then 1, 1.6 and 2.</summary>
        public static float DebrisScale(int level) => level <= 0 ? 0 : level == 1 ? 1 : level == 2 ? 1.6f : 2;

        /// <summary>How much more mist and glints a gore level makes than Splatty.</summary>
        public static float MistScale(int level) => level <= 0 ? 0 : level == 1 ? 1 : level == 2 ? 1.4f : 1.8f;

        /// <summary>How much further the debris flies: Ultra Gore flings it.</summary>
        public static float SpeedScale(int level) => level >= 3 ? 1.35f : 1;

        /// <summary>Splats on top of the profile's own: none at Splatty, 6 at Extra Splatty, 12 at Ultra Gore.</summary>
        public static int ExtraSplats(int level) => level >= 3 ? 12 : level == 2 ? 6 : 0;

        /// <summary>How many pieces of a debris spec a gore level throws (a special's stay the same).</summary>
        public static int CountOf(DebrisSpec spec, int level)
        {
            if (level <= 0)
                return 0;
            return spec.fixedCount ? spec.count : Mathf.Max(1, Mathf.RoundToInt(spec.count * DebrisScale(level)));
        }

        /// <summary>Debris pieces one death throws at a gore level (all its specs), for the pool's size and the budget.</summary>
        public static int DebrisOf(MonsterExplosion explosion, int level)
        {
            var total = 0;
            foreach (var spec in explosion.debris)
                total += CountOf(spec, level);
            return total;
        }

        // ---------------------------------------------------------------- The table

        private static Color Mix(Color a, Color b, float t) => new Color(Mathf.Lerp(a.r, b.r, t), Mathf.Lerp(a.g, b.g, t), Mathf.Lerp(a.b, b.b, t), 1);

        private static Color Rgb(float r, float g, float b) => new Color(r, g, b, 1);

        private static Color Dark(Color c, float by) => new Color(c.r * by, c.g * by, c.b * by, 1);

        private static void Ensure()
        {
            if (all != null && all[0] != null)
                return;
            var list = new List<MonsterExplosion>();
            byId = new Dictionary<string, MonsterExplosion>();
            foreach (var head in MonsterHeads.All)
            {
                var explosion = Make(head.Id, head.Blood);
                explosion.hideFlags = HideFlags.HideAndDontSave;
                list.Add(explosion);
                byId[head.Id] = explosion;
            }
            all = list.ToArray();
        }

        private static MonsterExplosion Make(string id, Color blood)
        {
            var e = ScriptableObject.CreateInstance<MonsterExplosion>();
            e.name = id;
            e.headId = id;
            e.goop = blood;
            // The splat is the blood too: the goop models are tinted once per colour (Gore.Tinted), so a second colour would copy their meshes at a death
            e.splat = blood;
            e.mist = blood;
            var spark = Rgb(1, 0.8f, 0.3f);
            switch (id)
            {
                case "yeti":
                    // Shattering ice shards, snowballs, a burst of freezing white frost
                    e.debris = new[]
                    {
                        DebrisSpec.Of(DebrisKind.IceShard, DebrisMotion.Ballistic, 12, 3.5f, 8, 720, 0.8f, 1.3f, 2.6f),
                        DebrisSpec.Of(DebrisKind.Snowball, DebrisMotion.Ballistic, 5, 2, 5, 300, 0.8f, 1.2f, 3f),
                    };
                    e.mist = Mix(blood, Color.white, 0.65f);
                    e.mistPuffs = 14; e.mistSize = new Vector2(0.16f, 0.32f); e.mistLife = 0.9f; e.mistRise = 0.3f; e.mistSpeed = 2.6f;
                    e.glint = Rgb(0.85f, 0.97f, 1); e.glints = 20;
                    e.sound = ExplosionSound.Ice;
                    break;
                case "vampire":
                    // Deep purple mist, tiny cartoon bats flying outward
                    e.debris = new[] { DebrisSpec.Of(DebrisKind.Bat, DebrisMotion.Fly, 6, 3, 5.5f, 0, 1f, 1.5f, 2.2f, fixedCount: true) };
                    e.mist = Dark(blood, 0.8f);
                    e.mistPuffs = 20; e.mistSize = new Vector2(0.2f, 0.4f); e.mistLife = 1.7f; e.mistRise = 0.6f; e.mistSpeed = 1.6f;
                    e.special = ExplosionSpecial.Bats;
                    e.sound = ExplosionSound.Mist;
                    break;
                case "leprechaun":
                    // Bright green shamrock confetti, gold spark dust
                    e.debris = new[] { DebrisSpec.Of(DebrisKind.Shamrock, DebrisMotion.Flutter, 24, 2, 6, 540, 0.9f, 1.4f, 3.2f) };
                    e.mist = Mix(blood, Color.white, 0.4f);
                    e.mistPuffs = 6; e.mistSize = new Vector2(0.12f, 0.22f); e.mistLife = 0.8f; e.mistRise = 0.2f; e.mistSpeed = 1.5f;
                    e.glint = Rgb(1, 0.82f, 0.2f); e.glints = 18;
                    e.spark = Rgb(1, 0.85f, 0.25f); e.sparks = 10; e.sparkSpeed = 6;
                    e.sound = ExplosionSound.Pop;
                    break;
                case "swamp":
                    // Dark brown mud, tangled weeds, dirty pond water
                    e.debris = new[] { DebrisSpec.Of(DebrisKind.WeedTangle, DebrisMotion.Ballistic, 4, 1.5f, 4, 360, 0.9f, 1.3f, 3f) };
                    e.mist = Mix(blood, Rgb(0.4f, 0.35f, 0.2f), 0.3f);
                    e.mistPuffs = 8; e.mistSize = new Vector2(0.12f, 0.24f); e.mistLife = 0.9f; e.mistRise = 0.2f; e.mistSpeed = 1.8f;
                    e.extraGobs = 14; e.splats = 3;
                    e.drop = Rgb(0.28f, 0.36f, 0.22f); e.drops = 16;
                    e.sound = ExplosionSound.Splash;
                    break;
                case "cyclops":
                    // Muddy brown slime; one giant dazed eyeball bounces away
                    e.debris = new[] { DebrisSpec.Of(DebrisKind.Eyeball, DebrisMotion.Bounce, 1, 3, 4.5f, 0, 1, 1, 4.5f, fixedCount: true) };
                    e.mist = Mix(blood, Rgb(0.5f, 0.4f, 0.25f), 0.4f);
                    e.mistPuffs = 6; e.mistSize = new Vector2(0.12f, 0.22f); e.mistLife = 0.8f; e.mistRise = 0.2f; e.mistSpeed = 1.5f;
                    e.extraGobs = 10; e.splats = 3;
                    e.special = ExplosionSpecial.Eyeball;
                    e.sound = ExplosionSound.Splash;
                    break;
                case "cthulhu":
                    // Ocean-blue slime, writhing tentacles that deflate
                    e.debris = new[] { DebrisSpec.Of(DebrisKind.Tentacle, DebrisMotion.Deflate, 4, 2, 4.5f, 0, 0.9f, 1.2f, 3.2f, fixedCount: true) };
                    e.mist = blood;
                    e.mistPuffs = 0;
                    e.extraGobs = 12; e.splats = 3;
                    e.drop = Mix(blood, Color.white, 0.25f); e.drops = 10;
                    e.special = ExplosionSpecial.Tentacles;
                    e.sound = ExplosionSound.Splash;
                    break;
                case "alien":
                    // Glowing sizzling yellow acid splatters
                    e.debris = new DebrisSpec[0];
                    e.mist = Mix(blood, Rgb(0.4f, 0.55f, 0.1f), 0.5f);
                    e.mistPuffs = 8; e.mistSize = new Vector2(0.1f, 0.2f); e.mistLife = 1.4f; e.mistRise = 1f; e.mistSpeed = 0.8f;
                    e.extraGobs = 16; e.splats = 6;
                    e.glint = Mix(blood, Color.white, 0.2f); e.glints = 24;
                    e.drop = blood; e.drops = 14;
                    e.sound = ExplosionSound.Splash;
                    break;
                case "mummy":
                    // Unravelling bandages, a sand cloud
                    e.debris = new[] { DebrisSpec.Of(DebrisKind.BandageStrip, DebrisMotion.Flutter, 10, 2.5f, 5.5f, 400, 0.9f, 1.3f, 3.5f) };
                    e.mist = Mix(blood, Rgb(0.85f, 0.7f, 0.42f), 0.5f);
                    e.mistPuffs = 26; e.mistSize = new Vector2(0.26f, 0.5f); e.mistLife = 1.9f; e.mistRise = 0.15f; e.mistSpeed = 1.4f;
                    e.sound = ExplosionSound.Mist;
                    break;
                case "werewolf":
                    // Brown fur tufts, snapping fangs, bright orange sparks
                    e.debris = new[]
                    {
                        DebrisSpec.Of(DebrisKind.FurTuft, DebrisMotion.Flutter, 12, 2.5f, 6, 500, 0.9f, 1.3f, 3f),
                        DebrisSpec.Of(DebrisKind.Fang, DebrisMotion.Ballistic, 4, 3, 6, 1500, 1f, 1.2f, 3f),
                    };
                    e.mist = Mix(blood, Rgb(0.4f, 0.3f, 0.22f), 0.5f);
                    e.mistPuffs = 6; e.mistSize = new Vector2(0.12f, 0.22f); e.mistLife = 0.8f; e.mistRise = 0.2f; e.mistSpeed = 1.5f;
                    e.spark = Rgb(1, 0.5f, 0.08f); e.sparks = 22; e.sparkSpeed = 8;
                    e.sound = ExplosionSound.Pop;
                    break;
                case "pumpkin":
                    // Orange pumpkin goop, flying seeds, dark candle smoke
                    e.debris = new[] { DebrisSpec.Of(DebrisKind.PumpkinSeed, DebrisMotion.Ballistic, 18, 2, 6, 600, 0.9f, 1.4f, 3f) };
                    e.mist = Dark(Mix(blood, Rgb(0.25f, 0.2f, 0.18f), 0.8f), 0.7f);
                    e.mistPuffs = 12; e.mistSize = new Vector2(0.2f, 0.36f); e.mistLife = 2.4f; e.mistRise = 1f; e.mistSpeed = 0.7f;
                    e.extraGobs = 10; e.splats = 3;
                    e.sound = ExplosionSound.Splash;
                    break;
                case "frankenstein":
                    // Bright green slime, cracking electric-blue sparks
                    e.debris = new[] { DebrisSpec.Of(DebrisKind.Bolt, DebrisMotion.Ballistic, 2, 3, 5, 500, 1, 1.1f, 3.2f, fixedCount: true) };
                    e.mist = Mix(blood, Rgb(0.4f, 0.8f, 1), 0.3f);
                    e.mistPuffs = 6; e.mistSize = new Vector2(0.12f, 0.2f); e.mistLife = 0.7f; e.mistRise = 0.3f; e.mistSpeed = 1.5f;
                    e.extraGobs = 10; e.splats = 3;
                    e.spark = Rgb(0.4f, 0.8f, 1); e.sparks = 26; e.sparkSpeed = 9;
                    e.special = ExplosionSpecial.ElectricSparks; e.bolts = 4;
                    e.sound = ExplosionSound.Sparks;
                    break;
                case "santa":
                    // Goop in the head's colour, red confetti, shattered candy canes
                    e.debris = new[]
                    {
                        DebrisSpec.Of(DebrisKind.CandyCane, DebrisMotion.Ballistic, 7, 3, 6.5f, 400, 0.9f, 1.2f, 3.2f),
                        DebrisSpec.Of(DebrisKind.ConfettiRed, DebrisMotion.Flutter, 28, 2, 6, 700, 0.9f, 1.4f, 3.4f),
                    };
                    e.mist = Mix(blood, Color.white, 0.4f);
                    e.mistPuffs = 6; e.mistSize = new Vector2(0.12f, 0.22f); e.mistLife = 0.8f; e.mistRise = 0.2f; e.mistSpeed = 1.5f;
                    e.extraGobs = 10; e.splats = 3;
                    e.sound = ExplosionSound.Pop;
                    break;
                default:
                    e.mistPuffs = 8;
                    e.spark = spark;
                    break;
            }
            return e;
        }
    }
}
