using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// A player as the balance simulation plays them (ticket 33.9): how well they aim, dodge a telegraph, hit the copter's moving sacs and
    /// jump the slam, and how long their routes are over the straight line. Two profiles: one who knows the fight, and an average first
    /// attempt.
    /// </summary>
    public sealed class BossAttacker
    {
        public string name = "";
        [Tooltip("Degrees of aim error added to the weapon's own spread")]
        public float aimDeg;
        [Tooltip("Chance to avoid a telegraphed attack (a sidestep, cover, a step out of the ring)")]
        public float dodge;
        [Tooltip("Chance a lead shot on a moving spore sac hits")]
        public float track;
        [Tooltip("Chance to be in the air when the slam's ring passes")]
        public float jump;
        [Tooltip("Route length over the straight line (cover to cover)")]
        public float detour = 1.15f;
        [Tooltip("Share of the time the copter is in sight and range of the electro-gun")]
        public float sacDuty = 0.6f;
        [Tooltip("How much of a telegraph's time goes on dodging it instead of firing (1 is the whole share)")]
        public float busy = 1;
        [Tooltip("Seconds to find the target after a window opens")]
        public float reaction = 0.6f;

        public static BossAttacker Expert() => new BossAttacker { name = "expert", aimDeg = 0.4f, dodge = 0.7f, track = 0.8f, jump = 0.8f, detour = 1.1f, sacDuty = 0.65f, reaction = 0.5f, busy = 0.6f };

        public static BossAttacker Average() => new BossAttacker { name = "average", aimDeg = 1.0f, dodge = 0.45f, track = 0.5f, jump = 0.5f, detour = 1.25f, sacDuty = 0.5f, reaction = 1.0f };
    }

    /// <summary>What one simulated fight came to.</summary>
    public sealed class BossFightResult
    {
        public bool won, timedOut;
        public float seconds;
        public float[] phaseSeconds = new float[3];
        public int livesLost;
        public int[] livesLostIn = new int[3];
        public int phaseReached, kneels;
        public Dictionary<string, float> taken = new Dictionary<string, float>();
        public float barLeft;
    }

    /// <summary>
    /// The scripted attacker of ticket 33.9: plays the whole fight against the real <see cref="BossState"/> and <see cref="BossAttackPlanner"/>
    /// (and the real flags, weapons table and lives) at 0.1 s steps, on the arena's real distances (<c>arena.level</c>), with damage from
    /// hit geometry: a weapon's spread plus the attacker's aim error is a disc at the target's distance, and the share of it inside each of
    /// Prime's shapes (the capsule and sphere sizes of <c>Boss.BuildShapes</c> at 4.6 times) is the share of the shots that land there. It
    /// is an expected-value model of one player; the red bots' help and the blue crew's pressure are flat rates (below). It does not
    /// replace playing the fight.
    /// <list type="bullet">
    /// <item>Phase 1: fetch the blue flag (90 m), carry it to the red stand (83.5 m at 90% speed), capture; in the kneel window walk
    /// towards Prime and fire dual pistols at the gills from where 42 stands (the stand is 96 m from the dais).</item>
    /// <item>Phase 2: fetch the electro-gun at (0, 26), fire at the sacs (40 on the target, 20 on two others); after the crash walk to
    /// the wreck and fire the pistols at the gills in the daze.</item>
    /// <item>Phase 3: take the crown from the wreck, carry it home (Prime hunts the carrier), take the champion's minigun from the
    /// plinth, then fire at the eyes from about 25 m, backing off when he closes.</item>
    /// </list>
    /// </summary>
    public sealed class BossFightSim
    {
        // The arena (content/levels/arena.level), x east, z north
        public static readonly Vector2 RedSpawn = new Vector2(-50, -18), RedStand = new Vector2(-47.5f, 0), BlueStand = new Vector2(36, 0),
            Dais = new Vector2(49, 0), Wreck = new Vector2(6, 0), ElectroPod = new Vector2(0, 26), Plinth = new Vector2(-50, 10);

        // Prime's hit shapes at 4.6 times (Boss.BuildShapes): the cap sphere 0.5 and the stem 0.2 round, the gills 0.24, an eye 0.12
        public const float BodyRadius = 2.4f, GillsRadius = 1.1f, EyeRadius = 0.55f;

        // Flat rates for what the sim does not play (per second, at Normal): the red bots' fire on Prime, the blue crew's, pods and caps
        public float botPrimeDps = 12;
        public float botSacDps = 10;
        public float botHandDps = 10;
        public float crewDps = 8;
        public float regenDps = 2;
        public float blueCapturesPerMinute = 0.15f;
        public float walkSpeed = 4.4f;
        [Tooltip("Scales everything that hurts 42: 0 is a god-mode run (the raw lengths), 1 the model as it stands")]
        public float pressure = 0.55f;

        /// <summary>The fight's main events with their times, for reading one run.</summary>
        public readonly List<string> trace = new List<string>();

        private void Note(string what) => trace.Add($"{boss.FightSeconds,6:0.0} s  {what}  (bars {boss.BarHealth(0):0}/{boss.BarHealth(1):0}/{boss.BarHealth(2):0})");

        private readonly BossRules rules;
        private readonly BossAttacker who;
        private readonly System.Random rng;
        private readonly WeaponTable weapons = WeaponTable.Default();
        private MatchState match;
        private CaptureTheFlagState ctf;
        private BossState boss;
        private PlayerState you;

        private Vector2 pos, primePos;
        private float health, deadFor, protectedFor;
        private bool carrying, firstTick;
        private int stage;
        private float stageTime, windowTime, shotsOwed;
        private BossAttack resolved, noticed;
        private float busy, burstLeft;
        private int burstScouts;
        private bool inSight;
        private float sightFlip;
        private BossPhase lastPhase;
        private float transitions;
        private int blueCaptureEdge, kneels;

        public BossFightSim(BossRules rules, BossAttacker attacker, int seed)
        {
            this.rules = rules;
            who = attacker;
            rng = new System.Random(seed);
        }

        private float Roll() => (float)rng.NextDouble();

        private readonly Dictionary<string, float> taken = new Dictionary<string, float>();

        private void Hurt(string source, float amount)
        {
            amount *= pressure;
            health -= amount;
            taken[source] = (taken.TryGetValue(source, out var sum) ? sum : 0) + amount;
        }

        /// <summary>Plays one fight from phase 1 to Prime's end, 42's last life or <paramref name="limitSeconds"/> of fighting.</summary>
        public BossFightResult Play(float limitSeconds = 1500)
        {
            match = new MatchState(new MatchRules { objective = MatchObjective.Boss, singleRound = true });
            foreach (var team in Teams.All)
                for (var i = 1; i <= match.Rules.teamSize; i++)
                    match.AddPlayer($"{team.Name()} {i}", team, i > 1 || team == Team.Blue);
            ctf = new CaptureTheFlagState(match);
            boss = new BossState(rules, match, ctf);
            you = match.Players[0];
            match.Begin();
            for (var t = 0f; t < match.Rules.countdownSeconds + match.Rules.spawnProtectionSeconds + 0.2f; t += 0.05f)
            {
                match.Tick(0.05f);
                ctf.Tick(0.05f);
            }
            var result = new BossFightResult();
            var lost = false;
            boss.Lost += () => lost = true;
            boss.Stunned += (kind, seconds) => { if (kind == BossStunKind.Kneel) kneels++; Note($"{kind} window {seconds:0.#} s"); };
            boss.PhaseChanged += phase => Note("phase " + phase);
            boss.LifeLost += left => Note($"42 died, {left} lives left");
            boss.CrashStarted += by => Note("last sac burst");
            boss.CrownCapturedBy += by => Note("crown captured");
            ctf.Taken += (flag, by) => { if (by == you) Note($"{flag.Team} flag taken"); };
            boss.Begin();
            health = you.MaxHealth;
            pos = RedSpawn;
            lastPhase = BossPhase.Intro;
            const float dt = 0.1f;
            var phaseStart = 0f;
            while (boss.FightSeconds < limitSeconds && boss.Phase != BossPhase.Victory && boss.Phase != BossPhase.Defeat)
            {
                if (boss.Phase != lastPhase)
                {
                    if (lastPhase.IsFight())
                        result.phaseSeconds[lastPhase.BarOf()] = boss.FightSeconds - phaseStart;
                    phaseStart = boss.FightSeconds;
                    EnterPhase();
                }
                if (boss.Phase == BossPhase.Transition12 || boss.Phase == BossPhase.Transition23)
                {
                    transitions += 6;
                    boss.FinishTransition();
                    continue;
                }
                Step(dt);
                if (lost)
                    break;
            }
            if (boss.Phase.IsFight())
                result.phaseSeconds[boss.Phase.BarOf()] = boss.FightSeconds - phaseStart;
            else if (boss.Phase == BossPhase.Victory)
                result.phaseSeconds[2] = boss.FightSeconds - phaseStart;
            result.won = boss.Phase == BossPhase.Victory;
            result.timedOut = !result.won && !lost;
            result.seconds = boss.FightSeconds;
            result.livesLost = boss.LivesLost;
            for (var i = 0; i < 3; i++)
                result.livesLostIn[i] = boss.LivesLostIn(i + 1);
            result.phaseReached = Math.Max(1, boss.Phase.BarOf() + 1);
            if (boss.Phase == BossPhase.Defeat)
                result.phaseReached = Math.Max(1, boss.Bar + 1);
            result.barLeft = boss.CurrentBar;
            result.taken = taken;
            result.kneels = kneels;
            return result;
        }

        /// <summary>Seconds of the transitions the fight went through (not in <see cref="BossFightResult.seconds"/>).</summary>
        public float TransitionSeconds => transitions;

        // A phase starts: the attacker's plan for it begins at the start of its first stage (the lives and the bars are the state's)
        private void EnterPhase()
        {
            lastPhase = boss.Phase;
            stage = 0;
            stageTime = 0;
            windowTime = 0;
            shotsOwed = 0;
            carrying = false;
            resolved = null;
            primePos = boss.Phase == BossPhase.Phase3 ? Wreck : Dais;
            blueCaptureEdge = 0;
            if (boss.Phase == BossPhase.Phase1)
                pos = RedSpawn;
        }

        private void Step(float dt)
        {
            stageTime += dt;
            match.Tick(dt);
            ctf.Tick(dt);
            boss.Tick(dt);
            if (deadFor > 0)
            {
                deadFor -= dt;
                if (deadFor <= 0)
                {
                    health = you.MaxHealth;
                    pos = RedSpawn;
                    protectedFor = match.Rules.spawnProtectionSeconds;
                    stage = StartStage();
                }
            }
            else
            {
                protectedFor = Math.Max(0, protectedFor - dt);
                sightFlip -= dt;
                if (sightFlip <= 0)
                {
                    sightFlip = 2;
                    inSight = Roll() < 0.7f;
                }
                switch (boss.Phase)
                {
                    case BossPhase.Phase1: Phase1(dt); break;
                    case BossPhase.Phase2: Phase2(dt); break;
                    case BossPhase.Phase3: Phase3(dt); break;
                }
                Ambient(dt);
            }
            PlanAndResolve(dt);
            if (deadFor <= 0 && health <= 0)
                Die();
        }

        // After a respawn: back to the plan's first stage for the phase (a dropped flag is found again where it lies)
        private int StartStage() => 0;

        private void Die()
        {
            if (carrying)
            {
                ctf.Drop(you, new Vector3(pos.x, 0, pos.y));
                if (boss.Phase == BossPhase.Phase1)
                    ctf.ReturnDropped(Team.Blue);
                carrying = false;
            }
            boss.ReportDeath(you);
            deadFor = match.Rules.respawnSeconds;
            shotsOwed = 0;
        }

        // ---------------------------------------------------------------- Movement and fire

        private bool MoveTo(Vector2 goal, float dt, bool slowed = false)
        {
            var speed = walkSpeed * (slowed ? ctf.Rules.carrierSpeed : 1) / who.detour;
            var delta = goal - pos;
            var step = speed * dt;
            if (delta.magnitude <= Math.Max(step, 1.5f))
            {
                pos = goal;
                return true;
            }
            pos += delta.normalized * step;
            return false;
        }

        private float Scatter(float distance, float spreadDeg) =>
            distance * Mathf.Tan(Mathf.Deg2Rad * Mathf.Sqrt(spreadDeg * spreadDeg + who.aimDeg * who.aimDeg));

        // The share of shots in a disc of radius scatter that land in a shape of radius radius at its centre
        private static float Share(float radius, float scatter) => scatter <= radius ? 1 : radius * radius / (scatter * scatter);

        /// <summary>
        /// Fires <paramref name="weapon"/> at Prime for <paramref name="dt"/>: the gills when a window is open, the eyes when they are
        /// open and the phase is 3, the body otherwise. Expected damage by part through <see cref="BossState.Hurt"/>.
        /// </summary>
        private void FirePrime(float dt, WeaponStats weapon, float spreadDeg, float duty, bool champion)
        {
            if (windowTime < who.reaction && boss.Vulnerable)
                return;
            var d = Vector2.Distance(pos, primePos);
            var shots = weapon.shotsPerSecond * dt * duty;
            var damage = weapon.DamageAt(d);
            var scatter = Scatter(d, spreadDeg);
            var any = Share(BodyRadius, scatter);
            float gills = 0, eyes = 0;
            if (boss.Vulnerable)
                gills = Share(GillsRadius, scatter);
            else if (boss.Phase == BossPhase.Phase3)
                eyes = Math.Min(any, 2 * Share(EyeRadius, scatter));
            var body = Math.Max(0, any - gills - eyes);
            if (gills > 0) boss.Hurt(shots * damage * gills, BossPart.Gills, you, champion);
            if (eyes > 0) boss.Hurt(shots * damage * eyes, BossPart.Eyes, you, champion);
            if (body > 0) boss.Hurt(shots * damage * body, BossPart.Body, you, champion);
        }

        private void Ambient(float dt)
        {
            var crew = rules.CrewCount(boss.Phase, boss.SoreLoser);
            var engage = boss.Phase == BossPhase.Phase1 ? 0.10f : 0.08f;
            // The crew shoots at 42 in bursts of 2 s by one to all of them (the same mean as crew x engage of the time); nothing under
            // spawn protection
            if (crew > 0 && burstLeft <= 0 && Roll() < crew * engage / (crew + 1) * dt)
            {
                burstLeft = 2;
                burstScouts = 1 + rng.Next(crew);
            }
            if (burstLeft > 0)
            {
                burstLeft -= dt;
                if (protectedFor <= 0)
                    Hurt("crew", burstScouts * rules.crewSkill * crewDps * dt);
            }
            health = Math.Min(health + regenDps * dt * (health < you.MaxHealth ? 1 : 0), you.MaxHealth);
        }

        // ---------------------------------------------------------------- Prime's attacks

        private void PlanAndResolve(float dt)
        {
            var targets = new List<BossTarget>();
            if (deadFor <= 0 && protectedFor <= 0)
                targets.Add(new BossTarget
                {
                    Player = you,
                    Distance = Vector2.Distance(pos, primePos),
                    InSight = inSight,
                    Carrier = carrying,
                    RecentDamage = boss.RecentDamageOf(you),
                });
            boss.Plan(dt, targets);
            var attack = boss.Planner.Current;
            busy = boss.Vulnerable ? 0 : Math.Max(0, busy - dt);
            // Dodging a telegraph is time not spent firing: a share of its wind-up and effect, by how much the attack asks of 42
            if (attack != null && attack != noticed)
            {
                noticed = attack;
                var share = attack.Kind switch
                {
                    BossAttackKind.SporeMortar => 0.8f,
                    BossAttackKind.RoyalGlare => 0.6f,
                    BossAttackKind.CapCannon => 0.6f,
                    BossAttackKind.DowndraftDive => 0.8f,
                    BossAttackKind.CapSlam => 0.2f,
                    BossAttackKind.HandGrab => 0.7f,
                    _ => 0,
                };
                busy = (attack.Spec.windUp + attack.Spec.active) * share * who.busy;
            }
            if (attack == null || attack.Stage != BossAttackStage.Active || attack == resolved)
                return;
            resolved = attack;
            Resolve(attack);
        }

        // What an attack that has just gone active does to 42 (expected value with a roll for whether it lands)
        private void Resolve(BossAttack attack)
        {
            if (deadFor > 0 || protectedFor > 0)
                return;
            var s = attack.Spec;
            var miss = Roll() < who.dodge;
            var d = Vector2.Distance(pos, primePos);
            switch (attack.Kind)
            {
                case BossAttackKind.SporeMortar:
                    // Three balls aimed at 42 and up to two bots: the ball at 42 lands, the cloud lingers and 42 walks out of half of it
                    if (!miss) Hurt("mortar", s.damage + rules.mortarCloudPerSecond * rules.damageScale * rules.mortarCloudSeconds * 0.5f);
                    break;
                case BossAttackKind.RoyalGlare:
                    // The beam follows at 25 degrees a second: a sidestep and cover leave it a short time on 42
                    if (!miss) Hurt("glare", s.damage * s.active * 0.6f);
                    break;
                case BossAttackKind.CapCannon:
                    Hurt("cannon", s.damage * s.count * 0.3f * (miss ? 0.2f : 1f));
                    break;
                case BossAttackKind.DowndraftDive:
                    if (d < 40 && !miss) Hurt("dive", s.damage);
                    break;
                case BossAttackKind.CapSlam:
                    // A jump clears the ring (35 and a push); one still on the ground is hit
                    if (d <= rules.closeRange && Roll() >= who.jump) Hurt("slam", s.damage);
                    break;
                case BossAttackKind.HandGrab:
                    if (d <= 30 && !miss) Hurt("grab", s.damage);
                    break;
            }
        }

        // ---------------------------------------------------------------- Phase 1

        private void Phase1(float dt)
        {
            if (boss.Vulnerable)
            {
                windowTime += dt;
                carrying = false;
                // Walk towards Prime, stopping 32 m from the dais, firing dual pistols at the gills
                MoveTo(Dais + (RedStand - Dais).normalized * 32, dt);
                FirePrime(dt, weapons.Get(WeaponIds.DualPistols), weapons.Get(WeaponIds.DualPistols).spread, 1, false);
                shotsOwed = 0;
                BotFire(dt, botPrimeDps, BossPart.Body);
                return;
            }
            windowTime = 0;
            // Blue's own captures (the crew plays capture the flag too): a flat rate of heals
            if (Roll() < blueCapturesPerMinute / 60f * dt && ctf.Flag(Team.Red).AtHome && ctf.Flag(Team.Blue).AtHome)
            {
                var blue = match.Players.First(p => p.Team == Team.Blue);
                if (ctf.TouchStand(blue, Team.Red))
                    ctf.TouchStand(blue, Team.Blue);
            }
            if (!carrying)
            {
                if (MoveTo(BlueStand, dt) && ctf.TouchStand(you, Team.Blue))
                    carrying = true;
            }
            else if (MoveTo(RedStand, dt, true))
            {
                if (ctf.TouchStand(you, Team.Red))
                    carrying = false;
            }
        }

        // The red bots' fire on Prime, a flat rate scaled by what Prime can be hurt through
        private void BotFire(float dt, float dps, BossPart part)
        {
            if (windowTime < who.reaction)
                return;
            boss.Hurt(dps * dt, part, null);
        }

        // ---------------------------------------------------------------- Phase 2

        private void Phase2(float dt)
        {
            var electro = weapons.Get(WeaponIds.ElectroGun);
            if (boss.Copter == CopterStage.Flying)
            {
                windowTime = 0;
                if (stage == 0)
                {
                    if (MoveTo(ElectroPod, dt))
                        stage = 1;
                    return;
                }
                // Fire at the sacs while the copter is in sight and range
                if (busy <= 0)
                    shotsOwed += electro.shotsPerSecond * dt * who.sacDuty;
                while (shotsOwed >= 1 && boss.Parts.SacsAlive > 0)
                {
                    shotsOwed -= 1;
                    if (Roll() > who.track)
                        continue;
                    var order = Enumerable.Range(0, boss.Parts.SacCount).Where(boss.Parts.SacAlive).OrderByDescending(i => boss.Parts.Sac(i).Current).ToList();
                    boss.HurtSac(order[0], electro.damage, you);
                    foreach (var other in order.Skip(1).Take(electro.chainJumps))
                        boss.HurtSac(other, electro.chainDamage, you);
                }
                // The bots hunt the sacs too
                var bots = Enumerable.Range(0, boss.Parts.SacCount).Where(boss.Parts.SacAlive).OrderByDescending(i => boss.Parts.Sac(i).Current).ToList();
                if (bots.Count > 0)
                    boss.HurtSac(bots[0], botSacDps * dt, null);
                return;
            }
            // The crash: keep 12 m off the wreck's place until it lands, then run in and fire at the gills
            primePos = Wreck;
            if (boss.Copter == CopterStage.Crashing)
            {
                var away = Wreck + (pos - Wreck).normalized * 12;
                if (Vector2.Distance(pos, Wreck) < 12)
                    MoveTo(away, dt);
                return;
            }
            windowTime += dt;
            var pistols = weapons.Get(WeaponIds.DualPistols);
            MoveTo(Wreck + (pos - Wreck).normalized * 10, dt);
            FirePrime(dt, pistols, pistols.spread, 1, false);
            BotFire(dt, botPrimeDps, BossPart.Body);
        }

        // ---------------------------------------------------------------- Phase 3

        private void Phase3(float dt)
        {
            // Prime walks (2 m/s) to the carrier, else to where the crown lies loose, else to 42
            var crown = ctf.Flag(Team.Blue);
            var goal = crown.Status == FlagStatus.Dropped ? new Vector2(crown.Position.x, crown.Position.z) : pos;
            // He stands still in an attack or a window
            if (boss.Planner.Current == null && !boss.Vulnerable)
                primePos = Vector2.MoveTowards(primePos, goal, 2 * dt);
            if (crown.Status == FlagStatus.Dropped && Vector2.Distance(primePos, goal) < 3)
                boss.PrimeTouchesCrown();

            if (!boss.CrownCaptured)
            {
                if (!carrying)
                {
                    var at = crown.Status == FlagStatus.Dropped ? new Vector2(crown.Position.x, crown.Position.z) : Wreck;
                    if (MoveTo(at, dt))
                        carrying = crown.Status == FlagStatus.Dropped ? ctf.TouchFlag(you, Team.Blue) : ctf.TouchStand(you, Team.Blue);
                }
                else if (MoveTo(RedStand, dt, true) && ctf.TouchStand(you, Team.Red))
                    carrying = false;
                // The bots harass Prime's hands while the crown is out
                BotHands(dt);
                return;
            }
            // The champion's minigun: fetch it from the plinth, then fire at the eyes from about 25 m, backing off when he is closer
            if (stage < 2)
            {
                if (MoveTo(Plinth, dt))
                    stage = 2;
                BotHands(dt);
                return;
            }
            var minigun = weapons.Get(WeaponIds.Minigun);
            var d = Vector2.Distance(pos, primePos);
            if (d < 22)
                pos += (pos - primePos).normalized * walkSpeed / who.detour * dt;
            else if (d > 28)
                MoveTo(primePos + (pos - primePos).normalized * 25, dt);
            pos = new Vector2(Mathf.Clamp(pos.x, -57, 57), Mathf.Clamp(pos.y, -37, 37));
            windowTime = boss.Vulnerable ? windowTime + dt : 0;
            // A burst is 3 s with 0.6 s to spin up and down; the spread grows 0.8 to 4.5 degrees through it
            var duty = minigun.burstSeconds / (minigun.burstSeconds + minigun.spinUp + minigun.spinDown);
            FirePrime(dt, minigun, 3.0f, busy > 0 ? 0 : duty, true);
            BotHands(dt);
        }

        private void BotHands(float dt)
        {
            for (var i = 0; i < 2; i++)
                if (boss.Parts.HandAlive(i))
                {
                    boss.HurtHand(i, botHandDps * dt, null);
                    break;
                }
        }
    }

    /// <summary>
    /// Ticket 33.9: the balance simulation's runs. Many seeded fights per difficulty and attacker, with the lengths per phase printed to the
    /// log (<c>BALANCE</c> lines) and the targets of ticket 33 section 6 asserted for a player who knows the fight at Normal.
    /// </summary>
    public sealed class BossBalanceTests
    {
        private const int Runs = 120;

        private static List<BossFightResult> Fights(BossDifficulty difficulty, BossAttacker who, bool retryEasier = false, float? pressure = null)
        {
            var list = new List<BossFightResult>();
            for (var seed = 1; seed <= Runs; seed++)
                list.Add(new BossFightSim(BossRules.For(difficulty, retryEasier), who, seed * 7919 + (int)difficulty) { pressure = pressure ?? 0.55f }.Play());
            return list;
        }

        private static float Mean(IEnumerable<float> values)
        {
            var list = values.ToList();
            return list.Count == 0 ? 0 : list.Average();
        }

        private static float Percentile(IEnumerable<float> values, float p)
        {
            var list = values.OrderBy(v => v).ToList();
            return list.Count == 0 ? 0 : list[Math.Min(list.Count - 1, (int)(p * list.Count))];
        }

        private static string Line(string label, List<BossFightResult> fights)
        {
            var won = fights.Where(f => f.won).ToList();
            return $"BALANCE {label}: won {won.Count}/{fights.Count}, timed out {fights.Count(f => f.timedOut)}, " +
                   $"fight s mean {Mean(won.Select(f => f.seconds)):0} (p10 {Percentile(won.Select(f => f.seconds), 0.1f):0}, p90 {Percentile(won.Select(f => f.seconds), 0.9f):0}); " +
                   $"phases s {Mean(won.Select(f => f.phaseSeconds[0])):0} / {Mean(won.Select(f => f.phaseSeconds[1])):0} / {Mean(won.Select(f => f.phaseSeconds[2])):0}; " +
                   $"lives lost {Mean(fights.Select(f => (float)f.livesLost)):0.0} (phase {Mean(fights.Select(f => (float)f.livesLostIn[0])):0.0} / " +
                   $"{Mean(fights.Select(f => (float)f.livesLostIn[1])):0.0} / {Mean(fights.Select(f => (float)f.livesLostIn[2])):0.0}); " +
                   $"stalled at phase 1/2/3: {fights.Count(f => f.timedOut && f.phaseReached == 1)}/{fights.Count(f => f.timedOut && f.phaseReached == 2)}/{fights.Count(f => f.timedOut && f.phaseReached == 3)}; kneels {Mean(fights.Select(f => (float)f.kneels)):0.0}; lost at phase 1/2/3: {fights.Count(f => !f.won && !f.timedOut && f.phaseReached == 1)}/{fights.Count(f => !f.won && !f.timedOut && f.phaseReached == 2)}/{fights.Count(f => !f.won && !f.timedOut && f.phaseReached == 3)}";
        }

        [Test]
        public void Report_AllDifficultiesAndAttackers()
        {
            var one = new BossFightSim(BossRules.For(BossDifficulty.Normal), BossAttacker.Expert(), 7919);
            one.Play();
            Debug.Log("BALANCE trace, Normal / expert, one fight:\nBALANCE " + string.Join("\nBALANCE ", one.trace));
            foreach (var who in new[] { BossAttacker.Expert(), BossAttacker.Average() })
                foreach (var difficulty in new[] { BossDifficulty.Easy, BossDifficulty.Normal, BossDifficulty.Hard })
                    Debug.Log(Line($"{difficulty} / {who.name}", Fights(difficulty, who)));
            foreach (var difficulty in new[] { BossDifficulty.Easy, BossDifficulty.Normal, BossDifficulty.Hard })
                Debug.Log(Line($"{difficulty} / expert, god mode (nothing hurts 42)", Fights(difficulty, BossAttacker.Expert(), false, 0)));
            Debug.Log(Line("Normal+RetryEasier / average", Fights(BossDifficulty.Normal, BossAttacker.Average(), true)));
            var normal = Fights(BossDifficulty.Normal, BossAttacker.Expert());
            var sources = normal.SelectMany(f => f.taken.Keys).Distinct().OrderBy(k => k);
            Debug.Log("BALANCE damage taken per fight, Normal / expert: " + string.Join(", ", sources.Select(k => $"{k} {Mean(normal.Select(f => f.taken.TryGetValue(k, out var v) ? v : 0)):0}")));
        }

        [Test]
        public void Normal_ForAPlayerWhoKnowsTheFight_TakesSixToEightMinutes_WithEachPhaseInItsRange()
        {
            var all = Fights(BossDifficulty.Normal, BossAttacker.Expert());
            var fights = all.Where(f => f.won).ToList();
            Assert.GreaterOrEqual(fights.Count, all.Count * 0.9f, "an expert wins nearly every fight");
            Assert.That(Mean(fights.Select(f => f.seconds + 12)), Is.InRange(360f, 480f), "the fight with its two transitions (6 minutes at least, ticket 33's acceptance)");
            Assert.That(Mean(fights.Select(f => f.phaseSeconds[0])), Is.InRange(120f, 190f), "phase 1");
            Assert.That(Mean(fights.Select(f => f.phaseSeconds[1])), Is.InRange(70f, 190f), "phase 2");
            Assert.That(Mean(fights.Select(f => f.phaseSeconds[2])), Is.InRange(90f, 250f), "phase 3");
        }

        [Test]
        public void Normal_ForAnAveragePlayer_TakesSevenToTenMinutes_AndTheyWinMostFights()
        {
            var all = Fights(BossDifficulty.Normal, BossAttacker.Average());
            var fights = all.Where(f => f.won).ToList();
            Assert.That(fights.Count, Is.InRange(all.Count * 0.5f, all.Count * 0.97f), "a first-time player wins about three fights in four");
            Assert.That(Mean(fights.Select(f => f.seconds + 12)), Is.InRange(420f, 600f), "section 6: 7 to 10 minutes");
            Assert.That(Mean(fights.Select(f => f.phaseSeconds[1])), Is.InRange(100f, 190f), "phase 2");
            Assert.That(Mean(fights.Select(f => f.phaseSeconds[2])), Is.InRange(100f, 250f), "phase 3");
        }

        [Test]
        public void EveryDifficulty_IsWinnableByAnExpert_InSixToTwelveMinutes()
        {
            foreach (var difficulty in new[] { BossDifficulty.Easy, BossDifficulty.Normal, BossDifficulty.Hard })
            {
                var all = Fights(difficulty, BossAttacker.Expert());
                var won = all.Where(f => f.won).ToList();
                // Hard is meant to cost lives: two a phase, 30% more damage, shorter wind-ups (ticket 33 section 7)
                Assert.GreaterOrEqual(won.Count, all.Count * (difficulty == BossDifficulty.Hard ? 0.35f : 0.9f), difficulty.ToString());
                Assert.That(Mean(won.Select(f => f.seconds + 12)), Is.InRange(360f, 720f), difficulty.ToString());
            }
        }

        [Test]
        public void Easy_IsEasierThanNormal_IsEasierThanHard_ForTheSameAttacker()
        {
            foreach (var who in new[] { BossAttacker.Expert(), BossAttacker.Average() })
            {
                float Cost(BossDifficulty d) => Mean(Fights(d, who).Select(f => (float)f.livesLost + (f.won ? 0 : 3)));
                var easy = Cost(BossDifficulty.Easy);
                var normal = Cost(BossDifficulty.Normal);
                var hard = Cost(BossDifficulty.Hard);
                Assert.LessOrEqual(easy, normal, who.name + " easy vs normal");
                Assert.LessOrEqual(normal, hard, who.name + " normal vs hard");
            }
        }

        [Test]
        public void RetryEasier_HelpsAnAveragePlayer_AndNoFightStalls()
        {
            var plain = Fights(BossDifficulty.Normal, BossAttacker.Average());
            var easier = Fights(BossDifficulty.Normal, BossAttacker.Average(), true);
            Assert.AreEqual(0, plain.Count(f => f.timedOut) + easier.Count(f => f.timedOut), "no fight stalls");
            Assert.GreaterOrEqual(easier.Count(f => f.won), plain.Count(f => f.won), "RETRY EASIER wins at least as often");
            Assert.Less(Mean(easier.Select(f => (float)f.livesLost)), Mean(plain.Select(f => (float)f.livesLost)) + 0.001f);
        }
    }
}
