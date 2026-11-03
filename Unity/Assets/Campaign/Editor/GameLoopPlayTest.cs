using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfBattle;
using SdfGui;
using SdfHeads;
using SdfLevel;
using SdfPlatform;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// The endless game's play test (ticket 42; the campaign play test was removed in 41): Boot in Play mode, five matches driven through the
    /// GUI's own commands. Match 1, a first play, starts in the Fungal level with the warm-up (cleared here); the others are the rotation's picks. Match 2 is
    /// restarted from the pause menu (same level and team, nothing recorded). Match 3 opens CREDITS from the pause menu, waits for the
    /// credits, and comes back to the pause menu. Each match ends by captures (you 3-0, 1-3, 0-3, 3-0, 3-2), then the end card's countdown
    /// (ticket 48) runs out by itself, or Space skips it on match 4. The card is checked: the countdown runs from 10, the NEW reveal and the
    /// difficulty line are right, A and D (by physical key, through the Input System's virtual keyboard) switch the head and the head is worn
    /// in the next match, and the midgame ad hook (<c>WebPortal.MidgameBreaks</c>) goes up once per match end. It checks the team, the enemy side's skill for the difficulty the match was made for, the difficulty after each
    /// result, that no level is among the last three, the warm-up on the first match only (and not on the reload), and that the save holds the result.
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

        // The end card (ticket 48): the midgame ad hook's count at the match end (-1 when no check is pending), the card's steps and the head
        private static int adsAtEnd = -1;
        private static int cardStep;
        private static float cardSince, cardStart;
        private static string headBeforeKeys, expectedWorn;
        private static bool keysChecked;
        private static GuiEndScreen shownCard;

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
                        Expect(!checking, "a reload (not a first play) has no warm-up");
                        warmedFirst = true;
                        warm.Stop();
                        return;
                    }
                    Expect(index != 0 || checking || warmedFirst, "the first match has the warm-up");
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
                case 2: // the end screen: the difficulty has moved; the card counts down by itself
                    if (flow.Ending || (flow.screen != GuiScreen.Victory && flow.screen != GuiScreen.Defeat))
                    {
                        Wait(now, "the end screen");
                        return;
                    }
                    Ended(loop, flow, now);
                    return;
                case 4: // the end card: its checks, then the countdown (or Space) and the next level
                    Card(loop, flow, now);
                    return;
                case 3: // the credits roll over the running match and come back to the pause menu
                    if (UnityEngine.Object.FindAnyObjectByType<CreditsPlayer>() != null)
                    {
                        if (!creditsChecked && now - since > 0.2f)
                        {
                            creditsChecked = true;
                            Expect(flow.IsPaused && Mathf.Approximately(Time.timeScale, 1), "the match runs on under the credits (the menu does not stop time)");
                        }
                        if (flow.screen != GuiScreen.Credits)
                            errors.Add($"the credits left the screen for {flow.screen}");
                        Wait(now, "the credits to end");
                        return;
                    }
                    Expect(flow.screen == GuiScreen.Pause, "the credits come back to the pause menu");
                    Expect(flow.IsPaused && Mathf.Approximately(Time.timeScale, 1), "back on the pause menu after the credits, the match still running");
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
            // The midgame ad hook is called once per match end: the count taken at the end is one more now, before this match
            if (adsAtEnd >= 0)
            {
                Expect(WebPortal.MidgameBreaks == adsAtEnd + 1, $"the midgame ad hook was called once for the match end ({WebPortal.MidgameBreaks - adsAtEnd} calls)");
                adsAtEnd = -1;
            }
            // The head the card left on is the one worn in this match
            if (expectedWorn != null && match.You != null && match.You.WornHead != null)
            {
                var worn = HeadStandIn.Of(expectedWorn);
                if (worn != null)
                    Expect(match.You.WornHead.Id == worn, $"the next match wears {worn} (was {match.You.WornHead.Id})");
                expectedWorn = null;
            }
            if (index == 0)
            {
                Expect(System.Array.IndexOf(LevelRotation.Levels, level) >= 0, $"boot starts on one of the seven levels ({level})");
                if (!checking)
                {
                    Expect(difficulty == Difficulty.SuperEasy, "a fresh save starts on Super Easy");
                    Expect(level == LevelRotation.FirstPlayLevel, $"a fresh save starts in the Fungal level ({level})");
                }
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
            Expect(loop.DifficultyBefore == before, $"the card's 'before' is the difficulty the match was made for ({loop.DifficultyBefore})");
            after.Add(loop.Difficulty);
            trace.Add($"  result {yoursScore}-{enemyScore} ({(won ? "won" : "lost")}): difficulty {before} -> {loop.Difficulty}");
            adsAtEnd = WebPortal.MidgameBreaks;
            cardStep = 0;
            shownCard = null;
            phase = 4;
            since = now;
        }

        // The end card: opened (its countdown, NEW and the difficulty line), the head keys on the first match, the skip on the fourth,
        // the countdown running on the rest; then the next match goes live (checked in Live), or on the last match the run is done (phase 5).
        private static void Card(GameLoop loop, GuiFlow flow, float now)
        {
            // The card goes away once the next level is asked for, so its last state is kept from the frame it was seen
            var visible = VisibleCard(flow);
            if (visible != null)
                shownCard = visible;
            var card = shownCard;
            if (card == null || (visible == null && !card.CountdownDone))
            {
                Wait(now, "the end card");
                return;
            }
            switch (cardStep)
            {
                case 0:
                {
                    cardStart = card.CountdownRemaining;
                    Expect(!card.CountdownDone && cardStart > 8.5f && cardStart <= NextLevelCountdown.Seconds + 0.05f, $"the countdown starts at 10 ({cardStart:0.00})");
                    var newHead = loop.LastUnlockedHead;
                    var winWithReveal = YoursWanted[index] > EnemyWanted[index] && newHead != null;
                    Expect(card.NewHeadShown == winWithReveal, $"NEW shows on an unlocking win only (shown {card.NewHeadShown}, unlocked {newHead ?? "none"})");
                    if (winWithReveal)
                        Expect(card.NewHeadText.Contains(HeadStandIn.Label(newHead).ToUpperInvariant()), $"the reveal names the new head ({card.NewHeadText})");
                    var change = NextLevelCard.DifficultyLine(loop.DifficultyBefore, loop.Difficulty);
                    Expect(card.DifficultyShown == (change != null), $"the difficulty line shows only on a change (shown {card.DifficultyShown})");
                    if (change != null)
                        Expect(card.DifficultyText == change, $"the difficulty line says '{change}' (was '{card.DifficultyText}')");
                    trace.Add($"  card: countdown from {cardStart:0.0}s, NEW {(card.NewHeadShown ? card.NewHeadText : "no")}, difficulty line {(change ?? "none")}");
                    headBeforeKeys = loop.CurrentHead;
                    cardStep = index == 0 ? 1 : index == 3 ? 5 : 6;
                    cardSince = now;
                    return;
                }
                case 1: // A: the head before the current one, by physical key
                    if (now - cardSince < 1)
                        return;
                    expectedWorn = loop.PreviousHead();
                    Press(Key.A, true);
                    cardStep = 2;
                    cardSince = now;
                    return;
                case 2:
                    if (now - cardSince < 0.3f)
                        return;
                    Press(Key.A, false);
                    Expect(loop.CurrentHead == expectedWorn, $"A switches to the previous head {expectedWorn} (is {loop.CurrentHead}; keyboard {Keyboard.current?.name}, A {Keyboard.current?.aKey.wasPressedThisFrame})");
                    cardStep = 3;
                    cardSince = now;
                    return;
                case 3: // D: back to the head the card opened on
                    if (now - cardSince < 1)
                        return;
                    Press(Key.D, true);
                    cardStep = 4;
                    cardSince = now;
                    return;
                case 4:
                    if (now - cardSince < 0.3f)
                        return;
                    Press(Key.D, false);
                    Expect(loop.CurrentHead == headBeforeKeys, $"D switches to the next head {headBeforeKeys} (is {loop.CurrentHead})");
                    keysChecked = true;
                    expectedWorn = loop.CurrentHead;
                    trace.Add($"  keys: A then D on the card: {headBeforeKeys} -> {expectedWorn}, worn from the next match");
                    cardStep = 6;
                    cardSince = now;
                    return;
                case 5: // Space skips the rest of the countdown
                    if (now - cardSince < 1)
                        return;
                    Press(Key.Space, true);
                    cardStep = 7;
                    cardSince = now;
                    return;
                case 6: // the countdown runs on its own: it has gone down by now
                    if (now - cardSince < 2)
                        return;
                    Expect(card.CountdownRemaining <= cardStart - 1.5f, $"the countdown runs ({card.CountdownRemaining:0.00} from {cardStart:0.00})");
                    cardStep = 8;
                    cardSince = now;
                    return;
                case 7:
                    if (now - cardSince < 0.3f)
                        return;
                    Press(Key.Space, false);
                    Expect(card.CountdownDone, "Space skips the countdown");
                    trace.Add($"  Space skipped the countdown with {card.CountdownRemaining:0.0}s left (started at {cardStart:0.0}s)");
                    cardStep = 8;
                    cardSince = now;
                    return;
                case 8: // the card has nothing more to show: the next match goes live, or on the last match the run is done
                    if (!card.CountdownDone)
                    {
                        Wait(now, "the countdown to run out");
                        return;
                    }
                    // The head this card left on is the one the next match wears
                    expectedWorn = loop.CurrentHead;
                    index++;
                    ended = match;
                    if (index == Matches)
                    {
                        // The last countdown has asked for its ad; the next level is not loaded for the check, so the run is done here
                        Expect(WebPortal.MidgameBreaks == adsAtEnd + 1, $"the midgame ad hook was called once for the last match end ({WebPortal.MidgameBreaks - adsAtEnd} calls)");
                        Done(loop);
                        return;
                    }
                    phase = 0;
                    since = now;
                    return;
            }
        }

        // The end card that is up for this screen (the card objects are inactive until their screen shows)
        private static GuiEndScreen VisibleCard(GuiFlow flow)
        {
            foreach (var card in UnityEngine.Object.FindObjectsByType<GuiEndScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (card.isActiveAndEnabled && card.victory == (flow.screen == GuiScreen.Victory))
                    return card;
            return null;
        }

        private static Keyboard virtualKeyboard;
        private static InputSettings.BackgroundBehavior behaviourBefore;

        // A key by its place, pressed or let go on the Input System's virtual keyboard: queued, and applied from a player-loop update (see KeyDriver)
        private static void Press(Key key, bool down)
        {
            if (virtualKeyboard == null)
            {
                // The batch editor is never focused: the Input System would reset the keys then, so the test's keyboard ignores the focus
                behaviourBefore = InputSystem.settings.backgroundBehavior;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                virtualKeyboard = InputSystem.AddDevice<Keyboard>("SdfPlayTestKeyboard");
                // The game's keys are read from this keyboard (the machine's keyboard takes no part in the check)
                GameKeys.Source = () => virtualKeyboard;
                new GameObject("Play test keys") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<KeyDriver>().keyboard = virtualKeyboard;
            }
            KeyDriver.Set(key, down);
        }

        // The keyboard as a real one reports it: its whole state, every frame. A key set down stays down until it is let go here.
        [DefaultExecutionOrder(-1000)]
        private sealed class KeyDriver : MonoBehaviour
        {
            public Keyboard keyboard;
            private static Key? held;

            public static void Set(Key key, bool down) => held = down ? key : (Key?)null;

            private void Update()
            {
                if (!keyboard.enabled)
                    InputSystem.EnableDevice(keyboard);
                InputSystem.QueueStateEvent(keyboard, held is Key key ? new KeyboardState(key) : new KeyboardState());
                InputSystem.Update();
            }
        }

        private static void Done(GameLoop loop)
        {
            var saved = GameSave.Load(new GameStorage());
            if (checking)
            {
                // The reload: the first match is the rotation's pick, not one of the saved last three, and the save keeps the three before it
                // (the first two of them, then this match's level)
                var walk = File.ReadAllLines(TracePath);
                var walkDifficulty = walk.First(line => line.StartsWith("difficulty=")).Substring("difficulty=".Length);
                var before = walk.First(line => line.StartsWith("saved=")).Substring("saved=".Length).Split(',');
                Expect(DifficultyRules.Key(made[0]) == walkDifficulty, $"the reload is made for {walkDifficulty} (was {made[0]})");
                Expect(!before.Contains(levels[0]), $"the reload's first level {levels[0]} is not among the save's last three ({string.Join(", ", before)})");
                Same(new[] { before[1], before[2], levels[0] }, saved.lastLevels, "the save's last three levels after the boot");
                trace.Add($"reload: first match {levels[0]} made {made[0]}; saved last three {string.Join(", ", saved.lastLevels)}");
                Finish();
                return;
            }
            Expect(keysChecked, "A and D switched the head on the first card");
            Expect(saved.Difficulty == after[Matches - 1], "the save holds the last difficulty");
            // The last countdown has loaded the next level already, which is recorded as played when it goes live: the save holds it too
            var played = levels.ToList();
            if (loop.Current != played.Last())
                played.Add(loop.Current);
            Same(played.Skip(played.Count - 3), saved.lastLevels, "the save holds the last three levels");
            File.WriteAllLines(TracePath, new[]
            {
                "levels=" + string.Join(",", levels),
                "difficulty=" + DifficultyRules.Key(after[Matches - 1]),
                "saved=" + string.Join(",", saved.lastLevels),
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
            if (virtualKeyboard != null)
            {
                GameKeys.Source = null;
                InputSystem.settings.backgroundBehavior = behaviourBefore;
            }
            foreach (var line in trace)
                Debug.Log("GameLoop play test: " + line);
            foreach (var error in errors)
                Debug.Log("GameLoop play test error: " + error);
            Debug.Log($"GameLoop play test ({(checking ? "reload" : "five matches")}): {errors.Count} errors");
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }
    }
}
