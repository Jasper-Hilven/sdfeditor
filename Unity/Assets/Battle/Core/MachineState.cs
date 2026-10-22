using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// One machine as the rules see it (ticket 24): its kind, team and name, its <see cref="Health"/> (ticket 22's, for things that are
    /// not players), whether it is alive, and the 30 s wait before it is back. Plain C#; the scene's <c>Machine</c> component is its body.
    /// A machine's death is never a <see cref="Death"/>: it is not reported to the match, so it is not in the kill feed and nobody gets
    /// kill credit for it. Time moves only through <see cref="Tick"/>.
    /// </summary>
    public sealed class MachineState
    {
        public MachineState(int index, MachineKind kind, Team team, MachineStats stats = null)
        {
            Index = index;
            Kind = kind;
            Team = team;
            Stats = stats ?? MachineStats.Of(kind);
            Name = MachineRules.NameOf(kind, team);
            Health = new Health(Stats.health);
        }

        /// <summary>Its place among the match's machines.</summary>
        public int Index { get; }

        public MachineKind Kind { get; }

        public Team Team { get; }

        public MachineStats Stats { get; }

        /// <summary>"Blue tank": how the kill feed names it as a killer.</summary>
        public string Name { get; }

        public Health Health { get; }

        public bool Alive { get; private set; } = true;

        /// <summary>Seconds until it is back; 0 while alive.</summary>
        public float RespawnIn { get; private set; }

        /// <summary>Turrets come back where they stand; helicopters and tanks in their side's machine zone.</summary>
        public bool RespawnsInPlace => Kind == MachineKind.Turret;

        /// <summary>It died: the damage that killed it (whoever dealt it gets nothing for it).</summary>
        public event Action<MachineState, Damage> Died;

        /// <summary>The wait is over: it is back at full health.</summary>
        public event Action<MachineState> Respawned;

        /// <summary>
        /// Takes a hit, if <paramref name="allowed"/> (<see cref="CombatState.CanHurt(Team, Team)"/>: not its own team, not in the
        /// countdown); at 0 it dies and waits <see cref="MachineStats.respawnSeconds"/>. Returns the health taken.
        /// </summary>
        public float Hurt(Damage damage, bool allowed = true)
        {
            if (!Alive || !allowed || damage.Amount <= 0)
                return 0;
            var taken = Health.Hurt(damage.Amount);
            if (!Health.Alive)
            {
                Alive = false;
                RespawnIn = Stats.respawnSeconds;
                Died?.Invoke(this, damage);
            }
            return taken;
        }

        /// <summary>Lets <paramref name="seconds"/> pass; returns whether it came back now.</summary>
        public bool Tick(float seconds)
        {
            if (Alive || seconds <= 0)
                return false;
            RespawnIn = Mathf.Max(0, RespawnIn - seconds);
            if (RespawnIn > 0)
                return false;
            Respawn();
            return true;
        }

        /// <summary>Back at full health at once (a new round), alive or not.</summary>
        public void Reset()
        {
            var wasDead = !Alive;
            Health.Reset(Stats.health);
            Alive = true;
            RespawnIn = 0;
            if (wasDead)
                Respawned?.Invoke(this);
        }

        /// <summary>
        /// Out of play with no way back of its own (no death, no event): <see cref="Alive"/> is false and it never respawns until
        /// <see cref="Reset"/> brings it back. The Prime-copter's tank drop keeps its tanks stowed like this (ticket 33.5).
        /// </summary>
        public void Stow()
        {
            Health.Hurt(Health.Current);
            Alive = false;
            RespawnIn = float.PositiveInfinity;
        }

        private void Respawn()
        {
            Health.Reset(Stats.health);
            Alive = true;
            RespawnIn = 0;
            Respawned?.Invoke(this);
        }

        public override string ToString() => $"{Name}{(Alive ? "" : ", dead")} {Health.Current}/{Health.Max}";
    }

    /// <summary>Something a machine might shoot at, as its sight sees it: a player or not, its team, alive and hurtable, where it is.</summary>
    public struct MachineTarget
    {
        /// <summary>The roster index of a player (or any id of something else).</summary>
        public int id;
        public Team team;
        public bool isPlayer;
        public bool alive;
        /// <summary>Not under spawn protection: a shot would hurt it.</summary>
        public bool hurtable;
        /// <summary>Where to aim: the chest.</summary>
        public Vector3 position;

        public static MachineTarget Player(int id, Team team, Vector3 position, bool alive = true, bool hurtable = true) =>
            new MachineTarget { id = id, team = team, isPlayer = true, alive = alive, hurtable = hurtable, position = position };
    }

    /// <summary>
    /// Whom a machine shoots (ticket 24): the nearest enemy player it can see within its range; never a machine, never its own team,
    /// never the dead or the spawn-protected. The line of sight is the scene's (a ray), asked nearest first and only until one is seen,
    /// so a machine casts one or two rays a look, not one per player. The target it has keeps a few metres' lead, so it does not flick
    /// between two at about the same distance.
    /// </summary>
    public static class MachineTargeting
    {
        /// <summary>Metres the current target counts as nearer than it is.</summary>
        public const float Stickiness = 3;

        private static readonly List<(float distance, int index)> Order = new List<(float, int)>();

        /// <summary>Whether a machine of <paramref name="team"/> may shoot at <paramref name="target"/> at all.</summary>
        public static bool IsEnemy(Team team, in MachineTarget target) =>
            target.isPlayer && target.alive && target.hurtable && target.team != team;

        /// <summary>
        /// The id of the target to shoot from <paramref name="eye"/>, or -1: the nearest enemy player within <paramref name="range"/>
        /// that <paramref name="lineOfSight"/> sees, <paramref name="current"/> (an id) favoured by <see cref="Stickiness"/>.
        /// </summary>
        public static int Choose(Vector3 eye, Team team, float range, IReadOnlyList<MachineTarget> candidates, Func<MachineTarget, bool> lineOfSight,
            int current = -1)
        {
            Order.Clear();
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!IsEnemy(team, candidate))
                    continue;
                var distance = Vector3.Distance(eye, candidate.position);
                if (distance > range)
                    continue;
                Order.Add((candidate.id == current ? distance - Stickiness : distance, i));
            }
            Order.Sort((a, b) => a.distance.CompareTo(b.distance));
            foreach (var (_, index) in Order)
                if (lineOfSight == null || lineOfSight(candidates[index]))
                    return candidates[index].id;
            return -1;
        }
    }

    /// <summary>
    /// A machine's gun (ticket 24): the bots' aim (<see cref="BotAim"/>, ticket 23: the reaction time on a new target, then an error that
    /// shrinks while it keeps tracking) and the fire rate, about one shot a second, each wait varied a little so the machines do not fire
    /// in step. It fires only with a target it has reacted to and its gun turned onto where it aims (the scene says when).
    /// </summary>
    public sealed class MachineGun
    {
        private readonly System.Random random;

        public MachineGun(MachineStats stats, BotSkill skill = null, System.Random random = null)
        {
            Stats = stats ?? new MachineStats();
            this.random = random ?? new System.Random();
            Aim = new BotAim(skill ?? new BotSkill(), this.random);
        }

        public MachineStats Stats { get; }

        public BotAim Aim { get; }

        /// <summary>The target's id; -1 for none.</summary>
        public int Target => Aim.Target;

        /// <summary>Seconds until it may fire again.</summary>
        public float Cooldown { get; private set; }

        /// <summary>The aim's error as degrees of yaw (x) and pitch (y, down) to add to the true aim.</summary>
        public Vector2 Offset => Aim.Offset;

        /// <summary>Starts on a target (an id), or keeps the one it has; -1 loses it.</summary>
        public void Acquire(int target) => Aim.Acquire(target);

        public void Lose() => Aim.Lose();

        /// <summary>Dead or a new round: no target, ready to fire at the next one after its reaction.</summary>
        public void Reset()
        {
            Aim.Lose();
            Cooldown = 0;
        }

        /// <summary>
        /// Lets <paramref name="dt"/> pass, the target crossing the view at <paramref name="angularSpeed"/> degrees a second; returns
        /// whether a shot leaves now: a target reacted to, the gun <paramref name="onTarget"/> and the wait since the last shot over.
        /// </summary>
        public bool Tick(float dt, bool onTarget, float angularSpeed = 0)
        {
            Cooldown = Mathf.Max(0, Cooldown - dt);
            if (Aim.Target < 0)
                return false;
            Aim.Tick(dt, angularSpeed);
            if (!Aim.Ready || !onTarget || Cooldown > 0)
                return false;
            Cooldown = Stats.shotInterval * (0.85f + 0.3f * (float)random.NextDouble());
            return true;
        }
    }
}
