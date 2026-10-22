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

        /// <summary>The fight phase of a number 1 to 3 (anything else is phase 1).</summary>
        public static BossPhase FromNumber(int number) => number == 2 ? BossPhase.Phase2 : number == 3 ? BossPhase.Phase3 : BossPhase.Phase1;
    }
}
