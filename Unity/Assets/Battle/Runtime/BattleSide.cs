using System.Collections.Generic;
using SdfMachines;
using SdfPickups;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// One of a level's two battle sides (ticket 17), on Battle/West or Battle/East of the level scene: which side it is and its gameplay
    /// objects, so game code finds a side's spawn, flag and pods without searching by name. The level builder
    /// (SdfLevel.Editor.LevelBuilder) adds it and fills it from the level file's <c>sides</c> block on every rebuild; the objects are its
    /// children; its weapon pods are whichever the level file gives the side (the minigun and the pistol on most levels). The spawn is an empty marker, the middle of the side's spawn zone (ticket 20): its players appear anywhere within
    /// <see cref="spawnRadius"/> of it, facing its forward; drawn as a gizmo in the editor. The side's team is red for west, blue for east.
    /// On a level with machines (ticket 24) the side also has its <see cref="turrets"/>, static scene objects standing where they
    /// respawn, and its machine zone (<see cref="machineSpawn"/>, <see cref="machineSpawnRadius"/>), where its helicopter and tanks
    /// start and respawn; on other levels those are empty.
    /// </summary>
    public sealed class BattleSide : MonoBehaviour
    {
        [Tooltip("Which side this is: west (the pipeline's -x) or east")]
        public BattleSideId side;
        [Tooltip("The middle of this side's spawn zone, on the ground; its players appear facing its forward")]
        public Transform spawn;
        [Tooltip("The spawn zone's radius in metres: players appear at a free spot within it (the level file's spawn radius)")]
        public float spawnRadius = 3;
        [Tooltip("The side's flag stand, holding its flag")]
        public FlagStand flagStand;
        [Tooltip("The side's weapon pods, as many as the level file gives: the minigun and the pistol on most levels, any weapons on others")]
        public WeaponPickup[] weaponPods = System.Array.Empty<WeaponPickup>();
        [Tooltip("The minigun pod of a scene built before the side's weapon pods were a list; read only while weaponPods is empty, until the scene is rebuilt")]
        [HideInInspector]
        public WeaponPickup minigunPod;
        [Tooltip("The pistol pod of a scene built before the side's weapon pods were a list; read only while weaponPods is empty, until the scene is rebuilt")]
        [HideInInspector]
        public WeaponPickup pistolPod;
        public HealthPickup healthPod;
        [Tooltip("The side's health pods after the first, where the level file gives more than one")]
        public HealthPickup[] moreHealthPods = System.Array.Empty<HealthPickup>();
        [Tooltip("The side's turrets (ticket 24), the turret prefab of the side's colour, each standing where it respawns; empty on a level without machines")]
        public MachineRig[] turrets = System.Array.Empty<MachineRig>();
        [Tooltip("The middle of the side's machine zone (ticket 24), on the ground behind its castle: its helicopter and tanks start and respawn there, facing its forward; none on a level without machines")]
        public Transform machineSpawn;
        [Tooltip("The machine zone's radius in metres (the level file's machines radius)")]
        public float machineSpawnRadius = 3;

        /// <summary>
        /// The side's weapon pods, those that are there: <see cref="weaponPods"/>, or in a scene built before they were a list (and not
        /// rebuilt since) its <see cref="minigunPod"/> and <see cref="pistolPod"/>.
        /// </summary>
        public IEnumerable<WeaponPickup> WeaponPods
        {
            get
            {
                var pods = weaponPods != null && weaponPods.Length > 0 ? weaponPods : new[] { minigunPod, pistolPod };
                foreach (var pod in pods)
                    if (pod != null)
                        yield return pod;
            }
        }

        /// <summary>The side's health pods: the first and any more, those that are there.</summary>
        public IEnumerable<HealthPickup> HealthPods
        {
            get
            {
                if (healthPod != null)
                    yield return healthPod;
                foreach (var pod in moreHealthPods ?? System.Array.Empty<HealthPickup>())
                    if (pod != null)
                        yield return pod;
            }
        }

        /// <summary>The side's objects: the spawn, the flag stand, the pods, the turrets and the machine zone, those that are there.</summary>
        public IEnumerable<GameObject> Objects
        {
            get
            {
                var components = new List<Component> { spawn, flagStand };
                components.AddRange(WeaponPods);
                components.AddRange(HealthPods);
                if (turrets != null)
                    components.AddRange(turrets);
                components.Add(machineSpawn);
                foreach (var component in components)
                    if (component != null)
                        yield return component.gameObject;
            }
        }

        /// <summary>The team that plays from this side: red from the west, blue from the east.</summary>
        public Team Team => TeamOf(side);

        public static Team TeamOf(BattleSideId id) => id == BattleSideId.West ? Team.Red : Team.Blue;

        public static BattleSideId SideOf(Team team) => team == Team.Red ? BattleSideId.West : BattleSideId.East;

        /// <summary>The sides' colours, as their flags and scouts: west amanita red, east chanterelle blue.</summary>
        public static Color ColorOf(BattleSideId id) => id == BattleSideId.West ? new Color(0.85f, 0.12f, 0.1f) : new Color(0.15f, 0.4f, 0.9f);

        // The spawn marker: a player-sized upright outline on the ground and an arrow along the way it faces, in the zone's circle; the
        // machine zone's circle and arrow
        private void OnDrawGizmos()
        {
            if (machineSpawn != null)
                DrawZone(machineSpawn, machineSpawnRadius);
            if (spawn == null)
                return;
            const float radius = 0.3f, height = 1.75f;
            var previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(spawn.position, Quaternion.Euler(0, spawn.eulerAngles.y, 0), Vector3.one);
            Gizmos.color = ColorOf(side);
            const int segments = 16;
            for (var i = 0; i < segments; i++)
            {
                var a = Ring(i * 2 * Mathf.PI / segments, radius);
                var b = Ring((i + 1) * 2 * Mathf.PI / segments, radius);
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(a + Vector3.up * height, b + Vector3.up * height);
                if (i % 4 == 0)
                    Gizmos.DrawLine(a, a + Vector3.up * height);
            }
            const int zoneSegments = 48;
            for (var i = 0; i < zoneSegments; i++)
                Gizmos.DrawLine(Ring(i * 2 * Mathf.PI / zoneSegments, spawnRadius) + Vector3.up * 0.05f,
                    Ring((i + 1) * 2 * Mathf.PI / zoneSegments, spawnRadius) + Vector3.up * 0.05f);
            var tip = new Vector3(0, 0.05f, 1.2f);
            Gizmos.DrawLine(new Vector3(0, 0.05f, 0), tip);
            Gizmos.DrawLine(tip, new Vector3(-0.2f, 0.05f, 0.95f));
            Gizmos.DrawLine(tip, new Vector3(0.2f, 0.05f, 0.95f));
            Gizmos.matrix = previous;
        }

        // A zone on the ground: its circle, a smaller one inside it, and an arrow along the way it faces
        private void DrawZone(Transform zone, float zoneRadius)
        {
            var previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(zone.position, Quaternion.Euler(0, zone.eulerAngles.y, 0), Vector3.one);
            Gizmos.color = ColorOf(side);
            const int segments = 48;
            for (var i = 0; i < segments; i++)
            {
                var a = i * 2 * Mathf.PI / segments;
                var b = (i + 1) * 2 * Mathf.PI / segments;
                Gizmos.DrawLine(Ring(a, zoneRadius) + Vector3.up * 0.05f, Ring(b, zoneRadius) + Vector3.up * 0.05f);
                Gizmos.DrawLine(Ring(a, zoneRadius - 0.2f) + Vector3.up * 0.05f, Ring(b, zoneRadius - 0.2f) + Vector3.up * 0.05f);
            }
            var tip = new Vector3(0, 0.05f, zoneRadius * 0.6f);
            Gizmos.DrawLine(new Vector3(0, 0.05f, -zoneRadius * 0.6f), tip);
            Gizmos.DrawLine(tip, tip + new Vector3(-0.4f, 0, -0.4f));
            Gizmos.DrawLine(tip, tip + new Vector3(0.4f, 0, -0.4f));
            Gizmos.matrix = previous;
        }

        private static Vector3 Ring(float angle, float radius) => new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
    }
}
