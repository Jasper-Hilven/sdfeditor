using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SdfBattle
{
    /// <summary>
    /// The cheat codes of a match, typed blind while playing (there is no field to type them in): <see cref="CheatCode.Skip"/> gives your
    /// team <see cref="CheatCode.SkipPoints"/> captures, which wins a shooter level; in the boss fight, which captures do not decide,
    /// Prime's bars go at once and the fight is won with its victory beat.
    /// Reads the characters the keyboard sends (the Input System's text input, also on the web); made by the match.
    /// </summary>
    public sealed class Cheats : MonoBehaviour
    {
        private Match match;
        private readonly CheatCode skip = new CheatCode(CheatCode.Skip);

        public static Cheats Attach(Match match)
        {
            var cheats = match.gameObject.AddComponent<Cheats>();
            cheats.match = match;
            return cheats;
        }

#if ENABLE_INPUT_SYSTEM
        private Keyboard keyboard;

        private void OnEnable() => Listen(Keyboard.current);

        private void OnDisable() => Listen(null);

        // The keyboard can come and go (plugged in, or the web page's first key); listen to the current one
        private void Update()
        {
            if (keyboard != Keyboard.current)
                Listen(Keyboard.current);
        }

        private void Listen(Keyboard next)
        {
            if (keyboard != null)
                keyboard.onTextInput -= Typed;
            keyboard = next;
            if (keyboard != null)
                keyboard.onTextInput += Typed;
        }
#else
        private void Update()
        {
            foreach (var c in Input.inputString)
                Typed(c);
        }
#endif

        private void Typed(char c)
        {
            if (skip.Feed(c))
                SkipThisGame();
        }

        /// <summary>
        /// Your team scores <see cref="CheatCode.SkipPoints"/> captures, credited to you, only while the round is being played; in the
        /// boss fight Prime concedes instead (from a phase or a transition).
        /// </summary>
        public void SkipThisGame()
        {
            var state = match != null ? match.State : null;
            var you = match != null ? match.You : null;
            if (state == null || you == null)
                return;
            if (state.Rules.objective == MatchObjective.Boss)
            {
                Boss.Current?.State?.Concede();
                return;
            }
            for (var i = 0; i < CheatCode.SkipPoints && !state.Over; i++)
                state.ReportCapture(you.State.Team, you.State);
        }
    }
}
