using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfBattle;
using SdfGui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// The endless game's play test (ticket 42; the campaign play test was removed in 41): Boot in Play mode, five matches driven through the
    /// GUI's own commands. Match 1 starts on the rotation's first pick with the warm-up (cleared here); the others are the rotation's picks. Match 2 is
    /// restarted from the pause menu (same level and team, nothing recorded). Match 3 opens CREDITS from the pause menu, waits for the
    /// credits, and comes back to the pause menu. Each match ends by captures (you 3-0, 1-3, 0-3, 3-0, 3-2), then CONTINUE after a win or
    /// RETRY after a loss. It checks the team, the enemy side's skill for the difficulty the match was made for, the difficulty after each
    /// result, that no level is among the last three, the warm-up on the first match only, and that the save holds the result.
    /// <para>
    /// <c>Run</c> plays the five matches from a clean save and writes the trace to <c>sdf-playtest-42-trace.txt</c> in the temporary folder.
    /// <c>CheckReload</c>, in a new process, starts Boot again and checks that the save came back: the first match is the rotation's pick (not one of the
    /// save's last three), made for the difficulty the five left, and the save's last three levels are that pick after the trace's last two. Batch:
    /// <c>Unity -batchmode -projectPath Unity -executeMethod SdfCampaign.Editor.GameLoopPlayTest.Run</c> (no -quit: it exits itself, code 1 on a failure).
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class GameLoopPlayTest
    {
        private const string ActiveKey = "SdfCampaign.GameLoopPlayTest";
        private const string ModeKey = "SdfCampaign.GameLoopPlayTest.Mode";
        private const int Matches = 5;
        // The captures that end each match: yours and the enemy's; the enemy's come first
        private static readonly int[] YoursWanted = { 3, 1, 0, 3, 3 };
        private static readonly int[] EnemyWanted = { 0, 3, 3, 0, 2 };
        private const float CreditsSeconds = 47;

        private static readonly List<string> errors = new List<string>();
        private static readonly List<string> trace = new List<string>();
        private static readonly List<string> levels = new List<string>();
        private static readonly List<Difficulty> made = new List<Difficulty>();
        private static readonly List<Difficulty> after = new List<Difficulty>();

        private static bool checking, finished, waitReported, creditsChecked;
        private static int index, phase, yoursDone, enemyDone;
        private static float since, started = -1;
        private static bool restarted, credited, warmedFirst;
        private static Team yours;
        private static string restartLevel;
        private static Team restartTeam;
        private static Match match, ended;

        static GameLoopPlayTest()
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;
            checking = SessionState.GetString(ModeKey, "walk") == "check";
            SceneLoader.AutoPlay = true;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if ((type == LogType.Exception || type == LogType.Error) && !stack.Contains("UnityEditor.Search"))
                    errors.Add(message + "\n" + stack);
            };
            EditorApplication.update += Update;
        }

        /// <summary>Plays the five matches from a clean save.</summary>
        public static void Run()
        {
            PlayerPrefs.DeleteKey(GameSave.Key);
            Begin("walk");
        }

        /// <summary>Checks, in a new process, that the save of the five matches came back.</summary>
        public static void CheckReload() => Begin("check");

        private static void Begin(string mode)
        {
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(ModeKey, mode);
            EditorSceneManager.OpenScene(BootBuilder.ScenePath);
            EditorApplication.EnterPlaymode();
        }

        private static void Expect(bool ok, string what)
        {
            if (!ok)
                throw new Exception("Expected: " + what);
        }

        private static void Same<T>(IEnumerable<T> expected, IEnumerable<T> actual, string what)
        {
            var a = string.Join(",", expected);
            var b = string.Join(",", actual);
            Expect(a == b, $"{what}: {a} (was {b})");
        }

        private static string Active => SceneManager.GetActiveScene().name;

        private static void Update()
        {
            if (finished || !EditorApplication.isPlaying)
                return;
            var now = Time.realtimeSinceStartup;
            if (started < 0)
                started = since = now;
            var loop = UnityEngine.Object.FindAnyObjectByType<GameLoop>();
            var flow = UnityEngine.Object.FindAnyObjectByType<GuiFlow>();
            if (loop == null || flow == null)
                return;
            try
            {
                Step(loop, flow, now);
            }
            catch (Exception exception)
            {
                errors.Add(exception.ToString());
                Finish();
                return;
            }
            if (now - started > 900)
            {
                errors.Add($"timed out: match {index + 1}, phase {phase}, scene {Active}");
                Finish();
            }
        }

        private static void Step(GameLoop loop, GuiFlow flow, float now)
        {
            var loader = UnityEngine.Object.FindAnyObjectByType<SceneLoader>();
            if (loader != null && loader.Busy)
                return;
            switch (phase)
            {
                case 0: // a match is live: the loop says this scene, and the match is its new one
                    if (Active == "Boot" || Match.Current == null || Match.Current == ended || now - since < 1)
                        return;
                    match = Match.Current;
                    if (match.State == null || loop.Current != Active)
                        return;
                    var warm = UnityEngine.Object.FindAnyObjectByType<WarmUpPlayer>();
                    if (warm != null)
                    {
                        // The first match's warm-up is cleared here: the test does not move, the match is let go
                        Expect(index == 0, "only the first match has the warm-up");
                        warmedFirst = true;
                        warm.Stop();
                        return;
                    }
                    Expect(index != 0 || warmedFirst, "the first match has the warm-up");
                    if (match.Held)
                        return;
                    Live(loop, flow, now);
                    return;
                case 1: // the round: captures reported until it ends
                    if (match.State.Phase != RoundPhase.Playing)
                    {
                        Wait(now, "the round to play");
                        return;
                    }
                    Captures(flow, now);
                    return;
                case 2: // the end screen: the difficulty has moved; CONTINUE after a win, RETRY after a loss
                    if (flow.Ending || (flow.screen != GuiScreen.Victory && flow.screen != GuiScreen.Defeat))
                    {
                        Wait(now, "the end screen");
                        return;
                    }
                    Ended(loop, flow, now);
                    return;
                case 3: // the credits roll over the paused match and come back to the pause menu
                    if (UnityEngine.Object.FindAnyObjectByType<CreditsPlayer>() != null)
                    {
                        if (!creditsChecked && now - since > 0.2f)
                        {
                            creditsChecked = true;
                            Expect(flow.IsPaused && Mathf.Approximately(Time.timeScale, 0), "the match stays paused under the credits");
                        }
                        if (flow.screen != GuiScreen.Credits)
                            errors.Add($"the credits left the screen for {flow.screen}");
                        Wait(now, "the credits to end");
                        return;
                    }
                    Expect(flow.screen == GuiScreen.Pause, "the credits come back to the pause menu");
                    Expect(flow.IsPaused && Mathf.Approximately(Time.timeScale, 0), "the match is still paused after the credits");
                    trace.Add("  credits from the pause menu: rolled, and back on the pause menu");
                    flow.Execute(GuiCommandId.Resume, GuiScreen.Hud);
                    phase = 1;
                    since = now;
                    return;
            }
        }

        private static void Live(GameLoop loop, GuiFlow flow, float now)
        {
            var setup = match.Setup;
            Expect(setup != null, "the match plays by the loop's setup");
            yours = match.yourTeam;
            var enemy = yours.Other();
            var level = Active;
            // Your side keeps its skill; the enemy carries the difficulty the match was made for
            Expect(Mathf.Approximately(setup.SkillOf(yours), 0.5f), $"your side keeps its skill ({setup.SkillOf(yours)})");
            Expect(setup.yourTeam == yours, "the setup names the team you play");
            var difficulty = DifficultyOf(setup.SkillOf(enemy));
            if (index == 0)
            {
                Expect(System.Array.IndexOf(LevelRotation.Levels, level) >= 0, $"boot starts on one of the seven levels ({level})");
                if (!checking)
                    Expect(difficulty == Difficulty.Normal, "a fresh save starts on Normal");
            }
            if (index == 1 && !restarted)
            {
                // Match 2 is restarted from the pause menu: the same level and team, and nothing is recorded
                var before = GameSave.Load(new GameStorage()).lastLevels;
                restarted = true;
                restartLevel = level;
                restartTeam = yours;
                trace.Add($"match 2: {level} for {yours}, restarted from the pause menu");
                ended = match;
                flow.Show(GuiScreen.Pause);
                flow.Execute(GuiCommandId.Restart, GuiScreen.Hud);
                Same(before, GameSave.Load(new GameStorage()).lastLevels, "the restart records nothing");
                since = now;
                return;
            }
            if (index == 1)
                Expect(level == restartLevel && yours == restartTeam, "the restart plays the same level and team");
            // A match is recorded once, when it first goes live (a restart is the same match); it is never one of the three before it
            if (levels.Count == index)
            {
                var last = levels.Skip(Math.Max(0, levels.Count - 3)).ToList();
                Expect(!last.Contains(level), $"{level} is not one of the last three ({string.Join(", ", last)})");
                levels.Add(level);
                made.Add(difficulty);
                trace.Add($"match {index + 1}: {level}, you {yours}, made {difficulty} (enemy {enemy} skill {setup.SkillOf(enemy):0.00}, size {(enemy == Team.Red ? setup.redSize : setup.blueSize)})");
            }
            if (checking)
            {
                // The reload needs no more than the first match: its difficulty and the save
                Done(loop);
                return;
            }
            phase = 1;
            since = now;
            yoursDone = enemyDone = 0;
        }

        private static void Captures(GuiFlow flow, float now)
        {
            if (index == 2 && !credited)
            {
                // Match 3: CREDITS from the pause menu, then back to the pause menu, then on with the match
                credited = true;
                flow.Show(GuiScreen.Pause);
                flow.Execute(GuiCommandId.Credits, GuiScreen.Hud);
                Expect(flow.screen == GuiScreen.Credits, "CREDITS shows the credits");
                // The pause is applied by the GUI's next update: checked a moment later, in phase 3
                phase = 3;
                creditsChecked = false;
                since = now;
                return;
            }
            var enemy = yours.Other();
            if (enemyDone < EnemyWanted[index] && match.State.ReportCapture(enemy))
                enemyDone++;
            else if (yoursDone < YoursWanted[index] && match.State.ReportCapture(yours))
                yoursDone++;
            else
            {
                Wait(now, "the captures to count");
                return;
            }
            if (match.State.Over)
                phase = 2;
        }

        private static void Ended(GameLoop loop, GuiFlow flow, float now)
        {
            var yoursScore = YoursWanted[index];
            var enemyScore = EnemyWanted[index];
            var won = yoursScore > enemyScore;
            Expect(flow.screen == (won ? GuiScreen.Victory : GuiScreen.Defeat), "the right end screen");
            var before = made[index];
            var expected = Expected(before, yoursScore, enemyScore);
            Expect(loop.Difficulty == expected, $"after {yoursScore}-{enemyScore} from {before} the difficulty is {expected} (was {loop.Difficulty})");
            Expect(GameSave.Load(new GameStorage()).Difficulty == expected, "the save holds the difficulty after the match");
            after.Add(loop.Difficulty);
            trace.Add($"  result {yoursScore}-{enemyScore} ({(won ? "won" : "lost")}): difficulty {before} -> {loop.Difficulty}");
            index++;
            if (index == Matches)
            {
                Done(loop);
                return;
            }
            ended = match;
            flow.Execute(won ? GuiCommandId.Next : GuiCommandId.Retry, GuiScreen.Hud);
            phase = 0;
            since = now;
        }

        private static void Done(GameLoop loop)
        {
            var saved = GameSave.Load(new GameStorage());
            if (checking)
            {
                var walk = File.ReadAllLines(TracePath);
                var walkLevels = walk.First(line => line.StartsWith("levels=")).Substring("levels=".Length).Split(',');
                var walkDifficulty = walk.First(line => line.StartsWith("difficulty=")).Substring("difficulty=".Length);
                Expect(DifficultyRules.Key(made[0]) == walkDifficulty, $"the reload is made for {walkDifficulty} (was {made[0]})");
                Expect(!walkLevels.Skip(2).Contains(levels[0]), $"the reload's first level {levels[0]} is not among the save's last three ({string.Join(", ", walkLevels.Skip(2))})");
                Same(new[] { walkLevels[3], walkLevels[4], levels[0] }, saved.lastLevels, "the save's last three levels after the boot");
                trace.Add($"reload: first match {levels[0]} made {made[0]}; saved last three {string.Join(", ", saved.lastLevels)}");
                Finish();
                return;
            }
            Expect(saved.Difficulty == after[Matches - 1], "the save holds the last difficulty");
            Same(levels.Skip(levels.Count - 3), saved.lastLevels, "the save holds the last three levels");
            File.WriteAllLines(TracePath, new[]
            {
                "levels=" + string.Join(",", levels),
                "difficulty=" + DifficultyRules.Key(after[Matches - 1]),
            }.Concat(trace));
            Finish();
        }

        private static void Wait(float now, string what)
        {
            // Reported once: the failure ends the run
            if (now - since > 60 && !waitReported)
            {
                waitReported = true;
                errors.Add($"waited over a minute for {what} in match {index + 1}; scene {Active}; {Diagnose()}");
                Finish();
            }
        }

        private static string Diagnose()
        {
            var flows = UnityEngine.Object.FindObjectsByType<GuiFlow>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var m = Match.Current;
            var view = flows.Length > 0 ? flows[0].Match : null;
            return $"flows {flows.Length} screen {(flows.Length > 0 ? flows[0].screen.ToString() : "-")} ending {(flows.Length > 0 && flows[0].Ending)} " +
                   $"view {(view != null ? view.phase + " driven " + view.driven : "none")} " +
                   $"match {(m != null ? "phase " + m.State?.Phase + " over " + m.State?.Over + " hud " + (m.Hud != null) : "none")}";
        }

        private static Difficulty DifficultyOf(float skill)
        {
            if (Mathf.Abs(skill - 0.2f) < 0.01f) return Difficulty.SuperEasy;
            if (Mathf.Abs(skill - 0.35f) < 0.01f) return Difficulty.Easy;
            if (Mathf.Abs(skill - 0.65f) < 0.01f) return Difficulty.Hard;
            Expect(Mathf.Abs(skill - 0.5f) < 0.01f, $"the enemy skill is on the ladder ({skill})");
            return Difficulty.Normal;
        }

        // The ladder, written out again here: a loss one step easier, a win with the enemy on nothing one step harder, else the same
        private static Difficulty Expected(Difficulty from, int yours, int enemy)
        {
            var value = (int)from;
            if (yours < enemy)
                value--;
            else if (yours > enemy && enemy == 0)
                value++;
            return (Difficulty)Mathf.Clamp(value, 0, 3);
        }

        private static string TracePath => Path.Combine(Path.GetTempPath(), "sdf-playtest-42-trace.txt");

        private static void Finish()
        {
            // Runs once: the exit takes a moment in the editor, and the updates go on meanwhile
            if (finished)
                return;
            finished = true;
            EditorApplication.update -= Update;
            SessionState.EraseBool(ActiveKey);
            foreach (var line in trace)
                Debug.Log("GameLoop play test: " + line);
            foreach (var error in errors)
                Debug.Log("GameLoop play test error: " + error);
            Debug.Log($"GameLoop play test ({(checking ? "reload" : "five matches")}): {errors.Count} errors");
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }
    }
}
