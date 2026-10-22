using System;
using System.Collections.Generic;

namespace SdfBattle
{
    /// <summary>
    /// A player as Prime sees them when choosing a target (ticket 33): the world fills it in each time the planner is asked (distance and
    /// the line of sight are the scene's to measure).
    /// </summary>
    public struct BossTarget
    {
        public PlayerState Player;
        /// <summary>Metres from Prime.</summary>
        public float Distance;
        /// <summary>Prime has a clear line to them (the glare and the cannon need it).</summary>
        public bool InSight;
        /// <summary>Where Prime can't see: under the balcony in phase 1 (the turrets still can).</summary>
        public bool Hidden;
        /// <summary>They carry the flag (phase 3: the crown).</summary>
        public bool Carrier;
        /// <summary>Damage they dealt to Prime in the last seconds (<see cref="BossState.RecentDamageOf"/>).</summary>
        public float RecentDamage;
    }

    /// <summary>Whom Prime goes after (ticket 33, section 6).</summary>
    public static class Targeting
    {
        /// <summary>Alive, not under spawn protection, and not somewhere Prime cannot see (ticket 20, 22).</summary>
        public static bool IsValid(BossTarget target) =>
            target.Player != null && target.Player.Alive && !target.Player.Protected && !target.Hidden;

        /// <summary>
        /// The target of one attack among <paramref name="candidates"/> that pass <paramref name="accept"/> (any when null). Phase 3's
        /// order is the crown's carrier, then whoever dealt Prime the most damage lately, then the nearest; before that the nearest.
        /// Never a dead or spawn-protected player. Null when nobody qualifies.
        /// </summary>
        public static BossTarget? Choose(IReadOnlyList<BossTarget> candidates, BossPhase phase, Func<BossTarget, bool> accept = null)
        {
            BossTarget? carrier = null, damager = null, nearest = null;
            foreach (var c in candidates)
            {
                if (!IsValid(c) || (accept != null && !accept(c)))
                    continue;
                if (c.Carrier && (carrier == null || c.Distance < carrier.Value.Distance))
                    carrier = c;
                if (c.RecentDamage > 0 && (damager == null || c.RecentDamage > damager.Value.RecentDamage))
                    damager = c;
                if (nearest == null || c.Distance < nearest.Value.Distance)
                    nearest = c;
            }
            if (phase != BossPhase.Phase3)
                return nearest;
            return carrier ?? damager ?? nearest;
        }

        /// <summary>The mortar's targets: you first, then the nearest red bots, at most <paramref name="count"/>.</summary>
        public static List<BossTarget> Mortar(IReadOnlyList<BossTarget> candidates, int count)
        {
            var valid = new List<BossTarget>();
            foreach (var c in candidates)
                if (IsValid(c))
                    valid.Add(c);
            valid.Sort((a, b) =>
            {
                var humans = (a.Player.IsBot ? 1 : 0).CompareTo(b.Player.IsBot ? 1 : 0);
                return humans != 0 ? humans : a.Distance.CompareTo(b.Distance);
            });
            if (valid.Count > count)
                valid.RemoveRange(count, valid.Count - count);
            return valid;
        }
    }

    /// <summary>
    /// Picks Prime's next attack (ticket 33, section 10). Pure data in, pure data out: the world says what is going on
    /// (<see cref="Tick"/>) and draws <see cref="Current"/>.
    /// <list type="bullet">
    /// <item>One attack at a time, from its wind-up to the end of its recovery: never two wind-ups at once.</item>
    /// <item>Each attack has a cooldown counted from its start (<see cref="BossAttackSpec.cooldown"/>), and an opening delay at the
    /// start of a phase.</item>
    /// <item>The glare needs a target in sight (in phase 3 also farther than <see cref="BossRules.closeRange"/>), the cannon a target
    /// in sight, the slam a target within <see cref="BossRules.closeRange"/>, a grab a hand that is whole, the tank drop fewer than
    /// <see cref="BossRules.tanksMax"/> tanks alive.</item>
    /// <item>Nothing starts while Prime is stunned, or outside a fight phase.</item>
    /// </list>
    /// </summary>
    public sealed class BossAttackPlanner
    {
        private static readonly int KindCount = Enum.GetValues(typeof(BossAttackKind)).Length;
        private readonly float[] cooldown = new float[KindCount];
        private int nextHand;

        public BossAttackPlanner(BossRules rules)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public BossRules Rules { get; }

        /// <summary>The phase the cooldowns are for.</summary>
        public BossPhase Phase { get; private set; } = BossPhase.Intro;

        /// <summary>The attack in progress; null between attacks.</summary>
        public BossAttack Current { get; private set; }

        /// <summary>An attack began its wind-up.</summary>
        public event Action<BossAttack> Started;

        /// <summary>An attack went to its next stage (Active, Recover).</summary>
        public event Action<BossAttack> StageChanged;

        /// <summary>An attack ended after its recovery.</summary>
        public event Action<BossAttack> Finished;

        /// <summary>An attack was dropped halfway: Prime was stunned, or the phase ended.</summary>
        public event Action<BossAttack> Cancelled;

        public float CooldownLeft(BossAttackKind kind) => cooldown[(int)kind];

        /// <summary>A new phase: nothing in progress, the cooldowns at their opening delays.</summary>
        public void Reset(BossPhase phase)
        {
            Cancel();
            Phase = phase;
            Array.Clear(cooldown, 0, cooldown.Length);
            foreach (var kind in BossRules.AttacksOf(phase))
                cooldown[(int)kind] = Rules.Opening(kind);
        }

        /// <summary>Drops the attack in progress (Prime was stunned); the cooldowns stay.</summary>
        public void Cancel()
        {
            var attack = Current;
            Current = null;
            if (attack != null)
                Cancelled?.Invoke(attack);
        }

        /// <summary>
        /// Lets <paramref name="seconds"/> pass in <paramref name="phase"/> and starts the next attack when one is due.
        /// <paramref name="stunned"/>: Prime is in a window (nothing new starts). <paramref name="tanksAlive"/> are Prime's tanks now;
        /// the hands are whole or not.
        /// </summary>
        public void Tick(float seconds, BossPhase phase, bool soreLoser, bool stunned, IReadOnlyList<BossTarget> targets, int tanksAlive = 0,
            bool leftHand = true, bool rightHand = true)
        {
            if (phase != Phase)
                Reset(phase);
            if (!phase.IsFight() || seconds <= 0)
                return;
            for (var i = 0; i < cooldown.Length; i++)
                cooldown[i] = Math.Max(0, cooldown[i] - seconds);

            var left = seconds;
            while (Current != null && left > 0)
            {
                var attack = Current;
                left = attack.Advance(left, out var changed);
                if (!changed)
                    break;
                if (attack.Finished)
                {
                    Current = null;
                    Finished?.Invoke(attack);
                }
                else
                {
                    StageChanged?.Invoke(attack);
                }
            }
            if (Current == null && !stunned)
                TryStart(phase, soreLoser, targets, tanksAlive, leftHand, rightHand);
        }

        private void TryStart(BossPhase phase, bool soreLoser, IReadOnlyList<BossTarget> targets, int tanksAlive, bool leftHand, bool rightHand)
        {
            foreach (var kind in BossRules.AttacksOf(phase))
            {
                if (cooldown[(int)kind] > 0)
                    continue;
                var spec = Rules.SpecOf(kind, soreLoser && phase == BossPhase.Phase3);
                var attack = new BossAttack(spec);
                if (!Aim(attack, phase, targets, tanksAlive, leftHand, rightHand))
                    continue;
                cooldown[(int)kind] = spec.cooldown;
                Current = attack;
                Started?.Invoke(attack);
                return;
            }
        }

        /// <summary>Whether the attack has what it needs; adds its targets.</summary>
        private bool Aim(BossAttack attack, BossPhase phase, IReadOnlyList<BossTarget> targets, int tanksAlive, bool leftHand, bool rightHand)
        {
            BossTarget? pick;
            switch (attack.Kind)
            {
                case BossAttackKind.TankDrop:
                    return tanksAlive < Rules.tanksMax;
                case BossAttackKind.SporeMortar:
                    foreach (var t in Targeting.Mortar(targets, (int)attack.Spec.count))
                        attack.AddTarget(t.Player);
                    return attack.Targets.Count > 0;
                case BossAttackKind.RoyalGlare:
                    var far = phase == BossPhase.Phase3;
                    pick = Targeting.Choose(targets, phase, t => t.InSight && (!far || t.Distance > Rules.closeRange));
                    break;
                case BossAttackKind.CapCannon:
                    pick = Targeting.Choose(targets, phase, t => t.InSight);
                    break;
                case BossAttackKind.CapSlam:
                    pick = Targeting.Choose(targets, phase, t => t.Distance <= Rules.closeRange);
                    break;
                case BossAttackKind.HandGrab:
                    // The hands alternate; a burst hand is skipped
                    var hand = nextHand == 0 ? (leftHand ? 0 : rightHand ? 1 : -1) : (rightHand ? 1 : leftHand ? 0 : -1);
                    if (hand < 0)
                        return false;
                    pick = Targeting.Choose(targets, phase);
                    if (pick == null)
                        return false;
                    attack.Hand = hand;
                    nextHand = 1 - hand;
                    break;
                default:
                    pick = Targeting.Choose(targets, phase);
                    break;
            }
            if (pick == null)
                return false;
            attack.AddTarget(pick.Value.Player);
            return true;
        }
    }
}
