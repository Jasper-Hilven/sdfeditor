namespace SdfBattle
{
    /// <summary>How a player died.</summary>
    public enum DeathCause
    {
        /// <summary>Fell off the island, or out of the play area below the kill height.</summary>
        Fell,
        /// <summary>Killed by a player or a machine (tickets 22, 24).</summary>
        Killed,
        /// <summary>Taken out of the match by the rules, not killed (the boss fight's crew leaves with the phase, ticket 33.6): no kill message, no gore.</summary>
        Removed,
    }

    /// <summary>
    /// A player's death, as the match reports it (<see cref="MatchState.Died"/>) to the kill feed (22), the flag (21 drops it) and the
    /// respawn. The killer is a player, or something that is not one: a machine (ticket 24) has a <see cref="KillerName"/> and a
    /// <see cref="KillerTeam"/> ("Blue tank", blue) but no <see cref="Killer"/>, so it gets the kill message and no kill credit.
    /// </summary>
    public readonly struct Death
    {
        public Death(PlayerState victim, DeathCause cause, PlayerState killer = null, string killerName = null, Team? killerTeam = null, string weapon = null,
            bool headshot = false, string weaponId = null, bool heat = false)
        {
            Victim = victim;
            Cause = cause;
            Killer = killer;
            KillerName = killerName ?? killer?.Name;
            KillerTeam = killerTeam ?? killer?.Team;
            Weapon = weapon;
            Headshot = headshot;
            WeaponId = weaponId ?? weapon;
            Heat = heat;
        }

        public PlayerState Victim { get; }
        public DeathCause Cause { get; }
        /// <summary>The player who killed; null for a fall or a machine.</summary>
        public PlayerState Killer { get; }
        /// <summary>The killer as the kill feed writes it: a player's name or a machine's; null for a fall.</summary>
        public string KillerName { get; }
        public Team? KillerTeam { get; }
        /// <summary>The weapon's id (pistol, sniper, cannon); null for a fall.</summary>
        public string Weapon { get; }
        /// <summary>The weapon's own id, which tells dual pistols from a pistol (<see cref="Weapon"/> is the model's); the death lines use it.</summary>
        public string WeaponId { get; }
        /// <summary>The killing shot hit the head (the sniper's, ticket 22): the kill feed shows a popped cap.</summary>
        public bool Headshot { get; }

        /// <summary>A hot patch of the level burnt the victim (ticket 28.10): the kill feed says so, by the hazard's name; no killer, weapon or credit.</summary>
        public bool Heat { get; }

        /// <summary>Whether a player of the other team gets the kill.</summary>
        public bool CountsAsKill => Killer != null && Killer != Victim && Killer.Team != Victim.Team;
    }
}
