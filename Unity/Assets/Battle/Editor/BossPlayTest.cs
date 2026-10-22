using System;
using System.Collections.Generic;
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
    /// error logged. <c>Unity -batchmode -projectPath Unity -executeMethod SdfBattle.Editor.BossPlayTest.Run</c> (without -quit: it quits
    /// when done, exit code 1 on a failure).
    /// </summary>
    [InitializeOnLoad]
    public static class BossPlayTest
    {
        private const string ActiveKey = "SdfBattle.BossPlayTest";
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
            SessionState.SetBool(ActiveKey, true);
            EditorSceneManager.OpenScene("Assets/Scenes/Arena.unity");
            EditorApplication.EnterPlaymode();
        }

        private static void Expect(bool ok, string what)
        {
            if (!ok)
                throw new Exception("Expected: " + what);
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
            if (step < 12 && now - started < 420)
                return;
            if (now - started >= 420)
                errors.Add($"Timed out in step {step}, phase {boss.State.Phase}");
            Finish();
        }

        private static float gameStepStarted;

        private static PlayerState RedBot() => match.State.PlayersOf(Team.Red).First(p => p.IsBot && p.Alive);

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
                    // Nothing hurts him behind the shield
                    var blocked = state.Hurt(100, BossPart.Gills, RedBot());
                    Expect(blocked.Blocked, "the shield stops damage");
                    return true;
                case 1: // let the mortar and the glare come
                    if (waited < 14)
                        return false;
                    Expect(seen.Contains(BossAttackKind.SporeMortar) || seen.Contains(BossAttackKind.RoyalGlare), "phase 1 attacks start");
                    return true;
                case 2: // a red capture drops the shield
                    var bot = RedBot();
                    Expect(flags.TouchStand(bot, Team.Blue), "a red bot takes the blue flag");
                    Expect(flags.TouchStand(bot, Team.Red), "and captures it");
                    Expect(!state.ShieldUp && state.Vulnerable && state.StunKind == BossStunKind.Kneel, "the capture stuns Prime");
                    Expect(!boss.Shield.Up, "the dome is down");
                    var gills = state.Hurt(100, BossPart.Gills, bot);
                    Expect(!gills.Blocked && gills.Dealt >= 199, $"the gills take double, got {gills.Dealt}");
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
                case 11: // the last bar
                    state.Hurt(state.CurrentBar + 1, BossPart.Body, RedBot());
                    Expect(state.Phase == BossPhase.Victory, "bar 3 empty is the victory");
                    Expect(match.State.Over, "the match is over");
                    return waited > 1;
            }
            return true;
        }

        private static void Finish()
        {
            EditorApplication.update -= Update;
            Time.timeScale = 1;
            SessionState.SetBool(ActiveKey, false);
            foreach (var line in log)
                Debug.Log(line);
            if (errors.Count == 0)
                Debug.Log($"SDF boss play test: PASSED, attacks seen: {string.Join(", ", seen)}");
            else
                foreach (var error in errors)
                    Debug.LogError("SDF boss play test: FAILED: " + error);
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }
    }
}
