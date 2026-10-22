using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The angles a machine's gun turns to (ticket 24): the turning piece's yaw (the tank's turret, the turret's head) and the barrel's
    /// tilt. The barrels point along their pivot's local +z (the meshes: the tank's barrel from z 0 to 0.25 m, the turret's to 0.58 m, x and
    /// y round 0), and a positive angle about local x turns +z down towards -y (Unity's left-handed rotation: Quaternion.Euler(a, 0, 0) *
    /// forward = (0, -sin a, cos a)), so raising the muzzle by an elevation is a <b>negative</b> tilt (<see cref="TiltPerElevation"/>).
    /// </summary>
    public static class MachineAim
    {
        /// <summary>The barrel's local x angle per degree of elevation (muzzle up): -1.</summary>
        public const float TiltPerElevation = -1;

        /// <summary>The yaw (degrees about y, 0 along +z, 90 along +x) of a direction in the turning piece's parent space.</summary>
        public static float YawTo(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        /// <summary>Degrees a direction rises over the level (negative: down).</summary>
        public static float ElevationOf(Vector3 direction) =>
            Mathf.Atan2(direction.y, Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z)) * Mathf.Rad2Deg;

        /// <summary>
        /// The barrel's local x angle that points it along <paramref name="direction"/> (in the turning piece's space, the piece already
        /// turned towards it), the elevation held between <paramref name="lowest"/> (down, negative) and <paramref name="highest"/>.
        /// </summary>
        public static float TiltFor(Vector3 direction, float lowest, float highest) =>
            TiltPerElevation * Mathf.Clamp(ElevationOf(direction), lowest, highest);

        /// <summary>Where a barrel tilted by <paramref name="tilt"/> degrees about its local x points, in its parent's space.</summary>
        public static Vector3 BarrelForward(float tilt)
        {
            var radians = tilt * Mathf.Deg2Rad;
            return new Vector3(0, -Mathf.Sin(radians), Mathf.Cos(radians));
        }
    }

    /// <summary>
    /// A tank steering round what stands in its way (ticket 24): players and other machines ahead within a few metres slow it, down to a
    /// stop for one right in front, and turn it aside, away from the side they are on. Seen from above (x, z). It never pushes anyone.
    /// </summary>
    public static class MachineSteering
    {
        /// <summary>
        /// The speed factor (0 stop, 1 full) for driving from <paramref name="position"/> along <paramref name="heading"/> (a unit
        /// vector) with <paramref name="others"/> about, each to be kept <paramref name="radius"/> (both bodies) from its middle, looking
        /// <paramref name="lookAhead"/> metres ahead; <paramref name="steer"/> the sideways direction (scaled by how urgent) to turn to.
        /// </summary>
        public static float Avoid(Vector2 position, Vector2 heading, IReadOnlyList<Vector2> others, float radius, float lookAhead, out Vector2 steer)
        {
            steer = Vector2.zero;
            var factor = 1f;
            if (others == null)
                return factor;
            var right = new Vector2(heading.y, -heading.x);
            foreach (var other in others)
            {
                var to = other - position;
                var along = Vector2.Dot(to, heading);
                var side = Vector2.Dot(to, right);
                var distance = to.magnitude;
                // Touching already: stop if it is ahead at all, else drive on away from it
                if (distance < radius)
                {
                    if (along > 0)
                        factor = 0;
                    steer -= right * Mathf.Sign(side == 0 ? 1 : side);
                    continue;
                }
                if (along <= 0 || along > lookAhead || Mathf.Abs(side) >= radius + 0.3f)
                    continue;
                var urgency = 1 - (along - radius) / Mathf.Max(0.01f, lookAhead - radius);
                factor = Mathf.Min(factor, Mathf.Clamp01((along - radius) / Mathf.Max(0.01f, lookAhead - radius) * 1.5f));
                // Away from the side it is on; one dead ahead: to the right
                steer -= right * (side >= 0 ? 1 : -1) * Mathf.Clamp01(urgency);
            }
            return factor;
        }
    }

    /// <summary>
    /// A helicopter's flight (ticket 24): from the ground at its side's machine zone straight up, then from one random point over the play
    /// area to the next, 6 to 12 m over the ground, smoothly (its speed changes by an acceleration, it slows coming in and while it fights),
    /// never leaving the play area (so never into the hills round it), and always <see cref="MinHeight"/> or more over whatever stands
    /// under it and ahead of it: over a castle it climbs early to clear the tops (about 20 m) and holds back while it climbs. Its nose
    /// turns to its target or along its way, and it leans into its flight. Plain C#: the scene gives it the height of the highest surface
    /// under a point (<see cref="Surface"/>, a ray down) and moves its body to <see cref="Position"/>.
    /// </summary>
    public sealed class HelicopterPilot
    {
        private readonly System.Random random;
        private float cruise;
        private float holdFor;
        private float needIn;
        private float needed;
        private float here;

        public HelicopterPilot(Vector2 areaMin, Vector2 areaMax, Func<float, float, float> surface, System.Random random = null)
        {
            AreaMin = areaMin;
            AreaMax = areaMax;
            Surface = surface ?? ((x, z) => 0);
            this.random = random ?? new System.Random();
        }

        /// <summary>The play area's corners (x, z) it stays in, <see cref="Margin"/> inside.</summary>
        public Vector2 AreaMin { get; set; }
        public Vector2 AreaMax { get; set; }

        /// <summary>The height of the highest solid surface at (x, z): the ground, or a castle's top.</summary>
        public Func<float, float, float> Surface { get; set; }

        /// <summary>The highest it may fly (a closed room's ceiling less its size); none under a sky.</summary>
        public float? Ceiling { get; set; }

        public float Margin = 6;
        public float MinHeight = 6;
        public float MaxHeight = 12;
        /// <summary>Over a raised surface (more than <see cref="RaisedAbove"/> over <see cref="Floor"/>, a base) it flies this high over the floor; none: as over the ground (6 to 12 m over what is under it). Set by <see cref="HelicopterLimits"/>.</summary>
        public float? OverBases { get; set; }
        public float Floor { get; set; }
        public float RaisedAbove = 2;
        public float Speed = 5;
        /// <summary>Its speed while it has a target: it slows to fire.</summary>
        public float EngagedSpeed = 1.5f;
        public float Acceleration = 2.5f;
        public float ClimbSpeed = 3;
        public float SinkSpeed = 1.5f;
        public float TurnSpeed = 90;
        /// <summary>Seconds of flight ahead it keeps clear of.</summary>
        public float LookAhead = 3;
        /// <summary>Degrees it leans per metre a second of speed, and at most.</summary>
        public float LeanPerSpeed = 2.5f, MaxLean = 16;

        public Vector3 Position { get; private set; }
        public Vector3 Velocity { get; private set; }
        public Vector3 Waypoint { get; private set; }
        public float Yaw { get; private set; }

        /// <summary>Nose down (positive) or up, degrees: leaning into the flight.</summary>
        public float Pitch { get; private set; }

        /// <summary>Leaning right (negative, about z) or left, degrees.</summary>
        public float Roll { get; private set; }

        /// <summary>Rising from the ground after a (re)spawn: it does not move across until clear.</summary>
        public bool TakingOff { get; private set; }

        /// <summary>The height it needs here and ahead: <see cref="MinHeight"/> over the highest surface on its way.</summary>
        public float Needed => needed;

        /// <summary>On the ground at <paramref name="ground"/>, facing <paramref name="yaw"/>: it rises first.</summary>
        public void Start(Vector3 ground, float yaw)
        {
            Position = ground;
            Velocity = Vector3.zero;
            Yaw = yaw;
            Pitch = Roll = 0;
            TakingOff = true;
            holdFor = 0;
            needIn = 0;
            cruise = MinHeight + (float)random.NextDouble() * (MaxHeight - MinHeight);
            Waypoint = new Vector3(ground.x, ground.y + cruise, ground.z);
        }

        /// <summary>
        /// Lets <paramref name="dt"/> pass. With <paramref name="faceTarget"/> it fights: slows to <see cref="EngagedSpeed"/> and turns its
        /// nose to it.
        /// </summary>
        public void Tick(float dt, Vector3? faceTarget = null)
        {
            if (dt <= 0)
                return;
            var position = Position;
            needIn -= dt;
            if (needIn <= 0)
            {
                // A few rays a second, not every frame
                needed = HeightNeeded(position, Velocity);
                here = Surface(position.x, position.z);
                needIn = 0.2f;
            }

            // Up or down to the cruise height over what is under it (6-12 m), never below what it needs to clear ahead
            var wantY = Mathf.Max(needed, OverBase(here) ?? here + cruise);
            if (Ceiling != null)
                wantY = Mathf.Min(wantY, Ceiling.Value);
            var dy = wantY - position.y;
            var vy = dy > 0 ? Mathf.Min(ClimbSpeed, dy * 1.5f) : Mathf.Max(-SinkSpeed, dy * 1.0f);

            // Across: to the waypoint, slowing in and while fighting; held back while too low for what is ahead, or still taking off
            if (TakingOff && position.y >= here + MinHeight * 0.75f)
            {
                TakingOff = false;
                PickWaypoint(position);
            }
            var flat = new Vector3(Waypoint.x - position.x, 0, Waypoint.z - position.z);
            var distance = flat.magnitude;
            var speed = faceTarget.HasValue ? EngagedSpeed : Speed;
            speed = Mathf.Min(speed, distance * 0.6f);
            if (TakingOff || holdFor > 0)
                speed = 0;
            if (position.y < needed - 0.5f)
                speed = Mathf.Min(speed, 1);
            var wanted = distance > 1e-3f ? flat / distance * speed : Vector3.zero;
            var velocity = Vector3.MoveTowards(new Vector3(Velocity.x, 0, Velocity.z), wanted, Acceleration * dt);
            velocity.y = vy;
            position += velocity * dt;
            position.x = Mathf.Clamp(position.x, AreaMin.x + Margin - 1, AreaMax.x - Margin + 1);
            position.z = Mathf.Clamp(position.z, AreaMin.y + Margin - 1, AreaMax.y - Margin + 1);
            Position = position;
            Velocity = velocity;

            if (holdFor > 0)
            {
                holdFor -= dt;
                if (holdFor <= 0)
                    PickWaypoint(position);
            }
            else if (!TakingOff && distance < 2.5f)
                holdFor = 0.5f + (float)random.NextDouble() * 2;

            // The nose: to the target, else along its way once it moves
            var face = faceTarget.HasValue ? faceTarget.Value - position : new Vector3(velocity.x, 0, velocity.z);
            face.y = 0;
            if (face.sqrMagnitude > (faceTarget.HasValue ? 0.01f : 0.25f))
                Yaw = Mathf.MoveTowardsAngle(Yaw, MachineAim.YawTo(face), TurnSpeed * dt);

            // Leaning into the flight: nose down going forward, rolled into a sideways drift
            var radians = Yaw * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(radians), 0, Mathf.Cos(radians));
            var right = new Vector3(forward.z, 0, -forward.x);
            var wantPitch = Mathf.Clamp(Vector3.Dot(velocity, forward) * LeanPerSpeed, -MaxLean, MaxLean);
            var wantRoll = Mathf.Clamp(-Vector3.Dot(velocity, right) * LeanPerSpeed, -MaxLean, MaxLean);
            var ease = Mathf.Clamp01(dt * 3);
            Pitch = Mathf.Lerp(Pitch, wantPitch, ease);
            Roll = Mathf.Lerp(Roll, wantRoll, ease);
        }

        /// <summary><see cref="MinHeight"/> over the highest surface under it and along its way for <see cref="LookAhead"/> seconds.</summary>
        public float HeightNeeded(Vector3 position, Vector3 velocity)
        {
            var flat = new Vector3(velocity.x, 0, velocity.z);
            // Ahead at least a few metres, even slow, so it never noses into a wall it is turning towards
            var ahead = flat.sqrMagnitude > 0.01f ? flat * LookAhead : Vector3.zero;
            if (ahead.sqrMagnitude > 0 && ahead.magnitude < 4)
                ahead = ahead.normalized * 4;
            var highest = float.NegativeInfinity;
            for (var i = 0; i <= 3; i++)
            {
                var point = position + ahead * (i / 3f);
                highest = Mathf.Max(highest, Surface(Mathf.Clamp(point.x, AreaMin.x, AreaMax.x), Mathf.Clamp(point.z, AreaMin.y, AreaMax.y)));
            }
            var needs = highest + MinHeight;
            return OverBase(highest) is { } over ? Mathf.Max(needs, over) : needs;
        }

        /// <summary>The height (y) it flies at over a surface at <paramref name="surface"/> if that is a base in a room (<see cref="OverBases"/>), else null.</summary>
        private float? OverBase(float surface) =>
            OverBases.HasValue && surface > Floor + RaisedAbove ? Floor + OverBases.Value : (float?)null;

        /// <summary>A new random point over the play area, <see cref="Margin"/> inside its edge, and a new cruise height.</summary>
        public void PickWaypoint(Vector3 from)
        {
            holdFor = 0;
            cruise = MinHeight + (float)random.NextDouble() * (MaxHeight - MinHeight);
            for (var attempt = 0; ; attempt++)
            {
                var x = Mathf.Lerp(AreaMin.x + Margin, AreaMax.x - Margin, (float)random.NextDouble());
                var z = Mathf.Lerp(AreaMin.y + Margin, AreaMax.y - Margin, (float)random.NextDouble());
                // Not just round the corner: somewhere across the field
                if (attempt < 6 && (new Vector2(x - from.x, z - from.z)).magnitude < 15)
                    continue;
                Waypoint = new Vector3(x, Surface(x, z) + cruise, z);
                return;
            }
        }
    }
}
