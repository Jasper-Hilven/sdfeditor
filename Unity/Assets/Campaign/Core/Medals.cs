namespace SdfCampaign
{
    public enum Medal { None, Bronze, Silver, Gold }

    /// <summary>
    /// The medal of a finished battle (ticket 32, section 6): Bronze a win; Silver a win by 2 or more captures; Gold a win with blue at
    /// 0 captures within half the time limit, at Normal or Hard and not with RETRY EASIER. A win with RETRY EASIER earns at most Silver.
    /// </summary>
    public static class Medals
    {
        public static Medal Of(int redCaptures, int blueCaptures, float seconds, float timeLimitSeconds, Difficulty difficulty, bool retriedEasier)
        {
            if (redCaptures <= blueCaptures) return Medal.None;
            var silver = redCaptures - blueCaptures >= 2;
            var gold = blueCaptures == 0 && seconds <= timeLimitSeconds * 0.5f && difficulty != Difficulty.Easy && !retriedEasier;
            return gold ? Medal.Gold : silver ? Medal.Silver : Medal.Bronze;
        }

        public static string Key(Medal m) => m.ToString().ToLowerInvariant();

        public static Medal Parse(string key)
        {
            return key switch { "bronze" => Medal.Bronze, "silver" => Medal.Silver, "gold" => Medal.Gold, _ => Medal.None };
        }
    }
}
