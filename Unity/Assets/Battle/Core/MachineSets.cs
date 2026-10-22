using System.Text.RegularExpressions;

namespace SdfBattle
{
    /// <summary>
    /// Which models a level's machines are (ticket 27.2): the level file's <c>machines set=toys</c>, baked as <c>machineSet</c> into level.json.
    /// The default set, <see cref="Default"/>, is the fungal machines of ticket 24, named <c>turret-red</c>, <c>tank-blue-barrel</c> as
    /// ever; any other set <c>toys</c> has <c>&lt;kind&gt;-&lt;set&gt;-&lt;team&gt;</c> (<c>turret-toys-red</c>, parts
    /// <c>turret-toys-red-foot</c>), kinds <c>turret</c>, <c>tank</c> and <c>helicopter</c>. Plain C#, so the naming is tested without the editor.
    /// </summary>
    public static class MachineSets
    {
        /// <summary>The meadow's, the fungal level's and the chessboard's set.</summary>
        public const string Default = "mushroom";

        public static readonly string[] Kinds = { "tank", "helicopter", "turret" };

        public static readonly string[] Teams = { "red", "blue" };

        private static readonly Regex ModelPattern =
            new Regex("^(?<kind>tank|helicopter|turret)-(?:(?<set>[a-z0-9]+)-)?(?<team>red|blue)-(?<piece>[a-z0-9-]+)$", RegexOptions.CultureInvariant);

        /// <summary>The set a level names; <see cref="Default"/> when it names none.</summary>
        public static string Of(string set) => string.IsNullOrEmpty(set) ? Default : set;

        public static bool IsDefault(string set) => Of(set) == Default;

        /// <summary>A machine's name in a set: turret-red in the default set, turret-toys-red in toys.</summary>
        public static string Name(string kind, string set, string team) =>
            IsDefault(set) ? $"{kind}-{team}" : $"{kind}-{set}-{team}";

        /// <summary>Where the editor builds the machine prefabs.</summary>
        public const string PrefabFolder = "Assets/Machines/Prefabs";

        /// <summary>A machine's prefab: Assets/Machines/Prefabs/turret-toys-red.prefab.</summary>
        public static string PrefabPath(string kind, string set, string team) => $"{PrefabFolder}/{Name(kind, set, team)}.prefab";

        /// <summary>The name of one of a machine's pieces, the model it is meshed from: turret-toys-red-foot.</summary>
        public static string PieceName(string kind, string set, string team, string piece) => $"{Name(kind, set, team)}-{piece}";

        /// <summary>
        /// Reads a piece's model name back: tank-red-barrel is the tank, default set, red team, piece barrel; turret-toys-blue-foot the toys
        /// set's. False for any other name.
        /// </summary>
        public static bool TryParsePiece(string model, out string kind, out string set, out string team, out string piece)
        {
            var match = ModelPattern.Match(model ?? "");
            kind = match.Groups["kind"].Value;
            set = match.Groups["set"].Success ? match.Groups["set"].Value : Default;
            team = match.Groups["team"].Value;
            piece = match.Groups["piece"].Value;
            return match.Success;
        }
    }

    /// <summary>
    /// How a closed room limits a helicopter's flight (ticket 27.2, <see cref="HelicopterPilot"/>): 6 to 12 m over the floor, 16 m over
    /// a base, 3 m off the walls (not off the hills, a room has none), and never higher than <see cref="Ceiling"/>, the level's number
    /// (<c>machines set=toys ceiling=20</c>). A level under a sky has none; it flies as in ticket 24.
    /// </summary>
    public sealed class HelicopterLimits
    {
        public float MinHeight = 6;
        public float MaxHeight = 12;
        /// <summary>The height over the floor it flies at over a base (any surface standing more than <see cref="RaisedAbove"/> over the floor).</summary>
        public float OverBases = 16;
        public float RaisedAbove = 2;
        /// <summary>The least distance it keeps from a wall, in metres.</summary>
        public float WallClearance = 3;
        /// <summary>The highest it flies over the floor, in metres.</summary>
        public float Ceiling = 20;

        /// <summary>A room's limits under a flight ceiling, in metres over the floor.</summary>
        public static HelicopterLimits Room(float ceiling) => new HelicopterLimits { Ceiling = ceiling };

        /// <summary>Gives <paramref name="pilot"/> the limits; <paramref name="floor"/> is the floor's height (y).</summary>
        public void Apply(HelicopterPilot pilot, float floor)
        {
            pilot.MinHeight = MinHeight;
            pilot.MaxHeight = MaxHeight;
            pilot.Floor = floor;
            pilot.OverBases = OverBases;
            pilot.RaisedAbove = RaisedAbove;
            // Waypoints are picked one metre further in than the least distance it is flown to (HelicopterPilot clamps at Margin - 1)
            pilot.Margin = WallClearance + 1;
            pilot.Ceiling = floor + Ceiling;
        }
    }
}
