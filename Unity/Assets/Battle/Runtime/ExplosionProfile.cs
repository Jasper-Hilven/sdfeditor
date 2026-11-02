using SdfEffects;
using SdfHeads;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// A head's explosion (ticket 46) plugged into the head burst (ticket 51's <see cref="HeadBurstProfile"/>): right after the generic burst
    /// (the head's own seven pieces, its gobs and splats in its blood colour) the profile's debris, mist, glints, sparks, drops, lightning and
    /// sound play. The head's pieces stay (<see cref="HeadBurstProfile.ReplacesPieces"/> false). All twelve register themselves before the
    /// first scene; Off has no profile call (the head only puffs).
    /// </summary>
    public sealed class ExplosionProfile : HeadBurstProfile
    {
        private readonly MonsterExplosion explosion;

        public ExplosionProfile(MonsterExplosion explosion) => this.explosion = explosion;

        public MonsterExplosion Explosion => explosion;

        public override void Special(HeadBurst burst)
        {
            var world = burst.Gore != null ? burst.Gore.Monsters : null;
            MonsterBurst.Play(world, explosion, burst.Position, burst.Away, burst.Level, burst.Strength);
        }

        /// <summary>Registers the twelve explosions as the heads' profiles; safe to call again.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void RegisterAll()
        {
            foreach (var explosion in MonsterExplosions.All)
                Register(explosion.headId, new ExplosionProfile(explosion));
        }
    }

    /// <summary>The match's side of a monster explosion: the gore's gobs and splats, its ground, the combat's lightning, and the debris pool.</summary>
    public sealed class GoreMonsterWorld : IMonsterWorld
    {
        private readonly Gore gore;
        private readonly MonsterDebris debris;

        public GoreMonsterWorld(Gore gore, MonsterDebris debris)
        {
            this.gore = gore;
            this.debris = debris;
        }

        public FxParticles Particles => gore.Particles;

        public MonsterDebris Debris => debris;

        public bool Gob(Vector3 position, Vector3 velocity, float radius, Color colour) => gore.Throw(position, velocity, radius, colour);

        public void Splat(Vector3 at, Vector3 normal, float radius, Color colour) => gore.Splat(at, normal, radius, colour);

        public bool Ground(Vector3 from, out Vector3 point, out Vector3 normal) => Gore.Ground(from, out point, out normal, 4);

        public void Bolt(int index, Vector3 from, Vector3 to)
        {
            var fx = gore.Match != null ? gore.Combat?.Fx : null;
            if (fx != null)
                fx.Jump(gore, index, from, to);
        }
    }
}
