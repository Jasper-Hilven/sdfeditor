using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The spores' rules (ticket 25) in plain C#, beside the <see cref="CombatState"/> whose numbers they change: every player's
    /// <see cref="SporeMeter"/>, filled by kills, enemy machines destroyed and flag play, and the powers a full meter buys.
    /// <list type="bullet">
    /// <item>Gains: a kill (<see cref="MatchState.Died"/>, kill credit only), an enemy machine (<see cref="ReportMachineKilled"/>), taking
    /// the enemy flag, returning your own by touch, a capture (<see cref="CaptureTheFlagState"/>'s events). Nothing while a power is on;
    /// the meter fills again from 0 once it ends.</item>
    /// <item><see cref="Sporulate"/>: alive, the meter full, no power on and the round playing; it empties the meter.</item>
    /// <item>The powers through combat: Rush multiplies <see cref="CombatState.SpeedFactor"/>, Armour the damage of
    /// <see cref="CombatState.Hurt"/>, Regrowth heals through <see cref="CombatState.Heal"/> every step.</item>
    /// <item>A death ends the power and keeps the meter; a new round empties every meter.</item>
    /// </list>
    /// Time moves only through <see cref="Tick"/>, so EditMode tests play it without a scene.
    /// </summary>
    public sealed class SporeState
    {
        private readonly List<SporeMeter> meters = new List<SporeMeter>();

        public SporeState(MatchState match, CombatState combat, CaptureTheFlagState flags = null, SporeRules rules = null)
        {
            Match = match ?? throw new ArgumentNullException(nameof(match));
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            Flags = flags;
            Rules = rules ?? new SporeRules();
            match.Died += OnDied;
            match.RoundReset += OnRoundReset;
            combat.SpeedBoostOf = SpeedBoost;
            combat.DamageTakenOf = DamageTaken;
            if (flags != null)
            {
                flags.Taken += OnFlagTaken;
                flags.Returned += OnFlagReturned;
                flags.Captured += OnFlagCaptured;
            }
        }

        public MatchState Match { get; }

        public CombatState Combat { get; }

        public CaptureTheFlagState Flags { get; }

        public SporeRules Rules { get; }

        /// <summary>A player's meter went full.</summary>
        public event Action<PlayerState> Filled;

        /// <summary>A player sporulated: who, and the power.</summary>
        public event Action<PlayerState, SporePower> Sporulated;

        /// <summary>A player's power ended: it ran out, or they died, or a new round began.</summary>
        public event Action<PlayerState, SporePower> Ended;

        public SporeMeter MeterOf(PlayerState player)
        {
            while (meters.Count <= player.Index)
                meters.Add(new SporeMeter());
            return meters[player.Index];
        }

        /// <summary>The meter's share, 0 to 1.</summary>
        public float Share(PlayerState player) => Mathf.Clamp01(MeterOf(player).Amount / Mathf.Max(1e-4f, Rules.full));

        public bool IsFull(PlayerState player) => MeterOf(player).Amount >= Rules.full;

        /// <summary>Whether a power of this player's is on.</summary>
        public bool IsOn(PlayerState player, SporePower power) => MeterOf(player).Power == power;

        /// <summary>Adds spores to a player's meter, unless a power is on; up to full. Returns what was added.</summary>
        public float Gain(PlayerState player, float amount)
        {
            if (player == null || amount <= 0)
                return 0;
            var meter = MeterOf(player);
            if (meter.Active || meter.Amount >= Rules.full)
                return 0;
            var added = Mathf.Min(amount, Rules.full - meter.Amount);
            meter.Amount += added;
            if (meter.Amount >= Rules.full)
                Filled?.Invoke(player);
            return added;
        }

        /// <summary>Whether this player could sporulate now: alive, the meter full, no power on, the round playing.</summary>
        public bool CanSporulate(PlayerState player) =>
            player != null && player.Alive && Match.Phase == RoundPhase.Playing && !MeterOf(player).Active && IsFull(player);

        /// <summary>Spends a full meter on a power (see <see cref="CanSporulate"/>); false, and nothing changes, otherwise.</summary>
        public bool Sporulate(PlayerState player, SporePower power)
        {
            if (!CanSporulate(player))
                return false;
            var meter = MeterOf(player);
            meter.Amount = 0;
            meter.Power = power;
            meter.TimeLeft = Rules.SecondsOf(power);
            meter.RegrowthClock = 0;
            Sporulated?.Invoke(player, power);
            return true;
        }

        /// <summary>An enemy machine (ticket 24) was destroyed by this damage: its player, if any, gains spores.</summary>
        public void ReportMachineKilled(Damage damage)
        {
            if (damage.Attacker != null)
                Gain(damage.Attacker, Rules.machine);
        }

        /// <summary>Lets <paramref name="seconds"/> pass: the powers' time, and Regrowth's healing in steps of a quarter second.</summary>
        public void Tick(float seconds)
        {
            if (seconds <= 0)
                return;
            foreach (var player in Match.Players)
            {
                var meter = MeterOf(player);
                if (!meter.Active)
                    continue;
                var step = Mathf.Min(seconds, meter.TimeLeft);
                if (meter.Power == SporePower.Regrowth && player.Alive)
                {
                    meter.RegrowthClock += step;
                    while (meter.RegrowthClock >= CombatState.TickSeconds - 1e-5f)
                    {
                        meter.RegrowthClock -= CombatState.TickSeconds;
                        Combat.Heal(player, Rules.regrowthPerSecond * CombatState.TickSeconds);
                    }
                }
                meter.TimeLeft -= seconds;
                if (meter.TimeLeft <= 0)
                    End(player, meter);
            }
        }

        private void End(PlayerState player, SporeMeter meter)
        {
            if (!meter.Active)
                return;
            var power = meter.Power.Value;
            meter.End();
            Ended?.Invoke(player, power);
        }

        private float SpeedBoost(PlayerState player) => IsOn(player, SporePower.Rush) ? Rules.rushSpeed : 1;

        private float DamageTaken(PlayerState player) => IsOn(player, SporePower.Armour) ? Rules.armourDamage : 1;

        // ---------------------------------------------------------------- The match's and the flags' events

        private void OnDied(Death death)
        {
            End(death.Victim, MeterOf(death.Victim));
            if (death.CountsAsKill)
                Gain(death.Killer, Rules.kill);
        }

        private void OnRoundReset()
        {
            foreach (var player in Match.Players)
            {
                var meter = MeterOf(player);
                End(player, meter);
                meter.Amount = 0;
            }
        }

        private void OnFlagTaken(TeamFlag flag, PlayerState by) => Gain(by, Rules.flagTake);

        private void OnFlagReturned(TeamFlag flag, FlagReturn reason, PlayerState by)
        {
            if (reason == FlagReturn.Touched && by != null)
                Gain(by, Rules.flagReturn);
        }

        private void OnFlagCaptured(TeamFlag flag, PlayerState by) => Gain(by, Rules.capture);
    }
}
