namespace SdfBattle
{
    /// <summary>
    /// The two teams of a match (ticket 20): red plays from the west side (the amanita scouts, the red flag), blue from the east (the
    /// chanterelle scouts, the blue flag). The values are the sides' (SdfPickups.BattleSideId West 0, East 1), so a team is its side's
    /// index and the other way round.
    /// </summary>
    public enum Team
    {
        Red,
        Blue,
    }

    public static class Teams
    {
        public const int Count = 2;

        public static readonly Team[] All = { Team.Red, Team.Blue };

        public static Team Other(this Team team) => team == Team.Red ? Team.Blue : Team.Red;

        /// <summary>Red or Blue, as the HUD and the kill feed write it.</summary>
        public static string Name(this Team team) => team == Team.Red ? "Red" : "Blue";
    }
}
