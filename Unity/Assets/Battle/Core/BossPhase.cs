namespace SdfBattle
{
    /// <summary>
    /// The stages of the final boss fight (ticket 33): the intro (B7 and the name card), the three phases with the two transitions
    /// between them, and the two ends. <see cref="BossState"/> walks through them.
    /// </summary>
    public enum BossPhase
    {
        /// <summary>Before the fight: the story beat and Prime's name card.</summary>
        Intro,
        /// <summary>"The Royal Box": the spore shield, capture the flag drops it.</summary>
        Phase1,
        /// <summary>Prime calls a timeout; the helicopter rises (6 s, nobody can be hurt).</summary>
        Transition12,
        /// <summary>"The Prime-copter": the spore sacs, the crash, the daze.</summary>
        Phase2,
        /// <summary>The copter's last crash and the crown (6 s, nobody can be hurt).</summary>
        Transition23,
        /// <summary>"The Crown": Prime on foot, the crown is the flag.</summary>
        Phase3,
        /// <summary>Prime's last bar is gone.</summary>
        Victory,
        /// <summary>42 lost the last life.</summary>
        Defeat,
    }

    public static class BossPhases
    {
        /// <summary>Whether the fight is on: Prime attacks and can be hurt (a phase, not a transition or an end).</summary>
        public static bool IsFight(this BossPhase phase) => phase == BossPhase.Phase1 || phase == BossPhase.Phase2 || phase == BossPhase.Phase3;

        /// <summary>The bar a fight phase fights: 0 to 2; -1 for any other.</summary>
        public static int BarOf(this BossPhase phase) => phase switch
        {
            BossPhase.Phase1 => 0,
            BossPhase.Phase2 => 1,
            BossPhase.Phase3 => 2,
            _ => -1,
        };

        /// <summary>The phase's name on the phase card and the Defeat screen; "" for any other.</summary>
        public static string TitleOf(this BossPhase phase) => phase switch
        {
            BossPhase.Phase1 => "THE ROYAL BOX",
            BossPhase.Phase2 => "THE PRIME-COPTER",
            BossPhase.Phase3 => "THE CROWN",
            _ => "",
        };

        /// <summary>The fight phase of a number 1 to 3 (anything else is phase 1).</summary>
        public static BossPhase FromNumber(int number) => number == 2 ? BossPhase.Phase2 : number == 3 ? BossPhase.Phase3 : BossPhase.Phase1;
    }

    /// <summary>
    /// The ids of the boss fight's story beats (ticket 33.8): the two transitions and the victory. The shots are in the campaign's
    /// <c>Beats</c> (32's beat data); the fight asks for them by these ids and answers the cues their shots raise.
    /// </summary>
    public static class BossBeats
    {
        public const string Transition12 = "boss-transition-12", Transition23 = "boss-transition-23", Victory = "boss-victory";

        /// <summary>Seconds a transition lasts when no beat plays (after a retry, or in a scene played alone); the world still changes.</summary>
        public const float QuickTransitionSeconds = 1.5f;

        // The cues the beats' shots raise (a shot's cue is raised as the shot starts)
        public const string CopterRises = "copter-rises", BalconyFalls = "balcony-falls", CopterDown = "copter-down", CrownFlies = "crown-flies",
            PrimeClimbs = "prime-climbs", Shrink = "shrink", CrownRolls = "crown-rolls", Cheer = "cheer", PixelBreak = "pixel-break";

        /// <summary>The beat that plays for a transition; null for any other phase.</summary>
        public static string ForTransition(BossPhase phase) =>
            phase == BossPhase.Transition12 ? Transition12 : phase == BossPhase.Transition23 ? Transition23 : null;
    }
}
