namespace SdfBattle
{
    /// <summary>
    /// One participant of a match (ticket 20), you or a bot, as the rules see it: team, name, health, alive or dead, the respawn wait and
    /// the spawn protection, and the round's counts. Plain C#, owned and changed by <see cref="MatchState"/>; the scene's <c>Player</c>
    /// component is its body (the controller that moves it, the scout drawn for it). The health is only a field here: damage and healing
    /// are ticket 22's, which also asks <see cref="MatchState.CanHurt"/> first.
    /// </summary>
    public sealed class PlayerState
    {
        public PlayerState(int index, string name, Team team, bool isBot)
        {
            Index = index;
            Name = name;
            Team = team;
            IsBot = isBot;
        }

        /// <summary>The player's place in the match's roster, <see cref="MatchState.Players"/>.</summary>
        public int Index { get; }

        /// <summary>The name the HUD and the kill feed show: the cast's names (<see cref="Cast"/>), plain ones like "Red 2" past them.</summary>
        public string Name { get; set; }

        public Team Team { get; }

        /// <summary>A bot (ticket 23) rather than you; both move by the same rules.</summary>
        public bool IsBot { get; }

        public float Health { get; set; }

        public float MaxHealth { get; private set; }

        /// <summary>In play: spawned and not dead since. Nobody is alive before the match's first round.</summary>
        public bool Alive { get; private set; }

        /// <summary>Seconds left until a dead player is back at the team's spawn zone; 0 while alive.</summary>
        public float RespawnIn { get; private set; }

        /// <summary>Seconds of spawn protection left; 0 once it ran out or the player fired.</summary>
        public float ProtectionLeft { get; private set; }

        /// <summary>Alive and still under spawn protection: takes no damage.</summary>
        public bool Protected => Alive && ProtectionLeft > 0;

        /// <summary>
        /// Out of the match for now (<see cref="MatchState.SetBenched"/>, ticket 33.6: the boss fight's crew is as big as the phase says):
        /// not alive, and it does not respawn until it is back.
        /// </summary>
        public bool Benched { get; internal set; }

        /// <summary>Enemies this player killed this round.</summary>
        public int Kills { get; internal set; }

        /// <summary>Times this player died this round, by any cause.</summary>
        public int Deaths { get; internal set; }

        /// <summary>Flags this player captured this round (ticket 21 reports them).</summary>
        public int Captures { get; internal set; }

        /// <summary>Ends the spawn protection early: the player fired (ticket 22 reports it through <see cref="MatchState.ReportFired"/>).</summary>
        public void EndProtection() => ProtectionLeft = 0;

        public override string ToString() => $"{Name} ({Team.Name()}{(IsBot ? ", bot" : "")}{(Alive ? "" : ", dead")})";

        internal void Spawn(float health, float protection)
        {
            Alive = true;
            MaxHealth = health;
            Health = health;
            RespawnIn = 0;
            ProtectionLeft = protection;
        }

        internal void Die(float respawn)
        {
            Alive = false;
            Health = 0;
            ProtectionLeft = 0;
            RespawnIn = respawn;
            Deaths++;
        }

        /// <summary>Lets <paramref name="seconds"/> pass; returns whether a dead player's wait is over and they are to respawn now.</summary>
        internal bool Tick(float seconds)
        {
            if (Benched)
                return false;
            if (Alive)
            {
                ProtectionLeft = ProtectionLeft > seconds ? ProtectionLeft - seconds : 0;
                return false;
            }
            RespawnIn = RespawnIn > seconds ? RespawnIn - seconds : 0;
            return RespawnIn <= 0;
        }

        internal void ResetCounts()
        {
            Kills = 0;
            Deaths = 0;
            Captures = 0;
        }
    }
}
