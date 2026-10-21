using System;
using System.Collections.Generic;

namespace SdfBattle
{
    /// <summary>The phases of a round: the countdown, play, and the result before the next round.</summary>
    public enum RoundPhase
    {
        /// <summary>Everyone at their spawn and unable to move, for <see cref="MatchRules.countdownSeconds"/>.</summary>
        Countdown,
        /// <summary>The round is on: the clock runs, captures count.</summary>
        Playing,
        /// <summary>A team won; the result shows for <see cref="MatchRules.resultSeconds"/>, then the next round starts.</summary>
        RoundEnd,
    }

    /// <summary>
    /// The match's rules (ticket 20) in plain C#, so EditMode tests can play it through without a scene: the roster, the phases
    /// (countdown → playing → round end → the next round's countdown), the score per team, the clock and sudden death, deaths, respawns
    /// and spawn protection. Time only moves through <see cref="Tick"/>; the scene's <c>Match</c> component calls it every frame and does
    /// what the events ask in the world (puts a spawned player's body in the zone, hides a dead one).
    /// <para>
    /// The other rules report to it and leave the resets to it: capture the flag (21) calls <see cref="ReportCapture"/> and returns its
    /// flags on <see cref="RoundReset"/>; combat (22) asks <see cref="CanHurt"/>, calls <see cref="ReportFired"/> and
    /// <see cref="ReportDeath"/>, and refills the inventories on <see cref="Spawned"/>; machines (24) kill through a <see cref="Death"/>
    /// with a killer name and team but no killer player.
    /// </para>
    /// </summary>
    public sealed class MatchState
    {
        private readonly List<PlayerState> players = new List<PlayerState>();
        private readonly int[] score = new int[Teams.Count];

        public MatchState(MatchRules rules) => Rules = rules ?? new MatchRules();

        public MatchRules Rules { get; }

        public RoundPhase Phase { get; private set; } = RoundPhase.Countdown;

        /// <summary>The round, from 1; 0 before <see cref="Begin"/>.</summary>
        public int Round { get; private set; }

        public bool Started => Round > 0;

        /// <summary>Seconds left on the round's clock; it stays at 0 through sudden death.</summary>
        public float TimeLeft { get; private set; }

        /// <summary>Seconds left of the countdown, or of the result before the next round; 0 while playing.</summary>
        public float PhaseTimeLeft { get; private set; }

        /// <summary>The clock ran out on a tie: the next capture wins.</summary>
        public bool SuddenDeath { get; private set; }

        /// <summary>The team that won the round, from its end until the next round starts; null before.</summary>
        public Team? Winner { get; private set; }

        public IReadOnlyList<PlayerState> Players => players;

        /// <summary>Whether players may move: not during the countdown, when everyone waits at their spawn.</summary>
        public bool CanMove => Phase != RoundPhase.Countdown;

        /// <summary>
        /// A new round begins, before anyone spawns: scores and counts are back to zero, every player is about to respawn. Rules with
        /// their own state reset it here (21 sends both flags home, 22 empties the inventories).
        /// </summary>
        public event Action RoundReset;

        /// <summary>The phase changed: the new one.</summary>
        public event Action<RoundPhase> PhaseChanged;

        /// <summary>A player (re)appeared, at the round's start or after the respawn wait: the world puts them in the team's zone.</summary>
        public event Action<PlayerState> Spawned;

        /// <summary>A player died; they respawn after <see cref="MatchRules.respawnSeconds"/>.</summary>
        public event Action<Death> Died;

        /// <summary>A capture counted: the team that scored and who carried the flag (null when nobody is known).</summary>
        public event Action<Team, PlayerState> Captured;

        /// <summary>The clock ran out on a tie: the next capture wins.</summary>
        public event Action SuddenDeathStarted;

        /// <summary>The round is over: the winner.</summary>
        public event Action<Team> RoundEnded;

        public int Score(Team team) => score[(int)team];

        /// <summary>The players of one team, in roster order.</summary>
        public IEnumerable<PlayerState> PlayersOf(Team team)
        {
            foreach (var player in players)
                if (player.Team == team)
                    yield return player;
        }

        /// <summary>Adds a participant; once the match has begun they spawn at once.</summary>
        public PlayerState AddPlayer(string name, Team team, bool isBot)
        {
            var player = new PlayerState(players.Count, name, team, isBot);
            players.Add(player);
            if (Started)
                Spawn(player);
            return player;
        }

        /// <summary>Starts the first round: everyone spawns and the countdown begins.</summary>
        public void Begin()
        {
            if (!Started)
                StartRound();
        }

        /// <summary>Lets <paramref name="seconds"/> pass: the phase's timer or the clock, the respawn waits and the spawn protection.</summary>
        public void Tick(float seconds)
        {
            if (!Started || seconds <= 0)
                return;
            switch (Phase)
            {
                case RoundPhase.Countdown:
                    PhaseTimeLeft = Math.Max(0, PhaseTimeLeft - seconds);
                    if (PhaseTimeLeft <= 0)
                        SetPhase(RoundPhase.Playing);
                    break;
                case RoundPhase.Playing:
                    if (!SuddenDeath)
                    {
                        TimeLeft = Math.Max(0, TimeLeft - seconds);
                        if (TimeLeft <= 0)
                            TimeUp();
                    }
                    break;
                case RoundPhase.RoundEnd:
                    PhaseTimeLeft = Math.Max(0, PhaseTimeLeft - seconds);
                    if (PhaseTimeLeft <= 0)
                    {
                        StartRound();
                        return;
                    }
                    break;
            }
            // A copy, as a handler may add a player
            foreach (var player in players.ToArray())
                if (player.Tick(seconds))
                    Spawn(player);
        }

        /// <summary>
        /// A team brought the enemy flag home (ticket 21 decides when that is). Counts only while playing; the team wins on reaching
        /// <see cref="MatchRules.capturesToWin"/> or in sudden death. Returns whether it counted.
        /// </summary>
        public bool ReportCapture(Team team, PlayerState by = null)
        {
            if (!Started || Phase != RoundPhase.Playing)
                return false;
            score[(int)team]++;
            if (by != null)
                by.Captures++;
            Captured?.Invoke(team, by);
            if (SuddenDeath || score[(int)team] >= Rules.capturesToWin)
                EndRound(team);
            return true;
        }

        /// <summary>A player died; they respawn after <see cref="MatchRules.respawnSeconds"/>. Ignored for one already dead.</summary>
        public void ReportDeath(Death death)
        {
            var victim = death.Victim;
            if (victim == null || !victim.Alive)
                return;
            victim.Die(Rules.respawnSeconds);
            if (death.CountsAsKill)
                death.Killer.Kills++;
            Died?.Invoke(death);
        }

        /// <summary>The player fell off the island, or below the kill height.</summary>
        public void ReportFall(PlayerState victim) => ReportDeath(new Death(victim, DeathCause.Fell));

        /// <summary>The player fired: their spawn protection ends.</summary>
        public void ReportFired(PlayerState player) => player?.EndProtection();

        /// <summary>
        /// Whether something of team <paramref name="attacker"/> (a player or a machine) can hurt <paramref name="victim"/>: the victim
        /// is alive and no longer protected, of the other team (friendly fire is off), and the round is not counting down.
        /// </summary>
        public bool CanHurt(PlayerState victim, Team attacker) =>
            victim != null && victim.Alive && !victim.Protected && Started && Phase != RoundPhase.Countdown
            && (Rules.friendlyFire || attacker != victim.Team);

        private void StartRound()
        {
            Round++;
            score[0] = score[1] = 0;
            SuddenDeath = false;
            Winner = null;
            TimeLeft = Rules.roundSeconds;
            Phase = RoundPhase.Countdown;
            PhaseTimeLeft = Rules.countdownSeconds;
            foreach (var player in players)
                player.ResetCounts();
            RoundReset?.Invoke();
            foreach (var player in players.ToArray())
                Spawn(player);
            PhaseChanged?.Invoke(Phase);
            // No countdown at all: straight into play
            if (PhaseTimeLeft <= 0)
                SetPhase(RoundPhase.Playing);
        }

        private void Spawn(PlayerState player)
        {
            player.Spawn(Rules.maxHealth, Rules.spawnProtectionSeconds);
            Spawned?.Invoke(player);
        }

        private void TimeUp()
        {
            if (score[0] != score[1])
            {
                EndRound(score[0] > score[1] ? Team.Red : Team.Blue);
                return;
            }
            SuddenDeath = true;
            SuddenDeathStarted?.Invoke();
        }

        private void EndRound(Team winner)
        {
            Winner = winner;
            PhaseTimeLeft = Rules.resultSeconds;
            SetPhase(RoundPhase.RoundEnd);
            RoundEnded?.Invoke(winner);
        }

        private void SetPhase(RoundPhase phase)
        {
            Phase = phase;
            if (phase == RoundPhase.Playing)
                PhaseTimeLeft = 0;
            PhaseChanged?.Invoke(phase);
        }
    }
}
