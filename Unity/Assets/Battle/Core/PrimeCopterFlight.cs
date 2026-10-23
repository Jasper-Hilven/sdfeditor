using System;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>What the Prime-copter's flight is doing (ticket 33.5).</summary>
    public enum CopterMove
    {
        /// <summary>Not flying: before phase 2 and after it, or on the ground.</summary>
        Grounded,
        /// <summary>Climbing straight up from where it starts, rotors spinning up.</summary>
        Rising,
        /// <summary>Flying its laps of the arena at 14 to 18 m.</summary>
        Laps,
        /// <summary>The dive's wind-up: moving over the circle and tilting in.</summary>
        DiveIn,
        /// <summary>The dive: down to <see cref="BossRules.diveHeight"/> over the circle.</summary>
        DiveDown,
        /// <summary>The dive's recovery: back up to where it was, then on with the laps.</summary>
        DiveUp,
        /// <summary>The last sac burst: sputtering down to the wreck's place.</summary>
        Crashing,
        /// <summary>Down on the wreck's place, rotors winding down.</summary>
        Down,
    }

    /// <summary>
    /// The flight of the Prime-copter (ticket 33, section 6, phase 2): 24's <see cref="HelicopterPilot"/> with the boss's numbers
    /// (<see cref="BossRules"/>: the body 14 to 18 m over the floor, never under 9 m over anything under or ahead of it, 6 m/s, 8 m inside
    /// the wall) flies the laps, and this adds what the attacks need: the dive (move over a circle on the floor, drop to 6 m, climb back),
    /// the crash (down to the wreck's place in the seconds the state machine gives it) and the take-off from where the copter starts or
    /// lands. Plain C#, so EditMode tests fly it without a scene; time moves only through <see cref="Tick"/>. It owns the body's pose
    /// (position, yaw, nose-down pitch, roll) and the rotors' <see cref="Spin"/>; the scene object follows it.
    /// </summary>
    public sealed class PrimeCopterFlight
    {
        private const float DiveIntoPitch = 18, DivePitch = 28, CrashSpinPerSecond = 540;

        private readonly BossRules rules;
        private readonly HelicopterPilot pilot;
        private readonly Func<float, float, float> surface;
        private readonly float floor;

        private Vector3 position;
        private float yaw, pitch, roll, spin;
        private float timer, duration;
        private Vector3 from, point;
        private float diveBottom, diveStartY, diveWindUp, diveActive, diveRecover;
        private float crashYaw;
        private bool landedRaised;

        /// <param name="rules">The boss's numbers.</param>
        /// <param name="areaMin">The play area's corners (x, z); it stays <see cref="BossRules.copterMargin"/> inside.</param>
        /// <param name="floor">The floor's height: the cruise and the dive are measured from it.</param>
        /// <param name="surface">The height of the highest solid surface at (x, z): the floor, a shelter's cap, the Royal Box.</param>
        public PrimeCopterFlight(BossRules rules, Vector2 areaMin, Vector2 areaMax, float floor, Func<float, float, float> surface, System.Random random = null)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.floor = floor;
            this.surface = surface ?? ((x, z) => floor);
            pilot = new HelicopterPilot(areaMin, areaMax, this.surface, random)
            {
                Margin = rules.copterMargin,
                MinHeight = rules.copterMinHeight,
                MaxHeight = rules.copterMaxHeight,
                Clearance = rules.copterClearance,
                // Over a raised surface (a shelter's cap, the dais) it keeps to the top of its cruise
                OverBases = rules.copterMaxHeight,
                Floor = floor,
                Speed = rules.copterSpeed,
                EngagedSpeed = rules.copterSpeed * 0.5f,
                Acceleration = 2,
                ClimbSpeed = 4,
                SinkSpeed = 2,
                TurnSpeed = 60,
                MaxLean = 12,
                // A 2 m pillar must not slip between the points it looks at: one every 2.5 m or so of the 18 m ahead
                LookSamples = 8,
            };
        }

        public CopterMove Move { get; private set; } = CopterMove.Grounded;

        /// <summary>The body's origin (its skids' underside): where the model's root stands.</summary>
        public Vector3 Position => position;

        public Vector3 Velocity { get; private set; }

        /// <summary>Degrees about y, as <see cref="HelicopterPilot.Yaw"/>.</summary>
        public float Yaw => yaw;

        /// <summary>Nose down (positive) or up, degrees.</summary>
        public float Pitch => pitch;

        /// <summary>Leaning right (negative, about z) or left, degrees.</summary>
        public float Roll => roll;

        /// <summary>How fast the rotors turn, 0 to 1: spinning up after a take-off, sputtering in the crash, winding down on the wreck.</summary>
        public float Spin => spin;

        /// <summary>Where it is going over the floor while it flies its laps.</summary>
        public Vector3 Waypoint => pilot.Waypoint;

        /// <summary>The flight's pilot, for tests and gizmos.</summary>
        public HelicopterPilot Pilot => pilot;

        /// <summary>Whether it is in the air flying (not on the ground, not crashing).</summary>
        public bool Flying => Move != CopterMove.Grounded && Move != CopterMove.Crashing && Move != CopterMove.Down;

        /// <summary>Whether a dive is on (its wind-up, the drop or the climb): no new one starts, no new laps until it ends.</summary>
        public bool Diving => Move == CopterMove.DiveIn || Move == CopterMove.DiveDown || Move == CopterMove.DiveUp;

        /// <summary>The dive's circle's centre on the floor while it dives.</summary>
        public Vector3 DivePoint => point;

        /// <summary>Where the crash ends.</summary>
        public Vector3 CrashPoint => point;

        /// <summary>The dive reached the bottom, over its circle: Prime's hands slam; the circle's centre.</summary>
        public event Action<Vector3> DiveBottomed;

        /// <summary>The crash ended: the copter is down at <see cref="CrashPoint"/>.</summary>
        public event Action Landed;

        /// <summary>On the ground at <paramref name="ground"/> facing <paramref name="yaw"/>: it rises straight up, then flies its laps.</summary>
        public void Takeoff(Vector3 ground, float yaw)
        {
            pilot.Start(ground, yaw);
            position = ground;
            this.yaw = yaw;
            pitch = roll = 0;
            spin = 0.2f;
            Velocity = Vector3.zero;
            Move = CopterMove.Rising;
        }

        /// <summary>
        /// The dive (ticket 33, phase 2): during <paramref name="windUp"/> it moves over the circle at <paramref name="floorPoint"/> tilting
        /// in, during <paramref name="active"/> it drops to <see cref="BossRules.diveHeight"/> over the floor (never within 3 m of a
        /// surface under it) and raises <see cref="DiveBottomed"/>, during <paramref name="recover"/> it climbs back. Only from the laps.
        /// </summary>
        public bool BeginDive(Vector3 floorPoint, float windUp, float active, float recover)
        {
            if (Move != CopterMove.Laps)
                return false;
            point = floorPoint;
            from = position;
            diveWindUp = Mathf.Max(0.05f, windUp);
            diveActive = Mathf.Max(0.05f, active);
            diveRecover = Mathf.Max(0.05f, recover);
            diveBottom = Mathf.Max(floorPoint.y + rules.diveHeight, surface(floorPoint.x, floorPoint.z) + 3);
            diveStartY = position.y;
            diveBottom = Mathf.Min(diveBottom, diveStartY);
            timer = diveWindUp;
            Move = CopterMove.DiveIn;
            return true;
        }

        /// <summary>
        /// The crash: down to <paramref name="floorPoint"/> in <paramref name="seconds"/> (the state machine's warning), spinning and
        /// sputtering. From any flight.
        /// </summary>
        public void BeginCrash(Vector3 floorPoint, float seconds)
        {
            if (Move == CopterMove.Crashing || Move == CopterMove.Down)
                return;
            point = floorPoint;
            from = position;
            duration = Mathf.Max(0.05f, seconds);
            timer = duration;
            crashYaw = yaw;
            landedRaised = false;
            Move = CopterMove.Crashing;
        }

        /// <summary>Down at the crash point now (the state machine says the crash landed), whatever was left of the fall.</summary>
        public void Land()
        {
            if (Move == CopterMove.Down)
                return;
            position = point;
            Velocity = Vector3.zero;
            pitch = 6;
            roll = -10;
            Move = CopterMove.Down;
            if (!landedRaised)
            {
                landedRaised = true;
                Landed?.Invoke();
            }
        }

        /// <summary>Off for good (phase 3: the wreck's model is there instead).</summary>
        public void Ground()
        {
            Move = CopterMove.Grounded;
            Velocity = Vector3.zero;
            spin = 0;
        }

        /// <summary>
        /// Lets <paramref name="dt"/> pass. <paramref name="faceTarget"/> turns its nose to a point while it flies its laps (the cannon's
        /// aim): it slows to half its speed.
        /// </summary>
        public void Tick(float dt, Vector3? faceTarget = null)
        {
            if (dt <= 0)
                return;
            var before = position;
            switch (Move)
            {
                case CopterMove.Rising:
                case CopterMove.Laps:
                    pilot.Tick(dt, faceTarget);
                    position = pilot.Position;
                    yaw = pilot.Yaw;
                    pitch = pilot.Pitch;
                    roll = pilot.Roll;
                    if (Move == CopterMove.Rising && !pilot.TakingOff)
                        Move = CopterMove.Laps;
                    spin = Mathf.MoveTowards(spin, 1, dt * 0.8f);
                    break;
                case CopterMove.DiveIn:
                    DiveIn(dt);
                    break;
                case CopterMove.DiveDown:
                    DiveDown(dt);
                    break;
                case CopterMove.DiveUp:
                    DiveUp(dt);
                    break;
                case CopterMove.Crashing:
                    Crash(dt);
                    break;
                case CopterMove.Down:
                    spin = Mathf.MoveTowards(spin, 0, dt * 0.35f);
                    break;
                default:
                    spin = Mathf.MoveTowards(spin, 0, dt);
                    break;
            }
            Velocity = (position - before) / dt;
        }

        // ---------------------------------------------------------------- The dive

        private void DiveIn(float dt)
        {
            timer -= dt;
            // Over the circle by the end of the wind-up, nose down and turning to it
            var flat = new Vector3(point.x - position.x, 0, point.z - position.z);
            var step = flat.magnitude / Mathf.Max(dt, timer + dt) * dt;
            position += flat.normalized * Mathf.Min(flat.magnitude, step);
            if (flat.sqrMagnitude > 0.25f)
                yaw = Mathf.MoveTowardsAngle(yaw, MachineAim.YawTo(flat), 90 * dt);
            pitch = Mathf.MoveTowards(pitch, DiveIntoPitch, 40 * dt);
            roll = Mathf.MoveTowards(roll, 0, 30 * dt);
            if (timer > 0)
                return;
            position = new Vector3(point.x, position.y, point.z);
            from = position;
            timer = diveActive;
            Move = CopterMove.DiveDown;
        }

        private void DiveDown(float dt)
        {
            timer -= dt;
            // Falling: slow at first, fastest at the bottom
            var t = 1 - Mathf.Clamp01(timer / diveActive);
            position.y = Mathf.Lerp(from.y, diveBottom, t * t);
            pitch = Mathf.MoveTowards(pitch, DivePitch, 60 * dt);
            if (timer > 0)
                return;
            position.y = diveBottom;
            DiveBottomed?.Invoke(point);
            from = position;
            timer = diveRecover;
            Move = CopterMove.DiveUp;
        }

        private void DiveUp(float dt)
        {
            timer -= dt;
            var t = 1 - Mathf.Clamp01(timer / diveRecover);
            // Back to where the dive began, easing out
            position.y = Mathf.Lerp(diveBottom, diveStartY, Mathf.SmoothStep(0, 1, t));
            pitch = Mathf.MoveTowards(pitch, 0, 25 * dt);
            if (timer > 0)
                return;
            pilot.Place(position, yaw);
            pitch = roll = 0;
            Move = CopterMove.Laps;
        }

        // ---------------------------------------------------------------- The crash

        private void Crash(float dt)
        {
            timer -= dt;
            var t = 1 - Mathf.Clamp01(timer / duration);
            // Hurtling to the wreck's place, falling faster and faster, spinning, rolling and sputtering
            var along = Mathf.SmoothStep(0, 1, t);
            position.x = Mathf.Lerp(from.x, point.x, along);
            position.z = Mathf.Lerp(from.z, point.z, along);
            position.y = Mathf.Lerp(from.y, point.y, t * t);
            crashYaw += CrashSpinPerSecond * (1 - t * 0.7f) * dt;
            yaw = crashYaw;
            pitch = Mathf.Lerp(pitch, 12 + 20 * t, Mathf.Clamp01(dt * 4));
            roll = Mathf.Sin(t * 22) * 22;
            spin = 0.45f + 0.35f * Mathf.Sin(t * 40);
            if (timer <= 0)
                Land();
        }
    }
}
