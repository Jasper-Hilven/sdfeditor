using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// One hit (ticket 22): how much, with what, from whom and from where. The attacker is a player, or something that is not one: a
    /// machine (ticket 24) gives its <see cref="AttackerName"/> and <see cref="AttackerTeam"/> but no <see cref="Attacker"/>, so it gets
    /// the kill message and no kill credit. Friendly fire is decided by the team (<see cref="MatchState.CanHurt"/>).
    /// </summary>
    public readonly struct Damage
    {
        public Damage(float amount, string weapon, Team attackerTeam, PlayerState attacker = null, string attackerName = null, Vector3? from = null,
            bool headshot = false, bool overTime = false)
        {
            Amount = amount;
            Weapon = weapon;
            AttackerTeam = attackerTeam;
            Attacker = attacker;
            AttackerName = attackerName ?? attacker?.Name;
            From = from;
            Headshot = headshot;
            OverTime = overTime;
        }

        public float Amount { get; }

        /// <summary>The weapon's id (as the weapon table names it: dual-pistols for two pistols; a machine's own, like cannon).</summary>
        public string Weapon { get; }

        public Team AttackerTeam { get; }

        /// <summary>The player who dealt it; null for a machine.</summary>
        public PlayerState Attacker { get; }

        /// <summary>Who dealt it as the kill feed writes it: a player's name or a machine's.</summary>
        public string AttackerName { get; }

        /// <summary>Where it came from (the shooter's eye, the cloud's middle), for the HUD's damage direction; null when unknown.</summary>
        public Vector3? From { get; }

        public bool Headshot { get; }

        /// <summary>A burn's or a cloud's tick rather than a hit: no hit marker.</summary>
        public bool OverTime { get; }

        /// <summary>A player's hit.</summary>
        public static Damage By(PlayerState attacker, string weapon, float amount, Vector3? from = null, bool headshot = false) =>
            new Damage(amount, weapon, attacker.Team, attacker, null, from, headshot);

        /// <summary>A machine's hit (ticket 24): its name and team, no player behind it.</summary>
        public static Damage ByMachine(string machineName, Team team, string weapon, float amount, Vector3? from = null) =>
            new Damage(amount, weapon, team, null, machineName, from);

        /// <summary>The same source with another amount: a burn's or a cloud's tick.</summary>
        public Damage With(float amount, bool overTime = true, Vector3? from = null) =>
            new Damage(amount, Weapon, AttackerTeam, Attacker, AttackerName, from ?? From, false, overTime);

        /// <summary>The death this damage causes: the killer, the weapon (dual pistols are pistols in the kill feed), the headshot.</summary>
        public Death ToDeath(PlayerState victim) =>
            new Death(victim, DeathCause.Killed, Attacker, AttackerName, AttackerTeam, WeaponIds.ModelOf(Weapon), Headshot);
    }

    /// <summary>
    /// Hit points of something that is not a player (a player's are <see cref="PlayerState.Health"/>, which <see cref="CombatState"/>
    /// changes): a machine's (ticket 24: tank 150, helicopter 100, turret 250). Hurt and healed by amounts, never below 0 or above
    /// <see cref="Max"/>; whether a hit may hurt it at all is <see cref="CombatState.CanHurt(Team, Team)"/>.
    /// </summary>
    public sealed class Health
    {
        public Health(float max)
        {
            Max = Mathf.Max(1, max);
            Current = Max;
        }

        public float Max { get; private set; }

        public float Current { get; private set; }

        public bool Alive => Current > 0;

        public float Fraction => Current / Max;

        /// <summary>Takes <paramref name="amount"/> off; returns how much was taken (no more than was left).</summary>
        public float Hurt(float amount)
        {
            if (!Alive || amount <= 0)
                return 0;
            var taken = Mathf.Min(Current, amount);
            Current -= taken;
            return taken;
        }

        /// <summary>Adds <paramref name="amount"/> up to the most; nothing for the dead. Returns how much was added.</summary>
        public float Heal(float amount)
        {
            if (!Alive || amount <= 0)
                return 0;
            var added = Mathf.Min(Max - Current, amount);
            Current += added;
            return added;
        }

        /// <summary>Back to full, at a new most if given (a respawn).</summary>
        public void Reset(float? max = null)
        {
            if (max != null)
                Max = Mathf.Max(1, max.Value);
            Current = Max;
        }
    }

    /// <summary>
    /// The trigger of the weapon in hand: when it may fire, how many shots a frame holding it gives, the minigun's spin-up and the
    /// spread growing while firing. Plain C#, so the timing can be tested; the scene's <c>Arsenal</c> ticks it every frame.
    /// </summary>
    public sealed class WeaponTrigger
    {
        private float untilReady;
        private float spin;
        private float burst;

        public WeaponStats Stats { get; private set; }

        /// <summary>Seconds the trigger has been held and firing (falls back twice as fast when let go); the spread grows with it.</summary>
        public float FiringSeconds { get; private set; }

        /// <summary>How far the barrels have spun up, 0 to 1; 1 for a weapon without spin-up.</summary>
        public float SpinFraction => Stats == null || Stats.spinUp <= 0 ? 1 : spin / Stats.spinUp;

        /// <summary>The minigun fired its burst on end and is spinning down; it fires again once spun down and up anew.</summary>
        public bool Overheated { get; private set; }

        /// <summary>How much of the burst is used, 0 to 1; 0 for a weapon without a limit.</summary>
        public float BurstFraction => Stats == null || Stats.burstSeconds <= 0 ? 0 : Mathf.Clamp01(burst / Stats.burstSeconds);

        /// <summary>Seconds until the next shot can leave (drawing the weapon, or the time between shots).</summary>
        public float UntilReady => untilReady;

        /// <summary>Takes a weapon in hand: it fires after its draw time, its barrels still.</summary>
        public void Equip(WeaponStats stats)
        {
            Stats = stats;
            untilReady = stats?.drawSeconds ?? 0;
            spin = 0;
            burst = 0;
            Overheated = false;
            FiringSeconds = 0;
        }

        /// <summary>The spread of the next shot, degrees.</summary>
        public float Spread(bool zoomed) => Stats?.SpreadAfter(FiringSeconds, zoomed) ?? 0;

        /// <summary>
        /// Lets <paramref name="dt"/> pass with the trigger <paramref name="held"/> or not; returns the shots fired in it (for the cone,
        /// its damage ticks). The first shot leaves as soon as the trigger is held and the weapon is ready (and spun up).
        /// </summary>
        public int Tick(float dt, bool held)
        {
            if (Stats == null || dt <= 0)
                return 0;
            if (Stats.spinUp > 0)
            {
                // Spinning down (let go, or the burst used up) at the spin-down's pace; the burst is back once the barrels stand
                var down = Stats.spinUp / Mathf.Max(0.01f, Stats.spinDown > 0 ? Stats.spinDown : Stats.spinUp);
                spin = held && !Overheated ? Mathf.Min(Stats.spinUp, spin + dt) : Mathf.Max(0, spin - dt * down);
                if (spin <= 0)
                {
                    burst = 0;
                    Overheated = false;
                }
            }
            if (Overheated)
                held = false;
            if (!held || (Stats.spinUp > 0 && spin < Stats.spinUp))
            {
                FiringSeconds = Mathf.Max(0, FiringSeconds - dt * 2);
                untilReady = Mathf.Max(0, untilReady - dt);
                return 0;
            }
            FiringSeconds += dt;
            burst += dt;
            if (Stats.burstSeconds > 0 && burst >= Stats.burstSeconds)
            {
                // Used up: this frame's shots still leave, then it spins down
                Overheated = true;
            }
            untilReady -= dt;
            var shots = 0;
            while (untilReady <= 0 && shots < 64)
            {
                shots++;
                untilReady += Stats.Interval;
            }
            return shots;
        }
    }
}
