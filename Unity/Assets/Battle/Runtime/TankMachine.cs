using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SdfBattle
{
    /// <summary>
    /// A tank (ticket 24): it drives from one random point of the play area to the next on the Tank navmesh (ticket 23's agent type for
    /// 0.8 m: ground only, no stairs, no wall-walks; plain path queries, no NavMeshAgent), turning its hull to the way and keeping to the
    /// ground under it; it slows to a crawl while it fights, turning its turret (<see cref="SdfMachines.MachineRig.turn"/>, about local y)
    /// and tilting its barrel (<see cref="SdfMachines.MachineRig.tilt"/>, about local x: negative is up) to its target. It steers round
    /// players and other machines ahead and stops for one right in front (<see cref="MachineSteering"/>), and never pushes or is pushed
    /// (a kinematic body); a player who climbs onto it slides off. Stuck, it finds a new way. Respawns in its side's machine zone.
    /// </summary>
    public sealed class TankMachine : Machine
    {
        [Tooltip("Degrees a second the hull turns")]
        public float hullTurnSpeed = 80;
        [Tooltip("Its speed while it fights, as a share of its full speed")]
        public float fightingSpeed = 0.3f;
        [Tooltip("Lowest and highest elevation of the barrel, degrees")]
        public Vector2 elevation = new Vector2(-15, 30);
        [Tooltip("Metres ahead it looks for players and machines in its way")]
        public float lookAhead = 3;

        private readonly List<Vector3> corners = new List<Vector3>();
        private readonly List<Vector2> others = new List<Vector2>();
        private static NavMeshPath path;
        private NavMeshQueryFilter filter;
        private bool hasNavmesh;
        private int corner;
        private Vector3 destination;
        private float repathIn;
        private float pickIn;
        private float hullYaw;
        private float turretYaw;
        private float tilt;
        private float speed;
        private Vector3 up = Vector3.up;
        private Vector3 lastSample;
        private float sampleIn;
        private int stuck;

        protected override Vector3 CenterLocal => new Vector3(0, 0.38f, 0);

        protected override Vector3 MuzzleLocal => new Vector3(0, 0, 0.25f);

        protected override Transform MuzzleParent => Rig.tilt;

        public override float Heading => hullYaw + turretYaw;

        protected override string Doing => corners.Count > 0 ? "driving" : "waiting";

        protected override void OnBound()
        {
            var agent = BotNavigation.AgentTypeOf(BotNavigation.TankAgent);
            filter = new NavMeshQueryFilter { agentTypeID = agent ?? 0, areaMask = NavMesh.AllAreas };
            hasNavmesh = agent != null && NavMesh.SamplePosition(Side.machineSpawn.position, out _, 4, filter);
            if (!hasNavmesh)
                Debug.LogWarning($"{name}: no Tank navmesh at its machine zone; it stays put. Rebuild the level scene (Tools > SDF Level > Rebuild), then Tools > SDF Battle > Check Navmesh");
        }

        protected override void Place()
        {
            var spot = Owner.ZoneSpot(Side, this);
            hullYaw = Side.machineSpawn.eulerAngles.y;
            turretYaw = 0;
            tilt = 0;
            speed = 0;
            up = Vector3.up;
            transform.SetPositionAndRotation(spot, Quaternion.Euler(0, hullYaw, 0));
            corners.Clear();
            corner = 0;
            pickIn = (float)Random.NextDouble() * 1.5f;
            lastSample = spot;
            sampleIn = 2;
            stuck = 0;
            ApplyGun();
        }

        // ---------------------------------------------------------------- The gun

        protected override bool TurnGun(Vector3 point, float dt, bool hasTarget)
        {
            var turn = State.Stats.turnSpeed;
            if (!hasTarget)
            {
                turretYaw = Mathf.MoveTowardsAngle(turretYaw, 0, turn * 0.4f * dt);
                tilt = Mathf.MoveTowards(tilt, 0, turn * 0.4f * dt);
                ApplyGun();
                return false;
            }
            var hull = Rig.turn.parent;
            turretYaw = Mathf.MoveTowardsAngle(turretYaw, MachineAim.YawTo(hull.InverseTransformDirection(point - Rig.turn.position)), turn * dt);
            ApplyGun();
            tilt = Mathf.MoveTowards(tilt, MachineAim.TiltFor(Rig.turn.InverseTransformDirection(point - Rig.tilt.position), elevation.x, elevation.y), turn * dt);
            ApplyGun();
            return Vector3.Angle(Muzzle.forward, point - Muzzle.position) < 4;
        }

        private void ApplyGun()
        {
            if (Rig.turn != null)
                Rig.turn.localRotation = Quaternion.Euler(0, turretYaw, 0);
            if (Rig.tilt != null)
                Rig.tilt.localRotation = Quaternion.Euler(tilt, 0, 0);
        }

        // ---------------------------------------------------------------- Driving

        protected override void Move(float dt, Vector3? fighting)
        {
            PushOffRiders(dt);
            if (!hasNavmesh)
                return;
            var position = transform.position;
            if (corners.Count == 0 || corner >= corners.Count)
            {
                pickIn -= dt;
                if (pickIn <= 0 && !PickDestination(position))
                    pickIn = 0.5f;
                Settle(position, dt, 0);
                return;
            }
            repathIn -= dt;
            if (repathIn <= 0 && !FindPath(position, destination))
            {
                corners.Clear();
                return;
            }

            // The next corner: passed once within half a metre
            var next = corners[corner];
            var flat = new Vector3(next.x - position.x, 0, next.z - position.z);
            while (flat.magnitude < 0.5f)
            {
                corner++;
                if (corner >= corners.Count)
                {
                    corners.Clear();
                    pickIn = 1 + (float)Random.NextDouble() * 3;
                    Settle(position, dt, 0);
                    return;
                }
                next = corners[corner];
                flat = new Vector3(next.x - position.x, 0, next.z - position.z);
            }

            // Round players and machines ahead: slower, and turned aside
            var heading = new Vector2(Mathf.Sin(hullYaw * Mathf.Deg2Rad), Mathf.Cos(hullYaw * Mathf.Deg2Rad));
            CollectOthers(position);
            var clear = MachineSteering.Avoid(new Vector2(position.x, position.z), heading, others, 1.0f, lookAhead, out var steer);
            var wantYaw = MachineAim.YawTo(flat);
            if (steer.sqrMagnitude > 1e-4f)
                wantYaw = MachineAim.YawTo(new Vector3(flat.normalized.x + steer.x * 1.2f, 0, flat.normalized.z + steer.y * 1.2f));
            hullYaw = Mathf.MoveTowardsAngle(hullYaw, wantYaw, hullTurnSpeed * dt);
            var off = Mathf.Abs(Mathf.DeltaAngle(hullYaw, wantYaw));
            var full = State.Stats.speed * (fighting.HasValue ? fightingSpeed : 1);
            var wanted = full * Mathf.Clamp01(1 - off / 60) * clear * Mathf.Clamp01(flat.magnitude / 1.5f + 0.3f);
            speed = Mathf.MoveTowards(speed, wanted, 3 * dt);
            Settle(position, dt, speed);

            // Stuck (barely moving while it means to drive, not held up by someone in front): a new way, then a new goal
            sampleIn -= dt;
            if (sampleIn <= 0)
            {
                var moved = Vector3.Distance(transform.position, lastSample);
                stuck = moved < 0.3f && wanted > 0.5f ? stuck + 1 : 0;
                lastSample = transform.position;
                sampleIn = 2;
                if (stuck == 1)
                    FindPath(transform.position, destination);
                else if (stuck >= 2)
                {
                    corners.Clear();
                    pickIn = 0;
                    stuck = 0;
                }
            }
        }

        /// <summary>Drives <paramref name="metresPerSecond"/> along the hull's way for <paramref name="dt"/>, kept on the navmesh, on the ground under it.</summary>
        private void Settle(Vector3 position, float dt, float metresPerSecond)
        {
            var forward = new Vector3(Mathf.Sin(hullYaw * Mathf.Deg2Rad), 0, Mathf.Cos(hullYaw * Mathf.Deg2Rad));
            var next = position + forward * (metresPerSecond * dt);
            if (metresPerSecond > 0 && NavMesh.Raycast(position, next, out var edge, filter))
                next = edge.position;
            var normal = Vector3.up;
            if (Owner.Ground(next + Vector3.up * 0.9f, 2.2f, out var ground, out var groundNormal))
            {
                next.y = ground.y;
                normal = groundNormal;
            }
            else if (NavMesh.SamplePosition(next, out var onMesh, 1, filter))
                next.y = onMesh.position.y;
            // The hull follows the ground's slope, smoothly
            up = Vector3.Slerp(up, normal.y > 0.5f ? normal : Vector3.up, Mathf.Clamp01(dt * 6));
            var rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0, hullYaw, 0);
            transform.SetPositionAndRotation(next, rotation);
        }

        private void CollectOthers(Vector3 position)
        {
            others.Clear();
            var players = Owner.Match.Players;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player.State != null && player.State.Alive && (player.transform.position - position).sqrMagnitude < 25)
                    others.Add(new Vector2(player.transform.position.x, player.transform.position.z));
            }
            var machines = Owner.All;
            for (var i = 0; i < machines.Count; i++)
            {
                var machine = machines[i];
                if (machine != this && machine.Alive && (machine.transform.position - position).sqrMagnitude < 25)
                    others.Add(new Vector2(machine.transform.position.x, machine.transform.position.z));
            }
            // The train's cars (ticket 27.10), as points down both sides of each, so it steers round them like players
            SdfLevel.PathMover.AvoidPoints(position, 5, others);
        }

        /// <summary>A player standing on it (on top of its hull or turret) slides off, away from its middle.</summary>
        private void PushOffRiders(float dt)
        {
            var position = transform.position;
            var players = Owner.Match.Players;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player.State == null || !player.State.Alive || player.Capsule == null || !player.Capsule.enabled)
                    continue;
                var feet = player.transform.position;
                var above = feet.y - position.y;
                if (above < 0.3f || above > 1.2f)
                    continue;
                var away = new Vector3(feet.x - position.x, 0, feet.z - position.z);
                if (away.magnitude > 0.6f)
                    continue;
                if (away.sqrMagnitude < 1e-4f)
                    away = transform.right;
                player.Capsule.Move(away.normalized * (3 * dt));
            }
        }

        private bool PickDestination(Vector3 from)
        {
            Machines.PlayArea(out var min, out var max);
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var x = Mathf.Lerp(min.x + 4, max.x - 4, (float)Random.NextDouble());
                var z = Mathf.Lerp(min.y + 4, max.y - 4, (float)Random.NextDouble());
                if (new Vector2(x - from.x, z - from.z).magnitude < 12)
                    continue;
                var probe = new Vector3(x, Owner.Surface(x, z), z);
                // Not a castle's top or a wall-walk: the Tank navmesh has none, so nothing is found close under the surface there
                if (!NavMesh.SamplePosition(probe, out var hit, 1.5f, filter))
                    continue;
                if (FindPath(from, hit.position))
                    return true;
            }
            return false;
        }

        /// <summary>The corners to <paramref name="to"/>; false when no complete way leads there.</summary>
        private bool FindPath(Vector3 from, Vector3 to)
        {
            path ??= new NavMeshPath();
            repathIn = 6 + (float)Random.NextDouble() * 4;
            if (!NavMesh.SamplePosition(from, out var start, 2, filter))
                return false;
            if (!NavMesh.CalculatePath(start.position, to, filter, path) || path.status != NavMeshPathStatus.PathComplete)
                return false;
            destination = to;
            corners.Clear();
            corners.AddRange(path.corners);
            corner = corners.Count > 1 ? 1 : 0;
            return true;
        }

        protected override void OnDrawGizmos()
        {
            base.OnDrawGizmos();
            if (Owner == null || !Owner.showGizmos || State == null || !State.Alive)
                return;
            var from = transform.position + Vector3.up * 0.1f;
            for (var i = corner; i < corners.Count; i++)
            {
                var to = corners[i] + Vector3.up * 0.1f;
                Gizmos.DrawLine(from, to);
                from = to;
            }
        }
    }
}
