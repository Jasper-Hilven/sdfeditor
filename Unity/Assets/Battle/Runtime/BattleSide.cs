using System.Collections.Generic;
using SdfPickups;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// One of a level's two battle sides (ticket 17), on Battle/West or Battle/East of the level scene: which side it is and its gameplay
    /// objects, so game code finds a side's spawn, flag and pods without searching by name. The level builder
    /// (SdfLevel.Editor.LevelBuilder) adds it and fills it from the level file's <c>sides</c> block on every rebuild; the objects are its
    /// children. The spawn is an empty marker, the middle of the side's spawn zone (ticket 20): its players appear anywhere within
    /// <see cref="spawnRadius"/> of it, facing its forward; drawn as a gizmo in the editor. The side's team is red for west, blue for east.
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
        public WeaponPickup minigunPod;
        public WeaponPickup pistolPod;
        public HealthPickup healthPod;

        /// <summary>The side's objects: the spawn, the flag stand and the three pods, those that are there.</summary>
        public IEnumerable<GameObject> Objects
        {
            get
            {
                var components = new Component[] { spawn, flagStand, minigunPod, pistolPod, healthPod };
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

        // The spawn marker: a player-sized upright outline on the ground and an arrow along the way it faces, in the zone's circle
        private void OnDrawGizmos()
        {
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

        private static Vector3 Ring(float angle, float radius) => new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
    }
}
