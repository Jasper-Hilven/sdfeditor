using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 24.3: the machines' rules. They target enemy players only (never machines, never their own team, never the dead or the
    /// protected), the nearest one they see; they hit for 6 about once a second after the bots' reaction time; they die at 0, give nobody
    /// kill credit, and come back after 30 s; a player they kill dies with the machine's name as the killer. And their motion: the barrel's
    /// tilt sign, the tank's steering round players, the helicopter's flight over the play area and over a castle.
    /// </summary>
    public sealed class MachineTests
    {
        private static readonly Vector3 Eye = Vector3.zero;

        // ---------------------------------------------------------------- Numbers

        [Test]
        public void TheDefaults_AreTheUsers()
        {
            var rules = new MachineRules();
            Assert.AreEqual(1, rules.helicoptersPerSide);
            Assert.AreEqual(2, rules.tanksPerSide);
            Assert.AreEqual(60, rules.tank.health);
            Assert.AreEqual(60, rules.helicopter.health);
            Assert.AreEqual(90, rules.turret.health);
            Assert.AreEqual(25, rules.tank.range);
            Assert.AreEqual(35, rules.helicopter.range);
            Assert.AreEqual(30, rules.turret.range);
            foreach (var kind in new[] { MachineKind.Tank, MachineKind.Helicopter, MachineKind.Turret })
            {
                var stats = rules.StatsOf(kind);
                Assert.AreEqual(kind, stats.kind);
                Assert.AreEqual(WeaponTable.Default().Get(WeaponIds.Pistol).damage / 2, stats.damage, 1e-4f, "half the pistol's");
                Assert.AreEqual(1, stats.shotInterval, 1e-4f);
                Assert.AreEqual(30, stats.respawnSeconds, 1e-4f);
            }
            Assert.AreEqual(0, rules.turret.speed);
            Assert.AreEqual("Blue tank", MachineRules.NameOf(MachineKind.Tank, Team.Blue));
            Assert.AreEqual("Red helicopter", MachineRules.NameOf(MachineKind.Helicopter, Team.Red));
        }

        // ---------------------------------------------------------------- Targets

        [Test]
        public void Targets_EnemyPlayersOnly()
        {
            var candidates = new List<MachineTarget>
            {
                MachineTarget.Player(0, Team.Red, new Vector3(0, 0, 3)),
                new MachineTarget { id = 1, team = Team.Blue, isPlayer = false, alive = true, hurtable = true, position = new Vector3(0, 0, 4) },
                MachineTarget.Player(2, Team.Blue, new Vector3(0, 0, 5), alive: false),
                MachineTarget.Player(3, Team.Blue, new Vector3(0, 0, 6), hurtable: false),
                MachineTarget.Player(4, Team.Blue, new Vector3(0, 0, 20)),
            };
            // A red machine: its own team's player, the enemy machine, the dead and the protected are passed over
            Assert.AreEqual(4, MachineTargeting.Choose(Eye, Team.Red, 25, candidates, null));
            // A blue machine: only the red player is an enemy
            Assert.AreEqual(0, MachineTargeting.Choose(Eye, Team.Blue, 25, candidates, null));
            Assert.IsFalse(MachineTargeting.IsEnemy(Team.Red, candidates[1]), "never a machine");
            Assert.IsFalse(MachineTargeting.IsEnemy(Team.Red, candidates[0]), "never its own team");
        }

        [Test]
        public void Targets_TheNearestSeen_WithinRange()
        {
            var candidates = new List<MachineTarget>
            {
                MachineTarget.Player(5, Team.Blue, new Vector3(0, 0, 12)),
                MachineTarget.Player(6, Team.Blue, new Vector3(0, 0, 8)),
                MachineTarget.Player(7, Team.Blue, new Vector3(0, 0, 40)),
            };
            Assert.AreEqual(6, MachineTargeting.Choose(Eye, Team.Red, 30, candidates, null));
            // The nearest behind a wall: the next one it sees
            Assert.AreEqual(5, MachineTargeting.Choose(Eye, Team.Red, 30, candidates, t => t.id != 6));
            // Out of range: nobody
            Assert.AreEqual(-1, MachineTargeting.Choose(Eye, Team.Red, 30, new[] { candidates[2] }, null));
            Assert.AreEqual(-1, MachineTargeting.Choose(Eye, Team.Red, 30, candidates, t => false));
        }

        [Test]
        public void Targets_AskTheLineOfSight_NearestFirst_UntilOneIsSeen()
        {
            var candidates = new List<MachineTarget>
            {
                MachineTarget.Player(1, Team.Blue, new Vector3(0, 0, 20)),
                MachineTarget.Player(2, Team.Blue, new Vector3(0, 0, 5)),
                MachineTarget.Player(3, Team.Blue, new Vector3(0, 0, 10)),
                MachineTarget.Player(4, Team.Red, new Vector3(0, 0, 1)),
            };
            var asked = new List<int>();
            var chosen = MachineTargeting.Choose(Eye, Team.Red, 30, candidates, t =>
            {
                asked.Add(t.id);
                return t.id != 2;
            });
            Assert.AreEqual(3, chosen);
            CollectionAssert.AreEqual(new[] { 2, 3 }, asked, "one ray per candidate, nearest first, none after the first seen, none for friends");
        }

        [Test]
        public void Targets_KeepTheCurrentOne_UnlessAnotherIsClearlyNearer()
        {
            var candidates = new List<MachineTarget>
            {
                MachineTarget.Player(1, Team.Blue, new Vector3(0, 0, 10)),
                MachineTarget.Player(2, Team.Blue, new Vector3(0, 0, 11.5f)),
            };
            Assert.AreEqual(2, MachineTargeting.Choose(Eye, Team.Red, 30, candidates, null, current: 2));
            candidates[0] = MachineTarget.Player(1, Team.Blue, new Vector3(0, 0, 6));
            Assert.AreEqual(1, MachineTargeting.Choose(Eye, Team.Red, 30, candidates, null, current: 2));
        }

        // ---------------------------------------------------------------- Firing

        [Test]
        public void TheGun_WaitsTheReaction_ThenFiresAboutOnceASecond()
        {
            var stats = MachineStats.Of(MachineKind.Turret);
            var gun = new MachineGun(stats, new BotSkill(0.45f), new System.Random(7));
            Assert.IsFalse(gun.Tick(0.1f, true), "no target, no shot");
            gun.Acquire(3);
            var reaction = gun.Aim.ReactionLeft;
            Assert.Greater(reaction, 0.2f);
            var shots = new List<float>();
            for (var t = 0f; t < 60; t += 0.02f)
                if (gun.Tick(0.02f, true))
                    shots.Add(t);
            Assert.GreaterOrEqual(shots[0], reaction - 0.03f, "not before its reaction");
            Assert.That(shots.Count, Is.InRange(52, 70), "about one a second");
            for (var i = 1; i < shots.Count; i++)
                Assert.That(shots[i] - shots[i - 1], Is.InRange(0.84f, 1.17f));
        }

        [Test]
        public void TheGun_HoldsFire_UntilTurnedOntoItsAim()
        {
            var gun = new MachineGun(MachineStats.Of(MachineKind.Tank), new BotSkill(0.45f), new System.Random(8));
            gun.Acquire(1);
            for (var i = 0; i < 100; i++)
                Assert.IsFalse(gun.Tick(0.05f, false));
            Assert.IsTrue(gun.Tick(0.05f, true));
            gun.Reset();
            Assert.AreEqual(-1, gun.Target);
            Assert.IsFalse(gun.Tick(0.05f, true));
        }

        [Test]
        public void TheGun_AimsWithTheBotsError()
        {
            var gun = new MachineGun(MachineStats.Of(MachineKind.Helicopter), new BotSkill(0.45f), new System.Random(9));
            gun.Acquire(2);
            for (var i = 0; i < 200; i++)
                gun.Tick(0.05f, false);
            Assert.Greater(gun.Aim.Error, 0, "never perfect");
            Assert.AreEqual(gun.Aim.Skill.SteadyError, gun.Aim.Error, 0.1f);
        }

        [Test]
        public void AMachinesHit_Does6_AndKillsAPlayer_AsTheMachine_WithNoKillCredit()
        {
            var match = new MatchState(new MatchRules { countdownSeconds = 0 });
            var red = match.AddPlayer("Red 1", Team.Red, true);
            var blue = match.AddPlayer("Blue 1", Team.Blue, true);
            var combat = new CombatState(match, WeaponTable.Default());
            match.Begin();
            match.Tick(3);
            var deaths = new List<Death>();
            match.Died += deaths.Add;
            var stats = MachineStats.Of(MachineKind.Tank);
            var tank = new MachineState(0, MachineKind.Tank, Team.Blue, stats);
            Damage Shot() => Damage.ByMachine(tank.Name, tank.Team, stats.weapon, stats.damage, Vector3.zero);

            Assert.AreEqual(0, combat.Hurt(blue, Shot()), "never its own team");
            Assert.AreEqual(6, combat.Hurt(red, Shot()), 1e-4f);
            Assert.AreEqual(94, red.Health, 1e-4f);
            var hits = 1;
            while (red.Alive && hits < 100)
            {
                combat.Hurt(red, Shot());
                hits++;
            }
            Assert.AreEqual(17, hits, "100 health in hits of 6");
            Assert.AreEqual(1, deaths.Count);
            var death = deaths[0];
            Assert.AreEqual(DeathCause.Killed, death.Cause);
            Assert.IsNull(death.Killer);
            Assert.AreEqual("Blue tank", death.KillerName);
            Assert.AreEqual(Team.Blue, death.KillerTeam);
            Assert.AreEqual("cannon", death.Weapon);
            Assert.IsFalse(death.CountsAsKill);
            Assert.AreEqual(0, blue.Kills);
        }

        // ---------------------------------------------------------------- Damage, death and respawn

        [Test]
        public void AMachine_TakesDamage_DiesAtZero_AndGivesNoKillCredit()
        {
            var match = new MatchState(new MatchRules { countdownSeconds = 0 });
            var red = match.AddPlayer("Red 1", Team.Red, true);
            var combat = new CombatState(match, WeaponTable.Default());
            match.Begin();
            match.Tick(3);
            var reported = 0;
            match.Died += _ => reported++;
            var tank = new MachineState(1, MachineKind.Tank, Team.Blue);
            var died = 0;
            tank.Died += (m, d) => died++;
            Assert.AreEqual(60, tank.Health.Current);

            Assert.AreEqual(45, tank.Hurt(Damage.By(red, WeaponIds.Sniper, 45), combat.CanHurt(tank.Team, red.Team)), 1e-4f);
            Assert.IsTrue(tank.Alive);
            Assert.AreEqual(15, tank.Hurt(Damage.By(red, WeaponIds.Sniper, 45), combat.CanHurt(tank.Team, red.Team)), 1e-4f, "no more than was left");
            Assert.IsFalse(tank.Alive);
            Assert.AreEqual(1, died);
            Assert.AreEqual(0, tank.Hurt(Damage.By(red, WeaponIds.Sniper, 85)), "the dead take nothing");
            Assert.AreEqual(1, died);
            Assert.AreEqual(0, red.Kills, "no kill credit");
            Assert.AreEqual(0, reported, "not a player's death: not in the kill feed");
        }

        [Test]
        public void AMachine_IsNotHurt_ByItsOwnTeam_OrInTheCountdown()
        {
            var match = new MatchState(new MatchRules());
            var red = match.AddPlayer("Red 1", Team.Red, true);
            var blue = match.AddPlayer("Blue 1", Team.Blue, true);
            var combat = new CombatState(match, WeaponTable.Default());
            match.Begin();
            var turret = new MachineState(2, MachineKind.Turret, Team.Red);
            Assert.AreEqual(0, turret.Hurt(Damage.By(blue, WeaponIds.Pistol, 12), combat.CanHurt(turret.Team, blue.Team)), "countdown");
            match.Tick(match.Rules.countdownSeconds + 0.1f);
            Assert.AreEqual(0, turret.Hurt(Damage.By(red, WeaponIds.Pistol, 12), combat.CanHurt(turret.Team, red.Team)), "own team");
            Assert.AreEqual(12, turret.Hurt(Damage.By(blue, WeaponIds.Pistol, 12), combat.CanHurt(turret.Team, blue.Team)), 1e-4f);
            Assert.AreEqual(78, turret.Health.Current, 1e-4f);
        }

        [Test]
        public void AMachine_ComesBack_After30Seconds_AtFullHealth()
        {
            foreach (var kind in new[] { MachineKind.Tank, MachineKind.Helicopter, MachineKind.Turret })
            {
                var machine = new MachineState(0, kind, Team.Red);
                var back = 0;
                machine.Respawned += _ => back++;
                Assert.IsFalse(machine.Tick(100), "alive: nothing to wait for");
                machine.Hurt(Damage.ByMachine("x", Team.Blue, "test", 1000));
                Assert.IsFalse(machine.Alive);
                Assert.AreEqual(30, machine.RespawnIn, 1e-4f);
                for (var t = 0; t < 29; t++)
                    Assert.IsFalse(machine.Tick(1), $"{kind} still waiting at {t + 1} s");
                Assert.IsFalse(machine.Alive);
                Assert.IsTrue(machine.Tick(1.01f));
                Assert.IsTrue(machine.Alive);
                Assert.AreEqual(1, back);
                Assert.AreEqual(machine.Stats.health, machine.Health.Current, 1e-4f);
                Assert.AreEqual(kind == MachineKind.Turret, machine.RespawnsInPlace, "turrets in place, the others in the zone");
            }
        }

        [Test]
        public void ANewRound_BringsEveryMachineBack()
        {
            var machine = new MachineState(0, MachineKind.Helicopter, Team.Blue);
            machine.Hurt(Damage.ByMachine("x", Team.Red, "test", 30));
            machine.Reset();
            Assert.AreEqual(60, machine.Health.Current, 1e-4f);
            var back = 0;
            machine.Respawned += _ => back++;
            machine.Hurt(Damage.ByMachine("x", Team.Red, "test", 1000));
            machine.Tick(5);
            machine.Reset();
            Assert.IsTrue(machine.Alive);
            Assert.AreEqual(0, machine.RespawnIn);
            Assert.AreEqual(1, back);
        }

        // ---------------------------------------------------------------- Motion

        [Test]
        public void TheBarrel_TiltsNegative_ToPointUp()
        {
            // Unity: a positive angle about x turns +z down; the barrels point along +z, so raising them is negative
            Assert.Less(MachineAim.BarrelForward(30).y, 0);
            var tilt = MachineAim.TiltFor(new Vector3(0, 1, 1), -40, 40);
            Assert.AreEqual(-40, tilt, 1e-3f, "45 degrees up, held at the highest elevation");
            tilt = MachineAim.TiltFor(new Vector3(0, 1, 2), -40, 40);
            var forward = MachineAim.BarrelForward(tilt);
            var wanted = new Vector3(0, 1, 2).normalized;
            Assert.AreEqual(wanted.y, forward.y, 1e-4f);
            Assert.AreEqual(wanted.z, forward.z, 1e-4f);
            Assert.Greater(MachineAim.TiltFor(new Vector3(3, -1, 0), -40, 40), 0, "down is positive");
            Assert.AreEqual(90, MachineAim.YawTo(Vector3.right), 1e-4f);
            Assert.AreEqual(0, MachineAim.YawTo(Vector3.forward), 1e-4f);
        }

        [Test]
        public void TheTank_StopsForAPlayerRightAhead_AndSteersRoundOneAside()
        {
            var heading = Vector2.up;
            Assert.AreEqual(0, MachineSteering.Avoid(Vector2.zero, heading, new[] { new Vector2(0, 0.9f) }, 1, 3, out _), 1e-4f);
            Assert.AreEqual(1, MachineSteering.Avoid(Vector2.zero, heading, new[] { new Vector2(2.5f, 1) }, 1, 3, out var none), 1e-4f);
            Assert.AreEqual(Vector2.zero, none);
            Assert.AreEqual(1, MachineSteering.Avoid(Vector2.zero, heading, new[] { new Vector2(0, -1.5f) }, 1, 3, out _), 1e-4f, "behind");
            var factor = MachineSteering.Avoid(Vector2.zero, heading, new[] { new Vector2(-0.4f, 2.2f) }, 1, 3, out var steer);
            Assert.That(factor, Is.InRange(0.01f, 0.99f), "slows");
            Assert.Greater(steer.x, 0, "a player ahead on the left: turns right");
            MachineSteering.Avoid(Vector2.zero, heading, new[] { new Vector2(0.4f, 2.2f) }, 1, 3, out steer);
            Assert.Less(steer.x, 0);
        }

        [Test]
        public void TheHelicopter_RisesFirst_ThenFlies6To12MetresUp_InThePlayArea()
        {
            var min = new Vector2(-75, -50);
            var max = new Vector2(75, 50);
            var pilot = new HelicopterPilot(min, max, (x, z) => 0, new System.Random(3));
            pilot.Start(new Vector3(-70, 0, 22), 90);
            var ticks = 0;
            while (pilot.TakingOff && ticks++ < 400)
            {
                pilot.Tick(0.05f);
                if (!pilot.TakingOff)
                    break;
                Assert.AreEqual(-70, pilot.Position.x, 1e-3f, "straight up first");
                Assert.AreEqual(22, pilot.Position.z, 1e-3f);
            }
            Assert.IsFalse(pilot.TakingOff);
            Assert.Greater(ticks, 20, "it took a moment to rise");
            Assert.GreaterOrEqual(pilot.Position.y, 0.75f * pilot.MinHeight - 0.2f);
            var waypoints = new HashSet<Vector3>();
            for (var t = 0f; t < 300; t += 0.05f)
            {
                pilot.Tick(0.05f);
                var p = pilot.Position;
                Assert.That(p.x, Is.InRange(min.x, max.x));
                Assert.That(p.z, Is.InRange(min.y, max.y));
                if (!pilot.TakingOff && t > 10)
                    Assert.That(p.y, Is.InRange(5.5f, 12.5f), $"at {t} s");
                Assert.LessOrEqual(Mathf.Abs(pilot.Pitch), pilot.MaxLean + 1e-3f);
                waypoints.Add(pilot.Waypoint);
            }
            Assert.Greater(waypoints.Count, 6, "roams from point to point");
        }

        [Test]
        public void TheHelicopter_ClearsACastle_ByItsMinimumHeight()
        {
            // A 15 m block in the middle of the field, the way between every pair of waypoints crossing it now and then
            float Surface(float x, float z) => Mathf.Abs(x) < 8 && Mathf.Abs(z) < 30 ? 15 : 0;
            var pilot = new HelicopterPilot(new Vector2(-75, -50), new Vector2(75, 50), Surface, new System.Random(5));
            pilot.Start(new Vector3(-70, 0, 0), 90);
            var crossed = 0;
            for (var t = 0f; t < 600; t += 0.05f)
            {
                pilot.Tick(0.05f);
                var p = pilot.Position;
                if (Surface(p.x, p.z) > 0)
                {
                    crossed++;
                    Assert.GreaterOrEqual(p.y, 15 + pilot.MinHeight - 1, $"over the castle at {t} s");
                }
            }
            Assert.Greater(crossed, 0, "it did cross");
        }

        // ---------------------------------------------------------------- Keep-outs (ticket 30.7)

        [Test]
        public void KeepOutRules_FindPointsNearAndLegsAcross_AndLetOneInsideLeave()
        {
            var boxes = new[] { new KeepOut(new Vector2(-5, -5), new Vector2(5, 5)) };
            Assert.IsTrue(KeepOutRules.Near(boxes, new Vector2(0, 0), 1));
            Assert.IsTrue(KeepOutRules.Near(boxes, new Vector2(5.5f, 0), 1), "within the margin");
            Assert.IsFalse(KeepOutRules.Near(boxes, new Vector2(7, 0), 1));
            Assert.IsFalse(KeepOutRules.Near(null, Vector2.zero, 1));

            Assert.IsTrue(KeepOutRules.Blocks(boxes, new Vector2(-10, 0), new Vector2(10, 0), 1), "straight across");
            Assert.IsTrue(KeepOutRules.Blocks(boxes, new Vector2(-10, 5.5f), new Vector2(10, 5.5f), 1), "grazing inside the margin");
            Assert.IsFalse(KeepOutRules.Blocks(boxes, new Vector2(-10, 7), new Vector2(10, 7), 1), "past it");
            Assert.IsFalse(KeepOutRules.Blocks(boxes, new Vector2(-10, 0), new Vector2(-7, 0), 1), "stops short");
            Assert.IsFalse(KeepOutRules.Blocks(boxes, new Vector2(5.5f, 0), new Vector2(20, 0), 1), "leaving from inside the margin");

            Assert.AreEqual(new Vector2(5, 1), KeepOutRules.PushOut(boxes, new Vector2(4, 1)));
            Assert.AreEqual(new Vector2(7, 1), KeepOutRules.PushOut(boxes, new Vector2(7, 1)));
        }

        [Test]
        public void TheHelicopter_NeverFliesIntoAKeepOut_NorAcrossOne()
        {
            // A wall of tall models across the middle of the field with one gap, and a block the pilot starts beside
            var boxes = new[]
            {
                new KeepOut(new Vector2(-6, -50), new Vector2(6, -10)),
                new KeepOut(new Vector2(-6, 10), new Vector2(6, 50)),
                new KeepOut(new Vector2(-60, -3), new Vector2(-50, 3)),
            };
            var pilot = new HelicopterPilot(new Vector2(-75, -50), new Vector2(75, 50), (x, z) => 0, new System.Random(9)) { KeepOuts = boxes };
            pilot.Start(new Vector3(-70, 0, 0), 90);
            var crossedTheGap = false;
            var last = pilot.Position;
            for (var t = 0f; t < 900; t += 0.05f)
            {
                pilot.Tick(0.05f);
                var p = pilot.Position;
                foreach (var box in boxes)
                    Assert.IsFalse(box.Contains(new Vector2(p.x, p.z)), $"inside {box.min}..{box.max} at {t} s: {p}");
                if (last.x < 0 && p.x >= 0)
                    crossedTheGap = true;
                last = p;
            }
            Assert.IsTrue(crossedTheGap, "it still gets from one half to the other, through the gap");
        }

        [Test]
        public void TheHelicopter_HoversRatherThanCrossing_WhenEveryWayIsBlocked()
        {
            var boxes = new[] { new KeepOut(new Vector2(-1, -50), new Vector2(1, 50)) };
            var pilot = new HelicopterPilot(new Vector2(-75, -50), new Vector2(75, 50), (x, z) => 0, new System.Random(2)) { KeepOuts = boxes, Margin = 6 };
            pilot.Start(new Vector3(-30, 0, 0), 0);
            for (var t = 0f; t < 300; t += 0.05f)
            {
                pilot.Tick(0.05f);
                Assert.Less(pilot.Position.x, 1, $"at {t} s");
            }
        }

        [Test]
        public void TheHelicopter_SlowsWhileFighting_AndTurnsItsNoseToTheTarget()
        {
            var pilot = new HelicopterPilot(new Vector2(-75, -50), new Vector2(75, 50), (x, z) => 0, new System.Random(4));
            pilot.Start(new Vector3(0, 0, 0), 0);
            for (var t = 0f; t < 20; t += 0.05f)
                pilot.Tick(0.05f);
            var target = pilot.Position + new Vector3(10, -8, 0);
            for (var t = 0f; t < 6; t += 0.05f)
                pilot.Tick(0.05f, target);
            var flat = new Vector2(pilot.Velocity.x, pilot.Velocity.z);
            Assert.LessOrEqual(flat.magnitude, pilot.EngagedSpeed + 0.01f);
            var to = target - pilot.Position;
            Assert.AreEqual(0, Mathf.DeltaAngle(pilot.Yaw, MachineAim.YawTo(to)), 5);
        }
    }
}
