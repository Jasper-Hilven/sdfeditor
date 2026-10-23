using System.Collections.Generic;
using SdfEffects;
using SdfLevel;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The wet zones of a battle (ticket 29.7), on the Battle object next to the <see cref="Match"/> and <see cref="Combat"/>: each scout has a
    /// <see cref="WetTracker"/> fed with whether it stands in a wet zone of <see cref="LevelZones"/>, and the combat multiplies the electro gun's
    /// damage to a wet scout by 1.5 (<see cref="ZoneRules.DamageFactor"/>, through <see cref="CombatState.DamageFactorOf"/>). A wet scout
    /// walking splashes: a few drops into the effects' pool every <see cref="stepMetres"/> metres. The slick is the controller's.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class ZoneEffects : MonoBehaviour
    {
        [Tooltip("Metres a wet scout walks between two splashes")]
        public float stepMetres = 1.4f;

        private readonly Dictionary<PlayerState, WetTracker> trackers = new Dictionary<PlayerState, WetTracker>();
        private readonly Dictionary<PlayerState, Vector3> lastSplash = new Dictionary<PlayerState, Vector3>();
        private Match match;
        private Combat combat;

        private void Update()
        {
            var zones = LevelZones.Current;
            if (match == null)
                match = Match.Current;
            if (combat == null)
                combat = Combat.Current;
            if (zones == null || match == null || combat == null || combat.State == null)
                return;
            combat.State.DamageFactorOf = (victim, damage) =>
                ZoneRules.DamageFactor(damage.Weapon, trackers.TryGetValue(victim, out var tracker) && tracker.Wet);
            foreach (var player in match.Players)
            {
                if (!trackers.TryGetValue(player.State, out var tracker))
                    trackers[player.State] = tracker = new WetTracker();
                if (!player.State.Alive)
                {
                    tracker.Reset();
                    continue;
                }
                var feet = player.transform.position;
                tracker.Tick(zones.IsWet(feet), Time.deltaTime);
                if (tracker.SinceWet < 0.2f && player.Controller != null && player.Controller.IsGrounded)
                    Splash(player.State, feet);
            }
        }

        private void Splash(PlayerState player, Vector3 feet)
        {
            var particles = combat.Fx != null ? combat.Fx.Particles : null;
            if (particles == null)
                return;
            if (lastSplash.TryGetValue(player, out var last) && (feet - last).sqrMagnitude < stepMetres * stepMetres)
                return;
            lastSplash[player] = feet;
            var water = new Color(0.55f, 0.8f, 0.95f);
            for (var i = 0; i < 3; i++)
            {
                var sideways = Random.insideUnitCircle * 0.8f;
                particles.Emit(FxParticles.Kind.Drop, feet + Vector3.up * 0.05f, new Vector3(sideways.x, Random.Range(1.5f, 2.5f), sideways.y),
                    0.5f, 0.05f, water, Color.white, gravity: 9.8f);
            }
        }
    }
}
