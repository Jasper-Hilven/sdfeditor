using SdfLevel;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// A helicopter (ticket 24): flown by a <see cref="HelicopterPilot"/> from one random point over the play area to the next, 6 to 12 m
    /// up, smoothly and leaning into its flight, over the castles high enough to clear their tops and never out of the play area (so never
    /// into the hills); it rises straight up from its side's machine zone after every (re)spawn. Its rotor (<see cref="SdfMachines.MachineRig.turn"/>,
    /// about local y) and tail rotor (<see cref="SdfMachines.MachineRig.tilt"/>, about local x) spin. It has no gun that turns: it turns its
    /// nose (and its eyes, on its body) to its target, slowing to fire, and shoots down at it from under its front.
    /// </summary>
    public sealed class HelicopterMachine : Machine
    {
        [Tooltip("Degrees a second the rotor spins at full speed")]
        public float rotorSpeed = 1100;
        [Tooltip("Degrees a second the tail rotor spins at full speed")]
        public float tailRotorSpeed = 1600;
        [Tooltip("Degrees off its nose it still fires at a target")]
        public float fireCone = 30;

        private HelicopterPilot pilot;
        private float spin;

        protected override Vector3 CenterLocal => new Vector3(0, 0.3f, 0);

        protected override Vector3 MuzzleLocal => new Vector3(0, 0.12f, 0.22f);

        protected override Transform MuzzleParent => Rig.body;

        public override float Heading => pilot != null ? pilot.Yaw : base.Heading;

        protected override string Doing => pilot != null && pilot.TakingOff ? "taking off" : "flying";

        public HelicopterPilot Pilot => pilot;

        protected override void OnBound()
        {
            Machines.PlayArea(out var min, out var max);
            pilot = new HelicopterPilot(min, max, Owner.Surface, Random)
            {
                Speed = State.Stats.speed,
                TurnSpeed = State.Stats.turnSpeed,
            };
            // A room level with a machines line (ticket 27.2) gives it the room's limits: 16 m over bases, 3 m off the walls, under the level's
            // flight ceiling. A room without (the chessboard) keeps it under 10 m over its spawn; under a sky it is free
            var scene = IMMScene.Current;
            if (Owner.flightCeiling > 0)
                HelicopterLimits.Room(Owner.flightCeiling).Apply(pilot, Side.machineSpawn.position.y);
            // The level's no-fly boxes (ticket 29.6): the tub and other things that stand up into its flight band
            if (Owner.noFly != null && Owner.noFly.Length > 0)
            {
                var zones = new NoFlyZones();
                foreach (var box in Owner.noFly)
                    zones.Add(box.center, box.size, box.yaw);
                pilot.NoFly = zones;
            }
            else if (scene != null && scene.indoor)
                pilot.Ceiling = Side.machineSpawn.position.y + 10;
        }

        protected override void Place()
        {
            var spot = Owner.ZoneSpot(Side, this);
            pilot.Start(spot, Side.machineSpawn.eulerAngles.y);
            spin = 0.2f;
            transform.SetPositionAndRotation(spot, Quaternion.Euler(0, pilot.Yaw, 0));
            if (Rig.body != null)
                Rig.body.localRotation = Quaternion.identity;
        }

        protected override bool TurnGun(Vector3 point, float dt, bool hasTarget)
        {
            // The rotors spin up after a (re)spawn
            spin = Mathf.MoveTowards(spin, 1, dt * 0.8f);
            if (Rig.turn != null)
                Rig.turn.localRotation *= Quaternion.Euler(0, rotorSpeed * spin * dt, 0);
            if (Rig.tilt != null)
                Rig.tilt.localRotation *= Quaternion.Euler(tailRotorSpeed * spin * dt, 0, 0);
            if (!hasTarget || pilot.TakingOff)
                return false;
            var to = point - Muzzle.position;
            var flat = new Vector3(to.x, 0, to.z);
            return flat.sqrMagnitude < 1 || Mathf.Abs(Mathf.DeltaAngle(pilot.Yaw, MachineAim.YawTo(flat))) < fireCone;
        }

        protected override Vector3 ShotDirection(Vector3 point) => point - Muzzle.position;

        protected override void Move(float dt, Vector3? fighting)
        {
            pilot.Tick(dt, fighting);
            transform.SetPositionAndRotation(pilot.Position, Quaternion.Euler(0, pilot.Yaw, 0));
            if (Rig.body != null)
                Rig.body.localRotation = Quaternion.Euler(pilot.Pitch, 0, pilot.Roll);
        }

        protected override void OnDrawGizmos()
        {
            base.OnDrawGizmos();
            if (pilot == null || State == null || !State.Alive || Owner == null || !Owner.showGizmos)
                return;
            Gizmos.DrawLine(transform.position, pilot.Waypoint);
            Gizmos.DrawWireSphere(pilot.Waypoint, 0.4f);
        }
    }
}
