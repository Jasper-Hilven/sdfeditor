using SdfBattle;
using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// The level's zone mask at runtime (ticket 29.7): the bytes of the bake's zones.bin with where they lie, stored in the scene by the level
    /// builder (<c>LevelZonesBuilder</c>) so the web build needs no file. The lookup is <see cref="SdfBattle.ZoneMask"/>; the controller
    /// (slick) and <see cref="SdfBattle.ZoneEffects"/> (wet) read it by position through <see cref="Current"/>. A level without zones has no
    /// such object and <see cref="Current"/> is null: nothing changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelZones : MonoBehaviour
    {
        public const string ObjectName = "Zones";

        [Tooltip("The corner of the first cell in the level file's coordinates (x across, z along)")]
        public Vector2 min;
        public int width;
        public int height;
        public float cellSize = 1;
        [Tooltip("One byte a cell, rows from the lowest z up: bit 0 slick, bit 1 wet (zones.bin)")]
        public byte[] cells = new byte[0];

        private ZoneMask mask;

        /// <summary>The zones of the active scene; null where the level has none.</summary>
        public static LevelZones Current { get; private set; }

        public ZoneMask Mask => mask ??= new ZoneMask(min, width, height, cells, cellSize);

        public bool IsSlick(Vector3 world) => Mask.IsSlick(world);

        public bool IsWet(Vector3 world) => Mask.IsWet(world);

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this)
                Current = null;
        }
    }
}
