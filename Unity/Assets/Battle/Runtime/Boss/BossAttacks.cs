using System;
using System.Collections.Generic;
using SdfPrime;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// Chanterelle Prime's attacks in the world (ticket 33.6): what the planner (<see cref="BossAttackPlanner"/>) decides, done and shown.
    /// Each attack has the three cues of its wind-up at once, which are drawn from here: a <b>body pose</b> and an <b>eye glow</b>
    /// (<see cref="PrimeBody"/>) and a <b>ground marker</b> where it lands (<see cref="Markers"/>, drawn by <see cref="BossTelegraphs"/> and
    /// stepped out of by the bots); then the effect, with the damage through <see cref="Combat.Hurt"/> as <c>Damage.ByMachine("Chanterelle
    /// Prime", ...)</c>, so the kill feed and the death screen name him.
    /// <list type="bullet">
    /// <item><b>Spore Mortar</b>: three balls lobbed at 42 and up to two red bots (rings follow them for the first part of the wind-up, then
    /// stand); each does its damage within 3 m where it lands, then a poison cloud (22's <see cref="Area"/>).</item>
    /// <item><b>Royal Glare</b>: a thin aim line from both eyes, then a beam that follows its target at 25 degrees a second, stopped by whatever
    /// is solid, 30 a second on whoever it touches.</item>
    /// <item><b>Cap Cannon</b>: the Prime-copter turns its nose to the target and fires twelve hitscan shots over two seconds, 3 degrees off.</item>
    /// <item><b>Downdraft Dive</b> and <b>Tank Drop</b>: the Prime-copter's (<see cref="PrimeCopter.StartDive"/>, <see cref="PrimeCopter.DropTank"/>).</item>
    /// <item><b>Cap Slam</b>: a ring runs out from his feet at 12 m/s to 20 m, 0.6 m high: it hurts and throws whoever is on the ground when it
    /// passes; a jump clears it. Then the cap is stuck (the state's window).</item>
    /// <item><b>Hand Grab</b>: a hand hovers over the target with a shadow under it, then grabs whoever stands in the shadow.</item>
    /// </list>
    /// </summary>
    public sealed class BossAttacks
    {
        /// <summary>A circle on the floor an attack is about to hit.</summary>
        public struct Marker
        {
            public Vector3 centre;
            public float radius;
            public Color colour;
            /// <summary>0 to 1 through the wind-up (the ring pulses faster, an inner ring grows to the outer); 1 once it has landed.</summary>
            public float progress;
            /// <summary>Whether bots step out of it.</summary>
            public bool danger;
        }

        /// <summary>A spore ball in flight.</summary>
        public struct Ball
        {
            public Vector3 from, to;
            public float age, flight;
        }

        /// <summary>A cannon shot's streak, fading.</summary>
        public struct Tracer
        {
            public Vector3 from, to;
            public float life;
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private static readonly Comparison<RaycastHit> ByDistance = (a, b) => a.distance.CompareTo(b.distance);

        private static readonly Color Green = new Color(0.45f, 0.9f, 0.25f);
        private static readonly Color Gold = new Color(1f, 0.78f, 0.2f);
        private static readonly Color Ice = new Color(0.65f, 0.9f, 1f);
        private static readonly Color Red = new Color(1f, 0.35f, 0.25f);
        private static readonly Color Orange = new Color(1f, 0.55f, 0.15f);
        private static readonly Color Blue = new Color(0.4f, 0.75f, 1f);

        private readonly Boss boss;
        private readonly List<Vector3> points = new List<Vector3>();
        private BossAttack current;
        private float stageClock;
        private Vector3 hoverPoint;
        private float cannonLeft;
        private int shotsFired;
        private Vector3 glareDirection;
        private float glareTick;
        private float shockPrevious;
        private bool diveStarted;

        public BossAttacks(Boss owner)
        {
            boss = owner;
        }

        /// <summary>The markers to draw and step out of, rebuilt every frame.</summary>
        public List<Marker> Markers { get; } = new List<Marker>();

        public List<Ball> Balls { get; } = new List<Ball>();

        public List<Tracer> Tracers { get; } = new List<Tracer>();

        /// <summary>The glare is drawn: the thin aim line of the wind-up (<see cref="GlareThin"/>) or the beam. Its target point.</summary>
        public bool GlareOn { get; private set; }

        public bool GlareThin { get; private set; }

        public Vector3 GlareEnd { get; private set; }

        /// <summary>The slam's ring: its middle on the floor and its radius now; negative when there is none.</summary>
        public Vector3 ShockCentre { get; private set; }

        public float ShockRadius { get; private set; } = -1;

        /// <summary>The tank drop's hatch shows its blue light.</summary>
        public bool HatchGlow { get; private set; }

        /// <summary>The cannon's muzzle glows.</summary>
        public bool MuzzleGlow { get; private set; }

        /// <summary>The attack in progress, or null.</summary>
        public BossAttack Current => current;

        private BossRules Rules => boss.Rules;

        private BossState State => boss.State;

        private PrimeBody Body => boss.Body;

        // ---------------------------------------------------------------- The planner's events

        /// <summary>An attack began its wind-up.</summary>
        public void Started(BossAttack attack)
        {
            current = attack;
            stageClock = 0;
            points.Clear();
            diveStarted = false;
            hoverPoint = default;
            switch (attack.Kind)
            {
                case BossAttackKind.SporeMortar:
                    foreach (var target in attack.Targets)
                        points.Add(FloorOf(boss.Match.PlayerOf(target)));
                    Body.Glow(Green, 1);
                    break;
                case BossAttackKind.RoyalGlare:
                    points.Add(FloorOf(boss.Match.PlayerOf(attack.Target)));
                    glareDirection = (ChestOf(boss.Match.PlayerOf(attack.Target)) - Body.EyesCentre).normalized;
                    Body.Glow(Ice, 1);
                    break;
                case BossAttackKind.CapCannon:
                    points.Add(FloorOf(boss.Match.PlayerOf(attack.Target)));
                    Body.Glow(Red, 1);
                    break;
                case BossAttackKind.DowndraftDive:
                    StartDive(attack);
                    break;
                case BossAttackKind.TankDrop:
                    HatchGlow = true;
                    Body.Glow(Blue, 0.7f);
                    if (boss.Copter != null && boss.Copter.Active && BossArena.GroundAt(boss.Copter.HatchPosition, out var ground))
                        points.Add(ground);
                    break;
                case BossAttackKind.CapSlam:
                    points.Add(Body.transform.position);
                    Body.Glow(Gold, 1);
                    break;
                case BossAttackKind.HandGrab:
                    points.Add(FloorOf(boss.Match.PlayerOf(attack.Target)));
                    Body.Glow(Gold, 0.6f);
                    break;
            }
        }

        /// <summary>An attack went to its next stage.</summary>
        public void StageChanged(BossAttack attack)
        {
            stageClock = 0;
            if (attack.Stage == BossAttackStage.Active)
                Activate(attack);
            else if (attack.Stage == BossAttackStage.Recover)
                Recover(attack);
        }

        /// <summary>An attack ended after its recovery.</summary>
        public void Finished(BossAttack attack) => Clear(attack);

        /// <summary>An attack was dropped halfway (Prime was stunned, the phase changed).</summary>
        public void Cancelled(BossAttack attack) => Clear(attack);

        private void Clear(BossAttack attack)
        {
            if (current == attack)
                current = null;
            points.Clear();
            GlareOn = false;
            HatchGlow = false;
            MuzzleGlow = false;
            ShockRadius = -1;
            Balls.Clear();
            Body.Glow(Color.white, 0);
            Body.ReachHand(0, null);
            Body.ReachHand(1, null);
            Body.LookAt(null);
            if (boss.Copter != null)
                boss.Copter.FaceTarget = null;
            if (attack.Kind == BossAttackKind.CapSlam)
                Body.ShutEyes(false);
        }

        // ---------------------------------------------------------------- The stages

        private void Activate(BossAttack attack)
        {
            var spec = attack.Spec;
            switch (attack.Kind)
            {
                case BossAttackKind.SporeMortar:
                    for (var i = 0; i < points.Count; i++)
                        Balls.Add(new Ball { from = Body.HandPosition(i % 2) + Vector3.up * 0.5f, to = points[i], flight = Mathf.Max(0.3f, spec.active * 0.9f) });
                    break;
                case BossAttackKind.RoyalGlare:
                    GlareThin = false;
                    glareTick = 0;
                    break;
                case BossAttackKind.CapCannon:
                    cannonLeft = 0;
                    shotsFired = 0;
                    MuzzleGlow = true;
                    break;
                case BossAttackKind.TankDrop:
                    HatchGlow = false;
                    boss.Copter?.DropTank();
                    break;
                case BossAttackKind.CapSlam:
                    Body.SetAct(PrimeAct.SlamHit);
                    ShockCentre = Body.transform.position;
                    ShockRadius = 1;
                    shockPrevious = 0;
                    boss.Shake(0.5f, ShockCentre, 40);
                    boss.Arena?.Cheers(0.6f);
                    Dust(ShockCentre, 14);
                    break;
                case BossAttackKind.HandGrab:
                    // The hand comes down on the shadow
                    if (points.Count > 0)
                        hoverPoint = points[0] + Vector3.up * 1.2f;
                    Grab(attack);
                    break;
            }
        }

        private void Recover(BossAttack attack)
        {
            switch (attack.Kind)
            {
                case BossAttackKind.CapSlam:
                    Body.SetAct(PrimeAct.Stuck);
                    break;
                case BossAttackKind.HandGrab:
                    Body.ReachHand(attack.Hand, null);
                    break;
                case BossAttackKind.RoyalGlare:
                    GlareOn = false;
                    break;
                case BossAttackKind.CapCannon:
                    MuzzleGlow = false;
                    if (boss.Copter != null)
                        boss.Copter.FaceTarget = null;
                    break;
            }
        }

        // ---------------------------------------------------------------- Every frame

        /// <summary>Lets <paramref name="dt"/> seconds pass: the current attack's cues and effects, the balls and tracers in flight, and the markers' list.</summary>
        public void Update(float dt)
        {
            Markers.Clear();
            for (var i = Tracers.Count - 1; i >= 0; i--)
            {
                var tracer = Tracers[i];
                tracer.life -= dt;
                if (tracer.life <= 0)
                    Tracers.RemoveAt(i);
                else
                    Tracers[i] = tracer;
            }
            UpdateBalls(dt);
            if (State.Copter == CopterStage.Crashing)
                AddMarker(boss.Spots.crash.position, Rules.crashRadius, Red, 1 - State.CrashLeft / Mathf.Max(0.1f, Rules.crashSeconds), true);

            var attack = current;
            if (attack == null)
                return;
            stageClock += dt;
            switch (attack.Kind)
            {
                case BossAttackKind.SporeMortar:
                    UpdateMortar(attack);
                    break;
                case BossAttackKind.RoyalGlare:
                    UpdateGlare(attack, dt);
                    break;
                case BossAttackKind.CapCannon:
                    UpdateCannon(attack, dt);
                    break;
                case BossAttackKind.DowndraftDive:
                    UpdateDive(attack);
                    break;
                case BossAttackKind.TankDrop:
                    if (points.Count > 0 && attack.Stage == BossAttackStage.WindUp)
                        AddMarker(points[0], 2.5f, Blue, attack.WindUpProgress, false);
                    break;
                case BossAttackKind.CapSlam:
                    UpdateSlam(attack, dt);
                    break;
                case BossAttackKind.HandGrab:
                    UpdateGrab(attack, dt);
                    break;
            }
        }

        // The pose of a wind-up follows how far through it the attack is
        private void Pose(BossAttack attack, PrimeAct act)
        {
            if (attack.Stage == BossAttackStage.WindUp)
                Body.SetAct(act, attack.WindUpProgress);
        }

        // ---------------------------------------------------------------- Spore Mortar

        private void UpdateMortar(BossAttack attack)
        {
            Pose(attack, PrimeAct.Mortar);
            if (attack.Stage == BossAttackStage.Recover || attack.Stage == BossAttackStage.Ready)
            {
                if (Balls.Count == 0 && attack.Stage == BossAttackStage.Recover)
                    Body.SetAct(PrimeAct.Idle);
                return;
            }
            // The rings follow their targets for the first part of the wind-up, then stand: the time to run
            if (attack.Stage == BossAttackStage.WindUp && attack.WindUpProgress < 0.55f)
            {
                for (var i = 0; i < points.Count && i < attack.Targets.Count; i++)
                {
                    var player = boss.Match.PlayerOf(attack.Targets[i]);
                    if (player != null && attack.Targets[i].Alive)
                        points[i] = FloorOf(player);
                }
            }
            if (attack.Stage == BossAttackStage.WindUp || Balls.Count > 0)
                for (var i = 0; i < points.Count; i++)
                    AddMarker(points[i], 3, Green, attack.Stage == BossAttackStage.WindUp ? attack.WindUpProgress : 1, true);
        }

        private void UpdateBalls(float dt)
        {
            for (var i = Balls.Count - 1; i >= 0; i--)
            {
                var ball = Balls[i];
                ball.age += dt;
                if (ball.age >= ball.flight)
                {
                    Balls.RemoveAt(i);
                    Land(ball.to);
                    continue;
                }
                Balls[i] = ball;
            }
        }

        // The ball's hit within 3 m, then the cloud (22's Area, recoloured by CombatFx's own green)
        private void Land(Vector3 at)
        {
            var spec = Rules.SpecOf(BossAttackKind.SporeMortar);
            foreach (var player in boss.RedPlayers())
            {
                var flat = player.transform.position - at;
                flat.y = 0;
                if (flat.magnitude <= 3)
                    boss.Hurt(player, spec.damage, at + Vector3.up * 3);
            }
            if (boss.Combat != null)
            {
                var cloud = Damage.ByMachine(Boss.Name, Team.Blue, "cannon", Rules.mortarCloudPerSecond * Rules.damageScale, at);
                boss.Combat.State.AddArea(new Area(FireMode.Cloud, at + Vector3.up * 0.9f, 3, Rules.mortarCloudSeconds, cloud, 0.25f, 3));
            }
            Dust(at, 6);
        }

        // ---------------------------------------------------------------- Royal Glare

        private void UpdateGlare(BossAttack attack, float dt)
        {
            var target = boss.Match.PlayerOf(attack.Target);
            if (target == null || !attack.Target.Alive)
            {
                GlareOn = false;
                return;
            }
            var eyes = Body.EyesCentre;
            var chest = ChestOf(target);
            Body.LookAt(chest);
            switch (attack.Stage)
            {
                case BossAttackStage.WindUp:
                    Pose(attack, PrimeAct.Glare);
                    GlareOn = true;
                    GlareThin = true;
                    glareDirection = (chest - eyes).normalized;
                    GlareEnd = Trace(eyes, glareDirection, 120, out _);
                    AddMarker(FloorOf(target), 1.2f, Ice, attack.WindUpProgress, false);
                    break;
                case BossAttackStage.Active:
                    GlareOn = true;
                    GlareThin = false;
                    // Follows at its turn rate: sidestep to outrun it
                    var wanted = (chest - eyes).normalized;
                    glareDirection = Vector3.RotateTowards(glareDirection, wanted, Rules.glareTurnRate * Mathf.Deg2Rad * dt, 0).normalized;
                    GlareEnd = Trace(eyes, glareDirection, 120, out var hit);
                    glareTick += dt;
                    if (glareTick >= 0.1f)
                    {
                        var seconds = glareTick;
                        glareTick = 0;
                        if (hit != null && hit.Team != Team.Blue)
                            boss.HurtOverTime(hit, attack.Spec.damage * seconds, eyes);
                    }
                    break;
                default:
                    GlareOn = false;
                    break;
            }
        }

        /// <summary>
        /// A ray from Prime's eyes: stopped by whatever is solid (the pillars, the shelters, the walls), not by him, the copter or the blue
        /// crew. Returns where it ends; <paramref name="player"/> is the red player it ends on, if any.
        /// </summary>
        private Vector3 Trace(Vector3 origin, Vector3 direction, float range, out Player player)
        {
            player = null;
            var count = Physics.RaycastNonAlloc(origin, direction, Hits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(Hits, 0, count, Comparer<RaycastHit>.Create(ByDistance));
            for (var i = 0; i < count; i++)
            {
                var collider = Hits[i].collider;
                if (Passes(collider))
                    continue;
                player = Player.Of(collider);
                return Hits[i].point;
            }
            return origin + direction * range;
        }

        /// <summary>Whether nothing solid but the blue side's own stands between <paramref name="from"/> and the player's chest: Prime sees them.</summary>
        public static bool InSight(Vector3 from, Player player)
        {
            var chest = ChestOf(player);
            var along = chest - from;
            var distance = along.magnitude;
            if (distance < 0.1f)
                return true;
            var count = Physics.RaycastNonAlloc(from, along / distance, Hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var collider = Hits[i].collider;
                if (!Passes(collider) && Player.Of(collider) != player)
                    return false;
            }
            return true;
        }

        /// <summary>A shot or beam of Prime's goes through: his own parts, the copter, the blue crew, the dead and the machines on his side.</summary>
        public static bool Passes(Collider collider)
        {
            if (collider.GetComponentInParent<PrimeRig>() != null || collider.GetComponentInParent<PrimeHit>() != null || PrimeCopter.Owns(collider))
                return true;
            var player = Player.Of(collider);
            if (player != null)
                return player.State == null || !player.State.Alive || player.Team == Team.Blue;
            var machine = Machines.Of(collider);
            return machine != null && machine.Team == Team.Blue;
        }

        // ---------------------------------------------------------------- Cap Cannon

        private void UpdateCannon(BossAttack attack, float dt)
        {
            var copter = boss.Copter;
            var target = boss.Match.PlayerOf(attack.Target);
            if (copter == null || !copter.Active || target == null || !attack.Target.Alive)
                return;
            copter.FaceTarget = ChestOf(target);
            Body.LookAt(ChestOf(target));
            if (attack.Stage == BossAttackStage.WindUp)
            {
                MuzzleGlow = attack.WindUpProgress > 0.3f;
                AddMarker(FloorOf(target), 1.5f, Red, attack.WindUpProgress, false);
                return;
            }
            if (attack.Stage != BossAttackStage.Active)
                return;
            // Twelve shots over the stage, 3 degrees of error
            var spec = attack.Spec;
            var every = spec.active / Mathf.Max(1, spec.count);
            cannonLeft -= dt;
            while (cannonLeft <= 0 && shotsFired < spec.count)
            {
                cannonLeft += every;
                shotsFired++;
                var from = copter.MuzzlePosition;
                var direction = Combat.Spread((ChestOf(target) - from).normalized, 3);
                var end = Trace(from, direction, 160, out var hit);
                Tracers.Add(new Tracer { from = from, to = end, life = 0.12f });
                if (hit != null && hit.Team != Team.Blue)
                    boss.Hurt(hit, spec.damage, from);
            }
        }

        // ---------------------------------------------------------------- Downdraft Dive

        private void StartDive(BossAttack attack)
        {
            var copter = boss.Copter;
            var target = boss.Match.PlayerOf(attack.Target);
            if (copter == null || !copter.Active || target == null)
            {
                boss.Planner.Cancel();
                return;
            }
            var floor = DivePoint(FloorOf(target));
            points.Add(floor);
            diveStarted = copter.StartDive(floor, attack.Spec);
            if (!diveStarted)
                boss.Planner.Cancel();
            else
                Body.Glow(Orange, 0.8f);
        }

        // The dive's circle is never within 6 m of a shelter or a pillar: the copter bottoms out at 6 m up. Slides the point off cover,
        // round the target in rings of 7 and 14 m; the target's own spot when nothing is clear
        private Vector3 DivePoint(Vector3 floor)
        {
            var radius = Rules.diveRadius;
            if (!Blocked(floor, radius))
                return floor;
            for (var ring = 1; ring <= 2; ring++)
            {
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * 45f * Mathf.Deg2Rad;
                    var probe = floor + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (radius * 1.15f * ring);
                    if (Machines.PlayAreaContains(probe, radius) && !Blocked(probe, radius))
                        return probe;
                }
            }
            return floor;
        }

        // Anything solid between 2.5 and 5.5 m over the floor within the circle's square: a shelter's cap, a pillar, a box's wall. The
        // floor itself is flat in the play area, so it never reaches up that far
        private static bool Blocked(Vector3 floor, float radius)
        {
            var count = Physics.OverlapBoxNonAlloc(floor + Vector3.up * 4, new Vector3(radius, 1.5f, radius), Overlaps, Quaternion.identity, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var collider = Overlaps[i];
                if (Player.Of(collider) == null && Machines.Of(collider) == null && !PrimeCopter.Owns(collider) && collider.GetComponentInParent<PrimeRig>() == null &&
                    collider.GetComponentInParent<PrimeHit>() == null)
                    return true;
            }
            return false;
        }

        private static readonly Collider[] Overlaps = new Collider[48];

        private void UpdateDive(BossAttack attack)
        {
            if (points.Count == 0)
                return;
            if (attack.Stage == BossAttackStage.WindUp)
                AddMarker(points[0], Rules.diveRadius, Orange, attack.WindUpProgress, true);
            else if (attack.Stage == BossAttackStage.Active)
                AddMarker(points[0], Rules.diveRadius, Orange, 1, true);
        }

        // ---------------------------------------------------------------- Cap Slam

        private void UpdateSlam(BossAttack attack, float dt)
        {
            var feet = Body.transform.position;
            Body.ShutEyes(attack.Stage == BossAttackStage.WindUp);
            switch (attack.Stage)
            {
                case BossAttackStage.WindUp:
                    Pose(attack, PrimeAct.SlamWind);
                    // A 14 m gold ring round him: the slam's wind-up, and a hint of the 20 m it reaches
                    AddMarker(feet, 14, Gold, attack.WindUpProgress, true);
                    break;
                case BossAttackStage.Active:
                    var speed = attack.Spec.count / Mathf.Max(0.1f, attack.Spec.active);
                    var radius = Mathf.Min(attack.Spec.count, ShockRadius + speed * dt);
                    SlamRing(attack, shockPrevious, radius);
                    shockPrevious = radius;
                    ShockRadius = radius;
                    EmitRingDust(radius);
                    break;
            }
        }

        // Whoever the ring passed this frame: on the ground (not in the air, and under the ring's 0.6 m) they are hurt and thrown
        private void SlamRing(BossAttack attack, float from, float to)
        {
            foreach (var player in boss.RedPlayers())
            {
                var flat = player.transform.position - ShockCentre;
                var floorY = ShockCentre.y;
                flat.y = 0;
                var distance = flat.magnitude;
                if (distance <= from || distance > to + 0.4f)
                    continue;
                var grounded = player.Controller.IsGrounded && player.transform.position.y - floorY < 0.6f;
                var damage = Rules.DamageTo(BossAttackKind.CapSlam, grounded, State.SoreLoser);
                if (damage <= 0)
                    continue;
                boss.Hurt(player, damage, ShockCentre + Vector3.up);
                player.Controller.Shove(flat, attack.Spec.push);
            }
        }

        // ---------------------------------------------------------------- Hand Grab

        private void UpdateGrab(BossAttack attack, float dt)
        {
            if (points.Count == 0)
                return;
            var hand = attack.Hand;
            if (attack.Stage == BossAttackStage.WindUp)
            {
                Pose(attack, PrimeAct.Grab);
                // The shadow follows its target for the first half
                if (attack.WindUpProgress < 0.5f)
                {
                    var player = boss.Match.PlayerOf(attack.Target);
                    if (player != null && attack.Target.Alive)
                        points[0] = FloorOf(player);
                }
                hoverPoint = points[0] + Vector3.up * 4.5f;
                Body.ReachHand(hand, hoverPoint);
                AddMarker(points[0], 1.5f, Gold, attack.WindUpProgress, true);
            }
            else if (attack.Stage == BossAttackStage.Active)
            {
                Body.ReachHand(hand, hoverPoint);
                AddMarker(points[0], 1.5f, Gold, 1, true);
            }
        }

        private void Grab(BossAttack attack)
        {
            if (points.Count == 0)
                return;
            var spec = attack.Spec;
            foreach (var player in boss.RedPlayers())
            {
                var flat = player.transform.position - points[0];
                flat.y = 0;
                if (flat.magnitude > 1.5f)
                    continue;
                boss.Hurt(player, spec.damage, points[0] + Vector3.up * 3);
                var away = player.transform.position - Body.transform.position;
                player.Controller.Shove(away, spec.push);
            }
            boss.Shake(0.25f, points[0], 25);
            Dust(points[0], 6);
        }

        // ---------------------------------------------------------------- Helpers

        private void AddMarker(Vector3 centre, float radius, Color colour, float progress, bool danger)
        {
            // On the floor under the centre
            if (BossArena.GroundAt(centre + Vector3.up * 30, out var ground))
                centre.y = ground.y;
            Markers.Add(new Marker { centre = centre, radius = radius, colour = colour, progress = progress, danger = danger });
        }

        private static Vector3 FloorOf(Player player) => player != null ? player.transform.position : Vector3.zero;

        private static Vector3 ChestOf(Player player) => player != null ? player.transform.position + Vector3.up * 1.15f : Vector3.zero;

        private void Dust(Vector3 at, int puffs)
        {
            var particles = boss.Particles;
            if (particles == null)
                return;
            var band = new Color(0.88f, 0.82f, 0.7f);
            var core = new Color(0.5f, 0.45f, 0.35f);
            for (var i = 0; i < puffs; i++)
            {
                var direction = new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(0.1f, 0.6f), UnityEngine.Random.Range(-1f, 1f)).normalized;
                particles.Emit(SdfEffects.FxParticles.Kind.Puff, at + Vector3.up * 0.3f, direction * UnityEngine.Random.Range(2f, 6f), UnityEngine.Random.Range(0.6f, 1.1f),
                    UnityEngine.Random.Range(0.3f, 0.6f), band, core, gravity: -0.4f, drag: 3);
            }
        }

        // A few puffs along the shockwave's front each frame
        private void EmitRingDust(float radius)
        {
            var particles = boss.Particles;
            if (particles == null)
                return;
            var band = new Color(0.88f, 0.82f, 0.7f);
            var core = new Color(0.5f, 0.45f, 0.35f);
            for (var i = 0; i < 5; i++)
            {
                var angle = UnityEngine.Random.value * Mathf.PI * 2;
                var outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                particles.Emit(SdfEffects.FxParticles.Kind.Puff, ShockCentre + outward * radius + Vector3.up * 0.4f, outward * 2 + Vector3.up * 0.8f, 0.7f,
                    UnityEngine.Random.Range(0.3f, 0.55f), band, core, gravity: -0.3f, drag: 3);
            }
        }
    }
}
