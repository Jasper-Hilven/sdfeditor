using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// One bot's brain (ticket 23), on a bot <see cref="Player"/>, added by <see cref="Bots"/>. It drives the same API your input does:
    /// the <see cref="SdfLevel.FirstPersonController"/> (<c>SetMove</c>, <c>SetAim</c>, <c>Jump</c>), so it walks, turns and jumps by
    /// your rules, and the <see cref="Arsenal"/> (<c>SetTrigger</c>, <c>SetZoom</c>, <c>Select</c>), so it fires by them.
    /// <list type="bullet">
    /// <item>Goal: from its <see cref="Temperament"/> and the situation (<see cref="BotBrain"/>), again every few seconds and on events:
    /// its flag taken, a flag dropped or home, the carrier seen, low health, the goal reached or gone (an emptied pod).</item>
    /// <item>Way: the corners of its path on the navmesh (<see cref="BotNavigation"/>), found anew every one to three seconds or when the
    /// goal moves; stuck (barely moving while it walks) it jumps, then steps aside, then finds a new path, then a new goal.</item>
    /// <item>Sight: enemies in its view cone and line of sight (or close enough to hear), the last seen place remembered for a few
    /// seconds; hurt, it turns to where the shot came from. It fights whatever its goal, the carrier of its flag first. With no player
    /// to fight it shoots at an enemy machine (ticket 24) close by in its way, but never makes one its goal.</item>
    /// <item>Aim: the reaction time and the shrinking aim error of <see cref="BotAim"/>, a turn speed, a lead and the arc for lobbed
    /// shots (<see cref="Ballistics"/>); it fires once its view is on where it means to aim.</item>
    /// <item>Weapon: the held one that suits the distance (<see cref="WeaponChoice"/>), the sniper zoomed.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class Bot : MonoBehaviour
    {
        [Tooltip("The kind of player it leans to be")]
        public BotStyle style;
        [Tooltip("Its weights for the goals")]
        public Temperament temperament;
        [Tooltip("What it is doing now (shown for debugging)")]
        public string goalNow;

        private const float SeenRecently = 0.35f;

        private Bots owner;
        private BotSkill botSkill;
        private Player player;
        private Arsenal arsenal;
        private System.Random random;
        private BotAim aim;
        private BotMemory memory;
        private readonly BotSituation situation = new BotSituation();
        private BotDecision decision;

        private readonly List<Vector3> corners = new List<Vector3>();
        private int corner;
        private Vector3 destination;
        private Vector3 pathTarget;
        private bool hasPath;
        private float repathIn;

        private float thinkIn;
        private float senseIn;
        private float weaponIn;
        private float lastSwitch = -10;

        private Player target;
        // Ticket 24: an enemy machine in its way, shot at while no player is in sight; never a goal
        private Machine machineTarget;
        private float machineSeenAt = float.NegativeInfinity;
        private Vector3 lookAt;
        private float lookAtUntil;
        private float strafeSign = 1;
        private float strafeIn;

        private Vector3 lastSample;
        private float sampleIn;
        private int stuck;
        private Vector3 sidestep;
        private float sidestepLeft;

        private Vector3 guardPoint;
        private float guardIn;
        private Vector3 roamPoint;
        private bool roaming;

        private float yaw;
        private float pitch;
        private Vector3 desiredAim;
        private bool aimed;

        public Team Team => player.Team;

        public Player Player => player;

        public BotDecision Decision => decision;

        public Player Target => target;

        public IReadOnlyList<Vector3> Corners => corners;

        /// <summary>Aims with another skill from now on (RETRY EASIER, ticket 32).</summary>
        public void UseSkill(BotSkill value)
        {
            botSkill = value;
            aim = new BotAim(botSkill, random);
        }

        /// <summary>Ties the brain to its body; <see cref="Bots"/> calls it once.</summary>
        public void Bind(Bots bots, Player body, BotStyle botStyle, Temperament botTemperament, int seed)
        {
            owner = bots;
            player = body;
            arsenal = body.GetComponent<Arsenal>();
            style = botStyle;
            temperament = botTemperament;
            random = new System.Random(seed);
            botSkill = bots.SkillOf(body.Team);
            aim = new BotAim(botSkill, random);
            memory = new BotMemory(bots.Match.Players.Count, botSkill.MemorySeconds);
            OnSpawned();
        }

        /// <summary>A new life: no goal, no path, no target, nothing remembered.</summary>
        public void OnSpawned()
        {
            decision = new BotDecision(BotGoal.None);
            goalNow = "";
            corners.Clear();
            hasPath = false;
            target = null;
            machineTarget = null;
            aim?.Lose();
            memory?.Forget();
            thinkIn = (float)random.NextDouble() * 0.3f;
            senseIn = 0;
            stuck = 0;
            sidestepLeft = 0;
            roaming = false;
            guardIn = 0;
            lookAtUntil = 0;
            yaw = player.Controller.Yaw;
            pitch = 0;
            lastSample = transform.position;
            sampleIn = 0.5f;
            arsenal?.SetTrigger(false);
            arsenal?.SetZoom(false);
        }

        /// <summary>Thinks again within <paramref name="seconds"/> (an event: a flag taken, dropped, home).</summary>
        public void Rethink(float seconds)
        {
            var moment = seconds * (0.6f + 0.8f * (float)random.NextDouble());
            thinkIn = Mathf.Min(thinkIn, moment);
        }

        /// <summary>Hurt: it turns to where the shot came from (the shooter remembered there), and thinks again when low.</summary>
        public void OnHurt(Damage damage, float amount)
        {
            if (!player.State.Alive)
                return;
            var attacker = damage.Attacker != null ? owner.Match.PlayerOf(damage.Attacker) : null;
            if (attacker != null && !damage.OverTime)
            {
                memory.Seen(damage.Attacker.Index, attacker.transform.position, Time.time);
                lookAt = attacker.transform.position + Vector3.up * 1.2f;
                lookAtUntil = Time.time + 1.5f;
            }
            else if (damage.From.HasValue && !damage.OverTime)
            {
                lookAt = damage.From.Value;
                lookAtUntil = Time.time + 1.2f;
            }
            if (player.State.Health < BotBrain.LowHealth && player.State.Health + amount >= BotBrain.LowHealth)
                Rethink(0.3f);
        }

        // ---------------------------------------------------------------- Every frame

        private void Update()
        {
            if (owner == null || player.State == null)
                return;
            var controller = player.Controller;
            if (!player.State.Alive || !controller.enabled)
            {
                arsenal?.SetTrigger(false);
                return;
            }
            var dt = Time.deltaTime;
            var now = Time.time;

            senseIn -= dt;
            if (senseIn <= 0)
            {
                Sense(now);
                senseIn = 0.15f + 0.08f * (float)random.NextDouble();
            }
            thinkIn -= dt;
            if (thinkIn <= 0 || decision.goal == BotGoal.None)
                Think();
            if (!UpdateDestination(now))
                Think();
            UpdatePath(dt);
            ChooseWeapon(dt, now);
            Aim(dt, now);
            Steer(dt);
            Fire(now);
        }

        // ---------------------------------------------------------------- Sight

        private void Sense(float now)
        {
            var eye = arsenal != null ? arsenal.EyePosition : transform.position + Vector3.up * 1.5f;
            var forward = player.Controller.AimDirection;
            var skill = botSkill;
            var ownFlag = owner.Flags != null ? owner.Flags.State.Flag(Team) : null;
            Player best = null;
            var bestScore = float.PositiveInfinity;
            foreach (var other in owner.Match.Players)
            {
                if (other == player || other.State == null || !other.State.Alive || other.Team == Team)
                    continue;
                var point = other.transform.position + Vector3.up * 1.2f;
                // One already in its sights stays seen while it keeps line of sight, whichever way it turned
                var tracked = other == target && memory.SinceSeen(other.State.Index, now) < 1;
                if (!tracked && !BotSight.InView(eye, forward, point, skill))
                    continue;
                if (!owner.LineOfSight(eye, point))
                    continue;
                memory.Seen(other.State.Index, other.transform.position, now);
                var score = Vector3.Distance(eye, point);
                if (ownFlag != null && ownFlag.Carrier == other.State)
                    score -= 40;
                if (other == target)
                    score -= 8;
                score += other.State.Health * 0.04f;
                if (score < bestScore)
                    (best, bestScore) = (other, score);
            }

            if (best != null)
            {
                var carrierSeen = ownFlag != null && ownFlag.Carrier == best.State;
                if (carrierSeen && (target != best || decision.goal != BotGoal.ChaseCarrier))
                    Rethink(0.25f);
                target = best;
                aim.Acquire(best.State.Index);
            }
            else if (target != null && (!target.State.Alive || memory.SinceSeen(target.State.Index, now) > 1.2f))
            {
                target = null;
                aim.Lose();
            }
            SenseMachine(eye, forward, now);
        }

        /// <summary>The aim's ids for machines, past every roster index.</summary>
        private const int MachineAimId = 10000;

        /// <summary>Metres within which it shoots at an enemy machine in its way.</summary>
        private const float MachineReach = 25;

        /// <summary>
        /// Ticket 24: with no player to fight, the nearest enemy machine within <see cref="MachineReach"/>, in its view and line of sight,
        /// to shoot at on its way; it never walks to one, its goals stay the flags, players and pods.
        /// </summary>
        private void SenseMachine(Vector3 eye, Vector3 forward, float now)
        {
            var machines = Machines.Current;
            if (target != null || machines == null)
            {
                machineTarget = null;
                return;
            }
            Machine best = null;
            var bestDistance = MachineReach;
            foreach (var machine in machines.All)
            {
                if (!machine.Alive || machine.Team == Team)
                    continue;
                var centre = machine.Center;
                var distance = Vector3.Distance(eye, centre);
                if (distance >= bestDistance || (machine != machineTarget && !BotSight.InView(eye, forward, centre, botSkill)))
                    continue;
                if (machines.Visible(eye, machine))
                    (best, bestDistance) = (machine, distance);
            }
            if (best != null)
            {
                machineTarget = best;
                machineSeenAt = now;
                aim.Acquire(MachineAimId + best.State.Index);
            }
            else if (machineTarget != null && (!machineTarget.Alive || now - machineSeenAt > 1))
            {
                machineTarget = null;
                if (aim.Target >= MachineAimId)
                    aim.Lose();
            }
        }

        private bool MachineInSight(float now) => target == null && machineTarget != null && machineTarget.Alive && now - machineSeenAt <= SeenRecently;

        private bool TargetInSight(float now) =>
            target != null && target.State.Alive && memory.SinceSeen(target.State.Index, now) <= SeenRecently;

        // ---------------------------------------------------------------- Goals

        private void Think()
        {
            FillSituation();
            var before = decision;
            decision = BotBrain.Choose(Effective(), situation, decision, random);
            thinkIn = 2.5f + 2 * (float)random.NextDouble();
            if (!decision.Same(before))
            {
                hasPath = false;
                roaming = false;
                guardIn = 0;
                stuck = 0;
            }
            goalNow = decision.ToString();
        }

        /// <summary>The temperament, without the flag goals in a scene that has no flags.</summary>
        private Temperament Effective()
        {
            if (owner.Flags != null)
                return temperament;
            var t = temperament;
            t.attack = t.defend = t.chase = 0;
            return t;
        }

        private void FillSituation()
        {
            var s = situation;
            var feet = transform.position;
            var state = player.State;
            var flags = owner.Flags;
            if (flags != null)
            {
                var own = flags.State.Flag(Team);
                var enemy = flags.State.Flag(Team.Other());
                s.carrying = flags.State.IsCarrying(state);
                s.ownFlag = own.Status;
                s.enemyFlag = enemy.Status;
                s.toOwnStand = Vector3.Distance(feet, flags.Stand(Team).transform.position);
                s.toOwnFlag = Vector3.Distance(feet, FlagPosition(Team));
                s.toEnemyFlag = Vector3.Distance(feet, FlagPosition(Team.Other()));
            }
            else
            {
                s.carrying = false;
                s.ownFlag = s.enemyFlag = FlagStatus.Home;
            }
            s.health = state.Health;
            s.maxHealth = Mathf.Max(1, state.MaxHealth);
            var combat = owner.Combat;
            s.overheal = combat != null ? combat.Table.overheal : 200;
            var inventory = arsenal != null ? arsenal.Inventory : null;
            s.weaponsHeld = 0;
            if (inventory != null)
                for (var i = 0; i < Inventory.SlotCount; i++)
                    if (inventory.Has((WeaponSlot)i))
                        s.weaponsHeld++;
            var now = Time.time;
            s.enemyInSight = TargetInSight(now);
            s.carrierInSight = s.enemyInSight && flags != null && flags.State.Flag(Team).Carrier == target.State;

            s.pods.Clear();
            var pods = owner.Pods;
            for (var i = 0; i < pods.Count; i++)
            {
                var pod = pods[i];
                var health = pod.health != null;
                s.pods.Add(new PodOption
                {
                    index = i,
                    health = health,
                    weapon = health ? null : pod.weapon.weaponId,
                    distance = Vector3.Distance(feet, pod.Position),
                    available = pod.Available,
                    useful = health ? combat != null && combat.State.NeedsHealth(state) : inventory != null && inventory.Adds(pod.weapon.weaponId),
                });
            }
        }

        /// <summary>Where a team's flag is now: on its stand, a carrier's back or the ground.</summary>
        private Vector3 FlagPosition(Team team)
        {
            var flag = owner.Flags.FlagObject(team);
            return flag != null ? flag.position : owner.Flags.Stand(team).transform.position;
        }

        /// <summary>Sets where the goal is now; false when the goal no longer makes sense (think again).</summary>
        private bool UpdateDestination(float now)
        {
            var flags = owner.Flags;
            var feet = transform.position;
            switch (decision.goal)
            {
                case BotGoal.BringFlagHome:
                    if (flags == null || !flags.State.IsCarrying(player.State))
                        return false;
                    destination = flags.Stand(Team).transform.position;
                    return true;
                case BotGoal.TakeFlag:
                    if (flags == null || flags.State.Flag(Team.Other()).Status == FlagStatus.Carried)
                        return false;
                    destination = FlagPosition(Team.Other());
                    return true;
                case BotGoal.ReturnFlag:
                    if (flags == null || flags.State.Flag(Team).Status != FlagStatus.Dropped)
                        return false;
                    destination = FlagPosition(Team);
                    return true;
                case BotGoal.ChaseCarrier:
                    if (flags == null || flags.State.Flag(Team).Status != FlagStatus.Carried)
                        return false;
                    destination = FlagPosition(Team);
                    return true;
                case BotGoal.DefendFlag:
                {
                    if (flags == null || flags.State.Flag(Team).Status != FlagStatus.Home)
                        return false;
                    guardIn -= Time.deltaTime;
                    if (guardIn <= 0)
                    {
                        var stand = flags.Stand(Team).transform.position;
                        BotNavigation.RandomPoint(stand, 7, random, out guardPoint);
                        guardIn = 6 + 5 * (float)random.NextDouble();
                    }
                    destination = guardPoint;
                    return true;
                }
                case BotGoal.Hunt:
                {
                    if (TargetInSight(now))
                    {
                        destination = target.transform.position;
                        roaming = false;
                        return true;
                    }
                    // The enemy last seen, if it is still remembered
                    var best = float.PositiveInfinity;
                    foreach (var other in owner.Match.Players)
                    {
                        if (other.Team == Team || !other.State.Alive || !memory.Recall(other.State.Index, now, out var seen))
                            continue;
                        var distance = Vector3.Distance(feet, seen);
                        if (distance < best && distance > 1.5f)
                            (best, destination) = (distance, seen);
                    }
                    if (best < float.PositiveInfinity)
                    {
                        roaming = false;
                        return true;
                    }
                    if (!roaming || Flat(roamPoint - feet).magnitude < 2)
                        PickRoamPoint();
                    destination = roamPoint;
                    return true;
                }
                case BotGoal.FetchWeapon:
                case BotGoal.FetchHealth:
                {
                    if (decision.pod < 0 || decision.pod >= owner.Pods.Count)
                        return false;
                    var pod = owner.Pods[decision.pod];
                    if (!pod.Available)
                        return false;
                    if (pod.health != null ? owner.Combat == null || !owner.Combat.State.NeedsHealth(player.State)
                            : arsenal == null || !arsenal.Inventory.Adds(pod.weapon.weaponId))
                        return false;
                    destination = pod.Position;
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>Somewhere to look for a fight: round the middle of the island, leaning to the enemy's half.</summary>
        private void PickRoamPoint()
        {
            var arena = owner.Match.Arena;
            var own = arena.Side(BattleSide.SideOf(Team)).spawn.position;
            var enemy = arena.Side(BattleSide.SideOf(Team.Other())).spawn.position;
            var centre = Vector3.Lerp(own, enemy, 0.3f + 0.45f * (float)random.NextDouble());
            BotNavigation.RandomPoint(centre, 25, random, out roamPoint);
            roaming = true;
        }

        // ---------------------------------------------------------------- The way

        private void UpdatePath(float dt)
        {
            repathIn -= dt;
            var moving = decision.goal == BotGoal.ChaseCarrier || decision.goal == BotGoal.Hunt;
            if (!hasPath || repathIn <= 0 || (destination - pathTarget).sqrMagnitude > 2.5f * 2.5f)
            {
                BotNavigation.Path(transform.position, destination, corners);
                corner = corners.Count > 1 ? 1 : 0;
                pathTarget = destination;
                hasPath = true;
                repathIn = (moving ? 0.8f : 2.5f) + 0.5f * (float)random.NextDouble();
            }
            // Past the corners it has reached
            var feet = transform.position;
            while (corner < corners.Count)
            {
                var c = corners[corner];
                if (Flat(c - feet).magnitude > 0.45f || Mathf.Abs(c.y - feet.y) > 2.2f)
                    break;
                corner++;
            }
        }

        /// <summary>The level way along the path now: to the next corner, or straight at the destination past the last one.</summary>
        private Vector3 PathDirection(out bool arrived)
        {
            var feet = transform.position;
            if (corner < corners.Count)
            {
                arrived = false;
                var to = Flat(corners[corner] - feet);
                // Right over a corner far below (a drop link straight down): on the way the path came, over the edge
                if (to.magnitude < 0.3f)
                    to = corner > 0 ? Flat(corners[corner] - corners[corner - 1]) : transform.forward;
                return to.sqrMagnitude > 1e-4f ? to.normalized : Vector3.zero;
            }
            var rest = Flat(destination - feet);
            arrived = rest.magnitude < 0.6f;
            return arrived ? Vector3.zero : rest.normalized;
        }

        private void Steer(float dt)
        {
            var controller = player.Controller;
            var feet = transform.position;
            var now = Time.time;
            var want = PathDirection(out var arrived);
            var atEnd = corner >= corners.Count;

            // The goal reached
            if (arrived || (atEnd && Flat(destination - feet).magnitude < 1.5f))
            {
                switch (decision.goal)
                {
                    case BotGoal.FetchWeapon:
                    case BotGoal.FetchHealth:
                    case BotGoal.ReturnFlag:
                    case BotGoal.TakeFlag:
                        // Into the pod's or the flag's trigger; then the event or the next thought moves it on
                        Rethink(0.6f);
                        break;
                    case BotGoal.DefendFlag:
                        want = Vector3.zero;
                        break;
                    case BotGoal.BringFlagHome:
                        // On the stand, waiting for its own flag to come home
                        want = Vector3.zero;
                        break;
                }
            }
            // A goal up a step it cannot walk (a flag on a low rock): jump at it
            if (atEnd && destination.y > feet.y + 0.5f && Flat(destination - feet).magnitude < 2.5f && controller.IsGrounded)
                controller.Jump();

            // In a fight: hold the distance its weapon likes when hunting, and dodge sideways whatever the goal
            if (TargetInSight(now))
            {
                var toTarget = Flat(target.transform.position - feet);
                var distance = toTarget.magnitude;
                if (decision.goal == BotGoal.Hunt && distance > 0.01f)
                {
                    var preferred = WeaponChoice.PreferredDistance(arsenal != null && arsenal.Inventory != null ? arsenal.Inventory.CurrentWeapon : null);
                    var away = -toTarget / distance;
                    if (distance < preferred * 0.6f && BotNavigation.Clear(feet, feet + away * 2))
                        want = away;
                    else if (distance < preferred * 1.1f)
                        want = Vector3.zero;
                }
                strafeIn -= dt;
                var right = Vector3.Cross(Vector3.up, distance > 0.01f ? toTarget / distance : transform.forward).normalized;
                if (strafeIn <= 0)
                {
                    strafeSign = random.NextDouble() < 0.5 ? -1 : 1;
                    strafeIn = 0.5f + (float)random.NextDouble();
                    // Not off a wall-walk or into a wall
                    if (!BotNavigation.Clear(feet, feet + right * (strafeSign * 1.5f)))
                        strafeSign = -strafeSign;
                }
                var share = decision.goal switch
                {
                    BotGoal.Hunt or BotGoal.DefendFlag => 0.8f,
                    BotGoal.BringFlagHome => 0.2f,
                    _ => 0.4f,
                };
                want += right * (strafeSign * share);
            }

            if (sidestepLeft > 0)
            {
                sidestepLeft -= dt;
                want = sidestep;
            }
            // A train near the track it is about to cross (ticket 27.10): it waits at the track's edge until the train has passed
            if (BotNavigation.MustWait(feet, corners, corner, 14, SdfLevel.PathMover.Clock))
                want = Vector3.zero;
            want.y = 0;
            if (want.sqrMagnitude > 1)
                want.Normalize();

            // In the body's frame: it may walk any way while it looks another, as you can
            var local = Quaternion.Euler(0, -controller.Yaw, 0) * want;
            controller.SetMove(new Vector2(local.x, local.z));
            CheckStuck(dt, want.sqrMagnitude > 0.25f && !controller.frozen);
        }

        /// <summary>Barely moving while it walks: it jumps, then steps aside and jumps, then finds a new way, then a new goal.</summary>
        private void CheckStuck(float dt, bool walking)
        {
            var feet = transform.position;
            if (!walking)
            {
                lastSample = feet;
                sampleIn = 0.5f;
                return;
            }
            sampleIn -= dt;
            if (sampleIn > 0)
                return;
            var moved = Flat(feet - lastSample).magnitude;
            lastSample = feet;
            sampleIn = 0.5f;
            if (moved > 0.4f)
            {
                stuck = 0;
                return;
            }
            stuck++;
            var controller = player.Controller;
            switch (stuck)
            {
                case 1:
                    controller.Jump();
                    break;
                case 2:
                case 4:
                {
                    var angle = (random.NextDouble() < 0.5 ? -1 : 1) * (60 + 60 * (float)random.NextDouble());
                    sidestep = Quaternion.Euler(0, angle, 0) * (corner < corners.Count ? Flat(corners[corner] - feet).normalized : transform.forward);
                    sidestepLeft = 0.6f;
                    controller.Jump();
                    break;
                }
                case 3:
                    hasPath = false;
                    break;
                default:
                    stuck = 0;
                    decision = new BotDecision(BotGoal.None);
                    hasPath = false;
                    roaming = false;
                    guardIn = 0;
                    break;
            }
        }

        // ---------------------------------------------------------------- Weapons, aim and fire

        private void ChooseWeapon(float dt, float now)
        {
            weaponIn -= dt;
            if (weaponIn > 0 || arsenal == null || arsenal.Inventory == null)
                return;
            weaponIn = 0.4f;
            var inventory = arsenal.Inventory;
            var flags = owner.Flags;
            var context = new WeaponChoice.Context(22, defending: decision.goal == BotGoal.DefendFlag);
            if (TargetInSight(now))
            {
                context.distance = Vector3.Distance(transform.position, target.transform.position);
                context.targetIsCarrier = flags != null && flags.State.Flag(Team).Carrier == target.State;
                foreach (var other in owner.Match.Players)
                    if (other != target && other.Team == target.Team && other.State.Alive &&
                        Vector3.Distance(other.transform.position, target.transform.position) < 6)
                        context.groupedWithTarget++;
            }
            var slot = WeaponChoice.Best(inventory, context);
            if (slot != inventory.Current && now - lastSwitch > 1)
            {
                arsenal.Select(slot);
                lastSwitch = now;
            }
        }

        private void Aim(float dt, float now)
        {
            var controller = player.Controller;
            var skill = botSkill;
            var weapon = arsenal != null ? arsenal.Weapon : null;
            var eye = arsenal != null ? arsenal.EyePosition : transform.position + Vector3.up * 1.5f;
            float wantYaw, wantPitch;
            aimed = false;
            var turn = skill.TurnSpeed;

            if (TargetInSight(now))
            {
                var point = AimPoint(weapon, eye, out var pitchOverride, out var reachable);
                var to = point - eye;
                wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                wantPitch = pitchOverride ?? -Mathf.Asin(Mathf.Clamp(to.y / Mathf.Max(0.01f, to.magnitude), -1, 1)) * Mathf.Rad2Deg;
                // The aim's error, more while the target crosses the view
                var velocity = owner.VelocityOf(target);
                var across = Vector3.ProjectOnPlane(velocity, to.normalized).magnitude;
                var angular = across / Mathf.Max(1, to.magnitude) * Mathf.Rad2Deg;
                var offset = aim.Tick(dt, angular);
                wantYaw += offset.x;
                wantPitch += offset.y;
                aimed = reachable;
                desiredAim = new Vector3(wantYaw, wantPitch, 0);
            }
            else if (MachineInSight(now))
            {
                // A machine in its way: straight at its middle, with the aim's error; not with a lobbed gun
                var to = machineTarget.Center - eye;
                wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                wantPitch = -Mathf.Asin(Mathf.Clamp(to.y / Mathf.Max(0.01f, to.magnitude), -1, 1)) * Mathf.Rad2Deg;
                var offset = aim.Tick(dt);
                wantYaw += offset.x;
                wantPitch += offset.y;
                aimed = weapon != null && weapon.mode != FireMode.Cloud && weapon.mode != FireMode.Blob && to.magnitude <= weapon.range;
                desiredAim = new Vector3(wantYaw, wantPitch, 0);
            }
            else
            {
                if (aim.Target >= 0)
                    aim.Tick(dt);
                Vector3? look = null;
                if (now < lookAtUntil)
                    look = lookAt;
                else if (target != null && memory.Recall(target.State.Index, now, out var seen))
                    look = seen + Vector3.up * 1.2f;
                if (look.HasValue)
                {
                    var to = look.Value - eye;
                    wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    wantPitch = -Mathf.Asin(Mathf.Clamp(to.y / Mathf.Max(0.01f, to.magnitude), -1, 1)) * Mathf.Rad2Deg;
                }
                else
                {
                    // Along the way, a little down; at a guard point, out at the enemy's side
                    var way = PathDirection(out var arrived);
                    if (arrived && decision.goal == BotGoal.DefendFlag && owner.Flags != null)
                        way = Flat(owner.Flags.Stand(Team.Other()).transform.position - transform.position).normalized;
                    wantYaw = way.sqrMagnitude > 1e-4f ? Mathf.Atan2(way.x, way.z) * Mathf.Rad2Deg : yaw;
                    wantPitch = 4;
                }
                turn *= 0.6f;
            }
            yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, turn * dt);
            pitch = Mathf.MoveTowards(pitch, Mathf.Clamp(wantPitch, -80, 80), turn * dt);
            controller.SetAim(yaw, pitch);
        }

        /// <summary>
        /// Where to aim at the target: its chest for a shot that flies straight, ahead of it and up an arc for a lobbed one (the pitch
        /// then given); <paramref name="reachable"/> false when the shot cannot get there.
        /// </summary>
        private Vector3 AimPoint(WeaponStats weapon, Vector3 eye, out float? pitchOverride, out bool reachable)
        {
            pitchOverride = null;
            reachable = true;
            var feet = target.transform.position;
            if (weapon == null)
                return feet + Vector3.up * 1.15f;
            var distance = Vector3.Distance(eye, feet + Vector3.up);
            switch (weapon.mode)
            {
                case FireMode.Cloud:
                case FireMode.Blob:
                {
                    // A cloud round the feet, a blob at the body; ahead of it by the shot's flight
                    var point = feet + Vector3.up * (weapon.mode == FireMode.Cloud ? 0.3f : 0.9f);
                    var velocity = owner.VelocityOf(target);
                    velocity.y = 0;
                    var flight = 0f;
                    for (var i = 0; i < 2; i++)
                    {
                        var ahead = point + velocity * flight;
                        reachable = Ballistics.AimPitch(weapon, eye, ahead, out var lobPitch, out flight);
                        pitchOverride = lobPitch;
                    }
                    reachable &= distance <= weapon.range * 1.1f;
                    return point + velocity * flight;
                }
                case FireMode.Cone:
                    reachable = distance <= weapon.range + 0.5f;
                    return feet + Vector3.up * 1.0f;
                default:
                    reachable = distance <= weapon.range;
                    return feet + Vector3.up * 1.15f;
            }
        }

        private void Fire(float now)
        {
            if (arsenal == null)
                return;
            var weapon = arsenal.Weapon;
            var held = false;
            var zoom = false;
            if (weapon != null && aimed && aim.Ready && (TargetInSight(now) || MachineInSight(now)))
            {
                // The flame's cone is wide; the minigun keeps its barrels spinning through small turns
                var tolerance = weapon.mode == FireMode.Cone ? 12f : weapon.spinUp > 0 ? 6f : 2.5f;
                var off = Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(yaw, desiredAim.x)), Mathf.Abs(pitch - desiredAim.y));
                held = off <= tolerance;
                zoom = weapon.zoomFieldOfView > 0 && TargetInSight(now) && Vector3.Distance(transform.position, target.transform.position) > 12;
                // The sniper waits for a steadier aim before each shot
                if (weapon.zoomFieldOfView > 0 && aim.Tracked < 0.3f)
                    held = false;
            }
            arsenal.SetTrigger(held);
            arsenal.SetZoom(zoom);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        // ---------------------------------------------------------------- Debugging

        private void OnDrawGizmos()
        {
            if (owner == null || !owner.showPaths || player == null || player.State == null || !player.State.Alive)
                return;
            Gizmos.color = BattleSide.ColorOf(BattleSide.SideOf(Team));
            var from = transform.position + Vector3.up * 0.1f;
            for (var i = corner; i < corners.Count; i++)
            {
                var to = corners[i] + Vector3.up * 0.1f;
                Gizmos.DrawLine(from, to);
                from = to;
            }
            Gizmos.DrawWireSphere(destination + Vector3.up * 0.2f, 0.3f);
            if (target != null)
                Gizmos.DrawLine(transform.position + Vector3.up * 1.5f, target.transform.position + Vector3.up * 1.2f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.3f, $"{name}: {goalNow} ({style})");
#endif
        }
    }
}
