using UnityEngine;

namespace SdfBattle
{
    /// <summary>Where a team's flag is.</summary>
    public enum FlagStatus
    {
        /// <summary>On its stand.</summary>
        Home,
        /// <summary>On an enemy player's back.</summary>
        Carried,
        /// <summary>On the ground where its carrier died, going home after <see cref="FlagRules.returnSeconds"/>.</summary>
        Dropped,
    }

    /// <summary>Why a flag went home.</summary>
    public enum FlagReturn
    {
        /// <summary>A player of its own team touched it where it lay.</summary>
        Touched,
        /// <summary>It lay untouched for <see cref="FlagRules.returnSeconds"/>.</summary>
        TimedOut,
        /// <summary>It fell where nobody can get it: off the island, below the kill height, out of the play area, off the navmesh.</summary>
        Unreachable,
        /// <summary>The enemy brought it to their stand and scored.</summary>
        Captured,
        /// <summary>A new round began.</summary>
        RoundReset,
    }

    /// <summary>
    /// One team's flag as the rules see it (ticket 21): at home, carried (by whom) or dropped (where, and the seconds until it goes home by
    /// itself). Owned and changed by <see cref="CaptureTheFlagState"/>; the scene's <c>FlagStand</c> shows it, moving the flag object
    /// between the stand, the carrier's back and the ground.
    /// </summary>
    public sealed class TeamFlag
    {
        public TeamFlag(Team team) => Team = team;

        /// <summary>The team whose flag this is, and whose stand is its home.</summary>
        public Team Team { get; }

        public FlagStatus Status { get; internal set; } = FlagStatus.Home;

        /// <summary>The enemy player carrying it; null unless <see cref="FlagStatus.Carried"/>.</summary>
        public PlayerState Carrier { get; internal set; }

        /// <summary>Where it lies, the pole's foot on the ground; meaningful only while <see cref="FlagStatus.Dropped"/>.</summary>
        public Vector3 Position { get; internal set; }

        /// <summary>Seconds until a dropped flag goes home by itself; 0 unless <see cref="FlagStatus.Dropped"/>.</summary>
        public float ReturnIn { get; internal set; }

        public bool AtHome => Status == FlagStatus.Home;

        public override string ToString() => Status switch
        {
            FlagStatus.Carried => $"{Team.Name()} flag carried by {Carrier?.Name}",
            FlagStatus.Dropped => $"{Team.Name()} flag dropped, home in {ReturnIn:0.#} s",
            _ => $"{Team.Name()} flag at home",
        };

        internal void GoHome()
        {
            Status = FlagStatus.Home;
            Carrier = null;
            ReturnIn = 0;
        }
    }
}
