using System;
using System.Collections.Generic;
using SdfLevel;
using SdfPickups;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// Capture the flag in a battle scene (ticket 21), on the Battle object next to the <see cref="Match"/>, which adds it at its start
    /// (<see cref="Attach"/>); nothing is placed by hand. The rules are <see cref="State"/> (<see cref="CaptureTheFlagState"/>, plain C#);
    /// this does in the world what they say and tells them what only the world sees:
    /// <list type="bullet">
    /// <item>A trigger on each side's flag stand and one on each flag that lives while the flag lies on the ground
    /// (<see cref="FlagTrigger"/>, players only), reported as <see cref="CaptureTheFlagState.TouchStand"/> and
    /// <see cref="CaptureTheFlagState.TouchFlag"/>.</item>
    /// <item>The flag object moves with its state: on its stand (<see cref="FlagStand.ReturnFlag"/>), on the carrier's back, high and
    /// leaning back (hidden while it is on yours: you see the HUD instead), or planted where its carrier died, on the ground below
    /// (<see cref="CaptureTheFlagState.DropPoint"/>).</item>
    /// <item>A flag dropped below the kill height or out of the play area is out of reach and goes home at once; ticket 23's navmesh
    /// test plugs in as <see cref="NavmeshCheck"/>.</item>
    /// <item>The carrier walks at <see cref="FlagRules.carrierSpeed"/>: their controller's speed multiplier is multiplied by it while they
    /// carry and divided again after, so other slows (ticket 22's blob) combine with it.</item>
    /// <item>A glow column of the team's colour over each flag (<see cref="FlagGlowColumn"/>), and the flag HUD
    /// (<see cref="FlagHud"/>: team score, flag status, markers, banners) on the match's GUI.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class CaptureTheFlag : MonoBehaviour
    {
        public FlagRules rules = new FlagRules();

        [Header("Carrying")]
        [Tooltip("Where the pole's foot sits on the carrier, from their feet: up and behind (-z)")]
        public Vector3 carryOffset = new Vector3(0, 0.55f, -0.34f);
        [Tooltip("How far the carried flag leans back, degrees")]
        public float carryLean = 12;
        [Tooltip("How far a dropped flag leans, degrees")]
        public float droppedLean = 14;

        [Header("Triggers")]
        [Tooltip("The stand's trigger: its radius (the stand's foot is 1.05 m) and height")]
        public float standRadius = 1.15f;
        public float standHeight = 2.5f;
        [Tooltip("A dropped flag's trigger: its radius and height")]
        public float flagRadius = 0.8f;
        public float flagHeight = 2.4f;

        [Header("Glow columns")]
        public bool glowColumns = true;
        public float columnHeight = 45;
        public float columnWidth = 0.9f;

        [Header("Reach")]
        [Tooltip("A dropped flag looks this far down for the ground below where its carrier died")]
        public float groundSearch = 150;

        private readonly FlagStand[] stands = new FlagStand[Teams.Count];
        private readonly FlagTrigger[] flagTriggers = new FlagTrigger[Teams.Count];
        private readonly FlagGlowColumn[] columns = new FlagGlowColumn[Teams.Count];
        private readonly HashSet<Player> slowed = new HashSet<Player>();
        private float killHeight = float.NegativeInfinity;

        /// <summary>Capture the flag in the active battle scene; null where there is none.</summary>
        public static CaptureTheFlag Current { get; private set; }

        public Match Match { get; private set; }

        public CaptureTheFlagState State { get; private set; }

        public FlagHud Hud { get; private set; }

        /// <summary>
        /// Ticket 23's test whether a point is on the bots' navmesh, so a flag dropped off it goes home at once; null (every point on the
        /// island counts) until then.
        /// </summary>
        public Func<Vector3, bool> NavmeshCheck { get; set; }

        /// <summary>The flag stand of a team: red's on the west side, blue's on the east.</summary>
        public FlagStand Stand(Team team) => stands[(int)team];

        /// <summary>The flag object of a team (on its stand, a carrier's back or the ground).</summary>
        public Transform FlagObject(Team team) => stands[(int)team] != null ? stands[(int)team].flag : null;

        /// <summary>
        /// Adds capture the flag to the match's object and starts it on the match's rules. Called by <see cref="Match"/> at its start,
        /// after the players and the HUD exist and before the first round begins (whose reset sends the flags home). Null when the scene
        /// has no flag stands.
        /// </summary>
        public static CaptureTheFlag Attach(Match match)
        {
            var ctf = match.GetComponent<CaptureTheFlag>();
            if (ctf == null)
                ctf = match.gameObject.AddComponent<CaptureTheFlag>();
            return ctf.Begin(match) ? ctf : null;
        }

        private bool Begin(Match match)
        {
            if (State != null)
                return true;
            Match = match;
            var arena = match.Arena;
            foreach (var team in Teams.All)
            {
                var side = arena != null ? arena.Side(BattleSide.SideOf(team)) : null;
                stands[(int)team] = side != null ? side.flagStand : null;
            }
            if (stands[0] == null || stands[1] == null || stands[0].flag == null || stands[1].flag == null)
            {
                Debug.LogWarning("CaptureTheFlag: the scene's battle sides have no flag stands with flags; no capture the flag " +
                                 "(rebuild the level scene: Tools > SDF Level > Rebuild)");
                enabled = false;
                return false;
            }
            if (arena.west.spawn != null && arena.east.spawn != null)
                killHeight = Mathf.Min(arena.west.spawn.position.y, arena.east.spawn.position.y) - match.killDepth;

            State = new CaptureTheFlagState(match.State, rules)
            {
                DropPoint = GroundBelow,
                IsReachable = Reachable,
            };
            foreach (var team in Teams.All)
            {
                var stand = stands[(int)team];
                stand.State = State.Flag(team);
                FlagTrigger.Create(this, stand.transform, team, true, standRadius, standHeight);
                var trigger = FlagTrigger.Create(this, stand.flag, team, false, flagRadius, flagHeight);
                // A moving trigger: kinematic, so the physics follows it when the flag is moved
                var body = trigger.gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                trigger.Live = false;
                flagTriggers[(int)team] = trigger;
                if (glowColumns)
                    columns[(int)team] = FlagGlowColumn.Create(stand.flag, BattleSide.ColorOf(stand.side), columnHeight, columnWidth);
            }
            State.Changed += Show;
            match.State.Spawned += OnSpawned;
            Current = this;
            if (match.Hud != null)
                Hud = FlagHud.Create(this, match.Hud);
            foreach (var flag in State.Flags)
                Show(flag);
            return true;
        }

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

        private void OnDestroy()
        {
            if (State == null)
                return;
            State.Changed -= Show;
            State.Detach();
            if (Match != null && Match.State != null)
                Match.State.Spawned -= OnSpawned;
        }

        private void Update()
        {
            if (State != null)
                State.Tick(Time.deltaTime);
        }

        /// <summary>A player is at a stand or a dropped flag (from its <see cref="FlagTrigger"/>).</summary>
        public void Touch(FlagTrigger trigger, Player player)
        {
            if (State == null || !enabled)
                return;
            if (trigger.stand)
                State.TouchStand(player.State, trigger.team);
            else
                State.TouchFlag(player.State, trigger.team);
        }

        /// <summary>Whether a player can get to a point: above the kill height, in the play area, and on the navmesh once there is one.</summary>
        public bool Reachable(Vector3 point)
        {
            if (point.y < killHeight)
                return false;
            var scene = IMMScene.Current;
            if (scene != null && !scene.InPlayArea(point))
                return false;
            return NavmeshCheck == null || NavmeshCheck(point);
        }

        /// <summary>The ground below where a player is (died): the first solid surface down that is not a player; null over the void.</summary>
        private Vector3? GroundBelow(PlayerState state)
        {
            var player = Match.PlayerOf(state);
            if (player == null)
                return null;
            var from = player.transform.position + Vector3.up * 0.5f;
            var hits = Physics.RaycastAll(from, Vector3.down, groundSearch, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var best = float.PositiveInfinity;
            Vector3? ground = null;
            foreach (var hit in hits)
            {
                if (hit.distance >= best || Player.Of(hit.collider) != null)
                    continue;
                best = hit.distance;
                ground = hit.point;
            }
            return ground;
        }

        /// <summary>Puts a team's flag object where its state says, with its trigger, glow column and the carrier's speed.</summary>
        private void Show(TeamFlag flag)
        {
            var stand = stands[(int)flag.Team];
            var flagObject = stand.flag;
            var trigger = flagTriggers[(int)flag.Team];
            var visible = true;
            switch (flag.Status)
            {
                case FlagStatus.Home:
                    stand.ReturnFlag();
                    break;
                case FlagStatus.Carried:
                {
                    var carrier = Match.PlayerOf(flag.Carrier);
                    if (carrier == null)
                        break;
                    flagObject.SetParent(carrier.transform, false);
                    flagObject.localPosition = carryOffset;
                    // The cloth flies along the flag's +x: turned to trail behind the carrier, leaning back
                    flagObject.localRotation = Quaternion.Euler(-carryLean, 0, 0) * Quaternion.Euler(0, 90, 0);
                    visible = !carrier.IsYou;
                    break;
                }
                case FlagStatus.Dropped:
                    flagObject.SetParent(transform, true);
                    flagObject.position = flag.Position;
                    flagObject.rotation = Quaternion.Euler(0, (flag.Position.x * 37 + flag.Position.z * 11) % 360, 0) * Quaternion.Euler(0, 0, droppedLean);
                    break;
            }
            if (trigger != null)
                trigger.Live = flag.Status == FlagStatus.Dropped;
            foreach (var renderer in flagObject.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = visible;
            UpdateSpeeds();
        }

        /// <summary>Slows whoever carries, and gives back the speed to whoever stopped carrying.</summary>
        private void UpdateSpeeds()
        {
            var factor = Mathf.Max(0.01f, rules.carrierSpeed);
            foreach (var player in Match.Players)
            {
                var carrying = State.IsCarrying(player.State);
                var isSlowed = slowed.Contains(player);
                if (carrying == isSlowed)
                    continue;
                if (carrying)
                {
                    player.Controller.speedMultiplier *= factor;
                    slowed.Add(player);
                }
                else
                {
                    player.Controller.speedMultiplier /= factor;
                    slowed.Remove(player);
                }
            }
        }

        // A (re)spawned player starts at full speed (Player.Appear), so whatever slow was on them is gone
        private void OnSpawned(PlayerState state)
        {
            var player = Match.PlayerOf(state);
            if (player != null)
                slowed.Remove(player);
        }
    }
}
