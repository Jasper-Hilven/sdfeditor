using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SdfBattle
{
    /// <summary>
    /// Chanterelle Prime on foot in phase 3 (ticket 33.6): walks the arena's floor on the <b>Boss</b> navmesh (<see cref="BotNavigation.BossAgent"/>:
    /// a 1.8 m radius and 6.5 m tall, so it goes round the shelters and the wreck, which a scout passes), by plain path queries like
    /// <see cref="BotNavigation"/>, at 2 m/s, turning at 90 degrees a second. Moves the transform it is on (the rig's root); the boss tells it
    /// where to go (<see cref="GoTo"/>) and when to stand still (<see cref="Stop"/>). Without a Boss navmesh (a scene not rebuilt) it walks
    /// straight at its goal.
    /// </summary>
    public sealed class PrimeWalker : MonoBehaviour
    {
        [Tooltip("Metres a second he walks")]
        public float speed = 2;
        [Tooltip("Degrees a second he turns")]
        public float turnSpeed = 90;
        [Tooltip("Metres from the goal he stops")]
        public float arriveDistance = 1.5f;
        [Tooltip("Seconds between path queries while the goal moves")]
        public float repathEvery = 0.7f;

        private readonly List<Vector3> corners = new List<Vector3>();
        private NavMeshPath path;
        private int agentType = int.MinValue;
        private Vector3? goal;
        private Vector3 pathGoal;
        private int corner;
        private float repathIn;
        private bool moving;

        /// <summary>He is walking now (the bob, the cape).</summary>
        public bool Moving => moving;

        public Vector3? Goal => goal;

        /// <summary>Walks to a point on the floor, from now; null stands still.</summary>
        public void GoTo(Vector3? point)
        {
            goal = point;
            if (!point.HasValue)
            {
                moving = false;
                corners.Clear();
            }
        }

        public void Stop() => GoTo(null);

        /// <summary>The nearest point of the Boss navmesh to a point within <paramref name="radius"/>, or the point itself.</summary>
        public Vector3 Snap(Vector3 point, float radius)
        {
            return NavMesh.SamplePosition(point, out var hit, radius, Filter) ? hit.position : point;
        }

        private NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = AgentType, areaMask = NavMesh.AllAreas };

        private int AgentType
        {
            get
            {
                if (agentType == int.MinValue)
                    agentType = BotNavigation.AgentTypeOf(BotNavigation.BossAgent) ?? 0;
                return agentType;
            }
        }

        private void Update() => Step(Time.deltaTime);

        /// <summary>Lets <paramref name="dt"/> seconds of the walk pass (public for the batch test).</summary>
        public void Step(float dt)
        {
            if (dt <= 0)
                return;
            if (!goal.HasValue)
            {
                moving = false;
                return;
            }
            var here = transform.position;
            var flatToGoal = Flat(goal.Value - here);
            if (flatToGoal.magnitude <= arriveDistance)
            {
                moving = false;
                return;
            }
            repathIn -= dt;
            if (repathIn <= 0 || corners.Count == 0 || (goal.Value - pathGoal).sqrMagnitude > 4)
                FindPath(here);

            while (corner < corners.Count && Flat(corners[corner] - here).magnitude < 0.4f)
                corner++;
            var next = corner < corners.Count ? corners[corner] : goal.Value;
            var way = Flat(next - here);
            if (way.sqrMagnitude < 1e-4f)
            {
                moving = false;
                return;
            }
            // Turns first, walks once he faces it within 45 degrees
            var wantYaw = Mathf.Atan2(way.x, way.z) * Mathf.Rad2Deg;
            var yaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, wantYaw, turnSpeed * dt);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            moving = Mathf.Abs(Mathf.DeltaAngle(yaw, wantYaw)) < 45;
            if (!moving)
                return;
            var step = Mathf.Min(speed * dt, way.magnitude);
            var position = here + way.normalized * step;
            // Up and down with the navmesh's own height under him
            if (NavMesh.SamplePosition(position, out var ground, 2.5f, Filter))
                position.y = ground.position.y;
            transform.position = position;
        }

        private void FindPath(Vector3 from)
        {
            corners.Clear();
            corner = 0;
            pathGoal = goal.Value;
            repathIn = repathEvery * (0.8f + 0.4f * Random.value);
            path ??= new NavMeshPath();
            if (NavMesh.SamplePosition(from, out var start, 6, Filter) && NavMesh.SamplePosition(goal.Value, out var end, 8, Filter) &&
                NavMesh.CalculatePath(start.position, end.position, Filter, path) && path.status != NavMeshPathStatus.PathInvalid)
            {
                var found = path.corners;
                for (var i = 0; i < found.Length; i++)
                    corners.Add(found[i]);
                if (corners.Count > 1)
                    corner = 1;
                return;
            }
            // No navmesh: straight at the goal
            corners.Add(from);
            corners.Add(goal.Value);
            corner = 1;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    }
}
