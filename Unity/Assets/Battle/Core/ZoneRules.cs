using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The zone mask of a level (ticket 29.7; the level file's <c>zone region rule</c> lines, baked as zones.bin): a byte per cell over the
    /// playable area, <see cref="SlickBit"/> and <see cref="WetBit"/> flags, looked up by position. In the pipeline's coordinates: cell
    /// (i, j) is <c>cells[j * width + i]</c> and covers [min + (i, j) * cellSize, + cellSize], z rising with j. Unity mirrors x
    /// (<see cref="AtWorld"/>). Outside the mask nothing is set. Plain C#, so the rules are tested without a scene.
    /// </summary>
    public sealed class ZoneMask
    {
        public const byte SlickBit = 1;
        public const byte WetBit = 2;

        private readonly byte[] cells;

        public ZoneMask(Vector2 min, int width, int height, byte[] cells, float cellSize = 1)
        {
            if (cells == null || cells.Length != width * height)
                throw new System.ArgumentException($"A zone mask of {width} x {height} cells has {width * height} bytes");
            Min = min;
            Width = width;
            Height = height;
            CellSize = Mathf.Max(0.01f, cellSize);
            this.cells = cells;
        }

        /// <summary>The corner of the first cell in the pipeline's coordinates (x across, z along).</summary>
        public Vector2 Min { get; }

        public int Width { get; }

        public int Height { get; }

        public float CellSize { get; }

        /// <summary>The flags of the cell at a point of the pipeline's plane; 0 outside the mask.</summary>
        public byte At(float x, float z)
        {
            var i = Mathf.FloorToInt((x - Min.x) / CellSize);
            var j = Mathf.FloorToInt((z - Min.y) / CellSize);
            return i < 0 || j < 0 || i >= Width || j >= Height ? (byte)0 : cells[j * Width + i];
        }

        /// <summary>The flags under a point of Unity's world (the level's x mirrored).</summary>
        public byte AtWorld(Vector3 position) => At(-position.x, position.z);

        public bool IsSlick(Vector3 worldPosition) => (AtWorld(worldPosition) & SlickBit) != 0;

        public bool IsWet(Vector3 worldPosition) => (AtWorld(worldPosition) & WetBit) != 0;
    }

    /// <summary>
    /// What the zones do (ticket 29.7). A level without zones changes nothing; these only run where the controller or the combat finds a mask.
    /// <list type="bullet">
    /// <item>Slick (soap): a player's ground acceleration is x0.25 and braking x0.1 of the normal ground's, and the top speed x1.3. Normal
    /// ground has no slide in this game (the controller takes the wanted velocity at once); <see cref="Slide"/> models it with the reference
    /// times <see cref="AccelerationTime"/> and <see cref="BrakingTime"/> to reach the top speed from rest, or to stop from it, so the slick
    /// takes four times and ten times as long. A bot follows the navmesh and does not slide: it only gets x1.2 top speed.</item>
    /// <item>Wet (water): a scout standing in it, or out of it less than <see cref="WetSeconds"/> ago, takes x1.5 damage from the electro
    /// gun (<see cref="DamageFactor"/>, tracked by <see cref="WetTracker"/>).</item>
    /// </list>
    /// </summary>
    public static class ZoneRules
    {
        public const float SlickAcceleration = 0.25f;
        public const float SlickBraking = 0.1f;
        public const float SlickTopSpeedPlayer = 1.3f;
        public const float SlickTopSpeedBot = 1.2f;

        /// <summary>Seconds a slide takes to go from rest to the top speed on normal ground (x <see cref="SlickAcceleration"/>-fold longer on soap).</summary>
        public const float AccelerationTime = 0.15f;

        /// <summary>Seconds a slide takes to go from the top speed to rest on normal ground.</summary>
        public const float BrakingTime = 0.1f;

        public const float WetDamage = 1.5f;
        public const float WetSeconds = 2;

        /// <summary>The factor on the top speed on a slick: 1.3 for a player, 1.2 for a bot, 1 off one.</summary>
        public static float TopSpeedFactor(bool onSlick, bool isBot) => !onSlick ? 1 : isBot ? SlickTopSpeedBot : SlickTopSpeedPlayer;

        /// <summary>
        /// One step of a player's sliding on a slick: the horizontal <paramref name="velocity"/> moves towards <paramref name="wanted"/> (the
        /// velocity the keys ask for, the top speed included) by at most the slick's acceleration, or, when that change goes against the way
        /// it is going (letting go, turning back), by at most its braking. <paramref name="topSpeed"/> is the normal ground's, which sets the
        /// rates.
        /// </summary>
        public static Vector2 Slide(Vector2 velocity, Vector2 wanted, float topSpeed, float deltaTime)
        {
            var change = wanted - velocity;
            var distance = change.magnitude;
            if (distance < 1e-5f)
                return wanted;
            var braking = Vector2.Dot(change, velocity) < 0;
            var rate = braking ? topSpeed / BrakingTime * SlickBraking : topSpeed / AccelerationTime * SlickAcceleration;
            var step = rate * Mathf.Max(0, deltaTime);
            return step >= distance ? wanted : velocity + change / distance * step;
        }

        /// <summary>What a hit's damage is multiplied by: x1.5 for the electro gun on a wet scout, else 1.</summary>
        public static float DamageFactor(string weapon, bool wet) => wet && weapon == WeaponIds.ElectroGun ? WetDamage : 1;
    }

    /// <summary>
    /// Whether one scout counts as wet (ticket 29.7): standing in a wet zone, or out of it for less than <see cref="ZoneRules.WetSeconds"/>.
    /// Never wet before it first stood in one.
    /// </summary>
    public sealed class WetTracker
    {
        private float sinceWet = float.PositiveInfinity;

        public bool Wet => sinceWet < ZoneRules.WetSeconds;

        /// <summary>Seconds since the scout last stood in water; infinity if never.</summary>
        public float SinceWet => sinceWet;

        /// <summary>Advances the clock by <paramref name="deltaTime"/> seconds; <paramref name="inWater"/> is where the scout stands now.</summary>
        public void Tick(bool inWater, float deltaTime)
        {
            sinceWet = inWater ? 0 : sinceWet + Mathf.Max(0, deltaTime);
        }

        /// <summary>Dry again (a new round, a respawn).</summary>
        public void Reset() => sinceWet = float.PositiveInfinity;
    }
}
