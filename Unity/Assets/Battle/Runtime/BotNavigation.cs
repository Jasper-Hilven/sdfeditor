using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SdfBattle
{
    /// <summary>
    /// The bots' way about a battle scene (ticket 23): queries on the navmesh the level builder bakes per battle scene (Level/Navigation
    /// Scout, a NavMeshSurface of the AI Navigation package for the <see cref="ScoutAgent"/> agent type: the players' capsule, step and
    /// slope, drop links down ledges). Plain queries, no NavMeshAgent: a bot walks the corners of its path through its own
    /// <see cref="SdfLevel.FirstPersonController"/>, by the same rules as you. Synchronous and single-threaded, so it runs in WebGL 2.
    /// <para>
    /// Without a navmesh (a scene not rebuilt since ticket 23) every query says so and the bots walk straight at their goals, and
    /// <see cref="Reachable"/> counts every point as reachable.
    /// </para>
    /// </summary>
    public static class BotNavigation
    {
        /// <summary>The agent type the bots walk as: the players' capsule (r 0.3 m and 5 cm to spare, 1.75 m), step 0.4 m, slope 45 degrees.</summary>
        public const string ScoutAgent = "Scout";

        /// <summary>The agent type ticket 24's tanks drive as: 0.8 m, ground only (no stairs, no wall-walks).</summary>
        public const string TankAgent = "Tank";

        private static int agentType = int.MinValue;
        private static NavMeshPath scratch;
        private static Vector3? home;

        /// <summary>The query filter of the scouts' agent type, with the area costs of the moment (<see cref="NavAreaCosts"/>: a hot burner ring costs more).</summary>
        public static NavMeshQueryFilter Filter => NavAreaCosts.Filter(AgentType);

        /// <summary>The id of the scouts' agent type; 0 (the default type) when the project has none of that name.</summary>
        public static int AgentType
        {
            get
            {
                if (agentType == int.MinValue)
                    agentType = AgentTypeOf(ScoutAgent) ?? 0;
                return agentType;
            }
        }

        /// <summary>The id of the agent type named <paramref name="name"/> in the project's navigation settings, or null.</summary>
        public static int? AgentTypeOf(string name)
        {
            for (var i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                var id = NavMesh.GetSettingsByIndex(i).agentTypeID;
                if (NavMesh.GetSettingsNameFromID(id) == name)
                    return id;
            }
            return null;
        }

        /// <summary>Whether the scene has a navmesh for the scouts (the level builder baked one).</summary>
        public static bool HasNavmesh => NavMesh.SamplePosition(HomeProbe, out _, 1000, Filter);

        /// <summary>Forgets what it found of the last scene (a new match).</summary>
        public static void Reset()
        {
            agentType = int.MinValue;
            home = null;
        }

        /// <summary>Somewhere on the island all of it is reached from: the red spawn (set by the bots at the start).</summary>
        public static Vector3 HomeProbe { get; set; }

        /// <summary>The nearest point of the navmesh to <paramref name="point"/> within <paramref name="radius"/> metres.</summary>
        public static bool Snap(Vector3 point, float radius, out Vector3 onMesh)
        {
            if (NavMesh.SamplePosition(point, out var hit, radius, Filter))
            {
                onMesh = hit.position;
                return true;
            }
            onMesh = point;
            return false;
        }

        /// <summary>
        /// The corners of the way from <paramref name="from"/> to <paramref name="to"/> into <paramref name="corners"/> (from first, to
        /// last); with a partial path (nothing reaches <paramref name="to"/>) its corners up to the nearest point, and false. Without a
        /// navmesh: the straight line, and false.
        /// </summary>
        public static bool Path(Vector3 from, Vector3 to, List<Vector3> corners)
        {
            corners.Clear();
            scratch ??= new NavMeshPath();
            if (!Snap(from, 2.5f, out var start) || !Snap(to, 4, out var end))
            {
                corners.Add(from);
                corners.Add(to);
                return false;
            }
            if (!NavMesh.CalculatePath(start, end, Filter, scratch) || scratch.status == NavMeshPathStatus.PathInvalid)
            {
                corners.Add(from);
                corners.Add(to);
                return false;
            }
            var count = scratch.GetCornersNonAlloc(CornerBuffer);
            for (var i = 0; i < count; i++)
                corners.Add(CornerBuffer[i]);
            return scratch.status == NavMeshPathStatus.PathComplete;
        }

        private static readonly Vector3[] CornerBuffer = new Vector3[256];

        /// <summary>Whether a straight walk from <paramref name="from"/> along the navmesh to <paramref name="to"/> meets no edge.</summary>
        public static bool Clear(Vector3 from, Vector3 to) => !NavMesh.Raycast(from, to, out _, Filter);

        /// <summary>Metres past the track's edge a bot waits when a train is near its crossing.</summary>
        public const float WaitBeyondEdge = 1;

        /// <summary>
        /// The crossing check (ticket 27.10): whether a bot at <paramref name="feet"/> walking the <paramref name="corners"/> from index
        /// <paramref name="corner"/> on should stand still now, because within <paramref name="lookAhead"/> metres its way crosses the track
        /// of a mover (with <c>crossing</c> set) whose train is within that mover's crossing distance of the place, or is on it
        /// (<see cref="MoverCrossing"/>). It waits at the track's edge, <see cref="WaitBeyondEdge"/> short of it: a bot that is already
        /// nearer than the track's edge is on the track and walks on to get off it.
        /// </summary>
        public static bool MustWait(Vector3 feet, List<Vector3> corners, int corner, float lookAhead, float clock)
        {
            var movers = SdfLevel.PathMover.All;
            for (var m = 0; m < movers.Count; m++)
            {
                var mover = movers[m];
                if (mover.crossingWarn <= 0 || mover.path == null || mover.carLengths == null)
                    continue;
                var edge = mover.TrackHalfWidth;
                var from = new Vector2(feet.x, feet.z);
                var walked = 0f;
                for (var i = corner; i < corners.Count && walked < lookAhead; i++)
                {
                    var to = new Vector2(corners[i].x, corners[i].z);
                    var crossings = mover.Timetable.Path.Crossings(from, to);
                    for (var c = 0; c < crossings.Count; c++)
                    {
                        var distance = walked + crossings[c].Distance;
                        if (distance > edge + WaitBeyondEdge + 0.3f || distance < edge)
                            continue;
                        if (MoverCrossing.Check(mover.Timetable, clock, crossings[c].Arc, mover.crossingWarn).Wait)
                            return true;
                    }
                    walked += Vector2.Distance(from, to);
                    from = to;
                }
            }
            return false;
        }

        /// <summary>A random point of the navmesh within <paramref name="radius"/> of <paramref name="centre"/> that the island's home reaches.</summary>
        public static bool RandomPoint(Vector3 centre, float radius, System.Random random, out Vector3 point)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var angle = random.NextDouble() * Mathf.PI * 2;
                var distance = Mathf.Sqrt((float)random.NextDouble()) * radius;
                var probe = centre + new Vector3(Mathf.Cos((float)angle) * distance, 0, Mathf.Sin((float)angle) * distance);
                if (Snap(probe, 3, out point) && Mathf.Abs(point.y - probe.y) < 4)
                    return true;
            }
            point = centre;
            return false;
        }

        /// <summary>
        /// Ticket 21's test for a dropped flag (<see cref="CaptureTheFlag.NavmeshCheck"/>): whether a player can get to it, that is
        /// whether the navmesh within reach of the point (on it, or on the ground beside and up to a jump below) is joined to the island's
        /// home by a complete path. True when there is no navmesh at all.
        /// </summary>
        public static bool Reachable(Vector3 point)
        {
            var origin = HomeOnMesh();
            if (origin == null)
                return true;
            // On the train's track (ticket 27.10): a car cuts the mesh there only while it passes, so a flag that lies on the track stays
            if (SdfLevel.PathMover.OnAnyTrack(point, 0.5f))
                return true;
            scratch ??= new NavMeshPath();
            // On the point itself, then the ground below and beside it (a flag on a rock's top is reached by jumping up from beside it)
            foreach (var probe in ReachProbes(point))
            {
                if (!NavMesh.SamplePosition(probe, out var hit, 1.6f, Filter))
                    continue;
                if (NavMesh.CalculatePath(origin.Value, hit.position, Filter, scratch) && scratch.status == NavMeshPathStatus.PathComplete)
                    return true;
            }
            return false;
        }

        private static IEnumerable<Vector3> ReachProbes(Vector3 point)
        {
            yield return point;
            var below = point + Vector3.down * 1.1f;
            yield return below;
            yield return below + new Vector3(1.3f, 0, 0);
            yield return below + new Vector3(-1.3f, 0, 0);
            yield return below + new Vector3(0, 0, 1.3f);
            yield return below + new Vector3(0, 0, -1.3f);
        }

        private static Vector3? HomeOnMesh()
        {
            if (home == null && Snap(HomeProbe, 6, out var onMesh))
                home = onMesh;
            return home;
        }
    }
}
