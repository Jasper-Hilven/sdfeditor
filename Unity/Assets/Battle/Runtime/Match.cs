using System.Collections.Generic;
using SdfLevel;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The match of a battle scene (ticket 20), on its Battle object next to the <see cref="BattleArena"/>; the level builder adds it
    /// with its prefab references, nothing else is placed by hand. At the start it makes the eight players, you (the scene's player,
    /// with the camera) and seven bots (new objects under Battle/Players, each its team's scout), and begins the rules,
    /// <see cref="State"/> (<see cref="MatchState"/>: phases, score, clock, respawns), which it ticks every frame. It does in the world
    /// what the rules ask: a spawned player appears at a free spot in its side's spawn zone facing the zone's way, a dead one vanishes
    /// (a killed one bursts first, <see cref="Gore"/>, and your camera pulls back to watch, <see cref="DeathCamera"/>), nobody moves
    /// during the countdown; and it reports what only the world sees: a player falling off the island.
    /// <para>
    /// The other rules find it through <see cref="Current"/> and talk to <see cref="State"/>: capture the flag (21) reports captures
    /// and resets its flags on <see cref="MatchState.RoundReset"/>, combat (22) reports deaths, bots (23) drive their
    /// <see cref="Player.Controller"/>, machines (24) kill through a <see cref="Death"/> with their own name. The HUD reads it through
    /// <see cref="MatchHud"/>.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class Match : MonoBehaviour
    {
        public const string PlayersName = "Players";

        [Tooltip("The match's numbers; no countdown at a round's start (the user's decision after ticket 24: everyone moves at once)")]
        public MatchRules rules = new MatchRules { countdownSeconds = 0 };
        [Tooltip("You: the scene's player, with the camera; the level builder sets it")]
        public FirstPersonController you;
        public Team yourTeam = Team.Red;
        [Tooltip("The red team's body: the amanita scout prefab")]
        public GameObject redScout;
        [Tooltip("The blue team's body: the chanterelle scout prefab")]
        public GameObject blueScout;
        [Tooltip("The GUI: the scene's instance of Assets/GUI/Prefabs/GUI.prefab (or the prefab itself, copied at the start), its HUD on this match; none for no HUD")]
        public GameObject gui;
        [Tooltip("A player this many metres below the lowest spawn has fallen off the island")]
        public float killDepth = 12;
        [Tooltip("Out of the play area, this many metres below the lowest spawn is already a fall")]
        public float outsideKillDepth = 3;
        [Tooltip("Seeds where in the zones players spawn")]
        public int seed = 20;
        [Tooltip("Watch eight bots play (ticket 23): you are no player, your camera follows a bot (BotSpectator). In the editor also " +
                 "Tools > SDF Battle > Spectate Bots")]
        public bool spectate;

        /// <summary>The editor preference Tools > SDF Battle > Spectate Bots sets (ticket 23).</summary>
        public const string SpectatePreference = "SdfBattle.SpectateBots";

        private readonly List<Player> players = new List<Player>();
        private System.Random random;
        private float killHeight;
        private float outsideKillHeight;

        /// <summary>The match of the active battle scene; null where there is none, or before it started.</summary>
        public static Match Current { get; private set; }

        public MatchState State { get; private set; }

        /// <summary>The campaign's setup this match plays by (<see cref="MatchSetup.Pending"/> at its start); null plays the scene's own numbers.</summary>
        public MatchSetup Setup { get; private set; }

        /// <summary>A single-round match ended (ticket 32.3): winner, score, seconds. Never raised without a setup that asks for one round.</summary>
        public event System.Action<MatchResult> Ended;

        /// <summary>The HUD on this match (the GUI's view of it); null when the scene has no GUI.</summary>
        public MatchHud Hud { get; private set; }

        public BattleArena Arena { get; private set; }

        /// <summary>The death bursts: the killed scouts' pieces and their blood.</summary>
        public Gore Gore { get; private set; }

        /// <summary>The players by roster index, as <see cref="MatchState.Players"/>; you first.</summary>
        public IReadOnlyList<Player> Players => players;

        public Player You => players.Count > 0 ? players[0] : null;

        /// <summary>A team's scout prefab: amanita red, chanterelle blue; null when the level builder set none.</summary>
        public GameObject ScoutOf(Team team) => team == Team.Red ? redScout : blueScout;

        /// <summary>The body of a player of the rules.</summary>
        public Player PlayerOf(PlayerState state) => state != null && state.Index < players.Count ? players[state.Index] : null;

        private void OnEnable()
        {
            if (State != null)
                Current = this;
        }

        private void OnDisable()
        {
            if (Current == this)
                Current = null;
        }

        private void Start()
        {
            Arena = GetComponent<BattleArena>();
            if (Arena == null)
                Arena = BattleArena.Current;
            if (Arena == null || Arena.west == null || Arena.east == null || Arena.west.spawn == null || Arena.east.spawn == null)
            {
                Debug.LogWarning("Match: the scene has no complete battle sides (Battle/West, Battle/East with spawns); rebuild the level scene (Tools > SDF Level > Rebuild)");
                enabled = false;
                return;
            }
            // The campaign's setup (ticket 32.3) overrides the numbers; the rules are copied so the scene's own stay as authored
            Setup = MatchSetup.Pending;
            if (Setup != null)
            {
                rules = JsonUtility.FromJson<MatchRules>(JsonUtility.ToJson(rules));
                Setup.Apply(rules);
            }
            random = new System.Random(seed);
            var lowest = Mathf.Min(Arena.west.spawn.position.y, Arena.east.spawn.position.y);
            killHeight = lowest - killDepth;
            outsideKillHeight = lowest - outsideKillDepth;

            State = new MatchState(rules);
            State.Spawned += OnSpawned;
            State.Died += OnDied;
            State.MatchEnded += result => Ended?.Invoke(result);
            MakePlayers();
            Current = this;
            if (gui != null)
                Hud = MatchHud.Create(this, gui);
            // Capture the flag (ticket 21): before the first round, whose reset sends the flags home
            CaptureTheFlag.Attach(this);
            // Combat (ticket 22): before the first round, whose spawns hand out the single pistols
            var combat = Combat.Create(this);
            // Machines (ticket 24): the sides' turrets, helicopters and tanks, after combat (they register as targets), before the bots
            Machines.Create(this);
            // Spores (ticket 25): after combat and the flags, whose events fill the meters, before the first round
            Spores.Create(this, combat);
            // The gore: after combat (its effects and the hits' directions), before the first round; and your death camera
            Gore = Gore.Create(this, combat);
            if (You != null)
                DeathCamera.Attach(You, this);
            // Bots (ticket 23): their brains, after combat (their arsenals) and before the first round (their spawns)
            Bots.Create(this);
            State.Begin();
        }

        /// <summary>
        /// RETRY (ticket 32): the match starts over in place, scores and clock at zero, flags home, everyone respawned. A new
        /// <paramref name="setup"/> changes what can change without new bodies: the captures, the clock, blue's respawn, the bots' skill;
        /// the team sizes need the scene loaded again.
        /// </summary>
        public void Retry(MatchSetup setup = null)
        {
            if (State == null)
                return;
            if (setup != null)
            {
                Setup = setup;
                var sizes = (rules.teamSize, rules.blueTeamSize);
                setup.Apply(rules);
                (rules.teamSize, rules.blueTeamSize) = sizes;
                if (Bots.Current != null)
                    foreach (var team in Teams.All)
                        Bots.Current.SetSkill(team, new BotSkill(setup.SkillOf(team)));
                if (Spores.Current != null)
                    Spores.Current.State.Enabled = setup.sporesOn;
            }
            State.Restart();
        }

        /// <summary>You first, then your team's bots (the cast's numbers 2 to 4), then the other team's (1 to 4), named by <see cref="Cast"/>.</summary>
        private void MakePlayers()
        {
            if (you == null)
                you = IMMScene.Current != null && IMMScene.Current.player != null ? IMMScene.Current.player.GetComponent<FirstPersonController>() : null;
#if UNITY_EDITOR
            spectate |= UnityEditor.EditorPrefs.GetBool(SpectatePreference, false);
#endif
            // Spectating (ticket 23): your camera watches, the match is eight bots
            var spectating = spectate && you != null;
            if (spectating)
            {
                BotSpectator.Attach(you.gameObject, this);
                you = null;
            }
            if (you != null)
            {
                var player = you.GetComponent<Player>();
                if (player == null)
                    player = you.gameObject.AddComponent<Player>();
                player.Bind(State.AddPlayer(Setup != null ? Setup.NameOf(yourTeam, 1) : Cast.You, yourTeam, false));
                players.Add(player);
            }
            else
            {
                if (!spectating)
                    Debug.LogWarning("Match: the scene has no player of yours (SceneInfo's player); the match plays with bots only");
            }

            var root = new GameObject(PlayersName).transform;
            root.SetParent(transform, false);
            foreach (var team in new[] { yourTeam, yourTeam.Other() })
            {
                var first = team == yourTeam && you != null ? 2 : 1;
                for (var number = first; number <= rules.SizeOf(team); number++)
                    players.Add(MakeBot(Setup != null ? Setup.NameOf(team, number) : Cast.Name(team, number), team, root));
            }
        }

        private Player MakeBot(string botName, Team team, Transform root)
        {
            var bot = new GameObject(botName);
            bot.transform.SetParent(root, false);
            FirstPersonController.Configure(bot.AddComponent<CharacterController>());
            var controller = bot.AddComponent<FirstPersonController>();
            controller.readsInput = false;
            var player = bot.AddComponent<Player>();
            player.Bind(State.AddPlayer(botName, team, true));
            var prefab = team == Team.Red ? redScout : blueScout;
            if (prefab != null)
                player.body = ScoutBody.Create(prefab, controller);
            return player;
        }

        private void Update()
        {
            State.Tick(Time.deltaTime);
            var frozen = !State.CanMove;
            foreach (var player in players)
            {
                player.Controller.frozen = frozen;
                if (player.State.Alive && HasFallen(player.transform.position))
                    State.ReportFall(player.State);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // A test key, as the walls keep everyone on the island: F8 drops you off it
            if (Input.GetKeyDown(KeyCode.F8))
                DropYouOffTheIsland();
#endif
        }

        /// <summary>Below the kill height, or out of the play area (past the walls, off the island's edge) and falling.</summary>
        private bool HasFallen(Vector3 position)
        {
            if (position.y < killHeight)
                return true;
            var scene = IMMScene.Current;
            return scene != null && !scene.InPlayArea(position) && position.y < outsideKillHeight;
        }

        /// <summary>Puts you past the island's edge, in the air: you fall and die (a test for the fall and the respawn).</summary>
        [ContextMenu("Test: Drop You Off The Island")]
        public void DropYouOffTheIsland()
        {
            var player = You;
            if (player == null || !player.State.Alive)
                return;
            var scene = IMMScene.Current;
            var position = player.transform.position;
            var x = scene != null ? scene.extentMax.x + 5 : position.x;
            player.Controller.Teleport(new Vector3(x, position.y + 2, position.z), player.Controller.Yaw);
        }

        private void OnSpawned(PlayerState state)
        {
            var player = PlayerOf(state);
            if (player == null)
                return;
            var side = Arena.Side(BattleSide.SideOf(state.Team));
            var centre = side.spawn.position;
            var occupied = new List<Vector2>();
            foreach (var other in players)
                if (other != player && other.State.Alive)
                    occupied.Add(new Vector2(other.transform.position.x, other.transform.position.z));
            var spot = SpawnPicker.Pick(new Vector2(centre.x, centre.z), side.spawnRadius, occupied, random);
            player.Appear(new Vector3(spot.x, GroundHeight(spot, centre.y), spot.y) + Vector3.up * 0.02f, side.spawn.eulerAngles.y);
        }

        /// <summary>The ground under a spot of a zone (level, checked by the pipeline), or the zone's own height when nothing is hit.</summary>
        private static float GroundHeight(Vector2 spot, float zoneHeight)
        {
            var from = new Vector3(spot.x, zoneHeight + 1.5f, spot.y);
            return Physics.Raycast(from, Vector3.down, out var hit, 4, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Player>() == null
                ? hit.point.y
                : zoneHeight;
        }

        /// <summary>A killed player bursts (with gore on) from where its body stood, then vanishes.</summary>
        private void OnDied(Death death)
        {
            var player = PlayerOf(death.Victim);
            if (player == null)
                return;
            if (Gore != null)
                Gore.Burst(death, player);
            player.Vanish();
        }
    }
}
