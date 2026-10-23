using System;
using SdfEffects;
using SdfLevel;
using SdfMachines;
using SdfPrime;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The Prime-copter (ticket 33.5, phase 2 of the boss fight): 24's helicopter at seven times, flown over the arena with Chanterelle
    /// Prime hanging under it by his hands on a damped pendulum. Its flight is <see cref="PrimeCopterFlight"/> (24's
    /// <see cref="HelicopterPilot"/> with the numbers of <see cref="BossRules"/>: the body 14 to 18 m up, never under 9 m over anything,
    /// 6 m/s, 8 m inside the wall; the dive; the crash); the rules are <see cref="BossState"/>'s, which this follows:
    /// <list type="bullet">
    /// <item>The four spore sacs under its rim are <see cref="IDamageable"/>s registered with <see cref="Combat.Register"/> (the
    /// electro-gun's bolt jumps between them); what they take is <see cref="BossState.HurtSac"/>'s. A burst sac shows a puff of blue spores
    /// and is gone; two grow back with the daze's end.</item>
    /// <item>The fourth burst sac starts the crash (<see cref="BossState.CrashStarted"/>): the copter sputters down to the wreck's place
    /// in the state's two seconds and lands (<see cref="BossState.CrashLanded"/>): everyone of the other teams within
    /// <see cref="BossRules.crashRadius"/> takes <see cref="BossRules.CrashDamage"/>. Prime sits dazed beside it; when the daze ends and
    /// the sacs grow back, it takes off again, smoking.</item>
    /// <item>The dive (<see cref="StartDive"/>) and the tank drop (<see cref="DropTank"/>) are the attacks' hands: the boss component asks
    /// for them when the planner says so, with the numbers of the attack's spec. A dive's slam hurts and shoves players in its circle.</item>
    /// </list>
    /// Prime is the <see cref="PrimeRig"/> given to <see cref="Create"/>: while the copter flies he is parented to a pivot at the middle of
    /// the grips, hanging 7.5 m below it, swinging against the copter's acceleration, his hands on the runners; on the wreck he stands
    /// beside it with his cap tilted. <see cref="controlPrime"/> off leaves Prime's transform to someone else.
    /// <para>
    /// It does not appear until <see cref="TakeOff"/> (the boss calls it as the transition to phase 2 lets the copter rise from behind the
    /// Royal Box) and is gone again from phase 3 on (the wreck's model stands in the phase tagged level). Frame cost: one flight tick, one
    /// pendulum step, five surface rays five times a second, a few transforms.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class PrimeCopter : MonoBehaviour
    {
        public const string ObjectName = "Prime-copter";
        public const string PrefabPath = "Assets/Machines/Prefabs/primecopter.prefab";

        /// <summary>Who the boss's damage is by, in the kill feed and on the death screen.</summary>
        public const string Name = "Chanterelle Prime";

        private static readonly RaycastHit[] Hits = new RaycastHit[24];
        private static readonly System.Comparison<RaycastHit> ByDistance = (a, b) => a.distance.CompareTo(b.distance);

        [Tooltip("Degrees a second the rotor spins at full speed (5 m across, so lazier than the machines')")]
        public float rotorSpeed = 700;
        [Tooltip("Degrees a second the tail rotor spins at full speed")]
        public float tailRotorSpeed = 1400;
        [Tooltip("Metres from the grips down to Prime's feet: his 6.5 m and a metre of arms over his head")]
        public float hangDrop = 7.5f;
        [Tooltip("Metres from the grips to Prime's middle, the pendulum's length")]
        public float hangLength = 4.2f;
        [Tooltip("Metres from the wreck's place to where Prime sits, to the copter's right")]
        public float seatDistance = 5;
        [Tooltip("Degrees Prime's cap tilts forward while he sits dazed (the kneel's)")]
        public float seatCapTilt = 35;
        [Tooltip("Whether this moves Prime's transform and pose (his hands, his cap); off leaves them to the boss's body component")]
        public bool controlPrime = true;
        [Tooltip("Draw the flight's waypoint in the Scene view")]
        public bool showGizmos = true;

        private BossState state;
        private Match match;
        private Machines machines;
        private PrimeRig prime;
        private PrimeCopterRig rig;
        private PrimeCopterFlight flight;
        private HangingPendulum pendulum;
        private Rigidbody body;
        private Collider[] bodyColliders = Array.Empty<Collider>();
        private PrimeSac[] sacs = Array.Empty<PrimeSac>();
        private Vector3[] sacScale = Array.Empty<Vector3>();
        private float[] sacPunch = new float[4];
        private float[] sacGrow = new float[4];
        private Quaternion[] eyeRest = Array.Empty<Quaternion>();
        private Vector3 crashSpot, launchSpot;
        private float launchYaw;
        private Vector3? faceTarget;
        private bool active;
        private float surfaceTop;
        private float smokeIn;
        private float clock;

        // Prime's own place before the copter took him, to put him back; his hands' and cap's pose
        private Transform primeParent;
        private Vector3 primePosition;
        private Quaternion primeRotation;
        private Vector3[] handRest = Array.Empty<Vector3>();
        private Quaternion capRest;
        private Transform hang;
        private float hangBlend = 1;
        private Vector3 seat;
        private Vector3 lastVelocity;

        /// <summary>The copter of the active fight; null where there is none.</summary>
        public static PrimeCopter Current { get; private set; }

        public PrimeCopterFlight Flight => flight;

        public HangingPendulum Pendulum => pendulum;

        /// <summary>The four spore sacs' hit shapes (alive or not: ask <see cref="SacAlive"/>), for the bots to shoot at (ticket 33.6).</summary>
        public System.Collections.Generic.IReadOnlyList<PrimeSac> SacShapes => sacs;

        public PrimeCopterRig Rig => rig;

        public BossState State => state;

        /// <summary>Whether it is in the scene now (from <see cref="TakeOff"/> to its end).</summary>
        public bool Active => active;

        /// <summary>The body's origin (the runners' underside).</summary>
        public Vector3 Position => flight != null ? flight.Position : transform.position;

        /// <summary>Where the tank drop's hatch is, under the tail, behind Prime.</summary>
        public Vector3 HatchPosition => rig.hatch.position;

        /// <summary>Where the cap cannon's shots leave from, under the nose.</summary>
        public Vector3 MuzzlePosition => rig.muzzle.position;

        /// <summary>The way the nose points on the ground (degrees about y).</summary>
        public float Yaw => flight != null ? flight.Yaw : 0;

        /// <summary>A point the copter turns its nose (and its eyes) to while it flies its laps: the cannon's target; null for along its way.</summary>
        public Vector3? FaceTarget
        {
            get => faceTarget;
            set => faceTarget = value;
        }

        /// <summary>Whether it is down on the wreck's place, Prime dazed beside it.</summary>
        public bool Down => flight != null && flight.Move == CopterMove.Down;

        /// <summary>A dive's slam: the circle's centre on the floor. Everyone in it has been hurt and shoved already.</summary>
        public event Action<Vector3> DiveSlammed;

        /// <summary>The crash landed at this place, everyone within reach hurt already (a screen shake, dust and debris are for the boss's effects).</summary>
        public event Action<Vector3> Crashed;

        /// <summary>A tank was dropped from the hatch.</summary>
        public event Action<TankMachine> TankDropped;

        /// <summary>A sac burst: its index and where it was.</summary>
        public event Action<int, Vector3> SacBurst;

        /// <summary>It took off (the first time or after the daze).</summary>
        public event Action TookOff;

        /// <summary>
        /// Makes the copter from its prefab (Assets/Machines/Prefabs/primecopter.prefab; <see cref="LoadPrefab"/> in the editor), hidden
        /// until <see cref="TakeOff"/>, for a fight whose rules are <paramref name="state"/>. <paramref name="spots"/> are the arena's
        /// places: where the crash lands (<see cref="BossSpots.crash"/>) and where Prime stands on the dais, behind whom it takes off
        /// (<see cref="BossSpots.prime"/>); <paramref name="prime"/> is the Prime that hangs under it (may be null) and
        /// <paramref name="machines"/> the scene's machines for the tank drop (may be null: no tanks).
        /// </summary>
        public static PrimeCopter Create(GameObject prefab, Transform parent, BossState state, Match match, Machines machines, PrimeRig prime, BossSpots spots)
        {
            if (prefab == null)
            {
                Debug.LogWarning($"PrimeCopter: no prefab ({PrefabPath}); build it (Tools > SDF Machines > Rebuild Prime-copter) and pass it in");
                return null;
            }
            var instance = Instantiate(prefab, parent);
            instance.name = ObjectName;
            var copter = instance.AddComponent<PrimeCopter>();
            copter.Bind(state, match, machines, prime, spots);
            return copter;
        }

#if UNITY_EDITOR
        /// <summary>The prefab by its path, in the editor (a built player is given it through <see cref="Create"/>).</summary>
        public static GameObject LoadPrefab() => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
#endif

        /// <summary>Whether <paramref name="collider"/> is one of the copter's (so a ground or surface ray passes it).</summary>
        public static bool Owns(Collider collider)
        {
            var rigidbody = collider != null ? collider.attachedRigidbody : null;
            return rigidbody != null && rigidbody.GetComponent<PrimeCopter>() != null;
        }

        // ---------------------------------------------------------------- Setting up

        private void Bind(BossState state, Match match, Machines machines, PrimeRig prime, BossSpots spots)
        {
            this.state = state;
            this.match = match;
            this.machines = machines;
            this.prime = prime;
            crashSpot = spots != null && spots.crash != null ? spots.crash.position : Vector3.zero;
            // It rises from the dais behind Prime, who faces the field (west): 3 m behind him
            launchSpot = spots != null && spots.prime != null ? spots.prime.position - spots.prime.forward * 3 : crashSpot;
            launchYaw = spots != null && spots.prime != null ? spots.prime.eulerAngles.y : 0;
            rig = GetComponent<PrimeCopterRig>();
            if (rig == null)
            {
                Debug.LogWarning("PrimeCopter: the prefab has no PrimeCopterRig; rebuild it (Tools > SDF Machines > Rebuild Prime-copter)");
                enabled = false;
                return;
            }

            Machines.PlayArea(out var min, out var max);
            var floor = crashSpot.y;
            surfaceTop = floor + 80;
            flight = new PrimeCopterFlight(state.Rules, min, max, floor, Surface, new System.Random(33));
            pendulum = new HangingPendulum { Length = hangLength };

            // One kinematic body over all its colliders (the sphere over the puffball, the sacs'): moved by script
            body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            bodyColliders = GetComponentsInChildren<Collider>(true);

            sacs = new PrimeSac[rig.sacs.Length];
            sacScale = new Vector3[rig.sacs.Length];
            sacPunch = new float[rig.sacs.Length];
            sacGrow = new float[rig.sacs.Length];
            for (var i = 0; i < rig.sacs.Length; i++)
            {
                // A sphere the sac's size, at its middle, 0.55 m under its pivot (its top at the rim's socket)
                var hit = new GameObject($"Sac {i + 1} Hit");
                hit.transform.SetParent(rig.sacs[i], false);
                hit.transform.localPosition = new Vector3(0, -0.55f, 0);
                var sphere = hit.AddComponent<SphereCollider>();
                sphere.radius = 0.55f;
                sacs[i] = hit.AddComponent<PrimeSac>();
                sacs[i].Bind(this, i);
                sacScale[i] = rig.sacs[i].localScale;
                Combat.Current?.Register(sacs[i]);
            }
            bodyColliders = GetComponentsInChildren<Collider>(true);

            eyeRest = new Quaternion[rig.eyes != null ? rig.eyes.Length : 0];
            for (var i = 0; i < eyeRest.Length; i++)
                eyeRest[i] = rig.eyes[i] != null ? rig.eyes[i].localRotation : Quaternion.identity;

            state.Parts.SacHurt += OnSacHurt;
            state.Parts.SacBurst += OnSacBurst;
            state.CrashStarted += OnCrashStarted;
            state.CrashLanded += OnCrashLanded;
            state.SacsRegrown += OnSacsRegrown;
            state.PhaseChanged += OnPhaseChanged;
            state.CheckpointRestored += OnCheckpointRestored;
            flight.DiveBottomed += OnDiveBottomed;
            Current = this;
            SetShown(false);
        }

        private void OnDestroy()
        {
            if (state != null)
            {
                state.Parts.SacHurt -= OnSacHurt;
                state.Parts.SacBurst -= OnSacBurst;
                state.CrashStarted -= OnCrashStarted;
                state.CrashLanded -= OnCrashLanded;
                state.SacsRegrown -= OnSacsRegrown;
                state.PhaseChanged -= OnPhaseChanged;
                state.CheckpointRestored -= OnCheckpointRestored;
            }
            if (flight != null)
                flight.DiveBottomed -= OnDiveBottomed;
            var combat = Combat.Current;
            if (combat != null)
                foreach (var sac in sacs)
                    combat.Unregister(sac);
            if (hang != null)
                Destroy(hang.gameObject);
            if (Current == this)
                Current = null;
        }

        // ---------------------------------------------------------------- The boss's hands

        /// <summary>
        /// Appears behind Prime on the dais and rises, rotors spinning up, to its laps (the transition to phase 2: the copter rises from
        /// behind the Royal Box), with Prime lifted by its runners as they pass his hands. Also what a retry of phase 2 does.
        /// </summary>
        public void TakeOff() => TakeOff(launchSpot, launchYaw);

        /// <summary>As <see cref="TakeOff()"/> from <paramref name="ground"/>, facing <paramref name="yaw"/>.</summary>
        public void TakeOff(Vector3 ground, float yaw)
        {
            if (flight == null)
                return;
            var first = !active;
            SetShown(true);
            state.Parts.Reset();
            ShowSacs();
            flight.Takeoff(ground, yaw);
            ApplyPose();
            if (first)
                AttachPrime();
            hangBlend = 0;
            pendulum.Reset();
            lastVelocity = Vector3.zero;
            TookOff?.Invoke();
        }

        /// <summary>
        /// The dive attack: moves over the circle at <paramref name="floorPoint"/> (on the floor), drops to 6 m and slams, climbs back,
        /// with the stages' seconds and the damage and push of <paramref name="spec"/>. False unless it is flying its laps.
        /// </summary>
        public bool StartDive(Vector3 floorPoint, BossAttackSpec spec)
        {
            if (!active)
                return false;
            diveSpec = spec;
            return flight.BeginDive(floorPoint, spec.windUp, spec.active, spec.recover);
        }

        private BossAttackSpec diveSpec;

        /// <summary>
        /// The tank drop attack: a blue tank that is out of play falls from the hatch, lands with a puff and drives off. Null when every
        /// blue tank is in play already (the planner does not ask then) or the scene has no machines.
        /// </summary>
        public TankMachine DropTank()
        {
            if (!active || machines == null)
                return null;
            var tank = machines.DropTank(Team.Blue, rig.hatch.position - Vector3.up * 0.8f, flight.Yaw);
            if (tank != null)
                TankDropped?.Invoke(tank);
            return tank;
        }

        /// <summary>The copter is gone (phase 3, a retry, the end): hidden, Prime back where he was.</summary>
        public void Retire()
        {
            if (flight == null)
                return;
            DetachPrime();
            flight.Ground();
            SetShown(false);
        }

        // ---------------------------------------------------------------- Sacs

        /// <summary>Whether sac <paramref name="index"/> is hanging there (the rules' health).</summary>
        public bool SacAlive(int index) => active && state != null && state.Phase == BossPhase.Phase2 && state.Parts.SacAlive(index);

        /// <summary>A hit on sac <paramref name="index"/>, from <see cref="PrimeSac.TakeDamage"/>: blue's own hits do nothing, else the rules take it.</summary>
        public float HurtSac(int index, Damage damage)
        {
            if (!SacAlive(index))
                return 0;
            var combat = Combat.Current;
            if (combat != null && !combat.State.CanHurt(Team.Blue, damage.AttackerTeam))
                return 0;
            return state.HurtSac(index, damage.Amount, damage.Attacker);
        }

        private void ShowSacs()
        {
            for (var i = 0; i < rig.sacs.Length; i++)
            {
                var alive = state.Parts.SacAlive(i);
                if (alive && !rig.sacs[i].gameObject.activeSelf)
                    sacGrow[i] = 1;
                rig.sacs[i].gameObject.SetActive(alive);
            }
        }

        private void OnSacHurt(int index, float taken) => sacPunch[index] = 1;

        private void OnSacBurst(int index)
        {
            var at = rig.sacs[index].TransformPoint(new Vector3(0, -0.55f, 0));
            rig.sacs[index].gameObject.SetActive(false);
            Emit(at, 1.0f, new Color(0.35f, 0.7f, 1f), new Color(0.75f, 0.95f, 1f), 18, 6);
            SacBurst?.Invoke(index, at);
        }

        // ---------------------------------------------------------------- The state machine's events

        private void OnCrashStarted(PlayerState by)
        {
            if (active)
                flight.BeginCrash(crashSpot, state.Rules.crashSeconds);
        }

        private void OnCrashLanded()
        {
            if (!active)
                return;
            flight.Land();
            // Prime sits beside it: at the copter's right, down on the floor
            var right = Quaternion.Euler(0, flight.Yaw, 0) * Vector3.right;
            seat = crashSpot + right * seatDistance;
            var ground = Machines.Current != null && Machines.Current.Ground(seat + Vector3.up * 8, 16, out var point, out _) ? point : seat;
            seat = ground;
            Crash(crashSpot);
        }

        private void OnSacsRegrown()
        {
            if (!active)
                return;
            // The daze is over: two sacs back, up it goes, smoking
            ShowSacs();
            flight.Takeoff(flight.Position, flight.Yaw);
            hangBlend = 0;
            pendulum.Reset();
            TookOff?.Invoke();
        }

        private void OnPhaseChanged(BossPhase phase)
        {
            // Phase 3 puts the wreck's model in its place; the phases before the take-off and the ends have no copter. Phase 2 itself
            // is the copter's (it rose in the transition before it)
            if (phase == BossPhase.Phase3 || phase == BossPhase.Phase1 || phase == BossPhase.Intro || phase == BossPhase.Victory || phase == BossPhase.Defeat)
                Retire();
        }

        // RETRY PHASE: everything starts over from the phase's start, with no transition to rise in: gone, and up again in phase 2
        private void OnCheckpointRestored(BossCheckpoint checkpoint)
        {
            Retire();
            if (checkpoint.Phase == BossPhase.Phase2)
                TakeOff();
        }

        private void OnDiveBottomed(Vector3 centre)
        {
            var radius = state.Rules.diveRadius;
            var players = match != null ? match.Players : null;
            if (players != null)
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (!InReach(player, centre, radius))
                        continue;
                    var away = player.transform.position - centre;
                    away.y = 0;
                    if (away.sqrMagnitude < 0.01f)
                        away = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0) * Vector3.forward;
                    var damage = Damage.ByMachine(Name, Team.Blue, "cannon", diveSpec.damage, centre);
                    Combat.Current?.Hurt(player, damage);
                    player.Controller?.Shove(away, diveSpec.push);
                }
            }
            // Dust ring and spores from the downdraft
            Emit(centre + Vector3.up * 0.3f, 0.7f, new Color(0.88f, 0.82f, 0.7f), new Color(0.5f, 0.45f, 0.35f), 0, 0, ring: radius);
            DiveSlammed?.Invoke(centre);
        }

        // ---------------------------------------------------------------- The crash

        private void Crash(Vector3 centre)
        {
            var radius = state.Rules.crashRadius;
            var players = match != null ? match.Players : null;
            if (players != null)
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (!InReach(player, centre, radius))
                        continue;
                    var damage = Damage.ByMachine(Name, Team.Blue, "cannon", state.Rules.CrashDamage, centre);
                    Combat.Current?.Hurt(player, damage);
                }
            }
            Emit(centre + Vector3.up * 0.5f, 1.2f, new Color(0.88f, 0.82f, 0.7f), new Color(0.5f, 0.45f, 0.35f), 10, 12, ring: radius);
            Crashed?.Invoke(centre);
        }

        // Within flat reach of a point, alive, and of a team the boss's blue hurts (never its own crew)
        private static bool InReach(Player player, Vector3 centre, float radius)
        {
            if (player == null || player.State == null || !player.State.Alive || player.Team == Team.Blue)
                return false;
            var flat = player.transform.position - centre;
            flat.y = 0;
            return flat.sqrMagnitude <= radius * radius;
        }

        // ---------------------------------------------------------------- Every frame

        private void Update() => Step(Time.deltaTime);

        // After the boss's own animation of Prime in Update: his place under the copter
        private void LateUpdate() => FollowWithPrime(Time.deltaTime);

        /// <summary>Lets <paramref name="dt"/> seconds of the flight pass: what <c>Update</c> does each frame (public for tests and the spectator).</summary>
        public void Step(float dt)
        {
            if (!active || flight == null)
                return;
            if (dt <= 0)
                return;
            var moving = match == null || match.State == null || match.State.CanMove;
            if (moving)
            {
                flight.Tick(dt, faceTarget);
                var accel = (flight.Velocity - lastVelocity) / dt;
                lastVelocity = flight.Velocity;
                // Spikes (a take-off, a landing) swing him less than they would: the grips are held, the body is heavy
                accel = Vector3.ClampMagnitude(accel, 12);
                if (flight.Flying)
                    pendulum.Step(dt, accel);
                else if (flight.Move == CopterMove.Crashing)
                    pendulum.Step(dt, accel * 0.5f);
                clock += dt;
            }
            ApplyPose();
            Spin(dt);
            Eyes(dt);
            Sacs(dt);
            Smoke(dt);
        }

        /// <summary>Puts Prime where the copter has him for <paramref name="dt"/> seconds: what <c>LateUpdate</c> does each frame.</summary>
        public void FollowWithPrime(float dt)
        {
            if (!active || flight == null)
                return;
            PlacePrime(dt);
        }

        private void ApplyPose()
        {
            transform.SetPositionAndRotation(flight.Position, Quaternion.Euler(0, flight.Yaw, 0));
            rig.body.localRotation = Quaternion.Euler(flight.Pitch, 0, flight.Roll);
        }

        private void Spin(float dt)
        {
            var spin = flight.Spin;
            if (rig.rotor != null)
                rig.rotor.localRotation *= Quaternion.Euler(0, rotorSpeed * spin * dt, 0);
            if (rig.tailRotor != null)
                rig.tailRotor.localRotation *= Quaternion.Euler(tailRotorSpeed * spin * dt, 0, 0);
        }

        // The eyes on the target while it fights, else ahead
        private void Eyes(float dt)
        {
            if (rig.eyes == null)
                return;
            var ease = Mathf.Clamp01(dt * (faceTarget.HasValue ? 12 : 5));
            for (var i = 0; i < rig.eyes.Length; i++)
            {
                var eye = rig.eyes[i];
                if (eye == null)
                    continue;
                var wanted = eyeRest[i];
                if (faceTarget.HasValue)
                {
                    var parent = eye.parent;
                    var local = parent.InverseTransformDirection(faceTarget.Value - eye.position);
                    if (local.sqrMagnitude > 1e-6f)
                    {
                        // At most 70 degrees off its rest, as the machines' eyes
                        var look = Vector3.RotateTowards(eyeRest[i] * Vector3.forward, local.normalized, 70 * Mathf.Deg2Rad, 0);
                        wanted = Quaternion.LookRotation(look, Vector3.up);
                    }
                }
                eye.localRotation = Quaternion.Slerp(eye.localRotation, wanted, ease);
            }
        }

        // Sacs hang and pulse; a hit punches one out, a regrown one pops in
        private void Sacs(float dt)
        {
            for (var i = 0; i < rig.sacs.Length; i++)
            {
                var sac = rig.sacs[i];
                if (!sac.gameObject.activeSelf)
                    continue;
                sacPunch[i] = Mathf.MoveTowards(sacPunch[i], 0, dt * 5);
                sacGrow[i] = Mathf.MoveTowards(sacGrow[i], 0, dt * 1.2f);
                var pulse = 1 + 0.05f * Mathf.Sin(clock * 3 + i * 1.7f) + 0.18f * sacPunch[i];
                var grow = 1 - sacGrow[i];
                sac.localScale = sacScale[i] * (pulse * grow);
                // They swing a little with the body, hanging from the rim
                sac.localRotation = Quaternion.Euler(Mathf.Sin(clock * 2 + i) * 4, 0, Mathf.Cos(clock * 2.3f + i) * 4);
            }
        }

        // Hurt (two sacs gone or fewer) it smokes: dark puffs from the body; on the wreck a thinner trail
        private void Smoke(float dt)
        {
            var burst = state.Parts.SacCount - state.Parts.SacsAlive;
            var smoking = burst >= 2 || flight.Move == CopterMove.Crashing || flight.Move == CopterMove.Down;
            if (!smoking)
                return;
            smokeIn -= dt;
            if (smokeIn > 0)
                return;
            smokeIn = flight.Move == CopterMove.Down ? 0.25f : 0.08f;
            var at = flight.Position + Quaternion.Euler(0, flight.Yaw, 0) * new Vector3(0, 3.2f, -0.6f);
            var particles = Particles();
            if (particles == null)
                return;
            var grey = new Color(0.22f, 0.22f, 0.26f);
            particles.Emit(FxParticles.Kind.Puff, at, new Vector3(UnityEngine.Random.Range(-0.4f, 0.4f), UnityEngine.Random.Range(1f, 2f), UnityEngine.Random.Range(-0.4f, 0.4f)),
                UnityEngine.Random.Range(1.2f, 2f), UnityEngine.Random.Range(0.5f, 0.9f), grey, grey * 0.5f, gravity: -0.5f, drag: 1.5f);
        }

        // ---------------------------------------------------------------- Prime under it

        private void AttachPrime()
        {
            if (prime == null || !controlPrime)
                return;
            if (hang == null)
            {
                hang = new GameObject("Prime-copter hang").transform;
                hang.SetParent(transform.parent, false);
            }
            var t = prime.transform;
            primeParent = t.parent;
            primePosition = t.position;
            primeRotation = t.rotation;
            capRest = prime.cap != null ? prime.cap.localRotation : Quaternion.identity;
            handRest = new Vector3[prime.hands != null ? prime.hands.Length : 0];
            for (var i = 0; i < handRest.Length; i++)
                handRest[i] = prime.hands[i].localPosition;
            // He stands where he is until the runners reach his hands, then hangs from them
            seat = t.position;
            t.SetParent(hang, false);
            t.localPosition = new Vector3(0, -hangDrop, 0);
            t.localRotation = Quaternion.identity;
            hang.SetPositionAndRotation(seat + Vector3.up * hangDrop, primeRotation);
        }

        private void DetachPrime()
        {
            if (prime == null || hang == null || prime.transform.parent != hang)
                return;
            var t = prime.transform;
            t.SetParent(primeParent, false);
            t.SetPositionAndRotation(primePosition, primeRotation);
            if (prime.cap != null)
                prime.cap.localRotation = capRest;
            for (var i = 0; i < handRest.Length; i++)
                prime.hands[i].localPosition = handRest[i];
        }

        private Vector3 GripMiddle() => (rig.gripLeft.position + rig.gripRight.position) * 0.5f;

        private void PlacePrime(float dt)
        {
            if (prime == null || hang == null || !controlPrime || prime.transform.parent != hang)
                return;
            var yaw = Quaternion.Euler(0, flight.Yaw, 0);
            if (flight.Move == CopterMove.Down)
            {
                // Dazed beside the wreck: feet on the floor, cap tilted forward, hands at his sides, facing the copter
                var toCopter = crashSpot - seat;
                toCopter.y = 0;
                var facing = toCopter.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toCopter) : yaw;
                hang.SetPositionAndRotation(Vector3.Lerp(hang.position, seat + Vector3.up * hangDrop, Mathf.Clamp01(dt * 4)), Quaternion.Slerp(hang.rotation, facing, Mathf.Clamp01(dt * 4)));
                if (prime.cap != null)
                    prime.cap.localRotation = Quaternion.Slerp(prime.cap.localRotation, capRest * Quaternion.Euler(seatCapTilt, 0, 0), Mathf.Clamp01(dt * 4));
                RestHands(dt);
                return;
            }
            hangBlend = Mathf.MoveTowards(hangBlend, 1, dt / 1.2f);
            var swing = pendulum.Rotation * yaw;
            var pivot = GripMiddle();
            // His feet never go below the floor he stood on: while the copter climbs, he stands until the runners reach his hands. Over a
            // moment he comes across from where he stood to under the grips
            var held = new Vector3(pivot.x, Mathf.Max(pivot.y, seat.y + hangDrop), pivot.z);
            var stood = new Vector3(seat.x, seat.y + hangDrop, seat.z);
            hang.position = hangBlend < 1 ? Vector3.Lerp(stood, held, Mathf.SmoothStep(0, 1, hangBlend)) : held;
            hang.rotation = hangBlend < 1 ? Quaternion.Slerp(hang.rotation, swing, Mathf.Clamp01(dt * 6)) : swing;
            if (prime.cap != null)
                prime.cap.localRotation = Quaternion.Slerp(prime.cap.localRotation, capRest, Mathf.Clamp01(dt * 6));
            GripHands();
        }

        // His hands on the runners, the one nearer the copter's left on the left runner (by where they float on his body)
        private void GripHands()
        {
            if (prime.hands == null || prime.hands.Length < 2)
                return;
            var a = prime.hands[0];
            var b = prime.hands[1];
            var aLocal = prime.transform.InverseTransformPoint(a.position).x;
            var bLocal = prime.transform.InverseTransformPoint(b.position).x;
            var left = aLocal <= bLocal ? a : b;
            var right = left == a ? b : a;
            left.position = rig.gripLeft.position;
            right.position = rig.gripRight.position;
        }

        private void RestHands(float dt)
        {
            for (var i = 0; i < handRest.Length; i++)
                prime.hands[i].localPosition = Vector3.Lerp(prime.hands[i].localPosition, handRest[i], Mathf.Clamp01(dt * 6));
        }

        // ---------------------------------------------------------------- Shown or not

        private void SetShown(bool shown)
        {
            active = shown;
            if (rig != null && rig.body != null)
                rig.body.gameObject.SetActive(shown);
            foreach (var collider in bodyColliders)
                if (collider != null)
                    collider.enabled = shown;
            if (rig != null)
                foreach (var sac in rig.sacs)
                    if (sac != null && !shown)
                        sac.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- The world under it

        // The height of the highest solid surface at (x, z), past the copter itself, Prime, players and machines
        private float Surface(float x, float z)
        {
            var count = Physics.RaycastNonAlloc(new Vector3(x, surfaceTop, z), Vector3.down, Hits, surfaceTop + 200, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(Hits, 0, count, System.Collections.Generic.Comparer<RaycastHit>.Create(ByDistance));
            for (var i = 0; i < count; i++)
            {
                var collider = Hits[i].collider;
                if (Player.Of(collider) != null || Machines.Of(collider) != null || Owns(collider) || collider.GetComponentInParent<PrimeRig>() != null)
                    continue;
                return Hits[i].point.y;
            }
            return crashSpot.y;
        }

        private FxParticles Particles() => Combat.Current != null && Combat.Current.Fx != null ? Combat.Current.Fx.Particles : null;

        // Dust and spores: puffs round a point (or in a ring of the given radius on the floor) and rising spores
        private void Emit(Vector3 centre, float size, Color band, Color core, int puffs, int spores, float ring = 0)
        {
            var particles = Particles();
            if (particles == null)
                return;
            for (var i = 0; i < puffs; i++)
            {
                var direction = UnityEngine.Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.8f;
                particles.Emit(FxParticles.Kind.Puff, centre + direction * 0.3f * size, direction * UnityEngine.Random.Range(1.5f, 4.5f) * size,
                    UnityEngine.Random.Range(0.6f, 1.1f), UnityEngine.Random.Range(0.25f, 0.5f) * size, band, core, gravity: -0.4f, drag: 3);
            }
            if (ring > 0)
            {
                const int around = 28;
                for (var i = 0; i < around; i++)
                {
                    var angle = i / (float)around * Mathf.PI * 2;
                    var outward = new Vector3(Mathf.Cos(angle), 0.1f, Mathf.Sin(angle));
                    particles.Emit(FxParticles.Kind.Puff, centre + outward * 0.8f, outward * ring * 1.4f, UnityEngine.Random.Range(0.6f, 0.9f),
                        UnityEngine.Random.Range(0.35f, 0.6f), band, core, gravity: -0.3f, drag: 3);
                }
            }
            var sporeBand = new Color(0.6f, 0.9f, 1.4f);
            for (var i = 0; i < spores; i++)
            {
                var at = centre + UnityEngine.Random.insideUnitSphere * 0.6f * size;
                particles.Emit(FxParticles.Kind.Spore, at, (at - centre).normalized * UnityEngine.Random.Range(0.6f, 2f) + Vector3.up * 0.6f,
                    UnityEngine.Random.Range(1.4f, 2.6f), UnityEngine.Random.Range(0.02f, 0.04f), sporeBand, Color.white, gravity: -0.25f, drag: 1.2f);
            }
        }

        private void OnDrawGizmos()
        {
            if (!showGizmos || !active || flight == null)
                return;
            Gizmos.color = new Color(0.3f, 0.6f, 1f);
            Gizmos.DrawLine(flight.Position, flight.Waypoint);
            Gizmos.DrawWireSphere(flight.Waypoint, 1);
        }
    }
}
