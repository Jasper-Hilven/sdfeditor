using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SdfBattle.Editor
{
    /// <summary>
    /// Plays the Champion's Arena without a window through its three phases (ticket 33.6) with Prime scripted to be hurt: the rules are
    /// driven as the players would drive them (a red bot takes the blue flag and captures it; the sacs are burst; the crown is taken home),
    /// and the world must follow: the shield and the crew in phase 1, the Prime-copter and its sacs, the crash and the daze in phase 2,
    /// Prime on his feet by the wreck and the crown as the flag in phase 3, the champion's minigun on its plinth, the victory. Checks that
    /// Prime's attacks of each phase started (the clock runs fast), that 42 was never hurt past the test's protection, and fails on any
    /// error logged. Ticket 33.7 adds the HUD: the boss HUD exists, its lives, bar and cards follow the fight, a death costs a life, and
    /// RETRY PHASE restores the phase in place; with <c>-hudShots &lt;folder&gt;</c> (and no -nographics) it saves PNGs of the GUI at the
    /// key moments for a look. <c>Unity -batchmode -projectPath Unity -executeMethod SdfBattle.Editor.BossPlayTest.Run</c> (without -quit: it quits
    /// when done, exit code 1 on a failure).
    /// </summary>
    [InitializeOnLoad]
    public static class BossPlayTest
    {
        private const string ActiveKey = "SdfBattle.BossPlayTest";
        private const string ShotsKey = "SdfBattle.BossPlayTest.Shots";
        private const float Speed = 4;

        private static readonly List<string> errors = new List<string>();
        private static readonly HashSet<BossAttackKind> seen = new HashSet<BossAttackKind>();
        private static readonly List<string> log = new List<string>();
        private static int step;
        private static float stepStarted;
        private static float started = -1;
        private static bool hooked;
        private static Boss boss;
        private static Match match;
        private static string shots;
        private static bool captured;

        static BossPlayTest()
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if ((type == LogType.Exception || type == LogType.Error) && !stack.Contains("UnityEditor.Search"))
                    errors.Add(message + "\n" + stack);
            };
            EditorApplication.update += Update;
        }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, "-hudShots");
            SessionState.SetString(ShotsKey, at >= 0 && at + 1 < args.Length ? args[at + 1] : "");
            SessionState.SetBool(ActiveKey, true);
            EditorSceneManager.OpenScene("Assets/Scenes/Arena.unity");
            EditorApplication.EnterPlaymode();
        }

        private static void Expect(bool ok, string what)
        {
            if (!ok)
                throw new Exception("Expected: " + what);
        }

        // The GUI as the player sees it, into the -hudShots folder (nothing without one)
        private static void Shot(string name)
        {
            if (string.IsNullOrEmpty(shots))
                return;
            Directory.CreateDirectory(shots);
            SdfGui.Editor.GuiSnapshot.ShootOpen(match.gameObject.scene, Path.Combine(shots, name + ".png"));
        }

        private static void Note(string line)
        {
            log.Add($"[{Time.time:0.0}] {line}");
            Debug.Log("SDF boss play test: " + line);
        }

        private static void Update()
        {
            if (!EditorApplication.isPlaying)
                return;
            var now = Time.realtimeSinceStartup;
            if (started < 0)
                started = stepStarted = now;
            match = Match.Current;
            boss = Boss.Current;
            if (match == null || boss == null || boss.State == null)
            {
                if (now - started > 60)
                {
                    errors.Add("No match or boss after 60 s");
                    Finish();
                }
                return;
            }
            if (!hooked)
            {
                hooked = true;
                shots = SessionState.GetString(ShotsKey, "");
                Time.timeScale = Speed;
                boss.transitionSeconds = 2;
                boss.State.Planner.Started += attack => { seen.Add(attack.Kind); Note($"attack {attack.Kind} in {boss.State.Phase}"); };
                // 42 is never killed: the test drives the rules, it does not play
                Combat.Current.State.DamageTakenOf = victim => victim.IsBot ? 1 : 0;
            }
            try
            {
                if (!Advance(Time.time - gameStepStarted))
                    return;
                Note($"step {step} done");
                step++;
                gameStepStarted = Time.time;
            }
            catch (Exception exception)
            {
                errors.Add(exception.ToString());
                step = int.MaxValue;
            }
            if (step < 15 && now - started < 420)
                return;
            if (now - started >= 420)
                errors.Add($"Timed out in step {step}, phase {boss.State.Phase}");
            Finish();
        }

        private static float gameStepStarted;

        private static PlayerState RedBot() => match.State.PlayersOf(Team.Red).First(p => p.IsBot && p.Alive);

        // A red bot takes the crown home, as phase 3 asks (ends the immunity)
        private static void RedCrownCapture()
        {
            var flags = CaptureTheFlag.Current.State;
            var runner = RedBot();
            var blue = flags.Flag(Team.Blue);
            var red = flags.Flag(Team.Red);
            Expect(flags.TouchStand(runner, Team.Blue), $"a red bot takes the crown (blue flag {blue.Status}, red flag {red.Status}, runner alive {runner.Alive}, match {match.State.Phase})");
            Expect(flags.TouchStand(runner, Team.Red), $"and takes it home (red flag {red.Status})");
        }

        private static bool Advance(float waited)
        {
            var state = boss.State;
            var flags = CaptureTheFlag.Current.State;
            switch (step)
            {
                case 0: // phase 1: the shield, the crew, the dais
                    if (state.Phase != BossPhase.Phase1)
                        return false;
                    Expect(state.ShieldUp && boss.Shield.Up, "the shield is up in phase 1");
                    var blueAlive = match.State.PlayersOf(Team.Blue).Count(p => p.Alive);
                    Expect(blueAlive == boss.Rules.CrewCount(BossPhase.Phase1, false), $"the crew is {boss.Rules.CrewCount(BossPhase.Phase1, false)} in phase 1, got {blueAlive}");
                    Expect(!(boss.Copter != null && boss.Copter.Active), "no copter in phase 1");
                    Expect(boss.Arena.Phase == 1, "the arena shows phase 1");
                    Expect(boss.Body.transform.localScale.x > 4.5f, "Prime is 4.6 times");
                    Expect(boss.Hud != null, "the boss HUD is on the GUI");
                    Expect(boss.Objective.Contains("Capture"), "the objective says to capture the flag");
                    Shot("hud-1-name-card");
                    // Nothing hurts him behind the shield
                    var blocked = state.Hurt(100, BossPart.Gills, RedBot());
                    Expect(blocked.Blocked, "the shield stops damage");
                    return true;
                case 1: // let the mortar and the glare come
                    if (waited < 14)
                        return false;
                    Expect(seen.Contains(BossAttackKind.SporeMortar) || seen.Contains(BossAttackKind.RoyalGlare), "phase 1 attacks start");
                    boss.Hud.ShowHit(new BossHit(100, 0, true, false, false), boss.Body.transform.position + Vector3.up * 4);
                    Shot("hud-2-shield");
                    return true;
                case 2: // a red capture drops the shield
                    var bot = RedBot();
                    if (!captured)
                    {
                        captured = true;
                        Expect(flags.TouchStand(bot, Team.Blue), "a red bot takes the blue flag");
                        Expect(flags.TouchStand(bot, Team.Red), "and captures it");
                        Expect(!state.ShieldUp && state.Vulnerable && state.StunKind == BossStunKind.Kneel, "the capture stuns Prime");
                        Expect(!boss.Shield.Up, "the dome is down");
                        var gills = state.Hurt(100, BossPart.Gills, bot);
                        Expect(!gills.Blocked && gills.Dealt >= 199, $"the gills take double, got {gills.Dealt}");
                        boss.Hud.ShowHit(gills, boss.Body.transform.position + Vector3.up * 4);
                        // A death costs a life, once
                        var lives = state.Lives;
                        state.ReportDeath(match.You.State);
                        Expect(state.Lives == lives - 1 && state.LivesLost == 1, "a death costs 42 a life");
                        // The HUD draws it on its next frame
                        return false;
                    }
                    Shot("hud-3-stunned");
                    state.Hurt(state.CurrentBar + 1, BossPart.Gills, bot);
                    Expect(state.Phase == BossPhase.Transition12, "bar 1 empty goes to the transition");
                    return true;
                case 3: // the transition: the copter rises; then phase 2
                    if (state.Phase == BossPhase.Transition12)
                    {
                        Expect(boss.Copter != null && boss.Copter.Active, "the Prime-copter rises in the transition");
                        return false;
                    }
                    Expect(state.Phase == BossPhase.Phase2, "phase 2 follows");
                    Expect(boss.Arena.Phase == 2, "the arena shows phase 2");
                    var crew2 = match.State.PlayersOf(Team.Blue).Count(p => p.Alive);
                    Expect(crew2 <= boss.Rules.CrewCount(BossPhase.Phase2, false), $"the crew drops to 2, got {crew2}");
                    Expect(boss.Objective.Contains("sacs"), "the objective names the sacs");
                    Shot("hud-4-phase-2-card");
                    return true;
                case 4: // phase 2 plays: the cannon, the dive, the tank drop
                    if (waited < 22)
                        return false;
                    Expect(seen.Contains(BossAttackKind.CapCannon) || seen.Contains(BossAttackKind.DowndraftDive), "phase 2 attacks start");
                    return true;
                case 5: // the four sacs, the crash, the daze
                    for (var i = 0; i < 4; i++)
                        state.HurtSac(i, 500, RedBot());
                    Expect(state.Copter == CopterStage.Crashing, "the last sac starts the crash");
                    return true;
                case 6:
                    if (state.Copter == CopterStage.Crashing)
                        return false;
                    Expect(state.Copter == CopterStage.Dazed && boss.Copter.Down, "the copter lands and Prime is dazed");
                    Expect(state.Vulnerable, "the daze opens the gills");
                    return true;
                case 7: // the gills finish bar 2
                    state.Hurt(state.CurrentBar + 1, BossPart.Gills, RedBot());
                    Expect(state.Phase == BossPhase.Transition23, "bar 2 empty goes to the second transition");
                    return true;
                case 8:
                    if (state.Phase == BossPhase.Transition23)
                        return false;
                    Expect(state.Phase == BossPhase.Phase3, "phase 3 follows");
                    Expect(boss.Arena.Phase == 3, "the arena shows phase 3");
                    Expect(!boss.Copter.Active, "the copter is gone");
                    Expect(state.Immune, "Royal Immunity is on");
                    Shot("hud-5-phase-3-immune");
                    var wreck = boss.Spots.crash.position;
                    Expect(Vector3.Distance(boss.Body.transform.position, wreck) < 18, "Prime is by the wreck");
                    return true;
                case 9: // Prime walks and attacks on his feet
                    if (waited < 16)
                        return false;
                    Expect(seen.Contains(BossAttackKind.CapSlam) || seen.Contains(BossAttackKind.HandGrab) || seen.Contains(BossAttackKind.RoyalGlare), "phase 3 attacks start");
                    return true;
                case 10: // the crown comes home: immunity off, the champion's minigun out
                    // The red raider may have the crown already: start from the crown home
                    flags.Reset();
                    var runner = RedBot();
                    Expect(flags.TouchStand(runner, Team.Blue), "a red bot takes the crown");
                    Expect(!state.Immune, "taking the crown ends the immunity");
                    Expect(flags.TouchStand(runner, Team.Red), "and takes it to the red stand");
                    Expect(state.CrownCaptured, "the crown is captured");
                    var half = state.Hurt(100, BossPart.Body, runner);
                    var champion = state.Hurt(100, BossPart.Body, runner, true);
                    Expect(Math.Abs(champion.Dealt - half.Dealt * 1.5f) < 0.01f, $"the champion's minigun does x1.5, got {champion.Dealt} against {half.Dealt}");
                    return true;
                case 11: // RETRY PHASE: in place, from the phase's start: the bar full, the lives full, the lives lost still counted
                    state.Hurt(500, BossPart.Body, match.You.State);
                    Expect(state.CurrentBar < state.CurrentBarMax, "the bar has been hurt");
                    state.ReportDeath(match.You.State);
                    Expect(state.LivesLost == 2, "two lives lost so far");
                    boss.RetryPhase();
                    return true;
                case 12:
                    if (waited < 1 || state.Phase != BossPhase.Phase3 || match.State.Phase != RoundPhase.Playing)
                        return false;
                    Expect(state.CurrentBar == state.CurrentBarMax, "RETRY PHASE refills the phase's bar");
                    Expect(state.Lives == boss.Rules.lives, "and the lives");
                    Expect(state.LivesLost == 2, "the lives lost stay counted");
                    Expect(state.Immune, "the crown is Prime's again");
                    Shot("hud-6-retry-phase");
                    return true;
                case 13: // the last bar
                    flags.Reset();
                    RedCrownCapture();
                    state.Hurt(state.CurrentBar + 1, BossPart.Body, match.You.State);
                    return true;
                case 14: // the victory, and the end's numbers the screens read
                    Expect(state.Phase == BossPhase.Victory, "bar 3 empty is the victory");
                    Expect(match.State.Over, "the match is over");
                    Expect(state.LivesLost == 2 && state.DamageDealt > 0, $"the fight counted {state.LivesLost} lives lost and {state.DamageDealt} damage");
                    Expect(boss.PhaseReached == 3, "the fight got to phase 3");
                    var end = match.Hud.view.end;
                    Expect(end.set && end.won && end.boss && end.bossPhase == 3 && end.livesLost == 2, "the match's end is written for the boss screens");
                    Expect(end.damageDealt > 0 && end.seconds > 0, "with the damage and the time");
                    Shot("hud-7-victory-banner");
                    return waited > 1;
            }
            return true;
        }

        private static void Finish()
        {
            EditorApplication.update -= Update;
            Time.timeScale = 1;
            SessionState.SetBool(ActiveKey, false);
            foreach (var line in log.ToArray())
                Debug.Log(line);
            if (errors.Count == 0)
                Debug.Log($"SDF boss play test: PASSED, attacks seen: {string.Join(", ", seen)}");
            else
                foreach (var error in errors.ToArray())
                    Debug.LogError("SDF boss play test: FAILED: " + error);
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }
    }
}
