using SdfLevel;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// What the cars of a <see cref="PathMover"/> do to what they meet (ticket 27.10, the rules in <see cref="MoverImpact"/>): every frame,
    /// after the cars have been placed, each car's box is tested for players and machines it overlaps. A scout or tank in it is hurt by the
    /// mover's hazard damage (<see cref="Damage.ByHazard"/>: the mover's name in the kill feed, no weapon, no kill credit, no spores) and, if it
    /// survives, pushed off the car's side; the same car does not hit the same victim again within the mover's cooldown. Flags, pods,
    /// turrets and helicopters are never looked for, so they ignore the train; a dropped flag is only a trigger and stays where it lies.
    /// A player standing on top of a car (the flat wagon) rides it and is left alone. Only while a round is being played.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class MoverStrikes : MonoBehaviour
    {
        /// <summary>Metres the car's box is wider than its colliders to the sides and ends: a car driving into someone has overlapped by the next frame.</summary>
        private const float Reach = 0.1f;

        /// <summary>A victim whose feet are this far under the car's top or higher stands on it.</summary>
        private const float RideHeight = 0.3f;

        private readonly MoverHitLog log = new MoverHitLog();
        private readonly Collider[] found = new Collider[24];
        private PathMover mover;
        private float lastClock;

        private void Awake() => mover = GetComponent<PathMover>();

        private void Update()
        {
            var match = Match.Current;
            var combat = Combat.Current;
            var clock = PathMover.Clock;
            if (clock < lastClock)
                log.Clear();
            lastClock = clock;
            if (mover == null || match == null || combat == null || match.State == null || match.State.Phase != RoundPhase.Playing)
                return;
            foreach (var car in mover.Cars)
                Strike(car, match, combat, clock);
        }

        private void Strike(PathMoverCar car, Match match, Combat combat, float clock)
        {
            var bounds = car.LocalBounds;
            var centre = car.transform.TransformPoint(bounds.center);
            var half = bounds.extents + new Vector3(Reach, 0, Reach);
            var count = Physics.OverlapBoxNonAlloc(centre, half, found, car.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            var top = car.transform.TransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)).y;
            var impact = mover.Impact;
            for (var i = 0; i < count; i++)
            {
                var collider = found[i];
                if (collider.GetComponentInParent<PathMoverCar>() != null)
                    continue;
                var player = Player.Of(collider);
                if (player != null)
                {
                    StrikePlayer(player, car, centre, top, combat, clock, impact);
                    continue;
                }
                var machine = collider.GetComponentInParent<Machine>();
                if (machine != null)
                    StrikeMachine(machine, car, centre, combat, clock, impact);
            }
        }

        private void StrikePlayer(Player player, PathMoverCar car, Vector3 centre, float top, Combat combat, float clock, MoverImpact impact)
        {
            if (player.State == null || !player.State.Alive || player.Capsule == null || !player.Capsule.enabled || !MoverImpact.Affects(MoverTarget.Scout))
                return;
            var feet = player.transform.position;
            if (feet.y >= top - RideHeight || !log.TryHit(player, car, clock, impact.Cooldown))
                return;
            if (impact.Amount > 0)
                combat.Hurt(player, impact.DamageFrom(centre));
            if (impact.Distance <= 0 || !player.State.Alive)
                return;
            var landing = impact.PushedTo(feet, car.transform.position, car.Pose.Yaw, car.LocalBounds.extents.x);
            // Through the controller, so a wall stops the push instead of letting the player through it
            player.Capsule.Move(landing - feet);
        }

        private void StrikeMachine(Machine machine, PathMoverCar car, Vector3 centre, Combat combat, float clock, MoverImpact impact)
        {
            if (!machine.Alive || !MoverImpact.Affects(TargetOf(machine)) || !log.TryHit(machine, car, clock, impact.Cooldown))
                return;
            if (impact.Amount > 0)
                machine.TakeDamage(impact.DamageFrom(centre), machine.Center);
            if (impact.Distance > 0 && machine.Alive)
            {
                var feet = machine.transform.position;
                var landing = impact.PushedTo(feet, car.transform.position, car.Pose.Yaw, car.LocalBounds.extents.x);
                machine.transform.position = landing;
            }
        }

        /// <summary>What a machine is to a car: only a tank is knocked aside.</summary>
        public static MoverTarget TargetOf(Machine machine) =>
            machine is TankMachine ? MoverTarget.Tank : machine is HelicopterMachine ? MoverTarget.Helicopter : machine is TurretMachine ? MoverTarget.Turret : MoverTarget.Other;
    }
}
