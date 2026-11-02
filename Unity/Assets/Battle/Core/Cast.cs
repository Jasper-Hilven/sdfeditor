namespace SdfBattle
{
    /// <summary>
    /// The names of the players (ticket 32): red starts with you, then three team mates; blue has five. They are players' handles, each with a monster head (ticket 50). Past the lists a player is "Red 5" and the like.
    /// </summary>
    public static class Cast
    {
        public const string You = "Nightowl42";

        public static readonly string[] Red = { You, "ToastyMage", "Zed_13", "Pixelpuss" };

        public static readonly string[] Blue = { "Big Gerald", "SuzyTwoShoes", "Captain Nope", "xX_Vlad_Xx", "Grandma Gunner" };

        /// <summary>The name of a team's <paramref name="number"/>th player, counting from 1 (red's first is you).</summary>
        public static string Name(Team team, int number)
        {
            var names = team == Team.Red ? Red : Blue;
            return number >= 1 && number <= names.Length ? names[number - 1] : $"{team.Name()} {number}";
        }
    }
}
