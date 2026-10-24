using System.Collections.Generic;
using SdfBattle;
using SdfLevel;
using SdfPickups;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SdfAudio
{
    /// <summary>
    /// The sound of a battle, on the match's object (made by <see cref="Match"/> at its start, after everything it listens to). It only
    /// <em>listens</em>: it subscribes to the events the game already raises and plays the cues for them, changing no rule. What it hears:
    /// <list type="bullet">
    /// <item>Shots (<see cref="CombatFx"/>'s events): the pistol, dual pistols and sniper once a shot; the minigun as a head, a loop for as long as
    /// it fires (<see cref="Arsenal.Firing"/>) and a tail; the flamethrower as a loop; the electro-, poison- and blob-gun; the machines'
    /// guns; a shell casing a moment after a pistol or minigun shot. Your own weapon is heard on the screen (2D), everyone else's in the
    /// world (3D, culled beyond its cue's range).</item>
    /// <item>Hits and deaths: hit markers when you hit, a grunt when you are hurt, the guts of a scout that bursts (with gore on), your
    /// own death, the respawn; explosions when a machine dies (small for a turret, medium for a tank, large for a helicopter; the distant
    /// one when it is far) and when the boss's copter crashes.</item>
    /// <item>Steps: yours by the arena's surface (<see cref="ArenaAudio"/>), the bots' quiet and in the world; the jump and landing; the
    /// heartbeat that fades in below 40 % health (<see cref="HeartbeatRules"/>).</item>
    /// <item>Flags, spores, pods, the boss, sudden death, victory (the crowd and a sting) and defeat.</item>
    /// <item>The music's intensity: how many enemies are near and in sight, how recently you fired or were hit, a flag carried or chased,
    /// rated by <see cref="MusicIntensity"/> a few times a second. <see cref="MusicDirector"/> reads <see cref="MusicClips"/>,
    /// <see cref="LayerShares"/> and <see cref="MusicLevel"/> from here.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class GameAudio : MonoBehaviour
    {
        /// <summary>Seconds between two looks at the fight (enemies near and in sight).</summary>
        private const float SampleInterval = 0.25f;

        private sealed class Walker
        {
            public Player player;
            public Arsenal arsenal;
            public readonly StrideCounter stride = new StrideCounter(2f);
            public Vector3 last;
            public bool seen;
            public bool airborne;
            public float airSince;
            public bool spinning;
            /// <summary>0 no loop, 1 the minigun's, 2 the flamethrower's.</summary>
            public int loopKind;
            public AudioPlayer.Handle loop;
        }

        /// <summary>The match's sound; null where there is none.</summary>
        public static GameAudio Current { get; private set; }

        private readonly List<Walker> walkers = new List<Walker>();
        private readonly Dictionary<MachineState, Machine> machineOf = new Dictionary<MachineState, Machine>();
        private readonly Dictionary<Machine, AudioPlayer.Handle> machineLoops = new Dictionary<Machine, AudioPlayer.Handle>();
        private readonly List<float> shares = new List<float>(MusicIntensity.Layers) { 1, 0, 0, 0 };
        // The minigun's casings: one for every other shot (14 a second is more than the web player can start), for each shooter; the wait is 1.5 shots, so frame jitter keeps it to every other
        private readonly ShotSoundLimiter minigunCasings = new ShotSoundLimiter(1.5f / 14f);
        private readonly MusicIntensity intensity = new MusicIntensity();
        private Match match;
        private Combat combat;
        private ArenaAudio.Sounds arena;
        private CaptureTheFlagState flags;
        private BossState boss;
        private SporeState spores;
        private AudioPlayer.Handle heartbeat;
        private BattleMoment moment = BattleMoment.Calm;
        private float nextSample;
        private float lastShot = -999;
        private float lastHit = -999;
        private bool ended;
        private bool victoryPlayed;
        private int createdFrame;
        private AudioClip[] musicClips;
        private bool bossTrack;

        /// <summary>The clips of the music this battle plays (the arena's four layers, or the boss theme); null for none.</summary>
        public AudioClip[] MusicClips => musicClips;

        /// <summary>How loud each layer should be, 0 to 1, by the fight's intensity.</summary>
        public IReadOnlyList<float> LayerShares => shares;

        /// <summary>The music's level: the battle's, the boss's, or lower once the match has ended.</summary>
        public float MusicLevel => (bossTrack ? MusicDirector.BossLevel : MusicDirector.BattleLevel) * (ended ? 0.35f : 1);

        /// <summary>The fight's intensity now, 0 to 1 (the music's layers follow it).</summary>
        public float Intensity => intensity.Level;

        // ---------------------------------------------------------------- Set up

        /// <summary>Makes the sound of a match; <see cref="Match"/> calls it at the end of its start, before the first round begins.</summary>
        public static GameAudio Create(Match match, Combat combat)
        {
            if (Audio.Muted || !Audio.Ensure())
                return null;
            var audio = match.GetComponent<GameAudio>();
            if (audio == null)
                audio = match.gameObject.AddComponent<GameAudio>();
            audio.Attach(match, combat);
            return audio;
        }

        private void Attach(Match owner, Combat owningCombat)
        {
            match = owner;
            combat = owningCombat;
            createdFrame = Time.frameCount;
            arena = ArenaAudio.Resolve();
            EnsureListener();

            foreach (var player in match.Players)
                walkers.Add(new Walker { player = player, arsenal = player.GetComponent<Arsenal>() });

            var state = match.State;
            state.Spawned += OnSpawned;
            state.Died += OnDied;
            state.MatchEnded += OnMatchEnded;
            state.RoundReset += OnRoundReset;
            state.SuddenDeathStarted += OnSuddenDeath;
            combat.State.Damaged += OnDamaged;
            combat.HitLanded += OnHitLanded;
            combat.Fx.ShotFired += OnShot;
            combat.Fx.BoltFired += OnBolt;
            combat.Fx.BoltJumped += OnBoltJump;
            combat.Fx.Lobbed += OnLobbed;
            combat.Fx.Landed += OnLanded;
            WeaponPickup.PickedUp += OnWeaponPickedUp;
            HealthPickup.PickedUp += OnHealthPickedUp;

            var ctf = CaptureTheFlag.Current;
            if (ctf != null)
            {
                flags = ctf.State;
                flags.Taken += OnFlagTaken;
                flags.Dropped += OnFlagDropped;
                flags.Returned += OnFlagReturned;
                flags.Captured += OnFlagCaptured;
            }
            if (Spores.Current != null)
            {
                spores = Spores.Current.State;
                spores.Filled += OnSporeFilled;
                spores.Sporulated += OnSporulated;
            }
            if (Machines.Current != null)
                foreach (var machine in Machines.Current.All)
                {
                    machineOf[machine.State] = machine;
                    machine.State.Died += OnMachineDied;
                }
            if (Boss.Current != null)
            {
                boss = Boss.Current.State;
                boss.PhaseChanged += OnBossPhase;
                boss.ShieldChanged += OnBossShield;
                boss.CrashLanded += OnBossCrash;
                boss.Defeated += OnBossDefeated;
                boss.Lost += OnBossLost;
            }

            // The music: the boss theme in the boss's fight, else the arena's four layers; none, and the menu's track plays on
            bossTrack = boss != null && arena.bossTheme != null;
            musicClips = bossTrack ? new[] { arena.bossTheme } : arena.HasBattleMusic ? arena.layers : null;
            Current = this;
        }

        // Battle scenes have no audio listener of their own until their builder is run again: the camera in your player gets one
        private void EnsureListener()
        {
            if (FindFirstObjectByType<AudioListener>() != null)
                return;
            var camera = match.You != null ? match.You.GetComponentInChildren<Camera>() : Camera.main;
            if (camera != null)
                camera.gameObject.AddComponent<AudioListener>();
        }

        private void OnDestroy()
        {
            if (Current == this)
                Current = null;
            WeaponPickup.PickedUp -= OnWeaponPickedUp;
            HealthPickup.PickedUp -= OnHealthPickedUp;
            if (match != null && match.State != null)
            {
                match.State.Spawned -= OnSpawned;
                match.State.Died -= OnDied;
                match.State.MatchEnded -= OnMatchEnded;
                match.State.RoundReset -= OnRoundReset;
                match.State.SuddenDeathStarted -= OnSuddenDeath;
            }
            if (combat != null)
            {
                if (combat.State != null)
                    combat.State.Damaged -= OnDamaged;
                combat.HitLanded -= OnHitLanded;
                if (combat.Fx != null)
                {
                    combat.Fx.ShotFired -= OnShot;
                    combat.Fx.BoltFired -= OnBolt;
                    combat.Fx.BoltJumped -= OnBoltJump;
                    combat.Fx.Lobbed -= OnLobbed;
                    combat.Fx.Landed -= OnLanded;
                }
            }
            if (flags != null)
            {
                flags.Taken -= OnFlagTaken;
                flags.Dropped -= OnFlagDropped;
                flags.Returned -= OnFlagReturned;
                flags.Captured -= OnFlagCaptured;
            }
            if (spores != null)
            {
                spores.Filled -= OnSporeFilled;
                spores.Sporulated -= OnSporulated;
            }
            foreach (var state in machineOf.Keys)
                state.Died -= OnMachineDied;
            if (boss != null)
            {
                boss.PhaseChanged -= OnBossPhase;
                boss.ShieldChanged -= OnBossShield;
                boss.CrashLanded -= OnBossCrash;
                boss.Defeated -= OnBossDefeated;
                boss.Lost -= OnBossLost;
            }
            // Loops end with the scene
            heartbeat.Stop(0.2f);
            foreach (var walker in walkers)
                walker.loop.Stop(0.1f);
            foreach (var loop in machineLoops.Values)
                loop.Stop(0.1f);
        }

        private Player You => match != null ? match.You : null;

        private static float Now => Time.unscaledTime;

        // ---------------------------------------------------------------- Every frame

        private void Update()
        {
            if (match == null || match.State == null)
                return;
            var dt = Time.deltaTime;
            if (dt > 0)
            {
                foreach (var walker in walkers)
                {
                    UpdateSteps(walker, dt);
                    UpdateWeaponLoop(walker);
                }
                UpdateHeartbeat();
                UpdateMachineLoops();
            }
            UpdateMusic(Time.unscaledDeltaTime);
        }

        // ---------------------------------------------------------------- Steps, jumps and landings

        private void UpdateSteps(Walker walker, float dt)
        {
            var player = walker.player;
            if (player.State == null || !player.State.Alive || !player.Controller.enabled)
            {
                walker.seen = false;
                walker.airborne = false;
                walker.stride.Rest();
                return;
            }
            var position = player.transform.position;
            if (!walker.seen)
            {
                walker.last = position;
                walker.seen = true;
                return;
            }
            var delta = position - walker.last;
            walker.last = position;
            var flat = new Vector2(delta.x, delta.z).magnitude;
            // A jump of more than a few metres in a frame is a teleport (a spawn), not a walk
            if (flat > 2.5f)
            {
                walker.stride.Rest();
                return;
            }
            var grounded = player.Controller.IsGrounded;
            var speed = flat / dt;
            if (grounded && speed > 0.6f)
            {
                if (walker.stride.Advance(flat))
                    Step(walker, position);
            }
            else
                walker.stride.Rest();

            if (!player.IsYou)
                return;
            if (!grounded && !walker.airborne && delta.y > 0.01f)
            {
                walker.airborne = true;
                walker.airSince = Now;
                Audio.Play2D(Cues.Jump);
            }
            else if (grounded && walker.airborne)
            {
                walker.airborne = false;
                // A hop on a slope is not a landing
                if (Now - walker.airSince > 0.25f)
                    Audio.Play2D(Cues.Land);
            }
        }

        private void Step(Walker walker, Vector3 position)
        {
            if (walker.player.IsYou)
                Audio.Play(ArenaAudio.FootstepCue(arena.surface), position, twoD: true);
            else
                Audio.Play(Cues.FootstepEnemy, position);
        }

        // ---------------------------------------------------------------- Weapons

        private void OnShot(Object owner, string weapon, Transform muzzle, Vector3 end, bool landed)
        {
            if (muzzle == null)
                return;
            if (owner is Machine machine)
            {
                var cue = machine.Kind == MachineKind.Tank ? Cues.TankFire : machine.Kind == MachineKind.Helicopter ? Cues.HelicopterFire : Cues.TurretFire;
                Audio.Play(cue, muzzle.position);
                return;
            }
            if (!(owner is Player player))
                return;
            var you = player.IsYou;
            if (you)
                lastShot = Now;
            switch (weapon)
            {
                case WeaponIds.Pistol:
                    Audio.Play(Cues.Pistol, muzzle.position, twoD: you);
                    Casing(player);
                    break;
                case WeaponIds.DualPistols:
                    Audio.Play(Cues.DualPistols, muzzle.position, twoD: you);
                    Casing(player);
                    break;
                case WeaponIds.Sniper:
                    Audio.Play(Cues.Sniper, muzzle.position, twoD: you);
                    break;
                case WeaponIds.Minigun:
                    // The burst's loop is the sound; only the casings fall, for every other shot
                    if (minigunCasings.Allow(player.GetInstanceID(), Now))
                        Casing(player);
                    break;
            }
        }

        // A shell on the ground by the shooter's feet, a moment after the shot
        private static void Casing(Player player)
        {
            var at = player.transform.position + player.transform.right * 0.45f + player.transform.forward * 0.3f;
            Audio.Play(Cues.Casing, at, 1, Random.Range(0.25f, 0.5f));
        }

        private void OnBolt(Object owner, Transform muzzle, Vector3 end)
        {
            var you = owner is Player { IsYou: true };
            if (you)
                lastShot = Now;
            if (muzzle != null)
                Audio.Play(Cues.ElectroGun, muzzle.position, twoD: you);
        }

        private void OnBoltJump(Object owner, Vector3 to) => Audio.Play(Cues.ElectroChain, to);

        private void OnLobbed(FireMode mode, Transform muzzle)
        {
            if (muzzle == null)
                return;
            var arsenal = You != null ? combat.ArsenalOf(You) : null;
            var you = arsenal != null && arsenal.Muzzle == muzzle;
            if (you)
                lastShot = Now;
            Audio.Play(mode == FireMode.Cloud ? Cues.PoisonGun : Cues.BlobGun, muzzle.position, twoD: you);
        }

        private void OnLanded(FireMode mode, Vector3 at) => Audio.Play(mode == FireMode.Cloud ? Cues.PoisonImpact : Cues.BlobImpact, at);

        // The minigun's burst and the flamethrower's flame last as long as the trigger is held and the weapon fires
        private void UpdateWeaponLoop(Walker walker)
        {
            var arsenal = walker.arsenal;
            var player = walker.player;
            if (arsenal == null || player.State == null)
                return;
            var stats = arsenal.Weapon;
            var alive = player.State.Alive;
            var minigun = alive && stats != null && stats.id == WeaponIds.Minigun;
            var kind = alive && arsenal.Firing && stats != null ? (stats.id == WeaponIds.Minigun ? 1 : stats.mode == FireMode.Cone ? 2 : 0) : 0;

            // The barrels begin to turn
            var spinning = minigun && arsenal.Trigger.SpinFraction > 0.02f;
            if (spinning && !walker.spinning)
                PlayOn(walker, Cues.MinigunSpinUp);
            walker.spinning = spinning;

            if (player.IsYou && kind != 0)
                lastShot = Now;
            if (kind == walker.loopKind)
                return;
            if (walker.loopKind == 1)
            {
                walker.loop.Stop(0.04f);
                PlayOn(walker, Cues.MinigunTail);
            }
            else if (walker.loopKind == 2)
            {
                walker.loop.Stop(0.15f);
                PlayOn(walker, Cues.FlamethrowerEnd);
            }
            walker.loopKind = kind;
            if (kind == 1)
            {
                PlayOn(walker, Cues.MinigunHead);
                var head = Audio.CueOf(Cues.MinigunHead);
                var wait = head != null && head.Filled ? Mathf.Max(0, head.clips[0].length - 0.02f) : 0;
                walker.loop = PlayOn(walker, Cues.MinigunLoop, wait);
            }
            else if (kind == 2)
            {
                // The ignition first, the roar joining as it ends
                PlayOn(walker, Cues.FlamethrowerStart);
                var ignition = Audio.CueOf(Cues.FlamethrowerStart);
                var lead = ignition != null && ignition.Filled ? Mathf.Max(0, ignition.clips[0].length - 0.05f) : 0;
                walker.loop = PlayOn(walker, Cues.FlamethrowerLoop, lead);
            }
        }

        // A sound of a player's weapon: on the screen for you, following the player in the world otherwise
        private static AudioPlayer.Handle PlayOn(Walker walker, string cue, float delay = 0) =>
            Audio.Play(cue, Vector3.up * 1.2f, 1, delay, walker.player.IsYou, walker.player.transform);

        // ---------------------------------------------------------------- Hits, hurt, death, explosions

        private void OnHitLanded(Player shooter, Player victim, bool killed, bool head)
        {
            if (shooter == null || !shooter.IsYou)
                return;
            lastHit = Now;
            Audio.Play2D(killed ? Cues.HitMarkerKill : head ? Cues.HitMarkerHead : Cues.HitMarker);
        }

        private void OnDamaged(PlayerState victim, Damage damage, float taken)
        {
            if (taken <= 0 || You == null || victim != You.State)
                return;
            lastHit = Now;
            Audio.Play2D(Cues.Hurt);
        }

        private void OnSpawned(PlayerState state)
        {
            // The first spawns of the match are the match starting, not a respawn
            if (Time.frameCount == createdFrame || You == null || state != You.State)
                return;
            Audio.Play2D(Cues.Respawn);
        }

        private void OnDied(Death death)
        {
            var victim = match.PlayerOf(death.Victim);
            if (victim == null || death.Cause == DeathCause.Removed)
                return;
            if (victim.IsYou)
                Audio.Play2D(Cues.DeathYou);
            if (GoreRules.Bursts(Gore.Level, death.Cause))
                Audio.Play(Cues.DeathGore, victim.transform.position + Vector3.up * 0.9f);
        }

        private void OnMachineDied(MachineState state, Damage damage)
        {
            if (!machineOf.TryGetValue(state, out var machine) || machine == null)
                return;
            switch (state.Kind)
            {
                case MachineKind.Turret:
                    Explode(machine.Center, Cues.ExplosionSmall, Cues.DebrisSmall);
                    break;
                case MachineKind.Tank:
                    Explode(machine.Center, Cues.ExplosionMedium, Cues.DebrisMedium);
                    break;
                default:
                    Explode(machine.Center, Cues.ExplosionLarge, Cues.DebrisLarge);
                    break;
            }
        }

        /// <summary>An explosion at a place: its cue and debris falling a moment later, or only the far rumble when it is far away.</summary>
        public static void Explode(Vector3 at, string explosion, string debris)
        {
            if ((at - Audio.ListenerPosition).sqrMagnitude > 90 * 90)
            {
                Audio.Play(Cues.ExplosionDistant, at);
                return;
            }
            Audio.Play(explosion, at);
            Audio.Play(debris, at, 0.85f, 0.25f);
        }

        // ---------------------------------------------------------------- The match

        private void OnMatchEnded(MatchResult result)
        {
            ended = true;
            if (You == null)
                return;
            if (result.Winner == You.Team)
                Victory();
            else
                Audio.Play2D(Cues.Defeat);
        }

        private void Victory()
        {
            ended = true;
            if (victoryPlayed)
                return;
            victoryPlayed = true;
            Audio.Play2D(Cues.VictorySting);
        }

        private void OnRoundReset()
        {
            ended = false;
            victoryPlayed = false;
            intensity.Reset();
        }

        private void OnSuddenDeath() => Audio.Play2D(Cues.SuddenDeath);

        // ---------------------------------------------------------------- Flags, pods, spores, boss

        private Team YourTeam => You != null ? You.Team : match.yourTeam;

        private void OnFlagTaken(TeamFlag flag, PlayerState carrier) => Audio.Play2D(Cues.FlagPickup);

        private void OnFlagDropped(TeamFlag flag, PlayerState carrier) => Audio.Play2D(Cues.FlagDrop);

        private void OnFlagReturned(TeamFlag flag, FlagReturn why, PlayerState by) => Audio.Play2D(Cues.FlagReturn);

        private void OnFlagCaptured(TeamFlag flag, PlayerState carrier)
        {
            if (carrier != null && carrier.Team == YourTeam)
            {
                Audio.Play2D(Cues.FlagCapture);
                Audio.Play2D(Cues.CaptureSting);
            }
            else
                Audio.Play2D(Cues.FlagLost);
        }

        private void OnWeaponPickedUp(WeaponPickup pod, GameObject who) => Pickup(Cues.WeaponPickup, pod != null ? pod.transform.position : Vector3.zero, who);

        private void OnHealthPickedUp(HealthPickup pod, GameObject who) => Pickup(Cues.HealthPickup, pod != null ? pod.transform.position : Vector3.zero, who);

        private void Pickup(string cue, Vector3 at, GameObject who)
        {
            var player = who != null ? Player.Of(who.transform) : null;
            if (player != null && player.IsYou)
                Audio.Play2D(cue);
            else
                Audio.Play(cue, at);
        }

        private void OnSporeFilled(PlayerState player)
        {
            if (You != null && player == You.State)
                Audio.Play2D(Cues.SporeReady);
        }

        private void OnSporulated(PlayerState player, SporePower power)
        {
            var cue = power == SporePower.Rush ? Cues.SporeRush : power == SporePower.Regrowth ? Cues.SporeRegrowth : Cues.SporeArmour;
            var body = match.PlayerOf(player);
            if (body == null || body.IsYou)
                Audio.Play2D(cue);
            else
                Audio.Play(cue, body.transform.position + Vector3.up);
        }

        private void OnBossPhase(BossPhase phase) => Audio.Play2D(Cues.BossPhase);

        private void OnBossShield(bool up)
        {
            if (!up)
                Audio.Play2D(Cues.BossShieldDown);
        }

        private void OnBossCrash()
        {
            var spots = Boss.Current != null ? Boss.Current.Spots : null;
            var at = spots != null && spots.crash != null ? spots.crash.position : Audio.ListenerPosition;
            Audio.Play(Cues.BossCrash, at);
            Explode(at, Cues.ExplosionLarge, Cues.DebrisLarge);
        }

        private void OnBossDefeated() => Victory();

        private void OnBossLost()
        {
            ended = true;
            Audio.Play2D(Cues.Defeat);
        }

        // ---------------------------------------------------------------- Loops that follow things

        private void UpdateHeartbeat()
        {
            var you = You;
            var alive = you != null && you.State != null && you.State.Alive;
            var share = alive ? Mathf.Clamp01(you.State.Health / Mathf.Max(1, you.State.MaxHealth)) : 1;
            var volume = HeartbeatRules.Volume(share);
            if (!alive || volume < 0.02f)
            {
                if (heartbeat.Alive)
                    heartbeat.Stop(0.6f);
                return;
            }
            if (!heartbeat.Alive)
                heartbeat = Audio.Play2D(Cues.Heartbeat);
            heartbeat.SetVolume(volume);
            heartbeat.SetPitch(HeartbeatRules.Pitch(share));
        }

        // A live tank's engine and a live helicopter's rotor, looped on the machine (only once the cues have a clip)
        private void UpdateMachineLoops()
        {
            if (Machines.Current == null || !(Audio.Has(Cues.TankEngine) || Audio.Has(Cues.HelicopterRotor)))
                return;
            foreach (var machine in Machines.Current.All)
            {
                if (machine == null || machine.Kind == MachineKind.Turret)
                    continue;
                machineLoops.TryGetValue(machine, out var loop);
                if (machine.Alive && !loop.Alive)
                    machineLoops[machine] = Audio.Play(machine.Kind == MachineKind.Tank ? Cues.TankEngine : Cues.HelicopterRotor, Vector3.zero, 1, 0, false, machine.transform);
                else if (!machine.Alive && loop.Alive)
                    loop.Stop(0.3f);
            }
        }

        // ---------------------------------------------------------------- The music's intensity

        private void UpdateMusic(float dt)
        {
            if (Now >= nextSample)
            {
                nextSample = Now + SampleInterval;
                moment = Sample();
            }
            intensity.Tick(dt, moment);
            for (var i = 0; i < shares.Count; i++)
                shares[i] = bossTrack ? (i == 0 ? 1 : 0) : MusicIntensity.LayerShare(i, intensity.Level);
        }

        /// <summary>A look at the fight from your place: the enemies near and in sight, how long since a shot or a hit, the flags.</summary>
        private BattleMoment Sample()
        {
            var you = You;
            if (you == null || you.State == null || !you.State.Alive)
                return new BattleMoment(false, 0, 0, 999, 999, false, false);
            var camera = you.GetComponentInChildren<Camera>();
            var eye = camera != null ? camera.transform.position : you.transform.position + Vector3.up * 1.5f;
            var forward = camera != null ? camera.transform.forward : you.transform.forward;
            var near = 0;
            var view = 0;
            foreach (var other in match.Players)
            {
                if (other == you || other.State == null || !other.State.Alive || other.Team == you.Team)
                    continue;
                Judge(eye, forward, other.transform.position + Vector3.up, you, other, null, ref near, ref view);
            }
            if (Machines.Current != null)
                foreach (var machine in Machines.Current.All)
                    if (machine != null && machine.Alive && machine.Team != you.Team)
                        Judge(eye, forward, machine.Center, you, null, machine, ref near, ref view);

            var carrying = false;
            var chase = false;
            if (flags != null)
            {
                carrying = flags.Flag(you.Team.Other()).Carrier == you.State;
                chase = flags.Flag(you.Team).Status == FlagStatus.Carried;
            }
            return new BattleMoment(true, near, view, Now - lastShot, Now - lastHit, carrying, chase);
        }

        // Counts one enemy as near and/or in view; false when it counted as neither
        private bool Judge(Vector3 eye, Vector3 forward, Vector3 point, Player you, Player player, Machine machine, ref int near, ref int view)
        {
            var to = point - eye;
            var distance = to.magnitude;
            if (distance < 1e-3f || distance > MusicIntensity.ViewMetres)
                return false;
            var counted = false;
            if (distance <= MusicIntensity.NearMetres)
            {
                near++;
                counted = true;
            }
            if (Vector3.Dot(to / distance, forward) > 0.5f)
            {
                var hit = combat.Trace(eye, to / distance, distance, you);
                if (!hit.hit || (player != null && hit.player == player) || (machine != null && ReferenceEquals(hit.target, machine)))
                {
                    view++;
                    counted = true;
                }
            }
            return counted;
        }
    }
}
