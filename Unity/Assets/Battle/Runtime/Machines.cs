using System;
using System.Collections.Generic;
using SdfLevel;
using SdfMachines;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The machines of a battle scene (ticket 24), on the Battle object next to the <see cref="Match"/>, which makes it at its start
    /// (<see cref="Create"/>) after combat and before the bots, on a level whose sides have turrets or a machine zone. Per side: its four
    /// turrets, the level's scene objects (<see cref="BattleSide.turrets"/>), each given a <see cref="TurretMachine"/>; and its helicopter
    /// and two tanks, made here from the team's prefabs (<see cref="redTank"/>, ...; set by the level builder, and in the editor found by
    /// path when not) in the side's machine zone (<see cref="BattleSide.machineSpawn"/>), under Battle/Machines. Nothing is placed by hand.
    /// <para>
    /// The rules are <see cref="MachineState"/> per machine and <see cref="rules"/> (<see cref="MachineRules"/>: per side counts, each
    /// kind's numbers, the aim's skill). A new round brings every machine back where it starts. The shared scene queries live here: the
    /// line of sight and the shot (rays that pass a machine's own colliders), the ground under a tank, the highest surface under a
    /// helicopter.
    /// </para>
    /// <para>
    /// Frame cost (14 machines, 8 players): each looks for a target four times a second at staggered moments, one or two rays a look
    /// (nearest enemy in range first, <see cref="MachineTargeting"/>); a tank casts one ray down a frame and finds a path every several
    /// seconds; a helicopter casts four rays down five times a second; the rest is a few rotations. No threads, no compute: WebGL 2 safe.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-70)]
    public sealed class Machines : MonoBehaviour
    {
        public const string ObjectName = "Machines";
        public const string PrefabFolder = "Assets/Machines/Prefabs";

        public MachineRules rules = new MachineRules();
        [Tooltip("The red tank prefab (Assets/Machines/Prefabs/tank-red.prefab); the level builder sets them")]
        public GameObject redTank;
        public GameObject blueTank;
        public GameObject redHelicopter;
        public GameObject blueHelicopter;
        [Tooltip("The set of machine models (ticket 27.2): mushroom is turret-red, ...; toys is turret-toys-red, ...; the level builder sets it")]
        public string machineSet = MachineSets.Default;
        [Tooltip("A room level's helicopter ceiling over the floor in metres (ticket 27.2), with the room's other limits; 0: none, the old flight")]
        public float flightCeiling;
        [Tooltip("Seeds the machines' aim, roaming and zone spots")]
        public int seed = 24;
        [Tooltip("Draw each machine's target and way in the Scene view")]
        public bool showGizmos = true;

        private static readonly RaycastHit[] Hits = new RaycastHit[24];
        private static readonly Comparison<RaycastHit> ByDistance = (a, b) => a.distance.CompareTo(b.distance);

        private readonly List<Machine> machines = new List<Machine>();
        private readonly List<Vector2> occupied = new List<Vector2>();
        private System.Random random;
        private float surfaceTop;

        /// <summary>The machines of the active battle scene; null where there are none.</summary>
        public static Machines Current { get; private set; }

        public Match Match { get; private set; }

        public Combat Combat { get; private set; }

        public IReadOnlyList<Machine> All => machines;

        public System.Random Random => random;

        /// <summary>The prefab of a machine of a team: Assets/Machines/Prefabs/tank-red.prefab, ...</summary>
        public static string PrefabPath(MachineKind kind, Team team, string set = null) =>
            MachineSets.PrefabPath(kind.ToString().ToLowerInvariant(), set, team.Name().ToLowerInvariant());

        /// <summary>Whether a battle layout has machines: turrets or a machine zone on a side.</summary>
        public static bool HasMachines(BattleArena arena)
        {
            foreach (var side in new[] { arena != null ? arena.west : null, arena != null ? arena.east : null })
                if (side != null && ((side.turrets != null && side.turrets.Length > 0) || side.machineSpawn != null))
                    return true;
            return false;
        }

        /// <summary>
        /// Makes the match's machines, on a level that has them (else nothing, and null). Called by <see cref="Match"/> at its start,
        /// after <see cref="Combat"/> and before the bots.
        /// </summary>
        public static Machines Create(Match match)
        {
            if (!HasMachines(match.Arena))
                return null;
            var machines = match.GetComponent<Machines>();
            if (machines == null)
                machines = match.gameObject.AddComponent<Machines>();
            machines.Attach(match);
            return machines;
        }

        private void Attach(Match match)
        {
            Match = match;
            Combat = Combat.Current;
            random = new System.Random(seed);
            var arena = match.Arena;
            surfaceTop = Mathf.Max(arena.west.spawn.position.y, arena.east.spawn.position.y) + 80;
#if UNITY_EDITOR
            // A scene built before ticket 24.3 has no prefab references: the editor finds them by path
            redTank = redTank != null ? redTank : Load(MachineKind.Tank, Team.Red, machineSet);
            blueTank = blueTank != null ? blueTank : Load(MachineKind.Tank, Team.Blue, machineSet);
            redHelicopter = redHelicopter != null ? redHelicopter : Load(MachineKind.Helicopter, Team.Red, machineSet);
            blueHelicopter = blueHelicopter != null ? blueHelicopter : Load(MachineKind.Helicopter, Team.Blue, machineSet);
#endif
            var root = new GameObject(ObjectName).transform;
            root.SetParent(transform, false);
            foreach (var side in new[] { arena.west, arena.east })
            {
                if (side == null)
                    continue;
                var team = side.Team;
                if (side.turrets != null)
                    foreach (var rig in side.turrets)
                        if (rig != null)
                            Add<TurretMachine>(rig, MachineKind.Turret, team, side);
                if (side.machineSpawn == null)
                    continue;
                for (var i = 0; i < rules.helicoptersPerSide; i++)
                    Make<HelicopterMachine>(team == Team.Red ? redHelicopter : blueHelicopter, MachineKind.Helicopter, team, side, root, i);
                for (var i = 0; i < rules.tanksPerSide; i++)
                    Make<TankMachine>(team == Team.Red ? redTank : blueTank, MachineKind.Tank, team, side, root, i);
            }
            foreach (var machine in machines)
                machine.Restart();
            match.State.RoundReset += OnRoundReset;
            Current = this;
        }

#if UNITY_EDITOR
        private static GameObject Load(MachineKind kind, Team team, string set) => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(kind, team, set));
#endif

        private void Make<T>(GameObject prefab, MachineKind kind, Team team, BattleSide side, Transform root, int number) where T : Machine
        {
            if (prefab == null)
            {
                Debug.LogWarning($"Machines: no {kind.ToString().ToLowerInvariant()} prefab for {team.Name()} ({PrefabPath(kind, team, machineSet)}); build it (Tools > SDF Machines > Rebuild Machines) and rebuild the level");
                return;
            }
            var instance = Instantiate(prefab, side.machineSpawn.position, side.machineSpawn.rotation, root);
            instance.name = $"{MachineRules.NameOf(kind, team)} {number + 1}";
            var rig = instance.GetComponent<MachineRig>();
            if (rig == null)
            {
                Debug.LogWarning($"Machines: {prefab.name} has no MachineRig; rebuild the machines (Tools > SDF Machines > Rebuild Machines)");
                Destroy(instance);
                return;
            }
            Add<T>(rig, kind, team, side);
        }

        private void Add<T>(MachineRig rig, MachineKind kind, Team team, BattleSide side) where T : Machine
        {
            var machine = rig.GetComponent<T>();
            if (machine == null)
                machine = rig.gameObject.AddComponent<T>();
            var state = new MachineState(machines.Count, kind, team, rules.StatsOf(kind));
            machine.Bind(this, state, rig, side, seed * 37 + machines.Count);
            machines.Add(machine);
        }

        private void OnEnable()
        {
            if (Match != null)
                Current = this;
        }

        private void OnDisable()
        {
            if (Current == this)
                Current = null;
        }

        private void OnDestroy()
        {
            if (Match != null && Match.State != null)
                Match.State.RoundReset -= OnRoundReset;
        }

        // A new round: every machine back where it starts, at full health
        private void OnRoundReset()
        {
            foreach (var machine in machines)
                machine.Restart();
        }

        // ---------------------------------------------------------------- Scene queries

        /// <summary>The machine a collider belongs to (its colliders all hang on its kinematic body); null for anything else.</summary>
        public static Machine Of(Collider collider)
        {
            var body = collider != null ? collider.attachedRigidbody : null;
            return body != null ? body.GetComponent<Machine>() : null;
        }

        /// <summary>
        /// A free spot in a side's machine zone for <paramref name="machine"/> (away from the side's other machines standing in it), on
        /// the ground: the zone was checked open and level by the pipeline.
        /// </summary>
        public Vector3 ZoneSpot(BattleSide side, Machine machine)
        {
            var centre = side.machineSpawn.position;
            occupied.Clear();
            foreach (var other in machines)
                if (other != machine && other.Alive && other.Kind != MachineKind.Turret)
                    occupied.Add(new Vector2(other.transform.position.x, other.transform.position.z));
            var spot = SpawnPicker.Pick(new Vector2(centre.x, centre.z), Mathf.Max(0.5f, side.machineSpawnRadius - 0.3f), occupied, random, 1.6f);
            var point = new Vector3(spot.x, centre.y, spot.y);
            return Ground(point + Vector3.up * 1.5f, 4, out var ground, out _) ? ground : point;
        }

        /// <summary>
        /// The ground below <paramref name="from"/> within <paramref name="reach"/>: the first solid surface that is neither a player
        /// nor a machine.
        /// </summary>
        public bool Ground(Vector3 from, float reach, out Vector3 point, out Vector3 normal)
        {
            var count = Physics.RaycastNonAlloc(from, Vector3.down, Hits, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(Hits, 0, count, Comparer);
            for (var i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (Player.Of(hit.collider) != null || Of(hit.collider) != null)
                    continue;
                point = hit.point;
                normal = hit.normal;
                return true;
            }
            point = from;
            normal = Vector3.up;
            return false;
        }

        /// <summary>The height of the highest solid surface at (x, z), a castle's top or the ground (not players, not machines).</summary>
        public float Surface(float x, float z) =>
            Ground(new Vector3(x, surfaceTop, z), surfaceTop + 200, out var point, out _) ? point.y : surfaceTop - 80;

        /// <summary>
        /// Whether <paramref name="target"/>'s point is in sight of <paramref name="from"/>: the first solid thing along the way, past the
        /// machine's own colliders, is the target's capsule, or nothing before the point.
        /// </summary>
        public bool Visible(Vector3 from, Vector3 point, Machine self, Player target)
        {
            var along = point - from;
            var distance = along.magnitude;
            if (distance < 0.05f)
                return true;
            var hit = First(from, along / distance, distance, self);
            return hit.collider == null || (target != null && Player.Of(hit.collider) == target);
        }

        /// <summary>Whether <paramref name="machine"/>'s middle is in sight of <paramref name="from"/> (a bot's eye): the first solid thing that way is the machine.</summary>
        public bool Visible(Vector3 from, Machine machine)
        {
            var along = machine.Center - from;
            var distance = along.magnitude;
            if (distance < 0.05f)
                return true;
            var hit = First(from, along / distance, distance + 0.5f, null);
            return hit.collider == null || machine.Owns(hit.collider);
        }

        /// <summary>A shot of <paramref name="self"/> from <paramref name="from"/> along <paramref name="direction"/>: the first solid thing it meets (past its own colliders), if any.</summary>
        public RaycastHit Shoot(Vector3 from, Vector3 direction, float range, Machine self) => First(from, direction, range, self);

        private static RaycastHit First(Vector3 from, Vector3 direction, float range, Machine self)
        {
            var count = Physics.RaycastNonAlloc(from, direction, Hits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(Hits, 0, count, Comparer);
            for (var i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (self != null && self.Owns(hit.collider))
                    continue;
                // A dead player's capsule is off; a dead machine's colliders too
                return hit;
            }
            return default;
        }

        private static readonly IComparer<RaycastHit> Comparer = Comparer<RaycastHit>.Create(ByDistance);

        /// <summary>The play area's corners (x, z), from the scene info; a 150 x 100 m field round the origin without one.</summary>
        public static void PlayArea(out Vector2 min, out Vector2 max)
        {
            var scene = IMMScene.Current;
            if (scene != null && scene.PlayAreaSize.x > 1)
            {
                min = scene.playAreaMin;
                max = scene.playAreaMax;
                return;
            }
            min = new Vector2(-75, -50);
            max = new Vector2(75, 50);
        }
    }
}
