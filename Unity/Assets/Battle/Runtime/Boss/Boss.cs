using System;
using System.Collections.Generic;
using SdfEffects;
using SdfLevel;
using SdfPickups;
using SdfPrime;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The final boss fight in the world (ticket 33.6), on the Battle object next to the <see cref="Match"/>, which makes it at its start
    /// when the scene has a boss arena (Boss Spots, <see cref="BossArena.Present"/>). The rules are <see cref="BossState"/> (bars, shield,
    /// windows, lives, checkpoints) and its planner (<see cref="BossAttackPlanner"/>); this does in the world what they say:
    /// <list type="bullet">
    /// <item>Makes Prime from his prefab at 4.6 times (<see cref="PrimeBody"/>, hit shapes registered with <see cref="Combat.Register"/>), the
    /// Prime-copter (<see cref="PrimeCopter"/>), his walk (<see cref="PrimeWalker"/>), the spore shield, the arena's changes
    /// (<see cref="BossArena"/>) and the drawing (<see cref="BossTelegraphs"/>).</item>
    /// <item>Ticks the state, asks the planner ten times a second with who Prime sees, and hands each attack to <see cref="BossAttacks"/>;
    /// his damage goes through <see cref="Combat.Hurt"/> as <c>Damage.ByMachine("Chanterelle Prime", ...)</c>.</item>
    /// <item>Turns what hits his parts (<see cref="PrimeHit"/>) into the state's hits with the part's multiplier (<see cref="HurtPart"/>).</item>
    /// <item>Sets the blue crew's size and the bots' temperaments per phase, lets the bots see his parts and step out of markers.</item>
    /// <item>Changes phase: placeholder transitions (a timer, <see cref="transitionSeconds"/>; 33.8 replaces them with the beats), the
    /// arena's layout, flags (the crown is the blue flag in phase 3), lives, and the champion's minigun.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class Boss : MonoBehaviour
    {
        /// <summary>Who his damage is by, in the kill feed and on the death screen.</summary>
        public const string Name = "Chanterelle Prime";

        [Tooltip("Seconds a transition lasts when no story beat plays it (after a retry, or in a scene played alone)")]
        public float transitionSeconds = BossBeats.QuickTransitionSeconds;
        [Tooltip("Seconds between the planner's looks at the world")]
        public float planEvery = 0.1f;
        [Tooltip("Metres from the crown he picks it up")]
        public float crownReach = 3.5f;
        [Tooltip("Metres from his target he stops walking")]
        public float stopDistance = 8;

        private readonly List<BossTarget> targets = new List<BossTarget>();
        private readonly List<Player> redPlayers = new List<Player>();
        private float planAcc;
        private float botAcc;
        private float walkAcc;
        private float transitionClock;
        // A transition has begun and not been started yet (it starts on the next frame, not inside the hit that emptied the bar)
        private bool transitionPending;
        // A story beat plays the transition; the match is held for the whole transition, by this
        private bool beatPlaying, holding;
        // A retry has happened: the transitions after it do not tell their story again (ticket 33.7's RETRY PHASE, RESTART FIGHT)
        private bool retried;
        private int startPhase = 1;
        private bool restore;
        private bool restartRequested;
        private PrimeHit stemHit;
        private GameObject crownOnFlag;
        private float shakeLeft;
        private float shakeStrength;
        private bool crownHidden;

        /// <summary>
        /// Plays a story beat of the fight (ticket 33.8): its id (<see cref="BossBeats"/>), what to call with each cue its shots raise (and
        /// a way to ask where a beat's actor stands), and what to call when it is over, true when it was skipped. Returns false when it
        /// cannot play the beat. The campaign sets it (it owns the sequencer); with none the transitions are quick and silent and the
        /// victory has no beat.
        /// </summary>
        public delegate bool BeatPlayerHook(string beatId, Action<string, Func<string, Vector3?>> cue, Action<bool> done);

        public static BeatPlayerHook PlayBeat;

        public static Boss Current { get; private set; }

        /// <summary>The story beats' props: the flying crown, the shrink, the pixel blocks.</summary>
        public BossStaging Staging { get; private set; }

        public Match Match { get; private set; }

        public Combat Combat { get; private set; }

        public CaptureTheFlag Flags { get; private set; }

        public Machines Machines { get; private set; }

        public Bots Bots { get; private set; }

        public BossRules Rules { get; private set; }

        public BossState State { get; private set; }

        public BossAttackPlanner Planner => State.Planner;

        public BossArena Arena { get; private set; }

        public BossSpots Spots { get; private set; }

        public PrimeBody Body { get; private set; }

        public PrimeCopter Copter { get; private set; }

        public PrimeWalker Walker { get; private set; }

        public BossShield Shield { get; private set; }

        public BossAttacks Attacks { get; private set; }

        public BossTelegraphs Telegraphs { get; private set; }

        /// <summary>The boss HUD on the match's GUI (ticket 33.7); null when the GUI has no HUD.</summary>
        public BossHud Hud { get; private set; }

        public FxParticles Particles => Combat != null && Combat.Fx != null ? Combat.Fx.Particles : null;

        /// <summary>Prime was hit (also a blocked hit): the hit and where, for the HUD's marker and numbers (ticket 33.7).</summary>
        public event Action<BossHit, Vector3> PrimeHurt;

        /// <summary>The fight phase 1 to 3 the fight has got to (the last phase start), for the Defeat screen and its tip (ticket 33.7).</summary>
        public int PhaseReached => Mathf.Max(0, State.Checkpoint.Phase.BarOf()) + 1;

        /// <summary>What the player is to do now, in a line (the HUD's objective line, ticket 33.7).</summary>
        public string Objective
        {
            get
            {
                switch (State.Phase)
                {
                    case BossPhase.Phase1:
                        return State.Vulnerable ? "Shoot the gills!" : "Capture Prime's flag to drop the shield";
                    case BossPhase.Phase2:
                        return State.Copter == CopterStage.Flying ? $"Burst the spore sacs ({4 - State.Parts.SacsAlive} / 4)" : "Shoot the gills!";
                    case BossPhase.Phase3:
                        return State.CrownCaptured ? "Shoot the eyes!" : "Take the crown home";
                    default:
                        return "";
                }
            }
        }

        /// <summary>Makes the boss fight of a match; null when the scene has no arena or lacks what it needs.</summary>
        public static Boss Create(Match match)
        {
            var arena = BossArena.Attach();
            if (arena == null)
                return null;
            var boss = match.GetComponent<Boss>();
            if (boss == null)
                boss = match.gameObject.AddComponent<Boss>();
            if (boss.Begin(match, arena))
                return boss;
            Destroy(boss);
            return null;
        }

        // ---------------------------------------------------------------- Setting up

        private bool Begin(Match match, BossArena arena)
        {
            Match = match;
            Arena = arena;
            Spots = arena.Spots;
            Combat = Combat.Current;
            Flags = CaptureTheFlag.Current;
            Machines = Machines.Current;
            Bots = Bots.Current;
            if (Combat == null || Flags == null || Spots == null || Spots.prime == null || Spots.crash == null)
            {
                Debug.LogWarning("Boss: the scene lacks combat, capture the flag or the boss spots; no boss fight (rebuild the level scene)");
                return false;
            }
            var primePrefab = Spots.primePrefab;
            var copterPrefab = Spots.copterPrefab;
#if UNITY_EDITOR
            primePrefab = primePrefab != null ? primePrefab : UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prime/Prefabs/chanterelle-prime.prefab");
            copterPrefab = copterPrefab != null ? copterPrefab : PrimeCopter.LoadPrefab();
            Spots.healthPodPrefab = Spots.healthPodPrefab != null ? Spots.healthPodPrefab : UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Pickups/Pods/health-pod.prefab");
            Spots.goldMinigunPrefab = Spots.goldMinigunPrefab != null ? Spots.goldMinigunPrefab : UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mushrooms/minigun-gold.prefab");
            Spots.crownPrefab = Spots.crownPrefab != null ? Spots.crownPrefab : UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mushrooms/prime-crown.prefab");
#endif
            if (primePrefab == null)
            {
                Debug.LogWarning("Boss: no Chanterelle Prime prefab (Tools > SDF Prime > Rebuild Prime); no boss fight");
                return false;
            }

            var setup = match.Setup;
            Rules = BossRules.For((BossDifficulty)Mathf.Clamp(setup != null ? setup.bossDifficulty : (int)BossDifficulty.Normal, 0, 3), setup != null && setup.retryEasier);
            startPhase = Mathf.Clamp(setup != null ? setup.bossStartPhase : 1, 1, 3);
            // The match is the boss's, whatever the setup said: captures do not win, the clock does not run
            match.rules.objective = MatchObjective.Boss;
            var spores = Spores.Current;
            State = new BossState(Rules, match.State, Flags.State, spores != null ? spores.State : null);

            var instance = Instantiate(primePrefab, Spots.prime.position, Spots.prime.rotation, transform);
            instance.name = "Chanterelle Prime";
            instance.transform.localScale = Vector3.one * PrimeBody.BossScale;
            var rig = instance.GetComponent<PrimeRig>();
            if (rig == null)
            {
                Debug.LogWarning("Boss: the Prime prefab has no PrimeRig; rebuild it (Tools > SDF Prime > Rebuild Prime)");
                return false;
            }
            var rb = instance.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            Body = PrimeBody.Create(rig);
            Walker = instance.AddComponent<PrimeWalker>();
            BuildShapes(rig);

            Copter = PrimeCopter.Create(copterPrefab, transform, State, match, Machines, rig, Spots);
            Shield = BossShield.Create(this, Spots.shield != null ? Spots.shield.position : Spots.prime.position, Spots.shieldRadius);
            Attacks = new BossAttacks(this);
            Telegraphs = BossTelegraphs.Create(this);
            Staging = BossStaging.Create(this);
            Hud = BossHud.Create(this);
            PrimeHurt += (hit, at) =>
            {
                if (Hud != null)
                    Hud.ShowHit(hit, at);
            };

            Bots?.SetSkill(Team.Red, new BotSkill(Rules.redBotSkill));
            Bots?.SetSkill(Team.Blue, new BotSkill(Rules.crewSkill));
            Machines?.Stow(Team.Blue, MachineKind.Tank);

            State.PhaseChanged += OnPhaseChanged;
            State.ShieldChanged += OnShieldChanged;
            State.Stunned += OnStunned;
            State.StunEnded += OnStunEnded;
            State.Damaged += OnDamaged;
            State.SoreLoserStarted += ApplyCrew;
            State.CrownCapturedBy += OnCrownCaptured;
            State.Planner.Started += Attacks.Started;
            State.Planner.StageChanged += Attacks.StageChanged;
            State.Planner.Finished += Attacks.Finished;
            State.Planner.Cancelled += Attacks.Cancelled;
            State.Parts.HandBurst += OnHandBurst;
            State.Parts.HandRegrown += OnHandRegrown;
            State.Defeated += () => Arena.Cheers(1);
            if (Copter != null)
            {
                Copter.Crashed += OnCrashed;
                Copter.DiveSlammed += at => Shake(0.35f, at, 30);
                Copter.SacBurst += (i, at) => Arena.Cheers(0.5f);
            }
            match.State.Died += OnDied;
            match.State.Spawned += OnSpawned;
            match.State.RoundReset += OnRoundReset;
            Arena.ChampionGunTaken += OnChampionGunTaken;
            Current = this;
            return true;
        }

        private void OnDestroy()
        {
            if (Current == this)
                Current = null;
            if (Match != null && Match.State != null)
            {
                Match.State.Died -= OnDied;
                Match.State.Spawned -= OnSpawned;
                Match.State.RoundReset -= OnRoundReset;
            }
        }

        // The hit shapes: a capsule for the stem, a sphere for the cap, one under it for the gills, a sphere for each eye and each hand.
        // Colliders on the rig's own pieces, so they move with every pose; sizes in the prefab's metres (the rig is 4.6 times in the scene)
        private void BuildShapes(PrimeRig rig)
        {
            var top = rig.cap != null ? rig.cap.localPosition.y : 0.92f;
            stemHit = AddShape(rig.stem, "Hit Stem", PrimeHit.Part.Body, 0, shape =>
            {
                var capsule = shape.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0, top * 0.5f, 0);
                capsule.height = top + 0.1f;
                capsule.radius = 0.2f;
            });
            AddShape(rig.cap, "Hit Cap", PrimeHit.Part.Body, 0, shape => Sphere(shape, new Vector3(0, 0.12f, 0), 0.5f));
            AddShape(rig.cap, "Hit Gills", PrimeHit.Part.Gills, 0, shape => Sphere(shape, PrimeBody.GillsLocal, 0.24f));
            for (var i = 0; rig.eyes != null && i < rig.eyes.Length; i++)
                AddShape(rig.eyes[i], $"Hit Eye {i}", PrimeHit.Part.Eyes, i, shape => Sphere(shape, Vector3.zero, RadiusOf(rig.eyes[i], 0.12f)));
            for (var i = 0; rig.hands != null && i < rig.hands.Length; i++)
            {
                var hit = AddShape(rig.hands[i], $"Hit Hand {i}", PrimeHit.Part.Hand, i, shape => Sphere(shape, Vector3.zero, RadiusOf(rig.hands[i], 0.2f)));
                Combat.Register(hit);
            }
            Combat.Register(stemHit);
        }

        private PrimeHit AddShape(Transform parent, string shapeName, PrimeHit.Part part, int index, Action<GameObject> collider)
        {
            var shape = new GameObject(shapeName);
            shape.transform.SetParent(parent, false);
            collider(shape);
            var hit = shape.AddComponent<PrimeHit>();
            hit.Bind(this, part, index);
            return hit;
        }

        private static void Sphere(GameObject shape, Vector3 centre, float radius)
        {
            var sphere = shape.AddComponent<SphereCollider>();
            sphere.center = centre;
            sphere.radius = radius;
        }

        // The radius a piece's meshes span, in its own units
        private static float RadiusOf(Transform piece, float fallback)
        {
            var renderer = piece.GetComponentInChildren<Renderer>();
            if (renderer == null || piece.lossyScale.x < 1e-4f)
                return fallback;
            var extents = renderer.bounds.extents;
            return Mathf.Clamp(Mathf.Max(extents.x, extents.y, extents.z) / piece.lossyScale.x, 0.05f, 0.5f);
        }

        // ---------------------------------------------------------------- What hits him

        /// <summary>Whether a part of him can be hit now (the shots' and the flame's question).</summary>
        public bool PartAlive(PrimeHit.Part part, int index)
        {
            if (State == null)
                return false;
            switch (part)
            {
                case PrimeHit.Part.Shield:
                    return Shield != null && Shield.Up;
                case PrimeHit.Part.Eyes:
                    return State.Phase == BossPhase.Phase3;
                case PrimeHit.Part.Hand:
                    return State.Phase == BossPhase.Phase3 && State.Parts.HandAlive(index);
                default:
                    return State.Phase.IsFight();
            }
        }

        /// <summary>A hit on one of his parts: the state's, with the part's multiplier; the shield only shows a ripple. Returns the health the bar lost.</summary>
        public float HurtPart(PrimeHit.Part part, int index, Damage damage, Vector3 point)
        {
            if (State == null || damage.AttackerTeam == Team.Blue)
                return 0;
            switch (part)
            {
                case PrimeHit.Part.Shield:
                    Shield?.Hit(point);
                    // The HUD's dull "BLOCKED" for your own shots
                    if (damage.Attacker != null && !damage.Attacker.IsBot)
                        PrimeHurt?.Invoke(new BossHit(damage.Amount, 0, true, false, false), point);
                    return 0;
                case PrimeHit.Part.Hand:
                    return State.HurtHand(index, damage.Amount, damage.Attacker);
                default:
                    var kind = part == PrimeHit.Part.Gills ? BossPart.Gills : part == PrimeHit.Part.Eyes ? BossPart.Eyes : BossPart.Body;
                    // 42's golden minigun is the scene's only minigun: its bullets do half as much again to him
                    var champion = damage.Weapon == WeaponIds.Minigun && damage.Attacker != null && !damage.Attacker.IsBot;
                    var hit = State.Hurt(damage.Amount, kind, damage.Attacker, champion);
                    PrimeHurt?.Invoke(hit, point);
                    return hit.Dealt;
            }
        }

        // ---------------------------------------------------------------- Damage to players

        /// <summary>Everyone alive who is not on his side: the players his attacks hurt.</summary>
        public List<Player> RedPlayers()
        {
            redPlayers.Clear();
            foreach (var player in Match.Players)
                if (player.State != null && player.State.Alive && player.Team != Team.Blue)
                    redPlayers.Add(player);
            return redPlayers;
        }

        public float Hurt(Player victim, float amount, Vector3 from) =>
            Combat.Hurt(victim, Damage.ByMachine(Name, Team.Blue, "cannon", amount, from));

        /// <summary>A tick of a lingering damage (the glare): no hit marker.</summary>
        public float HurtOverTime(Player victim, float amount, Vector3 from) =>
            Combat.Hurt(victim, new Damage(amount, "cannon", Team.Blue, null, Name, from, false, true));

        /// <summary>Shakes your view for a moment when you are within <paramref name="radius"/> metres of <paramref name="at"/>.</summary>
        public void Shake(float strength, Vector3 at, float radius)
        {
            var you = Match.You;
            if (you == null || Vector3.Distance(you.transform.position, at) > radius)
                return;
            shakeStrength = Mathf.Max(shakeStrength, strength);
            shakeLeft = Mathf.Max(shakeLeft, 1f);
        }

        // ---------------------------------------------------------------- Every frame

        private void Update()
        {
            if (State == null)
                return;
            var dt = Time.deltaTime;
            // The match is held through a transition (nobody can be hurt, the crew and turrets hold fire), by this: the fight goes on ticking
            var playing = (!Match.Held || holding) && Match.State.Phase == RoundPhase.Playing;
            if (playing && transitionPending)
                BeginTransition();
            if (playing && (State.Phase == BossPhase.Intro || restartRequested))
            {
                restartRequested = false;
                if (restore)
                    State.RestoreCheckpoint();
                else
                    State.Begin(startPhase);
                restore = false;
            }
            if (!playing || dt <= 0)
                return;

            State.Tick(dt);
            planAcc += dt;
            if (planAcc >= planEvery)
            {
                PlanStep(planAcc);
                planAcc = 0;
            }
            Transitions(dt);
            Attacks.Update(dt);
            UpdateBody(dt);
            UpdateCrown();
            botAcc += dt;
            if (botAcc >= 0.2f)
            {
                botAcc = 0;
                BotSupport();
            }
            UpdateDangers();
        }

        private void LateUpdate()
        {
            // Your view shakes, on top of the pitch the controller keeps
            var you = Match != null ? Match.You : null;
            if (you == null || shakeStrength <= 0)
                return;
            var camera = you.GetComponentInChildren<Camera>();
            if (camera == null)
                return;
            shakeLeft = Mathf.Max(0, shakeLeft - Time.deltaTime);
            var basis = Quaternion.Euler(you.Controller.Pitch, 0, 0);
            if (shakeLeft <= 0)
            {
                camera.transform.localRotation = basis;
                shakeStrength = 0;
                return;
            }
            var amount = shakeStrength * shakeLeft * 2.5f;
            camera.transform.localRotation = basis * Quaternion.Euler(UnityEngine.Random.Range(-amount, amount), UnityEngine.Random.Range(-amount, amount), UnityEngine.Random.Range(-amount, amount) * 0.5f);
        }

        // ---------------------------------------------------------------- Asking the planner

        private void PlanStep(float seconds)
        {
            targets.Clear();
            var phase = State.Phase;
            var copterUp = Copter != null && Copter.Active;
            var origin = copterUp ? Copter.Position : Body.transform.position;
            var eyes = copterUp && Copter.Rig != null ? Copter.MuzzlePosition : Body.EyesCentre;
            foreach (var player in RedPlayers())
            {
                var flat = player.transform.position - origin;
                flat.y = 0;
                var target = new BossTarget
                {
                    Player = player.State,
                    Distance = flat.magnitude,
                    InSight = BossAttacks.InSight(eyes, player),
                    Hidden = phase == BossPhase.Phase1 && UnderBalcony(player),
                    Carrier = Flags.State.IsCarrying(player.State),
                    RecentDamage = State.RecentDamageOf(player.State),
                };
                targets.Add(target);
            }
            State.Plan(seconds, targets, Machines != null ? Machines.TanksAlive(Team.Blue) : 0);
        }

        // Phase 1: the balcony hangs over the blue flag stand and hides whoever stands under it from Prime's eyes (the turrets still see them)
        private static bool UnderBalcony(Player player)
        {
            var head = player.transform.position + Vector3.up * 1.7f;
            if (!Physics.Raycast(head, Vector3.up, out var hit, 8, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            var group = hit.collider.GetComponentInParent<PhaseGroup>();
            return group != null && group.first == 1 && group.last == 1;
        }

        // ---------------------------------------------------------------- Body, walk, crown

        private void UpdateBody(float dt)
        {
            Body.HeldByCopter = Copter != null && Copter.Active;
            var phase = State.Phase;
            var walking = false;
            if (phase == BossPhase.Phase3)
                walking = UpdateWalk(dt);
            Body.SetWalking(walking);
            if (Attacks.Current != null)
                return;
            var act = PrimeAct.Idle;
            if (phase == BossPhase.Victory)
                act = PrimeAct.Beaten;
            else if (State.StunKind == BossStunKind.Kneel)
                act = PrimeAct.Kneel;
            else if (State.StunKind == BossStunKind.StuckCap)
                act = PrimeAct.Stuck;
            else if (State.StunKind == BossStunKind.HandStun)
                act = PrimeAct.Dizzy;
            Body.SetAct(act);
            Body.ShutEyes(phase == BossPhase.Victory);
            var near = NearestRed();
            Body.LookAt(near != null ? near.transform.position + Vector3.up * 1.2f : (Vector3?)null);
        }

        private Player NearestRed()
        {
            Player best = null;
            var bestDistance = float.PositiveInfinity;
            foreach (var player in RedPlayers())
            {
                var distance = (player.transform.position - Body.transform.position).sqrMagnitude;
                if (distance < bestDistance)
                    (best, bestDistance) = (player, distance);
            }
            return best;
        }

        // Phase 3: he walks to the crown if it lies dropped, else to the target he chose (the carrier first), and stands to attack or when stunned
        private bool UpdateWalk(float dt)
        {
            var busy = Attacks.Current != null || State.Vulnerable;
            walkAcc += dt;
            if (busy)
            {
                Walker.Stop();
                FaceTarget(dt);
                return false;
            }
            if (walkAcc >= 0.5f || Walker.Goal == null)
            {
                walkAcc = 0;
                Vector3? goal = null;
                var crown = Flags.State.Flag(Team.Blue);
                if (crown.Status == FlagStatus.Dropped)
                {
                    goal = crown.Position;
                }
                else
                {
                    var pick = Targeting.Choose(targets, BossPhase.Phase3);
                    if (pick.HasValue)
                    {
                        var player = Match.PlayerOf(pick.Value.Player);
                        if (player != null && pick.Value.Distance > stopDistance)
                            goal = player.transform.position;
                    }
                }
                Walker.GoTo(goal);
            }
            return Walker.Moving;
        }

        // He turns to his target while he attacks on his feet
        private void FaceTarget(float dt)
        {
            if (State.Phase != BossPhase.Phase3 || State.StunKind == BossStunKind.StuckCap)
                return;
            var attack = Attacks.Current;
            var player = attack != null && attack.Target != null ? Match.PlayerOf(attack.Target) : null;
            if (player == null || attack.Stage != BossAttackStage.WindUp)
                return;
            var way = player.transform.position - Body.transform.position;
            way.y = 0;
            if (way.sqrMagnitude < 0.01f)
                return;
            var yaw = Mathf.MoveTowardsAngle(Body.transform.eulerAngles.y, Mathf.Atan2(way.x, way.z) * Mathf.Rad2Deg, Walker.turnSpeed * dt);
            Body.transform.rotation = Quaternion.Euler(0, yaw, 0);
        }

        // Prime touching the dropped crown puts it back on his head (a flag's own team returning it): Royal Immunity again
        private void UpdateCrown()
        {
            if (State.Phase != BossPhase.Phase3 || State.CrownCaptured)
                return;
            var crown = Flags.State.Flag(Team.Blue);
            if (crown.Status != FlagStatus.Dropped)
                return;
            var flat = crown.Position - Body.transform.position;
            flat.y = 0;
            if (flat.magnitude <= crownReach)
                State.PrimeTouchesCrown();
        }

        // ---------------------------------------------------------------- The bots

        private void BotSupport()
        {
            if (Bots == null)
                return;
            var list = Bots.BossTargets;
            list.Clear();
            var phase = State.Phase;
            if (phase == BossPhase.Phase1 && State.Vulnerable)
                AddGills(list);
            else if (phase == BossPhase.Phase2 && Copter != null && Copter.Active)
            {
                if (State.Copter == CopterStage.Flying)
                {
                    foreach (var sac in Copter.SacShapes)
                        if (sac != null && sac.Alive)
                            list.Add(sac);
                }
                else
                {
                    AddGills(list);
                }
            }
            else if (phase == BossPhase.Phase3)
            {
                if (State.Vulnerable)
                    AddGills(list);
                else if (stemHit != null)
                    list.Add(stemHit);
                foreach (var hit in Body.GetComponentsInChildren<PrimeHit>())
                    if (hit.part == PrimeHit.Part.Hand && hit.Alive)
                        list.Add(hit);
            }
            // Where the hunters gather to shoot at him
            Vector3? focus = null;
            if (phase == BossPhase.Phase2 && Copter != null && Copter.Active)
                focus = new Vector3(Copter.Position.x, Spots.crash.position.y, Copter.Position.z);
            else if (phase == BossPhase.Phase3)
                focus = Body.transform.position;
            Bots.SetHuntFocus(Team.Red, focus);
            Bots.huntFocusRange = phase == BossPhase.Phase3 ? 24 : 20;
        }

        private void AddGills(List<IDamageable> list)
        {
            foreach (var hit in Body.GetComponentsInChildren<PrimeHit>())
                if (hit.part == PrimeHit.Part.Gills)
                    list.Add(hit);
        }

        private void UpdateDangers()
        {
            if (Bots == null)
                return;
            var dangers = Bots.Dangers;
            dangers.Clear();
            foreach (var marker in Attacks.Markers)
                if (marker.danger)
                    dangers.Add(new DangerZone(new Vector2(marker.centre.x, marker.centre.z), marker.radius));
            if (Attacks.ShockRadius >= 0)
                dangers.Add(new DangerZone(new Vector2(Attacks.ShockCentre.x, Attacks.ShockCentre.z), Mathf.Min(Attacks.ShockRadius + 1, 20)));
        }

        // The crew is as big as the phase says: the rest of the blue players are benched; the temperaments follow the phase
        private void ApplyCrew()
        {
            var phase = State.Phase;
            if (!phase.IsFight())
                return;
            var want = Rules.CrewCount(phase, State.SoreLoser);
            var place = 0;
            foreach (var blue in Match.State.PlayersOf(Team.Blue))
            {
                Match.State.SetBenched(blue, place >= want);
                place++;
            }
            Match.rules.blueRespawnSeconds = Rules.CrewRespawn(phase);
            ApplyTemperaments();
        }

        private void ApplyTemperaments()
        {
            if (Bots == null)
                return;
            var places = new int[Teams.Count];
            foreach (var bot in Bots.All)
            {
                var team = bot.Team;
                var place = places[(int)team]++;
                var phase = State.Phase;
                var t = BossTemperaments.Of(team, phase, place);
                // Once the crown is home on the red stand nobody raids for it
                if (phase == BossPhase.Phase3 && State.CrownCaptured && team == Team.Red)
                    t = BossTemperaments.Red(BossPhase.Phase2, place);
                bot.temperament = t;
                bot.Rethink(0.2f);
            }
        }

        // ---------------------------------------------------------------- Phases

        private void OnPhaseChanged(BossPhase phase)
        {
            transitionClock = 0;
            transitionPending = BossBeats.ForTransition(phase) != null;
            planAcc = 0;
            Attacks.Update(0);
            var blueStand = Flags.Stand(Team.Blue);
            switch (phase)
            {
                case BossPhase.Phase1:
                    // After a win and RESTART FIGHT: Prime full size again, the floor whole
                    Staging.Clear();
                    Arena.ShowPhase(1);
                    Arena.PutBlueStand(blueStand != null ? blueStand : null, false);
                    SetStand(blueStand, true);
                    PlacePrime(Spots.prime.position, Spots.prime.rotation);
                    Shield.SetUp(true, false);
                    Arena.ShowDaisPod(false);
                    CrownOnFlag(false);
                    break;
                case BossPhase.Transition12:
                    // What changes is the beat's to say (CopterRises, BalconyFalls; BeginTransition)
                    break;
                case BossPhase.Phase2:
                    Arena.ShowPhase(2);
                    SetStand(blueStand, false);
                    if (Copter != null && !Copter.Active)
                        Copter.TakeOff();
                    Arena.ShowDaisPod(true);
                    CrownOnFlag(false);
                    break;
                case BossPhase.Transition23:
                    // CopterDown, CrownFlies, PrimeClimbs (BeginTransition)
                    break;
                case BossPhase.Phase3:
                    Staging.ClearCrown();
                    Copter?.Retire();
                    Arena.ShowPhase(3);
                    SetStand(blueStand, true);
                    Arena.PutBlueStand(blueStand, true);
                    blueStand?.ReturnFlag();
                    Arena.ShowDaisPod(true);
                    CrownOnFlag(true);
                    Body.Rig.crown.gameObject.SetActive(false);
                    if ((Body.transform.position - Spots.crash.position).magnitude > 18)
                        PlacePrimeByWreck();
                    Arena.HideTrophy();
                    Arena.HideChampionGun();
                    break;
                case BossPhase.Victory:
                    Walker.Stop();
                    Arena.Cheers(1);
                    break;
                case BossPhase.Defeat:
                    Walker.Stop();
                    break;
            }
            if (phase != BossPhase.Phase3)
                Body.Rig.crown.gameObject.SetActive(true);
            if (phase == BossPhase.Phase1 || phase == BossPhase.Phase2 || phase == BossPhase.Phase3)
            {
                Machines?.Stow(Team.Blue, MachineKind.Tank);
                StowFarTurrets(phase == BossPhase.Phase3);
                Arena.ThrowHealthCaps();
                Arena.Cheers(0.5f);
                ApplyCrew();
            }
            else
            {
                ApplyTemperaments();
            }
            Body.Settle();
        }

        // ---------------------------------------------------------------- Transitions and the victory (ticket 33.8)

        // A transition begins (on the frame after the bar emptied): the match is held, so nobody can be hurt and the crew, the bots and the
        // turrets hold fire, and the story beat plays; its cues change the world as it goes. Without a beat to play (after a retry, or no
        // campaign) the world changes at once and a short timer ends it.
        private void BeginTransition()
        {
            transitionPending = false;
            var phase = State.Phase;
            var id = BossBeats.ForTransition(phase);
            if (id == null)
                return;
            Match.Hold();
            holding = true;
            transitionClock = 0;
            if (!retried && PlayBeat != null && PlayBeat(id, Cue, skipped => EndTransition(phase)))
            {
                beatPlaying = true;
                return;
            }
            foreach (var cue in CuesOf(phase))
                Cue(cue, null);
        }

        private static string[] CuesOf(BossPhase phase) => phase == BossPhase.Transition12
            ? new[] { BossBeats.CopterRises, BossBeats.BalconyFalls }
            : new[] { BossBeats.CopterDown, BossBeats.CrownFlies, BossBeats.PrimeClimbs };

        // The quick transitions' timer
        private void Transitions(float dt)
        {
            if (beatPlaying || !holding)
                return;
            var phase = State.Phase;
            if (BossBeats.ForTransition(phase) == null)
                return;
            transitionClock += dt;
            if (transitionClock >= transitionSeconds)
                EndTransition(phase);
        }

        // The beat is over or skipped (its cues have all been told): the match goes on and the fight to its next phase
        private void EndTransition(BossPhase phase)
        {
            beatPlaying = false;
            if (holding)
            {
                holding = false;
                Match.Release();
            }
            if (State.Phase == phase)
                State.FinishTransition();
        }

        /// <summary>
        /// Plays the victory beat (the shrink, the recount, the crown, the break-up) when the campaign can; <paramref name="then"/> runs
        /// when it is over or skipped. False when there is none to play (the end screens then come at once).
        /// </summary>
        public bool PlayVictoryBeat(Action then) =>
            State != null && State.Phase == BossPhase.Victory && PlayBeat != null && PlayBeat(BossBeats.Victory, Cue, skipped => then());

        /// <summary>What a beat's cue does to the world: as the story says, the arena changes or Prime does something. Several cues in one word-list are told in order.</summary>
        public void Cue(string cues, Func<string, Vector3?> placeOf)
        {
            foreach (var cue in cues.Split(' '))
                switch (cue)
                {
                    case BossBeats.CopterRises:
                        Body.HeldByCopter = true;
                        Copter?.TakeOff();
                        Arena.Cheers(0.8f);
                        break;
                    case BossBeats.BalconyFalls:
                        Arena.ShowPhase(2);
                        Shake(0.5f, Spots.prime.position, 120);
                        break;
                    case BossBeats.CopterDown:
                        Copter?.Retire();
                        Arena.ShowPhase(3);
                        Arena.HideCentrePillar();
                        Shake(0.6f, Spots.crash.position, 120);
                        Arena.Cheers(1);
                        break;
                    case BossBeats.CrownFlies:
                        Staging.CrownFlies(Body.CapTop, Spots.crash.position + Vector3.up * Arena.wreckTop);
                        Body.Rig.crown.gameObject.SetActive(false);
                        break;
                    case BossBeats.PrimeClimbs:
                        PlacePrimeByWreck();
                        break;
                    case BossBeats.Shrink:
                        Staging.ShrinkPrime();
                        break;
                    case BossBeats.CrownRolls:
                        Arena.HideTrophy();
                        var you = Match.You;
                        var seven = placeOf?.Invoke(Cast.Red[1]);
                        Staging.CrownRolls(Body.transform.position + Vector3.up * 0.3f, you != null ? you.transform.position : Body.transform.position, seven);
                        break;
                    case BossBeats.Cheer:
                        Arena.Cheers(1);
                        break;
                    case BossBeats.PixelBreak:
                        Staging.PixelBreak();
                        break;
                }
        }

        private void OnShieldChanged(bool up)
        {
            Shield.SetUp(up, !up);
            if (!up)
                Arena.Cheers(0.9f);
        }

        private void OnStunned(BossStunKind kind, float seconds)
        {
            Arena.Cheers(0.9f);
            if (kind == BossStunKind.Kneel || kind == BossStunKind.Daze)
                Shake(0.3f, Body.transform.position, 40);
        }

        private void OnStunEnded() { }

        private void OnDamaged(BossHit hit) { }

        private void OnCrashed(Vector3 at)
        {
            Arena.HideCentrePillar();
            Shake(0.8f, at, 20);
            Arena.Cheers(0.8f);
        }

        private void OnHandBurst(int index)
        {
            if (Body.Rig.hands == null || index >= Body.Rig.hands.Length)
                return;
            var hand = Body.Rig.hands[index];
            var particles = Particles;
            if (particles != null)
                for (var i = 0; i < 12; i++)
                    particles.Emit(FxParticles.Kind.Puff, hand.position, UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(2f, 5f), 0.8f, UnityEngine.Random.Range(0.3f, 0.6f),
                        new Color(0.9f, 0.55f, 0.15f), new Color(0.5f, 0.25f, 0.05f), gravity: -0.4f, drag: 3);
            hand.gameObject.SetActive(false);
            Arena.Cheers(0.8f);
        }

        private void OnHandRegrown(int index)
        {
            if (Body.Rig.hands != null && index < Body.Rig.hands.Length)
                Body.Rig.hands[index].gameObject.SetActive(true);
        }

        // ---------------------------------------------------------------- Placing him

        private void PlacePrime(Vector3 position, Quaternion rotation)
        {
            if (Copter != null && Copter.Active)
                return;
            Walker.Stop();
            Body.transform.SetPositionAndRotation(position, rotation);
            if (Body.Rig.hands != null)
                for (var i = 0; i < Body.Rig.hands.Length; i++)
                    Body.Rig.hands[i].gameObject.SetActive(true);
            Body.SetAct(PrimeAct.Idle);
            Physics.SyncTransforms();
        }

        // Climbing out of the wreck: a few metres towards his old side of it, on the Boss navmesh, facing the red base
        private void PlacePrimeByWreck()
        {
            var toward = Spots.prime.position - Spots.crash.position;
            toward.y = 0;
            var position = Walker.Snap(Spots.crash.position + toward.normalized * 8, 10);
            var red = Flags.Stand(Team.Red).transform.position - position;
            red.y = 0;
            PlacePrime(position, Quaternion.LookRotation(red.sqrMagnitude > 0.01f ? red : Vector3.forward));
        }

        private static void SetStand(FlagStand stand, bool on)
        {
            if (stand != null && stand.gameObject.activeSelf != on)
                stand.gameObject.SetActive(on);
        }

        // The far two turrets were knocked out by the crash: they stay out in phase 3
        private void StowFarTurrets(bool stow)
        {
            if (Machines == null || !stow)
                return;
            foreach (var machine in Machines.All)
                if (machine.Kind == MachineKind.Turret && machine.Team == Team.Blue && Mathf.Abs(machine.transform.position.x) > 40)
                    machine.Stow();
        }

        // The crown on the blue flag's pole, with a gold glow column, in phase 3; the blue flag again otherwise
        private void CrownOnFlag(bool crown)
        {
            var flagObject = Flags.FlagObject(Team.Blue);
            if (flagObject == null)
                return;
            if (crown && crownOnFlag == null && Spots.crownPrefab != null)
            {
                crownOnFlag = Instantiate(Spots.crownPrefab, flagObject, false);
                crownOnFlag.name = "Crown";
                crownOnFlag.transform.localPosition = new Vector3(0, 2.7f, 0);
                crownOnFlag.transform.localScale = Vector3.one * 3;
                foreach (var collider in crownOnFlag.GetComponentsInChildren<Collider>())
                    collider.enabled = false;
            }
            if (crownOnFlag != null)
                crownOnFlag.SetActive(crown);
            var column = flagObject.GetComponentInChildren<FlagGlowColumn>(true);
            column?.SetColour(crown ? new Color(1f, 0.78f, 0.25f) : BattleSide.ColorOf(BattleSideId.East));
        }

        // ---------------------------------------------------------------- 42's lives, the champion's minigun

        private void OnDied(Death death)
        {
            if (death.Victim != null && !death.Victim.IsBot && death.Victim.Team == Team.Red)
                State.ReportDeath(death.Victim);
        }

        private void OnSpawned(PlayerState player)
        {
            // 42 keeps the champion's minigun through a death in phase 3
            if (!player.IsBot && State.Phase == BossPhase.Phase3 && State.CrownCaptured)
                Combat.State.Take(player, WeaponIds.Minigun);
        }

        private void OnCrownCaptured(PlayerState by)
        {
            Arena.Cheers(1);
            Arena.ShowChampionGun();
            Arena.ShowTrophy(Flags.Stand(Team.Red) != null ? Flags.Stand(Team.Red).transform : null);
            ApplyTemperaments();
            crownHidden = true;
        }

        private void OnChampionGunTaken(Player player)
        {
            if (player != null && player.State != null)
                Combat.State.Take(player.State, WeaponIds.Minigun);
        }

        private void LateCrown()
        {
            if (!crownHidden)
                return;
            // The captured crown is on the red stand: the blue flag is gone from the wreck
            var stand = Flags.Stand(Team.Blue);
            if (stand != null && stand.gameObject.activeSelf)
                stand.gameObject.SetActive(false);
            if (State.Phase != BossPhase.Phase3)
                crownHidden = false;
        }

        private void OnRoundReset()
        {
            // A transition cut short by the round's reset: the match is not held by it any more
            holding = beatPlaying = transitionPending = false;
            if (State.Phase != BossPhase.Intro)
            {
                Staging.Clear();
                restartRequested = true;
                crownHidden = false;
                Arena.HideChampionGun();
                Arena.HideTrophy();
            }
        }

        // ---------------------------------------------------------------- RETRY PHASE, RESTART FIGHT (32's screens call these, ticket 33.7)

        /// <summary>RETRY PHASE: starts the match over in place and the fight from the last phase start, the phase's bar full, flags home, everyone respawned.</summary>
        public void RetryPhase()
        {
            restore = true;
            retried = true;
            Match.Retry();
        }

        /// <summary>RESTART FIGHT: from phase 1, the counters cleared.</summary>
        public void RestartFight()
        {
            restore = false;
            retried = true;
            startPhase = 1;
            Match.Retry();
        }

        private void FixedUpdate() => LateCrown();
    }
}
