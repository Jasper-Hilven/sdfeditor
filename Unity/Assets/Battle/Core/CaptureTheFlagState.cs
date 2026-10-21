using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// Capture the flag's rules (ticket 21) in plain C#, on top of a <see cref="MatchState"/>, so EditMode tests can play them without a
    /// scene. Each team has a flag (<see cref="TeamFlag"/>): at home on its stand, carried by an enemy, or dropped where its carrier died.
    /// The world reports what players touch (<see cref="TouchStand"/>, <see cref="TouchFlag"/>) and time (<see cref="Tick"/>); the rules
    /// decide, report captures to the match and raise events the world and the HUD follow.
    /// <list type="bullet">
    /// <item>Touching the enemy stand while its flag is home, or the enemy flag where it lies, takes it. The carrier walks at
    /// <see cref="FlagRules.carrierSpeed"/> (<see cref="SpeedMultiplier"/>) and keeps all weapons.</item>
    /// <item>Touching your own stand with the enemy flag while your own flag is home captures: +1 for your team
    /// (<see cref="MatchState.ReportCapture"/>), and the captured flag goes home.</item>
    /// <item>A carrier's death (<see cref="MatchState.Died"/>, whatever killed them) drops the flag at <see cref="DropPoint"/>.</item>
    /// <item>A player of its own team touching a dropped flag sends it home at once; untouched it goes home after
    /// <see cref="FlagRules.returnSeconds"/>; dropped where <see cref="IsReachable"/> says nobody can get it, it goes home at once.</item>
    /// <item>Flags are taken, captured and returned by touch only while the round is <see cref="RoundPhase.Playing"/>; a new round
    /// (<see cref="MatchState.RoundReset"/>) sends both home.</item>
    /// </list>
    /// </summary>
    public sealed class CaptureTheFlagState
    {
        private readonly TeamFlag[] flags = new TeamFlag[Teams.Count];

        public CaptureTheFlagState(MatchState match, FlagRules rules = null)
        {
            Match = match ?? throw new ArgumentNullException(nameof(match));
            Rules = rules ?? new FlagRules();
            foreach (var team in Teams.All)
                flags[(int)team] = new TeamFlag(team);
            Match.Died += OnDied;
            Match.RoundReset += OnRoundReset;
        }

        public MatchState Match { get; }

        public FlagRules Rules { get; }

        public IReadOnlyList<TeamFlag> Flags => flags;

        /// <summary>
        /// Where a dead carrier's flag lands: on the ground below where they died. Null for no ground at all (fell off the island), which
        /// sends it home at once. Without it the flag lies at <see cref="Vector3.zero"/>. The scene sets it (a ray down from the body).
        /// </summary>
        public Func<PlayerState, Vector3?> DropPoint { get; set; }

        /// <summary>
        /// Whether a player can get to a flag lying at a point: not below the kill height, not outside the play area, and (ticket 23) on
        /// the bots' navmesh. A flag dropped where this says no goes home at once. Without it every point is reachable.
        /// </summary>
        public Func<Vector3, bool> IsReachable { get; set; }

        /// <summary>A flag was taken: the flag and the enemy player who has it now.</summary>
        public event Action<TeamFlag, PlayerState> Taken;

        /// <summary>A carrier died and the flag fell where they died: the flag and the player who had it.</summary>
        public event Action<TeamFlag, PlayerState> Dropped;

        /// <summary>A flag went home: why, and who touched it (null unless <see cref="FlagReturn.Touched"/> or captured).</summary>
        public event Action<TeamFlag, FlagReturn, PlayerState> Returned;

        /// <summary>
        /// A capture: the enemy flag that was brought home (it is back on its own stand now) and who brought it. The match has counted it
        /// already (<see cref="MatchState.Captured"/>).
        /// </summary>
        public event Action<TeamFlag, PlayerState> Captured;

        /// <summary>Any change of a flag's status, carrier or place, after the specific event: the world moves the flag object.</summary>
        public event Action<TeamFlag> Changed;

        public TeamFlag Flag(Team team) => flags[(int)team];

        /// <summary>Whether touches count now: the round is on.</summary>
        public bool InPlay => Match.Started && Match.Phase == RoundPhase.Playing;

        /// <summary>The flag <paramref name="player"/> carries, or null.</summary>
        public TeamFlag CarriedBy(PlayerState player)
        {
            if (player == null)
                return null;
            foreach (var flag in flags)
                if (flag.Status == FlagStatus.Carried && flag.Carrier == player)
                    return flag;
            return null;
        }

        public bool IsCarrying(PlayerState player) => CarriedBy(player) != null;

        /// <summary>The player's walking speed as a share of the normal one: <see cref="FlagRules.carrierSpeed"/> while carrying, else 1.</summary>
        public float SpeedMultiplier(PlayerState player) => IsCarrying(player) ? Rules.carrierSpeed : 1;

        /// <summary>
        /// <paramref name="player"/> is at <paramref name="stand"/>'s flag stand (the world calls it on entering and while standing
        /// there). The enemy stand with its flag home: they take it. Their own stand, carrying the enemy flag, with their own flag home:
        /// a capture. Returns whether anything happened.
        /// </summary>
        public bool TouchStand(PlayerState player, Team stand)
        {
            if (!CanAct(player))
                return false;
            if (stand != player.Team)
                return Flag(stand).AtHome && Take(Flag(stand), player);
            var enemy = CarriedBy(player);
            if (enemy == null || !Flag(player.Team).AtHome)
                return false;
            Capture(enemy, player);
            return true;
        }

        /// <summary>
        /// <paramref name="player"/> touched <paramref name="flag"/>'s flag where it lies (only a dropped flag counts; at home, the stand
        /// does). Their own team's: it goes home. The enemy's: they take it. Returns whether anything happened.
        /// </summary>
        public bool TouchFlag(PlayerState player, Team flag)
        {
            if (!CanAct(player))
                return false;
            var touched = Flag(flag);
            if (touched.Status != FlagStatus.Dropped)
                return false;
            if (flag == player.Team)
            {
                SendHome(touched, FlagReturn.Touched, player);
                return true;
            }
            return Take(touched, player);
        }

        /// <summary>
        /// The carrier lost the flag (they died): it falls at <paramref name="where"/>, or goes home at once when that is null or not
        /// reachable. Nothing when <paramref name="carrier"/> carries no flag.
        /// </summary>
        public void Drop(PlayerState carrier, Vector3? where)
        {
            var flag = CarriedBy(carrier);
            if (flag == null)
                return;
            flag.Status = FlagStatus.Dropped;
            flag.Carrier = null;
            flag.Position = where ?? Vector3.zero;
            flag.ReturnIn = Rules.returnSeconds;
            Dropped?.Invoke(flag, carrier);
            Changed?.Invoke(flag);
            if (where == null || !Reachable(where.Value))
                SendHome(flag, FlagReturn.Unreachable, null);
        }

        /// <summary>
        /// The world found out that a dropped flag cannot be reached after all (the navmesh came later, the ground moved): it goes home at
        /// once. Nothing for a flag that is not dropped.
        /// </summary>
        public void ReportUnreachable(Team team)
        {
            var flag = Flag(team);
            if (flag.Status == FlagStatus.Dropped)
                SendHome(flag, FlagReturn.Unreachable, null);
        }

        /// <summary>Lets <paramref name="seconds"/> pass: a dropped flag's wait, after which it goes home by itself.</summary>
        public void Tick(float seconds)
        {
            if (seconds <= 0)
                return;
            foreach (var flag in flags)
            {
                if (flag.Status != FlagStatus.Dropped)
                    continue;
                flag.ReturnIn = Math.Max(0, flag.ReturnIn - seconds);
                if (flag.ReturnIn <= 0)
                    SendHome(flag, FlagReturn.TimedOut, null);
            }
        }

        /// <summary>Both flags home, nobody carrying: the start of a round.</summary>
        public void Reset()
        {
            foreach (var flag in flags)
                if (!flag.AtHome)
                    SendHome(flag, FlagReturn.RoundReset, null);
        }

        /// <summary>Stops following the match (the scene's component goes away).</summary>
        public void Detach()
        {
            Match.Died -= OnDied;
            Match.RoundReset -= OnRoundReset;
        }

        private bool CanAct(PlayerState player) => player != null && player.Alive && InPlay;

        private bool Reachable(Vector3 point) => IsReachable == null || IsReachable(point);

        private bool Take(TeamFlag flag, PlayerState player)
        {
            // Your own flag is never yours to carry, and one flag per carrier (only one enemy flag exists anyway)
            if (flag.Team == player.Team || IsCarrying(player))
                return false;
            flag.Status = FlagStatus.Carried;
            flag.Carrier = player;
            flag.ReturnIn = 0;
            Taken?.Invoke(flag, player);
            Changed?.Invoke(flag);
            return true;
        }

        private void Capture(TeamFlag enemy, PlayerState player)
        {
            enemy.GoHome();
            // The score first, so what follows the capture sees it counted (and the round over, on the winning one)
            Match.ReportCapture(player.Team, player);
            Returned?.Invoke(enemy, FlagReturn.Captured, player);
            Captured?.Invoke(enemy, player);
            Changed?.Invoke(enemy);
        }

        private void SendHome(TeamFlag flag, FlagReturn reason, PlayerState by)
        {
            flag.GoHome();
            Returned?.Invoke(flag, reason, by);
            Changed?.Invoke(flag);
        }

        private void OnDied(Death death)
        {
            var victim = death.Victim;
            if (!IsCarrying(victim))
                return;
            Drop(victim, DropPoint != null ? DropPoint(victim) : Vector3.zero);
        }

        private void OnRoundReset() => Reset();
    }
}
