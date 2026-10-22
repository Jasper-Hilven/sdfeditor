using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// A turret (ticket 24): it stands where the level puts it (a scene object under its side, <see cref="BattleSide.turrets"/>), turns
    /// its head (<see cref="SdfMachines.MachineRig.turn"/>, Foot/Head, about local y) to its target and tilts its barrel
    /// (<see cref="SdfMachines.MachineRig.tilt"/>, about local x: negative is up, <see cref="MachineAim"/>) to it; without one it sweeps
    /// slowly to either side of the way it faces at rest. Comes back where it stood 30 s after dying.
    /// </summary>
    public sealed class TurretMachine : Machine
    {
        [Tooltip("Degrees either side of its rest it sweeps without a target")]
        public float sweep = 55;
        [Tooltip("Seconds of one sweep there and back")]
        public float sweepSeconds = 14;
        [Tooltip("Lowest and highest elevation of the barrel, degrees (it stands 2 m tall, so it looks down a long way)")]
        public Vector2 elevation = new Vector2(-40, 25);

        private float yaw;
        private float tilt;
        private float phase;

        protected override Vector3 CenterLocal => new Vector3(0, 1.3f, 0);

        protected override Vector3 MuzzleLocal => new Vector3(0, 0, 0.58f);

        protected override Transform MuzzleParent => Rig.tilt;

        public override float Heading => transform.eulerAngles.y + yaw;

        protected override string Doing => "watching";

        protected override void OnBound() => phase = (float)Random.NextDouble() * Mathf.PI * 2;

        protected override void Place()
        {
            OnRespawnInPlace();
        }

        protected override void OnRespawnInPlace()
        {
            yaw = 0;
            tilt = 0;
            Apply();
        }

        protected override bool TurnGun(Vector3 point, float dt, bool hasTarget)
        {
            var speed = State.Stats.turnSpeed;
            if (!hasTarget)
            {
                // The slow sweep, from where it is
                var want = Mathf.Sin(Time.time * Mathf.PI * 2 / Mathf.Max(1, sweepSeconds) + phase) * sweep;
                yaw = Mathf.MoveTowardsAngle(yaw, want, speed * 0.3f * dt);
                tilt = Mathf.MoveTowards(tilt, MachineAim.TiltPerElevation * -6, speed * 0.3f * dt);
                Apply();
                return false;
            }
            var foot = Rig.turn.parent;
            yaw = Mathf.MoveTowardsAngle(yaw, MachineAim.YawTo(foot.InverseTransformDirection(point - Rig.turn.position)), speed * dt);
            Apply();
            var wantTilt = MachineAim.TiltFor(Rig.turn.InverseTransformDirection(point - Rig.tilt.position), elevation.x, elevation.y);
            tilt = Mathf.MoveTowards(tilt, wantTilt, speed * dt);
            Apply();
            return Vector3.Angle(Muzzle.forward, point - Muzzle.position) < 4;
        }

        private void Apply()
        {
            if (Rig.turn != null)
                Rig.turn.localRotation = Quaternion.Euler(0, yaw, 0);
            if (Rig.tilt != null)
                Rig.tilt.localRotation = Quaternion.Euler(tilt, 0, 0);
        }
    }
}
