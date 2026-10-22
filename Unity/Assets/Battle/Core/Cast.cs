namespace SdfBattle
{
    /// <summary>
    /// The names of the players (ticket 32): red is Mushroom 42 (you) and the numbered amanitas, blue is Prime's crew. Plain names for
    /// now; the campaign's setups (ticket 32.3) will bring their own. Past the lists a player is "Red 5" and the like.
    /// </summary>
    public static class Cast
    {
        public const string You = "Mushroom 42";

        public static readonly string[] Red = { You, "Mushroom 7", "Mushroom 13", "Mushroom 99" };

        public static readonly string[] Blue = { "Runner-Up", "Honourable Mention", "Participant", "Also-Ran", "Participation Award" };

        /// <summary>The name of a team's <paramref name="number"/>th player, counting from 1 (red's first is you).</summary>
        public static string Name(Team team, int number)
        {
            var names = team == Team.Red ? Red : Blue;
            return number >= 1 && number <= names.Length ? names[number - 1] : $"{team.Name()} {number}";
        }
    }
}
