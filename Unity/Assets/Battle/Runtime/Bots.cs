using System.Collections.Generic;
using SdfPickups;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The bots of a battle scene (ticket 23), on the Battle object next to the <see cref="Match"/>, which adds it at its start
    /// (<see cref="Create"/>), after combat and before the first round; nothing is placed by hand. It gives every bot player its
    /// <see cref="Bot"/> brain with a <see cref="Temperament"/> of its own (each team a mix of styles, no team plan), sets the one
    /// <see cref="skill"/> they all aim with, and keeps what the brains share: the pods they may fetch, the players' velocities (to lead
    /// a lobbed shot), the line-of-sight test, and the events they react to (a flag taken, dropped, returned or captured; a bot hurt).
    /// It plugs the navmesh into capture the flag (<see cref="CaptureTheFlag.NavmeshCheck"/>) when the scene has one.
    /// <para>
    /// Frame cost: each bot steers and aims every frame (a few vector sums), looks for enemies five or six times a second (a ray per enemy
    /// in its view), finds its path on the navmesh every one to three seconds; no physics or navigation runs on other threads, so it is
    /// the same in WebGL 2.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class Bots : MonoBehaviour
    {
        [Tooltip("How good the bots are: 0 clumsy, 1 sharp (reaction time, aim error, turn speed, view)")]
        public BotSkill skill = new BotSkill();
        [Tooltip("Seeds the bots' temperaments and choices")]
        public int seed = 23;
        [Tooltip("Draw each bot's path and goal in the Scene view")]
        public bool showPaths = true;

        /// <summary>The skill a team's bots aim with: the match setup's (ticket 32.3), else the one <see cref="skill"/>.</summary>
        public BotSkill SkillOf(Team team) => teamSkill[(int)team] ?? skill;

        /// <summary>Gives a team's bots their own skill; null goes back to <see cref="skill"/>. Takes hold at once (RETRY EASIER).</summary>
        public void SetSkill(Team team, BotSkill value)
        {
            teamSkill[(int)team] = value;
            foreach (var bot in bots)
                if (bot.Player.Team == team)
                    bot.UseSkill(SkillOf(team));
        }

        /// <summary>A pod a bot may walk to: a weapon or a health pod.</summary>
        public struct Pod
        {
            public WeaponPickup weapon;
            public HealthPickup health;
            public Vector3 Position => weapon != null ? weapon.transform.position : health != null ? health.transform.position : Vector3.zero;
            // A pod switched off (the boss fight's health caps between their 30 seconds) is not there to fetch
            public bool Available => weapon != null ? weapon.Available : health != null && health.isActiveAndEnabled && health.Available;
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly RaycastHit[] TargetHits = new RaycastHit[16];

        private readonly BotSkill[] teamSkill = new BotSkill[Teams.Count];
        private readonly List<Bot> bots = new List<Bot>();
        private readonly List<Pod> pods = new List<Pod>();
        private Vector3[] lastPositions = new Vector3[0];
        private Vector3[] velocities = new Vector3[0];

        /// <summary>The bots of the active battle scene; null where there is none.</summary>
        public static Bots Current { get; private set; }

        public Match Match { get; private set; }

        public Combat Combat { get; private set; }

        /// <summary>Capture the flag, or null in a scene without flags (the bots then only hunt and fetch).</summary>
        public CaptureTheFlag Flags { get; private set; }

        public IReadOnlyList<Bot> All => bots;

        public IReadOnlyList<Pod> Pods => pods;

        /// <summary>
        /// Ticket 33.6: circles on the floor that Prime's attacks are about to hit; a bot standing in one steps out of it
        /// (<see cref="BotDanger"/>). The boss fills it every frame; empty outside the boss fight.
        /// </summary>
        public List<DangerZone> Dangers { get; } = new List<DangerZone>();

        /// <summary>
        /// Ticket 33.6: what the bots may shoot at on their way while no player is in sight, beside the enemy machines: the parts of Prime
        /// that can be hurt now, the Prime-copter's sacs, his hands. The boss fills it a few times a second; never a goal on its own.
        /// </summary>
        public List<IDamageable> BossTargets { get; } = new List<IDamageable>();

        /// <summary>Ticket 33.6: where a team's hunters go to fight when they have no one else to find (Prime, in phases 2 and 3); null for the roaming.</summary>
        public Vector3? HuntFocusOf(Team team) => huntFocus[(int)team];

        /// <summary>Metres from <see cref="HuntFocusOf"/> the hunters stand and shoot from.</summary>
        public float huntFocusRange = 18;

        public void SetHuntFocus(Team team, Vector3? focus) => huntFocus[(int)team] = focus;

        private readonly Vector3?[] huntFocus = new Vector3?[Teams.Count];

        /// <summary>Adds a pod the bots may fetch from (the boss fight's health pods, which are made after the bots).</summary>
        public void AddHealthPod(HealthPickup pod) => AddPod(null, pod);

        /// <summary>
        /// Whether nothing solid stands between an eye and a part of the boss: the first thing along the way, past the players, is that
        /// target's own collider. Triggers are seen through.
        /// </summary>
        public bool LineOfSightTo(Vector3 eye, IDamageable target)
        {
            var along = target.Center - eye;
            var distance = along.magnitude;
            if (distance < 0.01f)
                return true;
            var count = Physics.RaycastNonAlloc(eye, along / distance, TargetHits, distance + 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var nearest = float.PositiveInfinity;
            var found = false;
            for (var i = 0; i < count; i++)
            {
                var hit = TargetHits[i];
                if (hit.distance >= nearest || Player.Of(hit.collider) != null)
                    continue;
                nearest = hit.distance;
                found = hit.collider.GetComponentInParent<IDamageable>() == target;
            }
            return found || float.IsPositiveInfinity(nearest);
        }

        /// <summary>Whether the scene has the bots' navmesh; without it they walk straight at their goals.</summary>
        public bool HasNavmesh { get; private set; }

        /// <summary>
        /// Gives the match's bot players their brains. Called by <see cref="Match"/> at its start, after <see cref="Combat"/> and before
        /// the first round.
        /// </summary>
        public static Bots Create(Match match)
        {
            var bots = match.GetComponent<Bots>();
            if (bots == null)
                bots = match.gameObject.AddComponent<Bots>();
            bots.Attach(match);
            return bots;
        }

        private void Attach(Match match)
        {
            Match = match;
            if (match.Setup != null)
                foreach (var team in Teams.All)
                    teamSkill[(int)team] = new BotSkill(match.Setup.SkillOf(team));
            Combat = Combat.Current;
            Flags = CaptureTheFlag.Current;
            var arena = match.Arena;
            BotNavigation.Reset();
            BotNavigation.HomeProbe = arena.west.spawn.position;
            HasNavmesh = BotNavigation.HasNavmesh;
            if (HasNavmesh)
            {
                if (Flags != null)
                    Flags.NavmeshCheck = BotNavigation.Reachable;
            }
            else
            {
                Debug.LogWarning("Bots: the scene has no navmesh for the bots (agent type Scout); they walk straight at their goals. " +
                                 "Rebuild the level scene (Tools > SDF Level > Rebuild) with the AI Navigation package installed");
            }

            foreach (var side in new[] { arena.west, arena.east })
            {
                foreach (var pod in side.WeaponPods)
                    AddPod(pod, null);
                foreach (var pod in side.HealthPods)
                    AddPod(null, pod);
            }
            foreach (var pod in arena.neutralPods)
                AddPod(pod, null);

            var random = new System.Random(seed);
            var teamStart = new[] { random.Next(Temperament.Rotation.Length), random.Next(Temperament.Rotation.Length) };
            var place = new int[Teams.Count];
            foreach (var player in match.Players)
            {
                if (player.State == null || !player.State.IsBot)
                    continue;
                var team = (int)player.Team;
                var style = Temperament.StyleFor(place[team]++, teamStart[team]);
                var bot = player.gameObject.AddComponent<Bot>();
                bot.Bind(this, player, style, Temperament.Varied(style, random), seed * 31 + player.State.Index);
                bots.Add(bot);
            }
            lastPositions = new Vector3[match.Players.Count];
            velocities = new Vector3[match.Players.Count];

            match.State.Spawned += OnSpawned;
            if (Flags != null)
            {
                Flags.State.Taken += OnFlagTaken;
                Flags.State.Dropped += OnFlagChanged;
                Flags.State.Returned += OnFlagReturned;
                Flags.State.Captured += OnFlagChanged;
            }
            if (Combat != null)
                Combat.State.Damaged += OnDamaged;
            Current = this;
        }

        private void AddPod(WeaponPickup weapon, HealthPickup health)
        {
            if (weapon != null || health != null)
                pods.Add(new Pod { weapon = weapon, health = health });
        }

        private void OnEnable()
        {
            if (Match != null)
                Current = this;
        }

        private void OnDisable()
        {
            if (Current == this)
                Current = null;
        }

        private void OnDestroy()
        {
            if (Match != null && Match.State != null)
                Match.State.Spawned -= OnSpawned;
            if (Flags != null && Flags.State != null)
            {
                Flags.State.Taken -= OnFlagTaken;
                Flags.State.Dropped -= OnFlagChanged;
                Flags.State.Returned -= OnFlagReturned;
                Flags.State.Captured -= OnFlagChanged;
            }
            if (Combat != null && Combat.State != null)
                Combat.State.Damaged -= OnDamaged;
            if (Flags != null && Flags.NavmeshCheck == (System.Func<Vector3, bool>)BotNavigation.Reachable)
                Flags.NavmeshCheck = null;
        }

        private void Update()
        {
            // The players' velocities, smoothed, for leading lobbed shots and the aim's tracking lag
            var dt = Time.deltaTime;
            if (dt <= 0)
                return;
            var players = Match.Players;
            for (var i = 0; i < players.Count && i < velocities.Length; i++)
            {
                var position = players[i].transform.position;
                var velocity = (position - lastPositions[i]) / dt;
                // A teleport (a spawn) is no velocity
                if (velocity.sqrMagnitude > 30 * 30)
                    velocity = Vector3.zero;
                velocities[i] = Vector3.Lerp(velocities[i], velocity, Mathf.Clamp01(dt * 8));
                lastPositions[i] = position;
            }
        }

        /// <summary>A player's velocity, smoothed over a few frames.</summary>
        public Vector3 VelocityOf(Player player) =>
            player != null && player.State != null && player.State.Index < velocities.Length ? velocities[player.State.Index] : Vector3.zero;

        /// <summary>Whether nothing solid (but players) stands between an eye and a point; triggers are seen through.</summary>
        public bool LineOfSight(Vector3 eye, Vector3 point)
        {
            var along = point - eye;
            var distance = along.magnitude;
            if (distance < 0.01f)
                return true;
            var count = Physics.RaycastNonAlloc(eye, along / distance, Hits, Mathf.Max(0, distance - 0.2f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
                if (Player.Of(Hits[i].collider) == null)
                    return false;
            return true;
        }

        // ---------------------------------------------------------------- Events the bots react to

        private Bot BotOf(PlayerState state)
        {
            var player = Match.PlayerOf(state);
            return player != null ? player.GetComponent<Bot>() : null;
        }

        private void OnSpawned(PlayerState state) => BotOf(state)?.OnSpawned();

        // Its own flag taken: each of the team thinks again after its own moment of surprise
        private void OnFlagTaken(TeamFlag flag, PlayerState by)
        {
            foreach (var bot in bots)
                bot.Rethink(bot.Team == flag.Team ? 0.15f : 0.4f);
        }

        private void OnFlagChanged(TeamFlag flag, PlayerState by)
        {
            foreach (var bot in bots)
                bot.Rethink(0.4f);
        }

        private void OnFlagReturned(TeamFlag flag, FlagReturn reason, PlayerState by) => OnFlagChanged(flag, by);

        private void OnDamaged(PlayerState victim, Damage damage, float amount) => BotOf(victim)?.OnHurt(damage, amount);
    }
}
