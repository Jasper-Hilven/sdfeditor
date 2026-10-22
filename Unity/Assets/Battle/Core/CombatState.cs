using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// A poison cloud or a blob's splat on the ground (ticket 22): where, how big, how long it is left, whose. A cloud hurts the
    /// enemies of its team inside it; a splat slows them.
    /// </summary>
    public sealed class Area
    {
        public Area(FireMode kind, Vector3 centre, float radius, float seconds, Damage perSecond, float slow = 0, float slowSeconds = 0,
            float afterPerSecond = 0, float afterSeconds = 0)
        {
            Kind = kind;
            Centre = centre;
            Radius = radius;
            Duration = seconds;
            TimeLeft = seconds;
            PerSecond = perSecond;
            Slow = slow;
            SlowSeconds = slowSeconds;
            AfterPerSecond = afterPerSecond;
            AfterSeconds = afterSeconds;
        }

        /// <summary><see cref="FireMode.Cloud"/> or <see cref="FireMode.Blob"/> (a splat).</summary>
        public FireMode Kind { get; }

        /// <summary>A cloud's middle; a splat's point on the ground.</summary>
        public Vector3 Centre { get; }

        public float Radius { get; }

        public float Duration { get; }

        public float TimeLeft { get; internal set; }

        /// <summary>A cloud's damage a second, and whose it is (a splat's amount is 0: it only slows).</summary>
        public Damage PerSecond { get; }

        /// <summary>The share of speed a splat takes from who stands in it.</summary>
        public float Slow { get; }

        /// <summary>
        /// How long the slow lasts after the last step in it (a cloud's: 10 s; a splat's 0: it wears off a step after stepping out).
        /// </summary>
        public float SlowSeconds { get; }

        /// <summary>A cloud's afterpoison: damage a second, for <see cref="AfterSeconds"/> after leaving it (0: none).</summary>
        public float AfterPerSecond { get; }

        public float AfterSeconds { get; }

        public Team Team => PerSecond.AttackerTeam;

        /// <summary>
        /// Whether a player standing with their feet at <paramref name="feet"/> is in it: for a cloud, their body (an upright capsule
        /// <see cref="CombatState.BodyHeight"/> tall) reaches into the ball; for a splat, they stand on it, within its radius and about
        /// its height.
        /// </summary>
        public bool Contains(Vector3 feet)
        {
            if (Kind == FireMode.Blob)
            {
                var flat = new Vector2(feet.x - Centre.x, feet.z - Centre.z);
                return flat.magnitude <= Radius && feet.y > Centre.y - 0.6f && feet.y < Centre.y + 0.8f;
            }
            const float r = CombatState.BodyRadius;
            var bottom = feet.y + r;
            var top = feet.y + CombatState.BodyHeight - r;
            var nearest = new Vector3(feet.x, Mathf.Clamp(Centre.y, bottom, top), feet.z);
            return Vector3.Distance(nearest, Centre) <= Radius + r;
        }
    }

    /// <summary>
    /// Combat's rules (ticket 22) in plain C#, beside the <see cref="MatchState"/> they report to: every player's
    /// <see cref="Inventory"/>, damage and healing of <see cref="PlayerState.Health"/>, the flamethrower's burn and the blob's slow,
    /// and the poison clouds and splats on the ground. The scene's <c>Combat</c> finds what shots hit and hands the damage here;
    /// time moves only through <see cref="Tick"/>, so EditMode tests play it through without a scene.
    /// <list type="bullet">
    /// <item>Damage only where <see cref="MatchState.CanHurt"/> allows (alive, not spawn-protected, not the countdown, friendly fire
    /// off); health never below 0; at 0 the death goes to <see cref="MatchState.ReportDeath"/> with the killer, the weapon and a
    /// headshot.</item>
    /// <item>Every (re)spawn starts with a single pistol; a death takes everything else.</item>
    /// <item>Health pods heal up to the most; a pod is not taken at full health, nor a weapon already held.</item>
    /// <item>Burns, clouds and splats act in steps of <see cref="TickSeconds"/>; clouds do not add up (one at a time hurts a player).</item>
    /// <item>The living heal slowly by themselves below <see cref="RegenCap"/>: <see cref="RegenAmount"/> every
    /// <see cref="RegenSeconds"/>, never past it.</item>
    /// </list>
    /// </summary>
    public sealed class CombatState
    {
        /// <summary>The step of damage over time: burns, clouds and afterpoison hurt, splats slow, every quarter second.</summary>
        public const float TickSeconds = 0.25f;

        /// <summary>A player's body, as the clouds see it: the capsule every player has (FirstPersonController).</summary>
        public const float BodyHeight = 1.75f, BodyRadius = 0.3f;

        /// <summary>
        /// The slow heal: a living player under <see cref="RegenCap"/> health gets <see cref="RegenAmount"/> back every
        /// <see cref="RegenSeconds"/>.
        /// </summary>
        public const float RegenAmount = 1, RegenSeconds = 2, RegenCap = 80;

        /// <summary>
        /// Damage over time after a source (the flamethrower's burn, the cloud's afterpoison), on the steps of
        /// <see cref="TickSeconds"/>: ticks left, and whether it was set since the last step, which then passes it by, so it never ticks
        /// while the player stays in the source and keeps setting it anew.
        /// </summary>
        private sealed class Lingering
        {
            public int Ticks;
            public bool Fresh;
            public Damage PerSecond;

            public void Set(Damage perSecond, float seconds)
            {
                PerSecond = perSecond;
                Ticks = Mathf.RoundToInt(seconds / TickSeconds);
                Fresh = true;
            }

            public void Clear()
            {
                Ticks = 0;
                Fresh = false;
            }
        }

        /// <summary>The slows, one per kind of source (the blob and its splat, the poison), so a short strong one ends on time.</summary>
        private const int SlowKinds = 2;

        private sealed class Combatant
        {
            public readonly Inventory Inventory = new Inventory();
            public readonly Lingering Burn = new Lingering();
            public readonly Lingering Poison = new Lingering();
            public readonly float[] SlowLeft = new float[SlowKinds];
            public readonly float[] SlowShare = new float[SlowKinds];
            /// <summary>Seconds towards the next point of the slow heal; only runs while alive and under the cap.</summary>
            public float RegenClock;

            public void Clear()
            {
                RegenClock = 0;
                Burn.Clear();
                Poison.Clear();
                System.Array.Clear(SlowLeft, 0, SlowKinds);
                System.Array.Clear(SlowShare, 0, SlowKinds);
            }
        }

        private readonly List<Combatant> combatants = new List<Combatant>();
        private readonly List<Area> areas = new List<Area>();
        private float areaClock;

        public CombatState(MatchState match, WeaponTable weapons)
        {
            Match = match ?? throw new ArgumentNullException(nameof(match));
            Weapons = weapons ?? WeaponTable.Default();
            match.Spawned += OnSpawned;
            match.Died += OnDied;
            match.RoundReset += OnRoundReset;
        }

        public MatchState Match { get; }

        public WeaponTable Weapons { get; }

        /// <summary>Where a player's feet are, for the clouds and splats; the scene sets it (tests too). Without it, nobody is in one.</summary>
        public Func<PlayerState, Vector3> PositionOf { get; set; }

        /// <summary>What a player's walking speed is multiplied by on top of the slows (ticket 25's Mycelium Rush); null: 1.</summary>
        public Func<PlayerState, float> SpeedBoostOf { get; set; }

        /// <summary>What the damage a player takes is multiplied by (ticket 25's Puffball Armour); null: 1.</summary>
        public Func<PlayerState, float> DamageTakenOf { get; set; }

        /// <summary>The clouds and splats on the ground now.</summary>
        public IReadOnlyList<Area> Areas => areas;

        /// <summary>A player was hurt: who, by what, and how much health it took (0 is never reported).</summary>
        public event Action<PlayerState, Damage, float> Damaged;

        /// <summary>A player was healed: who and how much.</summary>
        public event Action<PlayerState, float> Healed;

        /// <summary>A player's inventory changed (a pickup, a switch, a loss on death, a fresh one on a spawn).</summary>
        public event Action<PlayerState> InventoryChanged;

        public event Action<Area> AreaAdded;

        public event Action<Area> AreaEnded;

        public Inventory InventoryOf(PlayerState player) => Of(player).Inventory;

        /// <summary>The numbers of the weapon a player has in hand.</summary>
        public WeaponStats WeaponOf(PlayerState player) => Weapons.Get(InventoryOf(player).CurrentWeapon);

        // ---------------------------------------------------------------- Damage

        /// <summary>Whether something of team <paramref name="attacker"/> can hurt this player now (<see cref="MatchState.CanHurt"/>).</summary>
        public bool CanHurt(PlayerState victim, Team attacker) => Match.CanHurt(victim, attacker);

        /// <summary>
        /// Whether something of team <paramref name="attacker"/> can hurt a thing of team <paramref name="target"/> that is not a player
        /// (a machine, ticket 24): the round is on or over (not counting down), and friendly fire is off.
        /// </summary>
        public bool CanHurt(Team target, Team attacker) =>
            Match.Started && Match.Phase != RoundPhase.Countdown && (Match.Rules.friendlyFire || target != attacker);

        /// <summary>
        /// Hurts a player, if <see cref="CanHurt(PlayerState, Team)"/>: takes the amount (times <see cref="DamageTakenOf"/>) off their
        /// health (no further than 0) and at 0 reports their death with this damage's killer and weapon. Returns the health it took.
        /// </summary>
        public float Hurt(PlayerState victim, Damage damage)
        {
            if (damage.Amount <= 0 || !CanHurt(victim, damage.AttackerTeam))
                return 0;
            var amount = damage.Amount * (DamageTakenOf != null ? Mathf.Max(0, DamageTakenOf(victim)) : 1);
            var taken = Mathf.Min(victim.Health, amount);
            victim.Health -= taken;
            if (taken > 0)
                Damaged?.Invoke(victim, damage, taken);
            if (victim.Health <= 0)
            {
                victim.Health = 0;
                Match.ReportDeath(damage.ToDeath(victim));
            }
            return taken;
        }

        /// <summary>Whether a health pod would do this player any good: alive and below the most.</summary>
        public bool NeedsHealth(PlayerState player) => player != null && player.Alive && player.Health < Mathf.Max(player.MaxHealth, Weapons.overheal);

        /// <summary>
        /// Heals a living player with <paramref name="amount"/> points (a health pod: the table's <see cref="WeaponTable.healthPack"/>):
        /// one health a point up to their most (100), half a health a point above it, up to the table's <see cref="WeaponTable.overheal"/>.
        /// So 40 points take 80 to 110, and 100 to 120. Returns the health added.
        /// </summary>
        public float Heal(PlayerState player, float amount)
        {
            if (!NeedsHealth(player) || amount <= 0)
                return 0;
            var after = HealthAfter(player.Health, player.MaxHealth, Weapons.overheal, amount);
            var added = after - player.Health;
            player.Health = after;
            if (added > 0)
                Healed?.Invoke(player, added);
            return added;
        }

        /// <summary>
        /// Health after <paramref name="points"/> of healing from <paramref name="health"/>: one for one up to <paramref name="max"/>,
        /// half above it, never past <paramref name="limit"/> (nor lowering a health already past it).
        /// </summary>
        public static float HealthAfter(float health, float max, float limit, float points)
        {
            var below = Mathf.Clamp(max - health, 0, points);
            health += below;
            points -= below;
            if (points > 0 && health < limit)
                health = Mathf.Min(limit, health + points / 2);
            return health;
        }

        /// <summary>
        /// Sets a player burning for <paramref name="seconds"/> at <paramref name="perSecond"/>'s amount a second (the flamethrower's
        /// afterburn), starting over if already burning, its first tick a step from now; nothing for a player the source cannot hurt.
        /// </summary>
        public void Burn(PlayerState victim, Damage perSecond, float seconds)
        {
            if (seconds <= 0 || perSecond.Amount <= 0 || !CanHurt(victim, perSecond.AttackerTeam))
                return;
            // Held in the flame, the burn keeps starting over and its first tick never comes: it burns on after leaving it
            Of(victim).Burn.Set(perSecond, seconds);
        }

        public bool IsBurning(PlayerState player) => Of(player).Burn.Ticks > 0;

        /// <summary>
        /// Poisons a player for <paramref name="seconds"/> at <paramref name="perSecond"/>'s amount a second (the cloud's afterpoison), like
        /// <see cref="Burn"/>: starting over while they stay in the cloud, so it hurts after they leave it.
        /// </summary>
        public void Poison(PlayerState victim, Damage perSecond, float seconds)
        {
            if (seconds <= 0 || perSecond.Amount <= 0 || !CanHurt(victim, perSecond.AttackerTeam))
                return;
            Of(victim).Poison.Set(perSecond, seconds);
        }

        public bool IsPoisoned(PlayerState player) => Of(player).Poison.Ticks > 0;

        /// <summary>
        /// Takes <paramref name="share"/> of a player's speed for <paramref name="seconds"/>, if the attacker can hurt them: a blob hit or
        /// a splat (<paramref name="source"/> Blob), or the poison (Cloud). Each kind of source keeps its own slow; the strongest counts.
        /// </summary>
        public void Slow(PlayerState victim, Team attacker, float share, float seconds, FireMode source = FireMode.Blob)
        {
            if (share <= 0 || seconds <= 0 || !CanHurt(victim, attacker))
                return;
            var c = Of(victim);
            var k = source == FireMode.Cloud ? 1 : 0;
            c.SlowShare[k] = c.SlowLeft[k] > 0 ? Mathf.Max(c.SlowShare[k], share) : share;
            c.SlowLeft[k] = Mathf.Max(c.SlowLeft[k], seconds);
        }

        public bool IsSlowed(PlayerState player) => Of(player).SlowLeft[0] > 0 || Of(player).SlowLeft[1] > 0;

        /// <summary>What a player's walking speed is multiplied by: 1, or less while slowed (the strongest slow), times <see cref="SpeedBoostOf"/>.</summary>
        public float SpeedFactor(PlayerState player)
        {
            var c = Of(player);
            var share = 0f;
            for (var k = 0; k < SlowKinds; k++)
                if (c.SlowLeft[k] > 0)
                    share = Mathf.Max(share, c.SlowShare[k]);
            return (1 - Mathf.Clamp01(share)) * (SpeedBoostOf != null ? Mathf.Max(0, SpeedBoostOf(player)) : 1);
        }

        /// <summary>A poison cloud or a splat on the ground, from now for its seconds.</summary>
        public Area AddArea(Area area)
        {
            areas.Add(area);
            AreaAdded?.Invoke(area);
            return area;
        }

        // ---------------------------------------------------------------- Weapons

        /// <summary>Whether walking onto a pod with this weapon takes it: alive, and not already held.</summary>
        public bool CanTake(PlayerState player, string weapon) => player != null && player.Alive && InventoryOf(player).CanTake(weapon);

        /// <summary>Takes a pod's weapon (and takes it in hand); see <see cref="CanTake"/>.</summary>
        public bool Take(PlayerState player, string weapon) => CanTake(player, weapon) && InventoryOf(player).Take(weapon);

        /// <summary>Selects a slot of a player's; returns whether that slot is held.</summary>
        public bool Select(PlayerState player, WeaponSlot slot) => InventoryOf(player).Select(slot);

        /// <summary>The player fired: their spawn protection ends (<see cref="MatchState.ReportFired"/>).</summary>
        public void Fired(PlayerState player) => Match.ReportFired(player);

        // ---------------------------------------------------------------- Time

        /// <summary>Lets <paramref name="seconds"/> pass: burns, slows, the slow heal, clouds and splats.</summary>
        public void Tick(float seconds)
        {
            if (seconds <= 0)
                return;
            foreach (var player in Match.Players)
            {
                var c = Of(player);
                for (var k = 0; k < SlowKinds; k++)
                    if (c.SlowLeft[k] > 0)
                        c.SlowLeft[k] = Mathf.Max(0, c.SlowLeft[k] - seconds);
                Regenerate(player, c, seconds);
            }

            areaClock += seconds;
            while (areaClock >= TickSeconds)
            {
                areaClock -= TickSeconds;
                StepAreas();
                // After the clouds, which set the afterpoison of who is in one anew: it only ticks once they are out
                foreach (var player in Match.Players)
                {
                    var c = Of(player);
                    Linger(player, c.Burn);
                    Linger(player, c.Poison);
                }
            }
            for (var i = areas.Count - 1; i >= 0; i--)
            {
                var area = areas[i];
                area.TimeLeft -= seconds;
                if (area.TimeLeft > 0)
                    continue;
                areas.RemoveAt(i);
                AreaEnded?.Invoke(area);
            }
        }

        /// <summary>
        /// The slow heal: <see cref="RegenAmount"/> every <see cref="RegenSeconds"/> while alive and under <see cref="RegenCap"/>, up to
        /// it and no further. The clock waits at 0 otherwise, so the first point comes a full wait after dropping under the cap.
        /// </summary>
        private void Regenerate(PlayerState player, Combatant c, float seconds)
        {
            if (!player.Alive || player.Health >= RegenCap)
            {
                c.RegenClock = 0;
                return;
            }
            c.RegenClock += seconds;
            var added = 0f;
            while (c.RegenClock >= RegenSeconds && player.Health < RegenCap)
            {
                c.RegenClock -= RegenSeconds;
                var point = Mathf.Min(RegenAmount, RegenCap - player.Health);
                player.Health += point;
                added += point;
            }
            if (added > 0)
                Healed?.Invoke(player, added);
        }

        private void Linger(PlayerState player, Lingering lingering)
        {
            if (lingering.Fresh)
            {
                lingering.Fresh = false;
                return;
            }
            if (lingering.Ticks <= 0 || !player.Alive)
                return;
            lingering.Ticks--;
            Hurt(player, lingering.PerSecond.With(lingering.PerSecond.Amount * TickSeconds));
        }

        /// <summary>One step of the areas: each enemy in a cloud is hurt once (clouds do not add up), each in a splat slowed a step.</summary>
        private void StepAreas()
        {
            if (areas.Count == 0 || PositionOf == null)
                return;
            foreach (var player in Match.Players)
            {
                if (!player.Alive)
                    continue;
                var feet = PositionOf(player);
                var poisoned = false;
                foreach (var area in areas)
                {
                    if (area.TimeLeft <= 0 || !area.Contains(feet) || !CanHurt(player, area.Team))
                        continue;
                    if (area.Kind == FireMode.Cloud)
                    {
                        if (poisoned)
                            continue;
                        poisoned = true;
                        Hurt(player, area.PerSecond.With(area.PerSecond.Amount * TickSeconds, true, area.Centre));
                        if (!player.Alive)
                            break;
                        // Breathing it in: the afterpoison and the slow start over with every step in it, and last on after leaving
                        Poison(player, area.PerSecond.With(area.AfterPerSecond, true, area.Centre), area.AfterSeconds);
                        Slow(player, area.Team, area.Slow, area.SlowSeconds, FireMode.Cloud);
                    }
                    else
                    {
                        // Standing in it keeps the slow on; it wears off a step after stepping out
                        Slow(player, area.Team, area.Slow, Mathf.Max(area.SlowSeconds, TickSeconds * 1.5f));
                    }
                }
            }
        }

        // ---------------------------------------------------------------- The match's events

        private Combatant Of(PlayerState player)
        {
            while (combatants.Count <= player.Index)
            {
                var c = new Combatant();
                var index = combatants.Count;
                c.Inventory.Changed += () =>
                {
                    if (index < Match.Players.Count)
                        InventoryChanged?.Invoke(Match.Players[index]);
                };
                combatants.Add(c);
            }
            return combatants[player.Index];
        }

        private void OnSpawned(PlayerState player)
        {
            var c = Of(player);
            c.Clear();
            c.Inventory.Reset();
        }

        private void OnDied(Death death)
        {
            // Everything lost but the single pistol, and nothing burns or slows the dead
            var c = Of(death.Victim);
            c.Clear();
            c.Inventory.Reset();
        }

        private void OnRoundReset()
        {
            for (var i = areas.Count - 1; i >= 0; i--)
            {
                var area = areas[i];
                areas.RemoveAt(i);
                AreaEnded?.Invoke(area);
            }
            areaClock = 0;
        }
    }
}
