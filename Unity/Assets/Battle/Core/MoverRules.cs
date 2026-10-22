using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>What a mover's car can meet on its way (ticket 27.10): only <see cref="MoverImpact.Affects"/> decides who it knocks aside.</summary>
    public enum MoverTarget
    {
        /// <summary>A player or a bot on foot.</summary>
        Scout,
        Tank,
        Helicopter,
        Turret,
        Flag,
        Pod,
        Other,
    }

    /// <summary>
    /// What a moving car does to what it meets (ticket 27.10; the level file's <c>mover</c> options <c>pushes</c>, <c>damage</c>, <c>name</c>
    /// and <c>cooldown</c>): a scout or tank in its way is pushed <see cref="Distance"/> metres off its side and takes
    /// <see cref="Amount"/> damage, at most once per car and victim in <see cref="Cooldown"/> seconds. The damage is a hazard's
    /// (<see cref="Damage.ByHazard"/>): the <see cref="Name"/> and no team, weapon, kill credit or spores. A car with no push and no damage
    /// is only a body in the way. Plain C#, so the rules are tested without a scene.
    /// </summary>
    public sealed class MoverImpact
    {
        public MoverImpact(string name, float distance, float amount, float cooldown = 1)
        {
            Name = string.IsNullOrEmpty(name) ? "Mover" : name;
            Distance = Mathf.Max(0, distance);
            Amount = Mathf.Max(0, amount);
            Cooldown = Mathf.Max(0, cooldown);
        }

        /// <summary>The mover as the kill feed names it ("Toy train").</summary>
        public string Name { get; }

        /// <summary>Metres off the car's side a victim is put.</summary>
        public float Distance { get; }

        public float Amount { get; }

        /// <summary>Seconds before the same car may hit the same victim again.</summary>
        public float Cooldown { get; }

        /// <summary>Whether the car hits anything at all.</summary>
        public bool Hits => Distance > 0 || Amount > 0;

        /// <summary>Scouts and tanks are knocked aside; flags, pods, turrets (and the flying helicopters) ignore the car.</summary>
        public static bool Affects(MoverTarget target) => target == MoverTarget.Scout || target == MoverTarget.Tank;

        /// <summary>The damage of one hit, from the car's place.</summary>
        public Damage DamageFrom(Vector3 from) => Damage.ByHazard(Name, Amount, from);

        /// <summary>
        /// Where a victim at <paramref name="victim"/> is put by a car at <paramref name="car"/> heading <paramref name="carYaw"/> (degrees
        /// as Unity turns, 0 facing +z) that is <paramref name="halfWidth"/> wide on each side: on the side of the track it is on (the right
        /// one if it is dead on the middle), <see cref="Distance"/> metres past the car's side, as far along the track as it was and at
        /// the same height. Never pulled back towards the car if it is already further out.
        /// </summary>
        public Vector3 PushedTo(Vector3 victim, Vector3 car, float carYaw, float halfWidth)
        {
            var yaw = carYaw * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
            var right = new Vector3(forward.z, 0, -forward.x);
            var offset = victim - car;
            var along = offset.x * forward.x + offset.z * forward.z;
            var lateral = offset.x * right.x + offset.z * right.z;
            var side = lateral < 0 ? -1f : 1f;
            var landing = side * Mathf.Max(Mathf.Abs(lateral), Mathf.Max(0, halfWidth) + Distance);
            var flat = car + forward * along + right * landing;
            return new Vector3(flat.x, victim.y, flat.z);
        }
    }

    /// <summary>
    /// Which (victim, car) pairs were hit when, so a car hits the same victim at most once in the cooldown (ticket 27.10). The clock is the
    /// round's; a clock that goes back (a new round) forgets, so nobody is spared by a hit of the round before.
    /// </summary>
    public sealed class MoverHitLog
    {
        private readonly Dictionary<(object victim, object car), float> last = new Dictionary<(object, object), float>();

        /// <summary>Whether <paramref name="car"/> may hit <paramref name="victim"/> at <paramref name="now"/>; if so it is noted as hit.</summary>
        public bool TryHit(object victim, object car, float now, float cooldown)
        {
            var key = (victim, car);
            if (last.TryGetValue(key, out var before) && now >= before && now - before < cooldown)
                return false;
            last[key] = now;
            return true;
        }

        public void Clear() => last.Clear();
    }

    /// <summary>When a mover gives off puffs (ticket 27.10): the chimney's every so often, the whistle's a few seconds before a climb.</summary>
    public static class MoverCues
    {
        /// <summary>How many multiples of <paramref name="interval"/> the clock passed going from <paramref name="from"/> to <paramref name="to"/> (0 if it went back or there is no interval).</summary>
        public static int Ticks(float interval, float from, float to)
        {
            if (interval <= 0 || to <= from)
                return 0;
            return Mathf.Max(0, Mathf.FloorToInt(to / interval) - Mathf.FloorToInt(from / interval));
        }

        /// <summary>
        /// Whether the head, going from <paramref name="headFrom"/> to <paramref name="headTo"/> metres along a path of
        /// <paramref name="length"/> metres, passed the place <paramref name="at"/> metres along it on any lap (false if it went back).
        /// </summary>
        public static bool Passed(float at, float length, float headFrom, float headTo)
        {
            if (length <= 0 || headTo <= headFrom)
                return false;
            return Mathf.FloorToInt((headTo - at) / length) > Mathf.FloorToInt((headFrom - at) / length);
        }

        /// <summary>
        /// Whether a whistle sounds going from clock <paramref name="from"/> to <paramref name="to"/>: the head passed
        /// <paramref name="lead"/> seconds' travel before one of the path's climbs (<see cref="ClosedPath.ClimbStarts"/>).
        /// </summary>
        public static bool Whistles(PathTimetable timetable, IReadOnlyList<float> climbs, float lead, float from, float to)
        {
            if (lead <= 0)
                return false;
            foreach (var climb in climbs)
                if (Passed(climb - timetable.Speed * lead, timetable.Path.Length, timetable.HeadDistance(from), timetable.HeadDistance(to)))
                    return true;
            return false;
        }
    }

    /// <summary>What a bot at the edge of a mover's track should do (ticket 27.10): the train is near or on the crossing, or it is not.</summary>
    public readonly struct CrossingWait
    {
        public CrossingWait(bool wait, float secondsUntilClear)
        {
            Wait = wait;
            SecondsUntilClear = secondsUntilClear;
        }

        /// <summary>Stay at the track's edge.</summary>
        public bool Wait { get; }

        /// <summary>Seconds until the whole train, with its margin, is past the crossing; 0 when there is no need to wait.</summary>
        public float SecondsUntilClear { get; }
    }

    /// <summary>
    /// The crossing check of the bots (ticket 27.10): a bot whose path crosses a mover's track waits at the edge while the train ahead is
    /// within <c>warn</c> metres of the crossing along the track, or is on it, until its last car has passed. The train is a function of
    /// the round's clock, so this is too; plain C#, tested without a scene.
    /// </summary>
    public static class MoverCrossing
    {
        /// <summary>
        /// Whether to wait to cross the track at <paramref name="arc"/> (metres along the path) at <paramref name="clock"/>: the head is no
        /// more than <paramref name="warn"/> metres before it, or the train (with <paramref name="margin"/> metres at both ends) is on it.
        /// </summary>
        public static CrossingWait Check(PathTimetable timetable, float clock, float arc, float warn, float margin = 2)
        {
            var length = timetable.Path.Length;
            var margins = Mathf.Max(0, margin);
            // How far the head has to go to reach the crossing, and how far past it the head is
            var ahead = timetable.Path.Wrap(arc - timetable.HeadDistance(clock));
            var past = length - ahead;
            var speed = Mathf.Max(0.01f, timetable.Speed);
            if (ahead <= margins)
                return new CrossingWait(true, (ahead + timetable.TrainLength + margins) / speed);
            if (past <= timetable.TrainLength + margins)
                return new CrossingWait(true, (timetable.TrainLength + margins - past) / speed);
            if (ahead - margins <= warn)
                return new CrossingWait(true, (ahead + timetable.TrainLength + margins) / speed);
            return new CrossingWait(false, 0);
        }

        /// <summary>
        /// Whether a walk from <paramref name="from"/> to <paramref name="to"/> (x, z) that crosses the track should wait now, and for how
        /// long; the nearest crossing that needs it, if the walk crosses the path more than once.
        /// </summary>
        public static CrossingWait CheckWalk(PathTimetable timetable, float clock, Vector2 from, Vector2 to, float warn, float margin = 2)
        {
            foreach (var crossing in timetable.Path.Crossings(from, to))
            {
                var check = Check(timetable, clock, crossing.Arc, warn, margin);
                if (check.Wait)
                    return check;
            }
            return new CrossingWait(false, 0);
        }
    }
}
