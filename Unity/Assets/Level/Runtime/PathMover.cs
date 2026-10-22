using System.Collections.Generic;
using SdfBattle;
using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// A train on a closed path and a fixed timetable (ticket 27.3, the level file's <c>mover</c>): every frame it places each car where
    /// <see cref="PathTimetable"/> puts it for the round clock (<see cref="Clock"/>), so a car's place is a function of the clock alone and
    /// a respawn or a new round saves nothing. The cars are kinematic bodies with convex colliders, so the physics sees them move and a
    /// player standing on one is carried (<see cref="FirstPersonController"/>, through <see cref="PathMoverCar.Carry"/>).
    /// <para>
    /// The path, heights and car lengths are in Unity's coordinates (the level builder mirrors x as it does for placements); the cars are
    /// children of this object, model forward +z, origin at the middle of the underside, in train order.
    /// </para>
    /// <para>
    /// Ticket 27.10 puts behaviour on it, all from the level file's <c>mover</c> options and off by default: what the cars do to what they
    /// meet (<see cref="Impact"/>, carried out by <see cref="MoverStrikes"/>), a puff from the first car every so often and a whistle puff before
    /// each climb (<see cref="CombatFx.TrainPuff"/>), a carving <see cref="UnityEngine.AI.NavMeshObstacle"/> on each car, and the distance within
    /// which bots wait to cross the track (<see cref="BotNavigation.MustWait"/>).
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class PathMover : MonoBehaviour
    {
        [Tooltip("The path's corners (x, z) in metres, closed by a leg back to the first")]
        public Vector2[] path;
        [Tooltip("(arc length, y) keys of the cars' height along the path; none for flat")]
        public Vector2[] heights;
        [Tooltip("Each car's length along the path, the first car ahead")]
        public float[] carLengths;
        public float speed = 7;
        [Tooltip("Metres between a car's rear and the next car's front")]
        public float gap = 0.5f;
        [Tooltip("Seconds the timetable is ahead at clock 0")]
        public float phase;

        [Header("What the cars do (ticket 27.10)")]
        [Tooltip("Metres a scout or tank in a car's way is pushed off its side; 0: not pushed")]
        public float pushes;
        [Tooltip("Damage of one hit by a car; 0: none")]
        public float damage;
        [Tooltip("How the kill feed names a kill by it")]
        public string hazardName = "Mover";
        [Tooltip("Seconds before one car hits the same victim again")]
        public float cooldown = 1;
        [Tooltip("Seconds between puffs from the first car; 0: none")]
        public float puffEvery;
        [Tooltip("Where the puff leaves the first car, in its own frame")]
        public Vector3 puffAt = new Vector3(0, 2.5f, 0);
        [Tooltip("Seconds before each climb of the path that a whistle puff is given; 0: none")]
        public float whistleLead;
        [Tooltip("Metres from the head within which a bot waits at the track's edge to cross it; 0: bots do not wait")]
        public float crossingWarn;
        [Tooltip("Each car cuts the navmeshes where it is")]
        public bool carveNavmesh = true;

        private static readonly List<PathMover> Movers = new List<PathMover>();

        private PathTimetable timetable;
        private PathMoverCar[] cars;
        private MoverImpact impact;
        private List<float> climbs;
        private float cueClock = float.NaN;

        /// <summary>The movers of the scene.</summary>
        public static IReadOnlyList<PathMover> All => Movers;

        /// <summary>What a car does to what it meets: <see cref="pushes"/>, <see cref="damage"/>, <see cref="hazardName"/>, <see cref="cooldown"/>.</summary>
        public MoverImpact Impact => impact ??= new MoverImpact(hazardName, pushes, damage, cooldown);

        /// <summary>Half the width of the train's track plus a little: bots and tanks keep out of it while a car comes.</summary>
        public float TrackHalfWidth
        {
            get
            {
                var half = 1f;
                foreach (var car in Cars)
                    half = Mathf.Max(half, car.LocalBounds.extents.x);
                return half + 0.3f;
            }
        }

        /// <summary>The cars, the children of this object that are <see cref="PathMoverCar"/>s, in train order (the first ahead).</summary>
        public PathMoverCar[] Cars
        {
            get
            {
                if (cars == null || cars.Length != transform.childCount)
                {
                    var found = new System.Collections.Generic.List<PathMoverCar>();
                    foreach (Transform child in transform)
                        if (child.TryGetComponent<PathMoverCar>(out var car))
                            found.Add(car);
                    cars = found.ToArray();
                }
                return cars;
            }
        }

        /// <summary>The timetable of this mover; built on first use.</summary>
        public PathTimetable Timetable => timetable ??= new PathTimetable(new ClosedPath(path, heights), speed, phase, gap, carLengths);

        /// <summary>
        /// The round clock in seconds: the match's (<see cref="MatchState.RoundClock"/>, from 0 at each round's start) while one runs,
        /// else <see cref="Time.time"/>.
        /// </summary>
        public static float Clock
        {
            get
            {
                var state = Match.Current != null ? Match.Current.State : null;
                return state != null && state.Started ? state.RoundClock : Time.time;
            }
        }

        private void OnEnable()
        {
            timetable = null;
            cars = null;
            impact = null;
            climbs = null;
            cueClock = float.NaN;
            Movers.Add(this);
        }

        private void OnDisable() => Movers.Remove(this);

        private void Start()
        {
            if (carveNavmesh)
                foreach (var car in Cars)
                    car.Carve(true);
            if (Impact.Hits && GetComponent<MoverStrikes>() == null)
                gameObject.AddComponent<MoverStrikes>();
        }

        private void Update()
        {
            var clock = Clock;
            Place(clock);
            GiveCues(clock);
        }

        /// <summary>
        /// The puffs between the last frame's clock and <paramref name="clock"/>: one from the first car's <see cref="puffAt"/> every
        /// <see cref="puffEvery"/> seconds, and a bigger one <see cref="whistleLead"/> seconds before the head reaches each climb of the path
        /// (a bridge). A clock that went back (a new round) or jumped on (a pause) gives none.
        /// </summary>
        private void GiveCues(float clock)
        {
            var before = cueClock;
            cueClock = clock;
            if (float.IsNaN(before) || clock <= before || clock - before > 1 || path == null || carLengths == null)
                return;
            var fx = Combat.Current != null ? Combat.Current.Fx : null;
            var cars = Cars;
            if (fx == null || cars.Length == 0)
                return;
            var at = cars[0].transform.TransformPoint(puffAt);
            var back = -cars[0].transform.forward;
            if (MoverCues.Ticks(puffEvery, before, clock) > 0)
                fx.TrainPuff(at, back, false);
            climbs ??= Timetable.Path.ClimbStarts(1);
            if (MoverCues.Whistles(Timetable, climbs, whistleLead, before, clock))
                fx.TrainPuff(at, back, true);
        }

        /// <summary>
        /// Whether <paramref name="point"/> is on the track of any mover, within <paramref name="margin"/> metres of its edge, at the
        /// height of the track there (not under a bridge). A flag dropped on the track stays where it lies: the navmesh is cut there only
        /// while a car is.
        /// </summary>
        public static bool OnAnyTrack(Vector3 point, float margin)
        {
            foreach (var mover in Movers)
            {
                if (mover.path == null || mover.carLengths == null)
                    continue;
                var path = mover.Timetable.Path;
                var distance = path.DistanceTo(new Vector2(point.x, point.z), out var arc);
                if (distance <= mover.TrackHalfWidth + margin && Mathf.Abs(point.y - path.HeightAt(arc)) < 2.5f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Points (x, z) along both sides of every car within <paramref name="range"/> metres of <paramref name="near"/>, a metre and a half
        /// apart, for machines that steer round players and other machines to steer round the train too.
        /// </summary>
        public static void AvoidPoints(Vector3 near, float range, List<Vector2> into)
        {
            foreach (var mover in Movers)
            {
                foreach (var car in mover.Cars)
                {
                    if ((car.transform.position - near).sqrMagnitude > (range + 4) * (range + 4))
                        continue;
                    var bounds = car.LocalBounds;
                    var side = Mathf.Max(0.8f, bounds.extents.x - 0.1f);
                    for (var z = -bounds.extents.z; z <= bounds.extents.z + 0.01f; z += 1.5f)
                        for (var sign = -1; sign <= 1; sign += 2)
                        {
                            var point = car.transform.TransformPoint(new Vector3(bounds.center.x + side * sign, 0, bounds.center.z + z));
                            if ((point - near).sqrMagnitude < range * range)
                                into.Add(new Vector2(point.x, point.z));
                        }
                }
            }
        }

        /// <summary>Puts every car where the timetable has it at <paramref name="clock"/>, remembering the step each took.</summary>
        public void Place(float clock)
        {
            if (path == null || carLengths == null)
                return;
            var moved = false;
            var cars = Cars;
            for (var i = 0; i < cars.Length && i < Timetable.CarCount; i++)
            {
                cars[i].MoveTo(Timetable.PoseOf(i, clock));
                moved = true;
            }
            // The colliders have to be where the cars are before anything this frame moves (the player's controller)
            if (moved)
                Physics.SyncTransforms();
        }
    }
}
