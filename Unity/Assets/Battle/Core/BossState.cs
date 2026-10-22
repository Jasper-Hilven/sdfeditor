using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>Why Prime can be hurt: the window that is open.</summary>
    public enum BossStunKind
    {
        None,
        /// <summary>Phase 1: kneeling after a red capture, the shield down.</summary>
        Kneel,
        /// <summary>Phase 2: dazed on the wreck after the crash.</summary>
        Daze,
        /// <summary>Phase 3: the cap stuck in the floor after a slam.</summary>
        StuckCap,
        /// <summary>Phase 3: a hand burst.</summary>
        HandStun,
    }

    /// <summary>What the Prime-copter is doing in phase 2.</summary>
    public enum CopterStage
    {
        /// <summary>Flying laps; the spore sacs hang under it.</summary>
        Flying,
        /// <summary>The last sac burst: the warning before the crash (2 s).</summary>
        Crashing,
        /// <summary>Down on the wreck, Prime dazed.</summary>
        Dazed,
    }

    /// <summary>What one hit on Prime did.</summary>
    public readonly struct BossHit
    {
        public BossHit(float requested, float dealt, bool blocked, bool weakPoint, bool barEmptied)
        {
            Requested = requested;
            Dealt = dealt;
            Blocked = blocked;
            WeakPoint = weakPoint;
            BarEmptied = barEmptied;
        }

        /// <summary>The damage that arrived, before the multipliers.</summary>
        public float Requested { get; }
        /// <summary>What came off the bar (the overflow at a bar's end is lost).</summary>
        public float Dealt { get; }
        /// <summary>Nothing came off: the shield, a closed window, a transition, a shut eye.</summary>
        public bool Blocked { get; }
        /// <summary>A weak point was open and hit: the gold hit marker and the bigger number.</summary>
        public bool WeakPoint { get; }
        /// <summary>This hit took the bar to 0.</summary>
        public bool BarEmptied { get; }
    }

    /// <summary>A phase start, which a retry comes back to: the phase, and what Prime's bars were at its start.</summary>
    public readonly struct BossCheckpoint
    {
        public BossCheckpoint(BossPhase phase, float[] bars)
        {
            Phase = phase;
            Bars = bars;
        }

        public BossPhase Phase { get; }
        public float[] Bars { get; }
    }

    /// <summary>
    /// The final boss fight's state machine (ticket 33, section 10): the phase, Prime's three bars, the shield, Royal Immunity, the
    /// windows in which the weak points are open, 42's lives, the checkpoints, and the events the world and the HUD follow. Plain C#, so
    /// EditMode tests play whole fights without a scene; time moves only through <see cref="Tick"/>.
    /// <list type="bullet">
    /// <item>Bars: 1500 / 1500 / 2000 at Normal (<see cref="BossRules"/>). A bar cannot be hurt past its end; its last point sends the fight to the
    /// next transition (<see cref="FinishTransition"/> goes on), or to <see cref="BossPhase.Victory"/> after bar 3.</item>
    /// <item>Phase 1: the shield stops everything until a red capture of the blue flag; Prime then kneels for 12 s (gills ×2). A blue
    /// capture heals 250, at most to the bar's top.</item>
    /// <item>Phase 2: only the spore sacs (<see cref="HurtSac"/>, their damage counts on the bar) and, after the fourth bursts, the
    /// crash and the daze (15 s, gills ×2) hurt Prime; a daze that ends with the bar not empty regrows two sacs.</item>
    /// <item>Phase 3: always hurtable: the eyes ×2 (shut ×0 during a wind-up), the gills ×2 while a window is open; half damage
    /// while the crown is Prime's (not taken, or back by Prime's touch, <see cref="PrimeTouchesCrown"/>); the crown's capture ends that
    /// for good and brings out the champion's minigun (×1.5 against Prime only).</item>
    /// <item>Lives: 42 gets <see cref="BossRules.lives"/> a phase; a death costs one (<see cref="ReportDeath"/>); the last is
    /// <see cref="Lost"/>. A phase start is a checkpoint: <see cref="RestoreCheckpoint"/> comes back to it.</item>
    /// </list>
    /// It follows <see cref="CaptureTheFlagState"/> (a red capture of the blue flag, a blue capture, the crown taken, returned and
    /// captured), reports Prime's damage to <see cref="SporeState"/> (the boss gains) and ends a <see cref="MatchObjective.Boss"/> match.
    /// </summary>
    public sealed class BossState
    {
        private readonly float[] bars = new float[3];
        private readonly int[] livesLostIn = new int[3];
        private readonly List<(float time, int player, float amount)> recent = new List<(float, int, float)>();
        private float vulnerableLeft;
        private float crashLeft;
        private PlayerState crashStarter;
        private float clock;
        private bool soreLoser;
        private bool crownCaptured;
        private bool crownAway;
        private bool shield, immune;

        public BossState(BossRules rules, MatchState match = null, CaptureTheFlagState flags = null, SporeState spores = null)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Match = match;
            Flags = flags;
            Spores = spores;
            Parts = new BossParts(rules);
            Planner = new BossAttackPlanner(rules);
            Planner.StageChanged += OnAttackStage;
            if (flags != null)
            {
                flags.Captured += OnCaptured;
                flags.Taken += OnTaken;
                flags.Returned += OnReturned;
            }
        }

        public BossRules Rules { get; }

        public MatchState Match { get; }

        public CaptureTheFlagState Flags { get; }

        public SporeState Spores { get; }

        /// <summary>The spore sacs and the hands.</summary>
        public BossParts Parts { get; }

        /// <summary>Picks Prime's attacks; the state cancels what a stun interrupts and turns the slam's recovery into the stuck cap.</summary>
        public BossAttackPlanner Planner { get; }

        public BossPhase Phase { get; private set; } = BossPhase.Intro;

        /// <summary>The bar being fought, 0 to 2; the bar of the last phase reached otherwise.</summary>
        public int Bar { get; private set; }

        public float BarHealth(int bar) => bars[bar];

        public float BarMax(int bar) => Rules.BarMax(bar);

        /// <summary>Prime's health left over all three bars.</summary>
        public float Health => bars[0] + bars[1] + bars[2];

        /// <summary>The current bar's health, and its most.</summary>
        public float CurrentBar => bars[Bar];

        public float CurrentBarMax => Rules.BarMax(Bar);

        /// <summary>The spore shield is up: phase 1, no window open. Nothing hurts Prime.</summary>
        public bool ShieldUp => Phase == BossPhase.Phase1 && !Vulnerable;

        /// <summary>Royal Immunity: phase 3, the crown Prime's (still on the wreck, or back from being dropped), so half damage.</summary>
        public bool Immune => Phase == BossPhase.Phase3 && !crownCaptured && !crownAway;

        /// <summary>The crown was captured at the red stand: no more immunity this phase, and the champion's minigun is out.</summary>
        public bool CrownCaptured => crownCaptured;

        /// <summary>A window is open: the gills show (phase 1, 2), a stuck cap or a stunned Prime (phase 3).</summary>
        public bool Vulnerable => vulnerableLeft > 0;

        public BossStunKind StunKind { get; private set; }

        public float VulnerableLeft => vulnerableLeft;

        public CopterStage Copter { get; private set; }

        /// <summary>Seconds until the copter lands, while it is <see cref="CopterStage.Crashing"/>.</summary>
        public float CrashLeft => crashLeft;

        /// <summary>Phase 3, bar 3 under <see cref="BossRules.soreLoserBelow"/>: wind-ups shorter, more blue scouts.</summary>
        public bool SoreLoser => soreLoser;

        /// <summary>Prime's eyes are shut: phase 3, an attack winding up. Hits on them do nothing.</summary>
        public bool EyesShut => Phase == BossPhase.Phase3 && Planner.Current != null && Planner.Current.Stage == BossAttackStage.WindUp;

        /// <summary>42's lives left in this phase.</summary>
        public int Lives { get; private set; }

        /// <summary>Lives lost in the whole fight, and in one phase (1 to 3), for the medals.</summary>
        public int LivesLost { get; private set; }

        public int LivesLostIn(int phaseNumber) => livesLostIn[Math.Max(1, Math.Min(3, phaseNumber)) - 1];

        /// <summary>Seconds fought so far (the phases, not the transitions' or the intro's time).</summary>
        public float FightSeconds { get; private set; }

        /// <summary>What you took off Prime's bars since the fight began (the Victory screen's number); a retry of a phase keeps it, RESTART FIGHT clears it.</summary>
        public float DamageDealt { get; private set; }

        /// <summary>Prime's attack damage is the rules' scaled numbers; nothing attacks outside a fight phase.</summary>
        public bool CanAttack => Phase.IsFight();

        /// <summary>The last phase start.</summary>
        public BossCheckpoint Checkpoint { get; private set; }

        /// <summary>The phase changed (also by a checkpoint restore).</summary>
        public event Action<BossPhase> PhaseChanged;

        /// <summary>Prime was hurt (also reports a blocked hit, to show the spark).</summary>
        public event Action<BossHit> Damaged;

        /// <summary>A window opened: why, and for how many seconds.</summary>
        public event Action<BossStunKind, float> Stunned;

        /// <summary>The window closed.</summary>
        public event Action StunEnded;

        /// <summary>The shield went up (true) or down.</summary>
        public event Action<bool> ShieldChanged;

        /// <summary>Royal Immunity came on (true) or went off.</summary>
        public event Action<bool> ImmunityChanged;

        /// <summary>A blue capture healed Prime by this much.</summary>
        public event Action<float> Healed;

        /// <summary>Phase 2: the last sac burst, the copter starts down. The player who burst it.</summary>
        public event Action<PlayerState> CrashStarted;

        /// <summary>Phase 2: the copter hit the floor.</summary>
        public event Action CrashLanded;

        /// <summary>Phase 2: the daze ended with the bar not empty; sacs grew back.</summary>
        public event Action SacsRegrown;

        /// <summary>Phase 3: Sore Loser began.</summary>
        public event Action SoreLoserStarted;

        /// <summary>Phase 3: the crown was captured by this player; the champion's minigun appears.</summary>
        public event Action<PlayerState> CrownCapturedBy;

        /// <summary>42 lost a life: the lives left.</summary>
        public event Action<int> LifeLost;

        /// <summary>A checkpoint was restored: the phase.</summary>
        public event Action<BossCheckpoint> CheckpointRestored;

        /// <summary>Prime's last bar is gone.</summary>
        public event Action Defeated;

        /// <summary>42 lost the last life.</summary>
        public event Action Lost;

        // ---------------------------------------------------------------- Flow

        /// <summary>
        /// Starts the fight at phase 1 (or the phase RETRY PHASE asks for): everything new, the lives and the counters too.
        /// </summary>
        public void Begin(int startPhase = 1)
        {
            LivesLost = 0;
            Array.Clear(livesLostIn, 0, livesLostIn.Length);
            FightSeconds = 0;
            DamageDealt = 0;
            EnterPhase(BossPhases.FromNumber(startPhase));
        }

        /// <summary>A transition is over (the sequencer's 6 s, or skipped): on to the next phase. Nothing outside a transition.</summary>
        public void FinishTransition()
        {
            if (Phase == BossPhase.Transition12)
                EnterPhase(BossPhase.Phase2);
            else if (Phase == BossPhase.Transition23)
                EnterPhase(BossPhase.Phase3);
        }

        /// <summary>The checkpoint (the last phase start) as it stands now.</summary>
        public BossCheckpoint Snapshot() => Checkpoint;

        /// <summary>
        /// Back to a phase start (RETRY PHASE): that phase's bar full, the earlier ones spent, the later ones full, the sacs and hands
        /// whole, no window, flags home, the meter full, 42's lives full. The lives lost so far stay counted. Defaults to the last checkpoint.
        /// </summary>
        public void RestoreCheckpoint(BossCheckpoint? checkpoint = null)
        {
            var point = checkpoint ?? Checkpoint;
            EnterPhase(point.Phase);
            CheckpointRestored?.Invoke(point);
        }

        /// <summary>RESTART FIGHT: from phase 1 with the counters cleared.</summary>
        public void Restart() => Begin(1);

        private void EnterPhase(BossPhase phase)
        {
            Phase = phase;
            Bar = Math.Max(0, phase.BarOf());
            for (var i = 0; i < bars.Length; i++)
                bars[i] = i < Bar ? 0 : Rules.BarMax(i);
            Parts.Reset();
            vulnerableLeft = 0;
            StunKind = BossStunKind.None;
            Copter = CopterStage.Flying;
            crashLeft = 0;
            crashStarter = null;
            soreLoser = false;
            crownCaptured = false;
            crownAway = false;
            recent.Clear();
            Lives = Rules.lives;
            Planner.Reset(phase);
            Flags?.Reset();
            Checkpoint = new BossCheckpoint(phase, (float[])bars.Clone());
            // A checkpoint gift: the meter is full at every phase start
            if (Spores != null && Match != null)
                foreach (var player in Match.Players)
                    if (!player.IsBot)
                        Spores.Gain(player, Spores.Rules.full);
            Refresh();
            PhaseChanged?.Invoke(Phase);
        }

        private void GoTo(BossPhase phase)
        {
            Phase = phase;
            if (vulnerableLeft > 0)
                EndWindow();
            Copter = CopterStage.Flying;
            crashLeft = 0;
            Planner.Reset(phase);
            Refresh();
            PhaseChanged?.Invoke(Phase);
        }

        // ---------------------------------------------------------------- Damage

        /// <summary>
        /// A hit on Prime's body: <paramref name="amount"/> to <paramref name="part"/>, dealt by <paramref name="by"/> (for the spores and
        /// phase 3's target choice). The part's multiplier applies (see the class), then Royal Immunity, then the champion's minigun's ×1.5
        /// when <paramref name="championMinigun"/>. Nothing in a transition, outside the fight, through the shield, or with a window closed in
        /// phases 1 and 2. A bar cannot be hurt past its end.
        /// </summary>
        public BossHit Hurt(float amount, BossPart part = BossPart.Body, PlayerState by = null, bool championMinigun = false)
        {
            var multiplier = PartMultiplier(part, out var weak);
            if (!Phase.IsFight() || amount <= 0 || multiplier <= 0)
            {
                var blocked = new BossHit(amount, 0, true, false, false);
                if (amount > 0)
                    Damaged?.Invoke(blocked);
                return blocked;
            }
            if (Immune)
                multiplier *= Rules.immunityMultiplier;
            if (championMinigun)
                multiplier *= Rules.championMinigunMultiplier;
            var dealt = TakeFromBar(amount * multiplier, by, out var emptied);
            var hit = new BossHit(amount, dealt, false, weak, emptied);
            Damaged?.Invoke(hit);
            return hit;
        }

        /// <summary>The multiplier a part has now, before immunity and the champion's minigun; 0 when it cannot be hurt.</summary>
        public float PartMultiplier(BossPart part, out bool weakPoint)
        {
            weakPoint = false;
            if (!Phase.IsFight())
                return 0;
            if (Phase != BossPhase.Phase3)
            {
                // The shield (phase 1) and the copter (phase 2) stop everything; only an open window lets damage through
                if (!Vulnerable)
                    return 0;
                weakPoint = part == BossPart.Gills;
                return part == BossPart.Gills ? Rules.gillsMultiplier : 1;
            }
            switch (part)
            {
                case BossPart.Eyes:
                    if (EyesShut)
                        return 0;
                    weakPoint = true;
                    return Rules.eyesMultiplier;
                case BossPart.Gills:
                    weakPoint = Vulnerable;
                    return Vulnerable ? Rules.gillsMultiplier : 1;
                default:
                    return 1;
            }
        }

        /// <summary>
        /// A hit on one of the copter's spore sacs (phase 2, while it flies): the sac's own health takes it and the bar takes the same.
        /// Returns what the sac took. The fourth sac to burst starts the crash.
        /// </summary>
        public float HurtSac(int index, float amount, PlayerState by = null)
        {
            if (Phase != BossPhase.Phase2 || Copter != CopterStage.Flying || amount <= 0 || !Parts.SacAlive(index))
                return 0;
            var taken = Parts.HurtSac(index, amount);
            TakeFromBar(taken, by, out _);
            if (!Parts.SacAlive(index))
            {
                Spores?.ReportBossSac(by);
                if (Phase == BossPhase.Phase2 && Parts.SacsAlive == 0)
                    StartCrash(by);
            }
            return taken;
        }

        /// <summary>
        /// A hit on one of Prime's hands (phase 3): the hand's health takes it, not the bar. A burst hand stuns Prime for 4 s (gills ×2)
        /// and grows back in 20 s. Returns what the hand took.
        /// </summary>
        public float HurtHand(int index, float amount, PlayerState by = null)
        {
            if (Phase != BossPhase.Phase3 || amount <= 0 || !Parts.HandAlive(index))
                return 0;
            var taken = Parts.HurtHand(index, amount);
            if (!Parts.HandAlive(index))
            {
                Spores?.ReportBossHand(by);
                Stun(BossStunKind.HandStun, Rules.Window(Rules.handStunSeconds), null);
            }
            return taken;
        }

        private float TakeFromBar(float amount, PlayerState by, out bool emptied)
        {
            emptied = false;
            var bar = Phase.BarOf();
            if (bar < 0 || amount <= 0)
                return 0;
            // The overflow past the bar's end is lost
            var dealt = Math.Min(bars[bar], amount);
            bars[bar] -= dealt;
            if (by != null && !by.IsBot)
                DamageDealt += dealt;
            if (by != null && dealt > 0)
            {
                recent.Add((clock, by.Index, dealt));
                Spores?.ReportBossDamage(by, dealt);
            }
            if (Phase == BossPhase.Phase3 && !soreLoser && bars[2] < Rules.BarMax(2) * Rules.soreLoserBelow)
            {
                soreLoser = true;
                SoreLoserStarted?.Invoke();
            }
            if (bars[bar] <= 0)
            {
                emptied = true;
                EndBar();
            }
            return dealt;
        }

        private void EndBar()
        {
            switch (Phase)
            {
                case BossPhase.Phase1:
                    GoTo(BossPhase.Transition12);
                    break;
                case BossPhase.Phase2:
                    GoTo(BossPhase.Transition23);
                    break;
                case BossPhase.Phase3:
                    GoTo(BossPhase.Victory);
                    Defeated?.Invoke();
                    Match?.EndBossFight(Team.Red);
                    break;
            }
        }

        private void Heal(float amount)
        {
            if (!Phase.IsFight())
                return;
            var bar = Phase.BarOf();
            var added = Math.Min(Rules.BarMax(bar) - bars[bar], amount);
            if (added <= 0)
                return;
            bars[bar] += added;
            Healed?.Invoke(added);
        }

        /// <summary>Damage <paramref name="player"/> dealt to Prime in the last <see cref="BossRules.recentDamageSeconds"/>.</summary>
        public float RecentDamageOf(PlayerState player)
        {
            var sum = 0f;
            foreach (var entry in recent)
                if (entry.player == player.Index && clock - entry.time <= Rules.recentDamageSeconds)
                    sum += entry.amount;
            return sum;
        }

        // ---------------------------------------------------------------- Windows

        private void Stun(BossStunKind kind, float seconds, PlayerState credit)
        {
            if (!Phase.IsFight() || seconds <= 0)
                return;
            // The slam's own stuck cap is the end of the attack that causes it; anything else interrupts what Prime was doing
            if (kind != BossStunKind.StuckCap)
                Planner.Cancel();
            if (seconds >= vulnerableLeft)
            {
                vulnerableLeft = seconds;
                StunKind = kind;
            }
            if (credit != null)
                Spores?.ReportBossStun(credit);
            Refresh();
            Stunned?.Invoke(kind, seconds);
        }

        private void EndWindow()
        {
            vulnerableLeft = 0;
            StunKind = BossStunKind.None;
            Refresh();
            StunEnded?.Invoke();
            if (Phase == BossPhase.Phase2 && Copter == CopterStage.Dazed)
            {
                Copter = CopterStage.Flying;
                if (bars[1] > 0)
                {
                    Parts.RegrowSacs(Rules.sacRegrow);
                    SacsRegrown?.Invoke();
                }
            }
        }

        private void StartCrash(PlayerState by)
        {
            Copter = CopterStage.Crashing;
            crashLeft = Rules.crashSeconds;
            crashStarter = by;
            Planner.Cancel();
            Spores?.ReportBossStun(by);
            CrashStarted?.Invoke(by);
        }

        // ---------------------------------------------------------------- Lives

        /// <summary>
        /// 42 died (a bot's death costs nothing: the red bots respawn without limit): one life less; the last one is a defeat.
        /// Nothing outside a fight phase.
        /// </summary>
        public void ReportDeath(PlayerState you)
        {
            if (you == null || you.IsBot || !Phase.IsFight() || Lives <= 0)
                return;
            Lives--;
            LivesLost++;
            livesLostIn[Bar]++;
            LifeLost?.Invoke(Lives);
            if (Lives > 0)
                return;
            GoTo(BossPhase.Defeat);
            Lost?.Invoke();
            Match?.EndBossFight(Team.Blue);
        }

        // ---------------------------------------------------------------- Flags

        private void OnCaptured(TeamFlag flag, PlayerState by)
        {
            if (by == null)
                return;
            if (flag.Team == Team.Blue && by.Team == Team.Red)
            {
                if (Phase == BossPhase.Phase1)
                {
                    Stun(BossStunKind.Kneel, Rules.Window(Rules.kneelSeconds), by);
                }
                else if (Phase == BossPhase.Phase3 && !crownCaptured)
                {
                    crownCaptured = true;
                    Refresh();
                    CrownCapturedBy?.Invoke(by);
                }
            }
            else if (flag.Team == Team.Red && by.Team == Team.Blue && Phase == BossPhase.Phase1)
            {
                Heal(Rules.blueCaptureHeal);
            }
        }

        private void OnTaken(TeamFlag flag, PlayerState by)
        {
            if (Phase != BossPhase.Phase3 || flag.Team != Team.Blue || crownCaptured)
                return;
            crownAway = true;
            Refresh();
        }

        private void OnReturned(TeamFlag flag, FlagReturn reason, PlayerState by)
        {
            if (Phase != BossPhase.Phase3 || flag.Team != Team.Blue || crownCaptured)
                return;
            if (reason == FlagReturn.Touched || reason == FlagReturn.TimedOut || reason == FlagReturn.Unreachable)
            {
                crownAway = false;
                Refresh();
            }
        }

        /// <summary>
        /// Prime walked onto the dropped crown: it is back on Prime's head and Royal Immunity with it (a flag's own team returning it,
        /// ticket 21). False when the crown is not dropped.
        /// </summary>
        public bool PrimeTouchesCrown() => Phase == BossPhase.Phase3 && !crownCaptured && Flags != null && Flags.ReturnDropped(Team.Blue);

        // ---------------------------------------------------------------- Time

        /// <summary>Lets <paramref name="seconds"/> pass: the windows, the crash, the hands' regrowth.</summary>
        public void Tick(float seconds)
        {
            if (seconds <= 0)
                return;
            clock += seconds;
            if (Phase.IsFight())
                FightSeconds += seconds;
            Parts.Tick(seconds);
            if (!Phase.IsFight())
                return;
            if (Copter == CopterStage.Crashing)
            {
                crashLeft -= seconds;
                if (crashLeft <= 0)
                {
                    crashLeft = 0;
                    Copter = CopterStage.Dazed;
                    CrashLanded?.Invoke();
                    Stun(BossStunKind.Daze, Rules.Window(Rules.dazeSeconds), null);
                    return;
                }
            }
            if (vulnerableLeft > 0)
            {
                vulnerableLeft -= seconds;
                if (vulnerableLeft <= 0)
                    EndWindow();
            }
        }

        /// <summary>
        /// Lets the planner decide what Prime does next (ticket 33, section 10): the same call as <see cref="Tick"/> with who Prime can
        /// pick. Nothing starts while a window is open or the copter is down; <paramref name="tanksAlive"/> are Prime's tanks now.
        /// </summary>
        public void Plan(float seconds, IReadOnlyList<BossTarget> targets, int tanksAlive = 0)
        {
            var stunned = Vulnerable || (Phase == BossPhase.Phase2 && Copter != CopterStage.Flying);
            Planner.Tick(seconds, Phase, soreLoser, stunned, targets, tanksAlive, Parts.HandAlive(0), Parts.HandAlive(1));
        }

        private void OnAttackStage(BossAttack attack)
        {
            // The slam ends with the cap stuck in the floor: the gills show for as long as its recovery lasts
            if (attack.Kind == BossAttackKind.CapSlam && attack.Stage == BossAttackStage.Recover)
                Stun(BossStunKind.StuckCap, attack.Spec.recover, null);
        }

        private void Refresh()
        {
            var nowShield = ShieldUp;
            if (nowShield != shield)
            {
                shield = nowShield;
                ShieldChanged?.Invoke(shield);
            }
            var nowImmune = Immune;
            if (nowImmune != immune)
            {
                immune = nowImmune;
                ImmunityChanged?.Invoke(immune);
            }
        }
    }
}
