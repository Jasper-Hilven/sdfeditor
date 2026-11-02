using SdfAudio;
using SdfHeads;
using UnityEngine;

namespace SdfEffects
{
    /// <summary>What a monster explosion needs from the place it happens in (ticket 46): the match's gore, or the special-effects bench.</summary>
    public interface IMonsterWorld
    {
        /// <summary>The small particles (puffs, motes, sparks, drops); null for none.</summary>
        FxParticles Particles { get; }

        /// <summary>The debris pool.</summary>
        MonsterDebris Debris { get; }

        /// <summary>A gob of goop in flight (false when the place has none); it splats where it lands.</summary>
        bool Gob(Vector3 position, Vector3 velocity, float radius, Color colour);

        /// <summary>A splat of goop on a surface.</summary>
        void Splat(Vector3 at, Vector3 normal, float radius, Color colour);

        /// <summary>The ground under a point within a few metres; false when there is none.</summary>
        bool Ground(Vector3 from, out Vector3 point, out Vector3 normal);

        /// <summary>The electro-gun's lightning, the <paramref name="index"/>th bolt of a burst, from one point to another.</summary>
        void Bolt(int index, Vector3 from, Vector3 to);
    }

    /// <summary>
    /// Plays a head's <see cref="MonsterExplosion"/> (ticket 46) on top of the generic burst: its debris, mist or smoke, glints, sparks and
    /// drops, its own gobs and splats, its lightning, and its sound family. Pure scripted emission into pools made beforehand: nothing is
    /// allocated per death (<see cref="LastAllocatedBytes"/> measures it). Off throws nothing: the head only puffs (ticket 51).
    /// </summary>
    public static class MonsterBurst
    {
        /// <summary>Bytes the managed heap grew by during the last <see cref="Play"/> on this thread; 0 once the pools are warm.</summary>
        public static long LastAllocatedBytes { get; private set; }

        /// <summary>Debris pieces the last <see cref="Play"/> threw.</summary>
        public static int LastDebris { get; private set; }

        /// <summary>The cue of a sound family.</summary>
        public static string CueOf(ExplosionSound sound) => sound switch
        {
            ExplosionSound.Ice => Cues.MonsterBurstIce,
            ExplosionSound.Mist => Cues.MonsterBurstMist,
            ExplosionSound.Splash => Cues.MonsterBurstSplash,
            ExplosionSound.Sparks => Cues.MonsterBurstSparks,
            _ => Cues.MonsterBurstPop,
        };

        /// <summary>
        /// The explosion at <paramref name="at"/> (the middle of the head), thrown away from <paramref name="away"/> (flat; zero for any way) with the
        /// weapon's <paramref name="strength"/> (1 for a rifle), at gore <paramref name="level"/>.
        /// </summary>
        public static void Play(IMonsterWorld world, MonsterExplosion e, Vector3 at, Vector3 away, int level, float strength, bool playSound = true)
        {
            LastDebris = 0;
            if (world == null || e == null || level <= 0)
                return;
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            var ground = world.Ground(at, out var groundPoint, out _) ? groundPoint.y : at.y - 1.5f;
            var reach = Mathf.Sqrt(Mathf.Max(0.5f, strength)) * MonsterExplosions.SpeedScale(level);

            // Debris
            var debris = world.Debris;
            if (debris != null)
                foreach (var spec in e.debris)
                {
                    var count = MonsterExplosions.CountOf(spec, level);
                    for (var i = 0; i < count; i++)
                    {
                        var direction = Direction(away, spec.motion);
                        var speed = Random.Range(spec.speed.x, spec.speed.y) * reach;
                        var size = Random.Range(spec.size.x, spec.size.y);
                        if (debris.Spawn(spec.kind, spec.motion, at + direction * 0.12f, direction * speed, spec.spin, size, spec.life, ground))
                            LastDebris++;
                    }
                }

            var particles = world.Particles;
            var mistScale = MonsterExplosions.MistScale(level);
            if (particles != null)
            {
                // Mist or smoke
                var puffs = Mathf.RoundToInt(e.mistPuffs * mistScale);
                var glow = new Color(e.mist.r * 0.4f, e.mist.g * 0.4f, e.mist.b * 0.4f, 1);
                for (var i = 0; i < puffs; i++)
                {
                    var direction = Random.onUnitSphere;
                    direction.y = Mathf.Abs(direction.y) * 0.8f;
                    particles.Emit(FxParticles.Kind.Puff, at + Vector3.Scale(direction, new Vector3(0.25f, 0.3f, 0.25f)), (direction + away * 0.3f) * Random.Range(0.4f, 1f) * e.mistSpeed,
                        e.mistLife * Random.Range(0.75f, 1.15f), Random.Range(e.mistSize.x, e.mistSize.y), e.mist, glow, gravity: -e.mistRise, drag: 3f);
                }
                // Glints: motes of light that drift up and dissolve
                var glints = Mathf.RoundToInt(e.glints * mistScale);
                if (glints > 0)
                {
                    var band = Hdr(e.glint, 1.4f);
                    var core = Hdr(Color.white, 1.6f);
                    for (var i = 0; i < glints; i++)
                    {
                        var direction = Random.onUnitSphere;
                        particles.Emit(FxParticles.Kind.Spore, at + direction * Random.Range(0.1f, 0.5f), direction * Random.Range(0.4f, 2f) + Vector3.up * Random.Range(0.2f, 0.9f),
                            Random.Range(1.2f, 2.4f), Random.Range(0.018f, 0.04f), band, core, gravity: -0.3f, drag: 1.4f);
                    }
                }
                // Sparks: streaks that fly out and die at once
                var sparks = Mathf.RoundToInt(e.sparks * mistScale);
                if (sparks > 0)
                {
                    var band = Hdr(e.spark, 1.4f);
                    var core = new Color(2.4f, 2.4f, 2.6f, 1);
                    for (var i = 0; i < sparks; i++)
                    {
                        var ray = Random.onUnitSphere;
                        particles.Emit(FxParticles.Kind.Spark, at + ray * 0.25f, ray * Random.Range(e.sparkSpeed * 0.5f, e.sparkSpeed * 1.3f), Random.Range(0.1f, 0.3f),
                            Random.Range(0.02f, 0.04f), band, core, gravity: 6);
                    }
                }
                // Drops: pond water, acid
                var drops = Mathf.RoundToInt(e.drops * mistScale);
                if (drops > 0)
                {
                    var shade = new Color(e.drop.r * 0.4f, e.drop.g * 0.4f, e.drop.b * 0.4f, 1);
                    for (var i = 0; i < drops; i++)
                        particles.Emit(FxParticles.Kind.Drop, at + Random.insideUnitSphere * 0.25f, (Random.onUnitSphere + Vector3.up * 0.6f + away * 0.5f) * Random.Range(1.5f, 4.5f),
                            Random.Range(0.35f, 0.8f), Random.Range(0.016f, 0.034f), e.drop, shade, gravity: 9.8f);
                }
            }

            // Its own gobs, and splats round it on the ground
            var gobs = Mathf.RoundToInt(e.extraGobs * MonsterExplosions.DebrisScale(level));
            for (var i = 0; i < gobs; i++)
            {
                var direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.9f + 0.35f;
                direction = (direction + away * 0.8f).normalized;
                if (!world.Gob(at + direction * 0.25f, direction * (Random.Range(2.2f, 6.5f) * reach), Random.Range(0.045f, 0.1f), e.goop))
                    break;
            }
            var splats = e.splats + (e.splats > 0 ? MonsterExplosions.ExtraSplats(level) : 0);
            for (var i = 0; i < splats; i++)
            {
                var offset = Random.insideUnitCircle * Random.Range(0.4f, 1.9f);
                var from = new Vector3(at.x + offset.x, at.y, at.z + offset.y);
                if (world.Ground(from, out var point, out var normal))
                    world.Splat(point, normal, Random.Range(0.12f, 0.3f), e.splat);
            }

            // The electro-gun's lightning
            for (var i = 0; i < e.bolts; i++)
            {
                var end = at + Random.onUnitSphere * Random.Range(0.8f, 1.6f);
                end.y = Mathf.Max(end.y, ground + 0.05f);
                world.Bolt(i, at + Random.insideUnitSphere * 0.15f, end);
            }

            if (playSound)
                Audio.Play(CueOf(e.sound), at);
            LastAllocatedBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        }

        /// <summary>The way a piece of debris leaves the head: out, up and away from the hit; a bat flies out nearly level.</summary>
        private static Vector3 Direction(Vector3 away, DebrisMotion motion)
        {
            var direction = Random.onUnitSphere;
            if (motion == DebrisMotion.Fly)
            {
                direction.y = Random.Range(0.1f, 0.55f);
                return (direction + away * 0.4f).normalized;
            }
            direction.y = Mathf.Abs(direction.y) * 0.9f + 0.35f;
            return (direction + away * 0.8f).normalized;
        }

        private static Color Hdr(Color color, float intensity) => new Color(color.r * intensity, color.g * intensity, color.b * intensity, 1);
    }
}
