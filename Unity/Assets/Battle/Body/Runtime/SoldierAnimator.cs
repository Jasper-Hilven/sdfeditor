using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBody
{
    /// <summary>
    /// The soldier body's animation on Unity's <see cref="Animator"/> (from BattleRoyale's SoldierAnimator, tickets 05 and 06). One per
    /// body, you or a bot, put on the body with <see cref="Attach"/>. The calls are wishes; the parameters are written once per frame in
    /// <see cref="Update"/>, damped so the blend tree never snaps. A parameter the controller does not have is skipped silently.
    /// <list type="bullet">
    /// <item><b>Locomotion</b>: <see cref="SetMove"/> takes the velocity in the body's frame; the tree is a blend over MoveX and MoveY, its
    /// playback speed (<see cref="LocomotionMath.Playback"/>) is matched to the ground speed so the feet do not slide, at any of the
    /// game's speeds (walk 4.4 m/s, the flag carrier's 0.9, Frenzy's 1.6).</item>
    /// <item><b>Stance</b>: <see cref="SetStance"/>, the clip set of the weapon in hand (rifle, pistol, heavy).</item>
    /// <item><b>Air</b>: <see cref="Jump"/> and <see cref="SetAirborne"/>: take-off, fall loop, landing.</item>
    /// <item><b>Upper body</b>: <see cref="Fire"/> (a single shot, or the looped burst when the shots come fast), <see cref="Reload"/>,
    /// <see cref="Hit"/>.</item>
    /// <item><b>Emotes</b>: <see cref="Emote"/> by state name (the dances, the roar, the victory).</item>
    /// <item><b>Death</b>: <see cref="Died"/> is the hook for the ragdoll (ticket 51): it stops the animation and holds the body's last
    /// pose, and raises <see cref="WasKilled"/> with the direction the hit pushes. No death clip plays.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SoldierAnimator : MonoBehaviour
    {
        /// <summary>The controller's parameter, layer and state names (the builder makes them; this class writes them).</summary>
        public static class Names
        {
            public const string BaseLayer = "Locomotion";
            public const string UpperBodyLayer = "UpperBody";
            public const string LocomotionState = "Locomotion";
            public const string JumpState = "JumpUp";
            public const string FallState = "Fall";
            public const string LandState = "Land";
            public const string EmoteMachine = "Emotes";
            public const string EmoteTag = "Emote";
            public const string EmptyState = "Empty";
            public const string FireState = "Fire";
            public const string BurstState = "FireBurst";
            public const string ReloadState = "Reload";
            public const string HitState = "Hit";

            /// <summary>Float, body-frame velocity right / run speed (−1 left … 1 right).</summary>
            public const string MoveX = "MoveX";
            /// <summary>Float, body-frame velocity forward / run speed (−1 back … 1 forward; up to the sprint radius).</summary>
            public const string MoveY = "MoveY";
            /// <summary>Float, the locomotion state's playback multiplier so the feet match the ground.</summary>
            public const string Speed = "Speed";
            /// <summary>Float 0 (rifle), 1 (pistol), 2 (heavy), damped: which clip set the locomotion blends.</summary>
            public const string Stance = "Stance";
            /// <summary>Trigger: a jump starts.</summary>
            public const string Jump = "Jump";
            /// <summary>Bool: off the ground.</summary>
            public const string Airborne = "Airborne";
            /// <summary>Trigger: one shot.</summary>
            public const string Fire = "Fire";
            /// <summary>Bool: the fire is automatic (shots closer than <see cref="CombatTuning.BurstInterval"/>): the burst clip loops.</summary>
            public const string FireAuto = "FireAuto";
            /// <summary>Trigger: a reload starts.</summary>
            public const string Reload = "Reload";
            /// <summary>Float: the Reload state's speed (clip length / the weapon's reload seconds).</summary>
            public const string ReloadSpeed = "ReloadSpeed";
            /// <summary>Trigger: a hit reaction.</summary>
            public const string Hit = "Hit";
            /// <summary>Float: where the hit came from in the body's frame (x right, −1 … 1).</summary>
            public const string HitX = "HitX";
            /// <summary>Bool: dead (stays true until <see cref="Revive"/>).</summary>
            public const string Dead = "Dead";
        }

        static readonly int MoveXId = Animator.StringToHash(Names.MoveX);
        static readonly int MoveYId = Animator.StringToHash(Names.MoveY);
        static readonly int SpeedId = Animator.StringToHash(Names.Speed);
        static readonly int StanceId = Animator.StringToHash(Names.Stance);
        static readonly int JumpId = Animator.StringToHash(Names.Jump);
        static readonly int AirborneId = Animator.StringToHash(Names.Airborne);
        static readonly int FireId = Animator.StringToHash(Names.Fire);
        static readonly int FireAutoId = Animator.StringToHash(Names.FireAuto);
        static readonly int ReloadId = Animator.StringToHash(Names.Reload);
        static readonly int ReloadSpeedId = Animator.StringToHash(Names.ReloadSpeed);
        static readonly int HitId = Animator.StringToHash(Names.Hit);
        static readonly int HitXId = Animator.StringToHash(Names.HitX);
        static readonly int DeadId = Animator.StringToHash(Names.Dead);
        static readonly int LocomotionStateId = Animator.StringToHash(Names.BaseLayer + "." + Names.LocomotionState);
        static readonly int EmptyStateId = Animator.StringToHash(Names.UpperBodyLayer + "." + Names.EmptyState);

        /// <summary>The numbers; null means <see cref="LocomotionTuning.LoadDefault"/> (the committed <c>SoldierAnimation.asset</c>).</summary>
        public LocomotionTuning Tuning;
        /// <summary>The combat numbers; null means <see cref="CombatTuning.LoadDefault"/>.</summary>
        public CombatTuning Combat;
        /// <summary>The body's Animator; null means the first one in the children.</summary>
        public Animator Body;
        /// <summary>Animate even when no camera sees the body (your own body, seen by the death camera).</summary>
        public bool AlwaysAnimate = true;

        /// <summary>The damped tree position now (MoveX, MoveY).</summary>
        public Vector2 Tree => tree;
        /// <summary>The tree position asked for by the last <see cref="SetMove"/>, before damping.</summary>
        public Vector2 TreeTarget => treeTarget;
        /// <summary>The playback multiplier written to <see cref="Names.Speed"/> this frame.</summary>
        public float Playback => playback;
        /// <summary>The stance asked for.</summary>
        public LocomotionClips.Stance Stance => stanceTarget;
        /// <summary>True after <see cref="Died"/> until <see cref="Revive"/>.</summary>
        public bool IsDead => dead;
        /// <summary>True while the fire is automatic (the burst loop).</summary>
        public bool FiringAutomatic => cadence.Automatic;
        /// <summary>True from <see cref="Reload"/> until its seconds are over or a shot ends it.</summary>
        public bool Reloading => reloadUntil >= 0f && Time.time < reloadUntil;

        /// <summary>
        /// Raised by <see cref="Died"/>, with the world direction the hit pushes (flat unit vector; zero when unknown). The hook for the
        /// ragdoll (ticket 51): until it exists the body holds its last pose.
        /// </summary>
        public event Action<SoldierAnimator, Vector3> WasKilled;

        /// <summary>The direction the last <see cref="Died"/> was pushed.</summary>
        public Vector3 DeathDirection { get; private set; }

        Vector2 tree, treeTarget;
        float playback = 1f, stance, reloadUntil = -1f;
        LocomotionClips.Stance stanceTarget;
        bool dead, ready, keepStateSaved, keepStateBefore;
        HashSet<int> parameters;
        readonly LocomotionSpeedRing[] walkRings = new LocomotionSpeedRing[3], runRings = new LocomotionSpeedRing[3], sprintRings = new LocomotionSpeedRing[3];
        /// <summary>Shots of an automatic weapon this close (seconds) are one burst.</summary>
        const float AutomaticWindow = 0.6f;

        FireCadence cadence;
        float lastShot = -1f, burstHold;
        Coroutine burstWatch;

        CombatTuning CombatSettings => Combat != null ? Combat : (Combat = CombatTuning.LoadDefault());

        /// <summary>
        /// Puts a <see cref="SoldierAnimator"/> on a soldier body (the root of a skin prefab, or anything holding its Animator) and returns
        /// it: the Animator gets <see cref="LocomotionTuning.Controller"/> (Soldier.controller), root motion off. Calling it again on the
        /// same body returns the same component.
        /// </summary>
        public static SoldierAnimator Attach(GameObject body, LocomotionTuning tuning = null)
        {
            if (body == null) return null;
            var animator = body.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = body.AddComponent<Animator>();
            var host = animator.gameObject;
            var soldier = host.GetComponent<SoldierAnimator>();
            if (soldier == null) soldier = host.AddComponent<SoldierAnimator>();
            if (tuning != null) soldier.Tuning = tuning;
            soldier.Body = animator;
            soldier.Setup();
            return soldier;
        }

        void Awake() => Setup();

        void Setup()
        {
            if (Tuning == null) Tuning = LocomotionTuning.LoadDefault();
            if (Body == null) Body = GetComponentInChildren<Animator>(true);
            if (Body == null) return;
            if (Tuning.Controller != null && Body.runtimeAnimatorController != Tuning.Controller)
                Body.runtimeAnimatorController = Tuning.Controller;
            Body.applyRootMotion = false;
            Body.cullingMode = AlwaysAnimate ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms;
            parameters = new HashSet<int>();
            foreach (var p in Body.parameters) parameters.Add(p.nameHash);
            for (var i = 0; i < 3; i++)
            {
                var s = (LocomotionClips.Stance)i;
                walkRings[i] = Tuning.WalkRing(s);
                runRings[i] = Tuning.RunRing(s);
                sprintRings[i] = Tuning.SprintRing(s);
            }
            ready = true;
        }

        // ---------------------------------------------------------------- Locomotion

        /// <summary>The body's velocity in its own frame (x right, y forward, metres per second; <see cref="LocomotionMath.BodyVelocity"/>).</summary>
        public void SetMove(Vector2 localVelocity)
        {
            if (!ready) Setup();
            treeTarget = dead || Tuning == null ? Vector2.zero : LocomotionMath.TreePosition(localVelocity, Tuning.RunSpeed, Tuning.MaxTreeRadius);
            if (treeTarget.magnitude * Tuning.RunSpeed > Tuning.EmoteCancelSpeed) EndEmote();
        }

        /// <summary>The clip set of the weapon in hand.</summary>
        public void SetStance(LocomotionClips.Stance value) => stanceTarget = value;

        /// <summary>A jump starts (the take-off clip); <see cref="SetAirborne"/> keeps it in the fall until the landing.</summary>
        public void Jump()
        {
            if (dead || Body == null) return;
            EndEmote();
            SetTrigger(JumpId);
        }

        /// <summary>Whether the body is off the ground (the fall loop plays, and the landing when it ends).</summary>
        public void SetAirborne(bool value)
        {
            if (!dead) SetBool(AirborneId, value);
        }

        /// <summary>Plays an emote state by name (<c>soldier_emote_dance_1</c>, <c>body_victory</c>); false when the controller has no such state or the body moves.</summary>
        public bool Emote(string key)
        {
            if (dead || Body == null || string.IsNullOrEmpty(key) || !Body.isActiveAndEnabled || Body.runtimeAnimatorController == null) return false;
            if (tree.magnitude * Tuning.RunSpeed > Tuning.EmoteCancelSpeed || treeTarget.magnitude * Tuning.RunSpeed > Tuning.EmoteCancelSpeed) return false;
            var state = EmoteStateId(key);
            if (!Body.HasState(0, state)) return false;
            Body.CrossFadeInFixedTime(state, Tuning.EmoteFadeSeconds, 0);
            return true;
        }

        /// <summary>The full path hash of the emote state for a key.</summary>
        public static int EmoteStateId(string key) => Animator.StringToHash(Names.BaseLayer + "." + Names.EmoteMachine + "." + key);

        /// <summary>Whether an emote plays or fades in on the base layer (not while it fades out).</summary>
        public bool IsEmoting
        {
            get
            {
                if (Body == null || !Body.isActiveAndEnabled || Body.runtimeAnimatorController == null) return false;
                if (Body.IsInTransition(0)) return Body.GetNextAnimatorStateInfo(0).IsTag(Names.EmoteTag);
                return Body.GetCurrentAnimatorStateInfo(0).IsTag(Names.EmoteTag);
            }
        }

        void EndEmote()
        {
            if (IsEmoting) Body.CrossFadeInFixedTime(LocomotionStateId, Tuning.EmoteFadeSeconds, 0);
        }

        // ---------------------------------------------------------------- Upper body

        /// <summary>
        /// One shot. Shots closer than <see cref="CombatTuning.BurstInterval"/> make the fire automatic: the burst clip loops.
        /// <paramref name="automatic"/> (the weapon's own mode: the minigun, the flamethrower, the electro-gun) makes it automatic from the
        /// second shot on whatever the rate (up to <c>AutomaticWindow</c> apart), the loop holding for a gap and a half.
        /// </summary>
        public void Fire(bool automatic = false)
        {
            if (dead) return;
            EndEmote();
            SetTrigger(FireId);
            var c = CombatSettings;
            reloadUntil = -1f;
            var now = Time.time;
            var gap = lastShot >= 0f ? now - lastShot : 0f;
            lastShot = now;
            burstHold = automatic ? Mathf.Max(c.BurstHoldSeconds, Mathf.Min(gap, AutomaticWindow) * 1.5f) : c.BurstHoldSeconds;
            if (!cadence.Shot(now, automatic ? Mathf.Max(AutomaticWindow, c.BurstInterval) : c.BurstInterval)) return;
            // Automatic: the burst loop plays while the bool holds; the single shot is not restarted (its trigger would wait otherwise).
            ResetTrigger(FireId);
            SetBool(FireAutoId, true);
            if (burstWatch == null && isActiveAndEnabled) burstWatch = StartCoroutine(WatchBurst());
        }

        IEnumerator WatchBurst()
        {
            while (!cadence.Tick(Time.time, burstHold))
                yield return null;
            SetBool(FireAutoId, false);
            burstWatch = null;
        }

        void StopAutomatic()
        {
            cadence.Reset();
            lastShot = -1f;
            if (burstWatch != null) { StopCoroutine(burstWatch); burstWatch = null; }
            SetBool(FireAutoId, false);
        }

        public void Reload(float seconds)
        {
            if (dead) return;
            EndEmote();
            var c = CombatSettings;
            StopAutomatic();
            ResetTrigger(FireId);
            SetFloat(ReloadSpeedId, CombatRules.ReloadSpeed(c.ReloadClipSeconds, seconds, c.ReloadMinSpeed, c.ReloadMaxSpeed));
            SetTrigger(ReloadId);
            reloadUntil = Time.time + Mathf.Max(0f, seconds);
        }

        /// <summary>A hit from the world point <paramref name="from"/>: a flinch to the side it came from.</summary>
        public void Hit(Vector3 from)
        {
            if (dead) return;
            var dir = LocomotionMath.DirectionFrom(from, transform.position, transform.rotation);
            SetFloat(HitXId, CombatRules.HitBlend(dir));
            // A flinch does not interrupt a reload (the controller has no Reload to Hit transition): no trigger left to fire late
            if (!Reloading) SetTrigger(HitId);
        }

        // ---------------------------------------------------------------- Death

        /// <summary>
        /// The body died, pushed by the hit along <paramref name="hitDirection"/> (world, any length; zero when unknown). Stops every
        /// animation and holds the last pose (the Animator is turned off), then raises <see cref="WasKilled"/>: ticket 51's ragdoll starts
        /// there and takes the pose over. No death clip is played.
        /// </summary>
        public void Died(Vector3 hitDirection)
        {
            if (dead) return;
            dead = true;
            hitDirection.y = 0f;
            DeathDirection = hitDirection.sqrMagnitude > 1e-6f ? hitDirection.normalized : Vector3.zero;
            treeTarget = Vector2.zero;
            StopAutomatic();
            reloadUntil = -1f;
            SetBool(DeadId, true);
            Freeze();
            WasKilled?.Invoke(this, DeathDirection);
        }

        /// <summary>The pose is evaluated once more (even off screen) and the Animator is turned off: the pose holds.</summary>
        void Freeze()
        {
            if (Body == null || !Body.enabled || !Body.isActiveAndEnabled || Body.runtimeAnimatorController == null) return;
            EvaluateNow();
            if (!keepStateSaved) { keepStateBefore = Body.keepAnimatorStateOnDisable; keepStateSaved = true; }
            Body.keepAnimatorStateOnDisable = true;
            Body.enabled = false;
        }

        void EvaluateNow()
        {
            var mode = Body.cullingMode;
            Body.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Body.Update(0f);
            Body.cullingMode = mode;
        }

        /// <summary>Back to life and standing (a body that comes back after a death, the next round).</summary>
        public void Revive()
        {
            dead = false;
            DeathDirection = Vector3.zero;
            tree = treeTarget = Vector2.zero;
            StopAutomatic();
            reloadUntil = -1f;
            if (Body == null || Body.runtimeAnimatorController == null) return;
            if (keepStateSaved) { Body.keepAnimatorStateOnDisable = keepStateBefore; keepStateSaved = false; }
            if (!Body.gameObject.activeInHierarchy) return;
            Body.enabled = true;
            SetBool(DeadId, false);
            SetBool(AirborneId, false);
            foreach (var id in new[] { FireId, ReloadId, HitId, JumpId }) ResetTrigger(id);
            Body.Play(LocomotionStateId, 0, 0f);
            var upper = Body.GetLayerIndex(Names.UpperBodyLayer);
            if (upper >= 0 && Body.HasState(upper, EmptyStateId)) Body.Play(EmptyStateId, upper, 0f);
            EvaluateNow(); // stand up this frame: a body never shows a frame of its last pose
        }

        void OnDisable()
        {
            burstWatch = null;
            if (cadence.Automatic) { cadence.Reset(); SetBool(FireAutoId, false); }
        }

        // ---------------------------------------------------------------- The frame

        void Update()
        {
            if (!ready || Body == null) return;
            Step(Time.deltaTime);
        }

        /// <summary>One frame of damping and the parameter writes (what Update does; tests call it).</summary>
        public void Step(float dt)
        {
            var t = Tuning;
            tree = LocomotionMath.Damp(tree, treeTarget, t.MoveDampSeconds, dt);
            stance = LocomotionMath.Damp(stance, (float)stanceTarget, t.StanceDampSeconds, dt);
            var i = (int)stanceTarget;
            playback = LocomotionMath.Playback(tree, t.RunSpeed, walkRings[i], runRings[i], sprintRings[i], t.MinPlayback, t.MaxPlayback);
            SetFloat(MoveXId, tree.x);
            SetFloat(MoveYId, tree.y);
            SetFloat(SpeedId, playback);
            SetFloat(StanceId, stance);
        }

        // ---------------------------------------------------------------- Parameter writes (skipped when the controller lacks them)

        bool Has(int id) => parameters != null && parameters.Contains(id);

        bool Live => Body != null && Body.runtimeAnimatorController != null && Body.gameObject.activeInHierarchy;

        void SetFloat(int id, float v) { if (Live && Has(id)) Body.SetFloat(id, v); }
        void SetBool(int id, bool v) { if (Live && Has(id)) Body.SetBool(id, v); }
        void SetTrigger(int id) { if (Live && Has(id)) Body.SetTrigger(id); }
        void ResetTrigger(int id) { if (Live && Has(id)) Body.ResetTrigger(id); }
    }
}
