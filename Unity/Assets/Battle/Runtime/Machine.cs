using System;
using System.Collections.Generic;
using SdfEffects;
using SdfMachines;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// A machine's body in a battle scene (ticket 24): on a machine prefab's root, next to its <see cref="MachineRig"/>, added by
    /// <see cref="Machines"/>. The rules are its <see cref="State"/> (<see cref="MachineState"/>: health, alive, the 30 s wait); this
    /// does the rest, the same for the three kinds, which differ in how they move and turn their gun
    /// (<see cref="TurretMachine"/>, <see cref="TankMachine"/>, <see cref="HelicopterMachine"/>):
    /// <list type="bullet">
    /// <item>Sight: four times a second, the nearest enemy player within its range in its line of sight (<see cref="MachineTargeting"/>:
    /// players only, never machines, never its own team, not the spawn-protected).</item>
    /// <item>Aim and fire: the bots' reaction time and aim error (<see cref="MachineGun"/>), its gun turned onto the aim point at its
    /// own speed; a hitscan shot of 6 about once a second, which hurts a player through <see cref="Combat.Hurt"/> with
    /// <see cref="Damage.ByMachine"/> (the kill feed names the machine; no kill credit) and draws a pistol-coloured tracer.</item>
    /// <item>The eyes: on the target, else glancing about now and then.</item>
    /// <item>Hits: an <see cref="IDamageable"/> behind its colliders (all on one kinematic body), registered with
    /// <see cref="Combat.Register"/>; at 0 a small burst (ticket 8's exploding mushroom in small: a flash, sparks, puffs in its colours
    /// and spores, its turning piece thrown off spinning while the rest squashes away), then hidden until it is back after 30 s.
    /// Its death is not a player's: no kill feed, no credit.</item>
    /// </list>
    /// Pods and the flag's triggers react to players only (a <see cref="CharacterController"/> or a <see cref="Player"/>), so they ignore it.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public abstract class Machine : MonoBehaviour, IDamageable
    {
        private const float SenseSeconds = 0.25f;
        private const float BurstSeconds = 1.0f;
        private const float PopSeconds = 0.35f;

        private static readonly List<MachineTarget> Candidates = new List<MachineTarget>();

        [Tooltip("What it is doing (shown for debugging)")]
        public string doing;

        protected Machines Owner { get; private set; }
        protected System.Random Random { get; private set; }
        protected MachineGun Gun { get; private set; }
        protected Transform Muzzle { get; private set; }

        private Collider[] colliders = Array.Empty<Collider>();
        private Rigidbody body;
        private Func<MachineTarget, bool> lineOfSight;
        private float senseIn;
        private Player target;
        private Vector3 aimPoint;

        private Quaternion[] eyeRest = Array.Empty<Quaternion>();
        private Vector3 glance;
        private float glanceIn;

        private Transform turnParent;
        private Vector3 turnPosition, bodyPosition;
        private Quaternion turnRotation, bodyRotation;
        private Vector3 turnScale, bodyScale;
        private float burstTime = -1;
        private float popTime = -1;
        private Vector3 flyVelocity;
        private Vector3 flyAngular;

        public MachineRig Rig { get; private set; }

        public MachineState State { get; private set; }

        public BattleSide Side { get; private set; }

        public MachineKind Kind => State.Kind;

        public Team Team => State.Team;

        public bool Alive => State != null && State.Alive;

        /// <summary>The player it is aiming at; null for none.</summary>
        public Player Target => target;

        /// <summary>Its middle, where a bolt jumps to, the flame aims at, and bots shoot.</summary>
        public Vector3 Center => transform.TransformPoint(CenterLocal);

        /// <summary>Where it looks from: between its eyes.</summary>
        public Vector3 EyePosition
        {
            get
            {
                if (Rig == null || Rig.eyes == null || Rig.eyes.Length == 0)
                    return Center;
                var sum = Vector3.zero;
                foreach (var eye in Rig.eyes)
                    sum += eye.position;
                return sum / Rig.eyes.Length;
            }
        }

        /// <summary>The way its gun (or nose) faces, degrees about y, for a camera following it.</summary>
        public virtual float Heading => transform.eulerAngles.y;

        /// <summary>Its middle in its own space.</summary>
        protected abstract Vector3 CenterLocal { get; }

        /// <summary>Where shots leave from, in the space of <see cref="MuzzleParent"/>.</summary>
        protected abstract Vector3 MuzzleLocal { get; }

        protected abstract Transform MuzzleParent { get; }

        /// <summary>Whether a collider is one of this machine's own.</summary>
        public bool Owns(Collider collider) => collider != null && body != null && collider.attachedRigidbody == body;

        // ---------------------------------------------------------------- Setting up

        /// <summary>Ties this body to its rules; <see cref="Machines"/> calls it once, as it makes the machine.</summary>
        public void Bind(Machines owner, MachineState state, MachineRig rig, BattleSide side, int seed)
        {
            Owner = owner;
            State = state;
            Rig = rig;
            Side = side;
            Random = new System.Random(seed);
            Gun = new MachineGun(state.Stats, owner.rules.aim, Random);
            lineOfSight = SeesTarget;

            // One kinematic body over all its colliders: moved by script, it pushes nobody and nobody pushes it
            body = GetComponent<Rigidbody>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            var solid = new List<Collider>();
            foreach (var collider in GetComponentsInChildren<Collider>(true))
                if (!collider.isTrigger)
                    solid.Add(collider);
            colliders = solid.ToArray();

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(MuzzleParent != null ? MuzzleParent : transform, false);
            muzzle.localPosition = MuzzleLocal;
            Muzzle = muzzle;

            eyeRest = new Quaternion[Rig.eyes != null ? Rig.eyes.Length : 0];
            for (var i = 0; i < eyeRest.Length; i++)
                eyeRest[i] = Rig.eyes[i] != null ? Rig.eyes[i].localRotation : Quaternion.identity;
            if (Rig.turn != null)
            {
                turnParent = Rig.turn.parent;
                turnPosition = Rig.turn.localPosition;
                turnRotation = Rig.turn.localRotation;
                turnScale = Rig.turn.localScale;
            }
            if (Rig.body != null)
            {
                bodyPosition = Rig.body.localPosition;
                bodyRotation = Rig.body.localRotation;
                bodyScale = Rig.body.localScale;
            }
            // Staggered, so the machines do not all look in the same frame
            senseIn = (float)Random.NextDouble() * SenseSeconds;
            state.Died += OnDied;
            state.Respawned += OnRespawned;
            owner.Combat?.Register(this);
            OnBound();
        }

        protected virtual void OnBound()
        {
        }

        private void OnDestroy()
        {
            if (State != null)
            {
                State.Died -= OnDied;
                State.Respawned -= OnRespawned;
            }
            if (Owner != null && Owner.Combat != null)
                Owner.Combat.Unregister(this);
        }

        /// <summary>A new round (or the match's start): alive at full health where it starts, no target.</summary>
        public void Restart()
        {
            State.Reset();
            EndBurst();
            Show(true);
            Gun.Reset();
            target = null;
            popTime = -1;
            Place();
        }

        /// <summary>Takes it out of play, hidden, until <see cref="Restart"/> brings it back (see <see cref="MachineState.Stow"/>).</summary>
        public void Stow()
        {
            State.Stow();
            LoseTarget();
            EndBurst();
            SetColliders(false);
            Show(false);
        }

        /// <summary>Puts it where it starts or respawns: a turret where it stands, the others at a free spot of the machine zone.</summary>
        protected abstract void Place();

        // ---------------------------------------------------------------- Every frame

        private void Update()
        {
            if (State == null)
                return;
            var dt = Time.deltaTime;
            if (dt <= 0)
                return;
            if (!State.Alive)
            {
                State.Tick(dt);
                if (burstTime >= 0)
                    StepBurst(dt);
                return;
            }
            if (popTime >= 0)
                StepPop(dt);

            var match = Owner.Match;
            var moving = match != null && match.State != null && match.State.CanMove;
            senseIn -= dt;
            if (senseIn <= 0)
            {
                senseIn = SenseSeconds * (0.85f + 0.3f * (float)Random.NextDouble());
                Sense();
            }

            var onTarget = false;
            if (target != null && target.State.Alive)
            {
                aimPoint = AimPoint();
                onTarget = TurnGun(aimPoint, dt, true);
            }
            else
            {
                if (target != null)
                    LoseTarget();
                TurnGun(default, dt, false);
            }
            if (moving)
                Move(dt, target != null ? aimPoint : (Vector3?)null);
            Look(dt);

            var angular = 0f;
            if (target != null && Bots.Current != null)
            {
                var to = target.transform.position - Muzzle.position;
                var across = Vector3.ProjectOnPlane(Bots.Current.VelocityOf(target), to.normalized).magnitude;
                angular = across / Mathf.Max(1, to.magnitude) * Mathf.Rad2Deg;
            }
            if (Gun.Tick(dt, onTarget && moving, angular))
                Fire();
        }

        /// <summary>What it does without a target, for <see cref="doing"/>.</summary>
        protected virtual string Doing => "looking about";

        /// <summary>
        /// Turns its gun to <paramref name="point"/> (with a target) or about (without) for <paramref name="dt"/>; returns whether the
        /// gun points at the point closely enough to fire.
        /// </summary>
        protected abstract bool TurnGun(Vector3 point, float dt, bool hasTarget);

        /// <summary>Moves for <paramref name="dt"/> (not the turret); <paramref name="fighting"/> the aim point while it has a target.</summary>
        protected virtual void Move(float dt, Vector3? fighting)
        {
        }

        // ---------------------------------------------------------------- Sight and aim

        private void Sense()
        {
            var match = Owner.Match;
            Candidates.Clear();
            var players = match.Players;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                var state = player.State;
                if (state == null)
                    continue;
                Candidates.Add(MachineTarget.Player(state.Index, state.Team, player.transform.position + Vector3.up * 1.15f,
                    state.Alive && player.Capsule != null && player.Capsule.enabled, !state.Protected));
            }
            var chosen = MachineTargeting.Choose(EyePosition, Team, State.Stats.range, Candidates, lineOfSight, target != null ? target.State.Index : -1);
            if (chosen < 0)
            {
                if (target != null)
                    LoseTarget();
                doing = Doing;
                return;
            }
            if (target != match.Players[chosen])
                doing = $"fighting {match.Players[chosen].name}";
            target = match.Players[chosen];
            Gun.Acquire(chosen);
        }

        private bool SeesTarget(MachineTarget candidate)
        {
            var player = Owner.Match.Players[candidate.id];
            return Owner.Visible(Muzzle.position, candidate.position, this, player) || Owner.Visible(EyePosition, candidate.position, this, player);
        }

        private void LoseTarget()
        {
            target = null;
            Gun.Lose();
        }

        /// <summary>The target's chest, off by the aim's error (degrees of yaw and pitch, as the bots' aim).</summary>
        private Vector3 AimPoint()
        {
            var chest = target.transform.position + Vector3.up * 1.15f;
            var from = Muzzle.position;
            var to = chest - from;
            var distance = to.magnitude;
            if (distance < 0.01f)
                return chest;
            var offset = Gun.Offset;
            var right = Vector3.Cross(Vector3.up, to);
            if (right.sqrMagnitude < 1e-6f)
                right = transform.right;
            var direction = Quaternion.AngleAxis(offset.x, Vector3.up) * (Quaternion.AngleAxis(offset.y, right.normalized) * (to / distance));
            return from + direction * distance;
        }

        /// <summary>The way a shot leaves: along the barrel (tank, turret), or from the muzzle to the aim point (helicopter).</summary>
        protected virtual Vector3 ShotDirection(Vector3 point) => Muzzle.forward;

        private void Fire()
        {
            var from = Muzzle.position;
            var direction = ShotDirection(aimPoint).normalized;
            var range = State.Stats.range * 1.2f;
            var hit = Owner.Shoot(from, direction, range, this);
            var landed = hit.collider != null;
            var end = landed ? hit.point : from + direction * range;
            if (landed)
            {
                var player = Player.Of(hit.collider);
                if (player != null && Owner.Combat != null)
                    Owner.Combat.Hurt(player, Damage.ByMachine(State.Name, Team, State.Stats.weapon, State.Stats.damage, from));
            }
            Owner.Combat?.Fx.Tracer(this, WeaponIds.Pistol, Muzzle, end, landed, landed ? hit.normal : -direction);
        }

        // ---------------------------------------------------------------- The eyes

        private void Look(float dt)
        {
            if (Rig.eyes == null || Rig.eyes.Length == 0)
                return;
            Vector3 point;
            if (target != null)
                point = target.transform.position + Vector3.up * 1.4f;
            else
            {
                glanceIn -= dt;
                if (glanceIn <= 0)
                {
                    // Looking about: a point off to one side or the other, a little up or down, now and then
                    // (in the space of the piece the eyes turn with)
                    glanceIn = 0.8f + 2.2f * (float)Random.NextDouble();
                    var yaw = ((float)Random.NextDouble() - 0.5f) * 110;
                    var pitch = ((float)Random.NextDouble() - 0.5f) * 30;
                    glance = Quaternion.Euler(pitch, yaw, 0) * Vector3.forward * 10;
                }
                var eyesParent = Rig.eyes[0].parent != null ? Rig.eyes[0].parent : transform;
                point = EyePosition + eyesParent.TransformDirection(glance);
            }
            var ease = Mathf.Clamp01(dt * (target != null ? 14 : 6));
            for (var i = 0; i < Rig.eyes.Length; i++)
            {
                var eye = Rig.eyes[i];
                if (eye == null)
                    continue;
                var parent = eye.parent != null ? eye.parent : transform;
                var local = parent.InverseTransformDirection(point - eye.position);
                if (local.sqrMagnitude < 1e-6f)
                    continue;
                // Not round to the back of its head: at most 70 degrees off its rest
                var rest = eyeRest[i] * Vector3.forward;
                local = Vector3.RotateTowards(rest, local.normalized, 70 * Mathf.Deg2Rad, 0);
                eye.localRotation = Quaternion.Slerp(eye.localRotation, Quaternion.LookRotation(local, Vector3.up), ease);
            }
        }

        // ---------------------------------------------------------------- IDamageable

        public float TakeDamage(Damage damage, Vector3 point)
        {
            var combat = Owner != null ? Owner.Combat : null;
            var allowed = combat == null || combat.State.CanHurt(Team, damage.AttackerTeam);
            return State.Hurt(damage, allowed);
        }

        // ---------------------------------------------------------------- Death and return

        private void OnDied(MachineState state, Damage damage)
        {
            // Its destroyer gains spores (ticket 25)
            if (Spores.Current != null)
                Spores.Current.State.ReportMachineKilled(damage);
            LoseTarget();
            SetColliders(false);
            Burst();
        }

        private void OnRespawned(MachineState state)
        {
            EndBurst();
            Gun.Reset();
            target = null;
            if (!state.RespawnsInPlace)
                Place();
            else
                OnRespawnInPlace();
            Show(true);
            popTime = 0;
            if (Rig.body != null)
                Rig.body.localScale = bodyScale * 0.2f;
        }

        /// <summary>A turret back where it stood: its gun at rest.</summary>
        protected virtual void OnRespawnInPlace()
        {
        }

        private void Show(bool shown)
        {
            if (Rig.body != null)
                Rig.body.gameObject.SetActive(shown);
            SetColliders(shown);
        }

        private void SetColliders(bool on)
        {
            foreach (var collider in colliders)
                if (collider != null)
                    collider.enabled = on;
        }

        /// <summary>
        /// The burst: a flash and a ring of sparks, puffs in its colours and cream, spores drifting up; the turning piece (the tank's
        /// turret, the turret's head, the helicopter's rotor) thrown up spinning, the rest squashing flat and away.
        /// </summary>
        private void Burst()
        {
            burstTime = 0;
            var centre = Center;
            var fx = Owner.Combat != null ? Owner.Combat.Fx : null;
            var particles = fx != null ? fx.Particles : null;
            if (particles != null)
            {
                var size = Kind == MachineKind.Turret ? 1.6f : 1f;
                var colour = BattleSide.ColorOf(BattleSide.SideOf(Team));
                var cream = new Color(0.95f, 0.9f, 0.78f);
                var flashBand = Hdr(new Color(1, 0.8f, 0.45f), 1.3f);
                var flashCore = Hdr(new Color(1, 0.97f, 0.85f), 2.6f);
                particles.Emit(FxParticles.Kind.Spore, centre, Vector3.zero, 0.1f, 0.4f * size, flashBand, flashCore);
                for (var i = 0; i < 12; i++)
                {
                    var ray = UnityEngine.Random.onUnitSphere;
                    particles.Emit(FxParticles.Kind.Spark, centre + ray * 0.25f * size, ray * UnityEngine.Random.Range(10f, 16f) * size,
                        UnityEngine.Random.Range(0.06f, 0.1f), UnityEngine.Random.Range(0.06f, 0.1f) * size, flashBand, flashCore);
                }
                for (var i = 0; i < 10; i++)
                {
                    var direction = UnityEngine.Random.onUnitSphere;
                    direction.y = Mathf.Abs(direction.y) * 0.8f;
                    var team = i % 3 == 0;
                    particles.Emit(FxParticles.Kind.Puff, centre + direction * 0.25f * size, direction * UnityEngine.Random.Range(0.6f, 1.8f) * size,
                        UnityEngine.Random.Range(0.45f, 0.75f), UnityEngine.Random.Range(0.14f, 0.26f) * size, team ? colour : cream,
                        team ? colour * 0.3f : new Color(0.2f, 0.16f, 0.08f), gravity: -0.6f, drag: 4);
                }
                var sporeBand = Hdr(new Color(0.85f, 1f, 0.5f), 1.4f);
                var sporeCore = Hdr(Color.white, 1.6f);
                for (var i = 0; i < 20; i++)
                {
                    var at = centre + UnityEngine.Random.insideUnitSphere * 0.4f * size;
                    particles.Emit(FxParticles.Kind.Spore, at, (at - centre).normalized * UnityEngine.Random.Range(0.4f, 1.4f) + Vector3.up * 0.5f,
                        UnityEngine.Random.Range(1.6f, 2.8f), UnityEngine.Random.Range(0.012f, 0.026f), sporeBand, sporeCore, gravity: -0.25f, drag: 1.4f);
                }
            }
            if (Rig.turn != null)
            {
                // Thrown off: free of its parent for the flight, put back afterwards
                Rig.turn.SetParent(transform, true);
                flyVelocity = (Vector3.up * 1.2f + UnityEngine.Random.insideUnitSphere * 0.5f).normalized * UnityEngine.Random.Range(3.5f, 5f);
                flyAngular = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(250f, 500f);
            }
        }

        private void StepBurst(float dt)
        {
            burstTime += dt;
            if (burstTime >= BurstSeconds)
            {
                EndBurst();
                Show(false);
                return;
            }
            if (Rig.turn != null && Rig.turn.parent == transform)
            {
                flyVelocity += Vector3.down * (9.8f * dt);
                Rig.turn.position += flyVelocity * dt;
                Rig.turn.rotation = Quaternion.Euler(flyAngular * dt) * Rig.turn.rotation;
                var shrink = 1 - Mathf.SmoothStep(0, 1, (burstTime - 0.55f) / (BurstSeconds - 0.55f));
                Rig.turn.localScale = turnScale * Mathf.Max(0.01f, shrink);
            }
            if (Rig.body != null)
            {
                // Squashed flat and wide, then gone
                var t = burstTime / 0.5f;
                var squash = t < 0.4f ? Mathf.Lerp(1, 0.25f, t / 0.4f) : Mathf.Lerp(0.25f, 0.01f, Mathf.Clamp01((t - 0.4f) / 0.6f));
                var wide = t < 0.4f ? Mathf.Lerp(1, 1.35f, t / 0.4f) : Mathf.Lerp(1.35f, 0.01f, Mathf.Clamp01((t - 0.4f) / 0.6f));
                Rig.body.localScale = Vector3.Scale(bodyScale, new Vector3(wide, squash, wide));
            }
        }

        /// <summary>The pieces back as they were (hidden or not): the turning piece on its hinge, the body unsquashed.</summary>
        private void EndBurst()
        {
            burstTime = -1;
            if (Rig.turn != null)
            {
                if (turnParent != null && Rig.turn.parent != turnParent)
                    Rig.turn.SetParent(turnParent, false);
                Rig.turn.localPosition = turnPosition;
                Rig.turn.localRotation = turnRotation;
                Rig.turn.localScale = turnScale;
            }
            if (Rig.body != null)
            {
                Rig.body.localPosition = bodyPosition;
                Rig.body.localRotation = bodyRotation;
                Rig.body.localScale = bodyScale;
            }
        }

        // Back again: a quick pop up to a little over its size and down
        private void StepPop(float dt)
        {
            popTime += dt;
            var t = Mathf.Clamp01(popTime / PopSeconds);
            var scale = t < 0.6f ? Mathf.Lerp(0.2f, 1.08f, t / 0.6f) : Mathf.Lerp(1.08f, 1, (t - 0.6f) / 0.4f);
            if (Rig.body != null)
                Rig.body.localScale = bodyScale * scale;
            if (t >= 1)
                popTime = -1;
        }

        private static Color Hdr(Color color, float intensity) => new Color(color.r * intensity, color.g * intensity, color.b * intensity, 1);

        // ---------------------------------------------------------------- Debugging

        protected virtual void OnDrawGizmos()
        {
            if (Owner == null || !Owner.showGizmos || State == null || !State.Alive)
                return;
            Gizmos.color = BattleSide.ColorOf(BattleSide.SideOf(Team));
            if (target != null)
                Gizmos.DrawLine(Muzzle.position, aimPoint);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(Center + Vector3.up * 0.8f, $"{name}: {doing} ({State.Health.Current:0}/{State.Health.Max:0})");
#endif
        }
    }
}
