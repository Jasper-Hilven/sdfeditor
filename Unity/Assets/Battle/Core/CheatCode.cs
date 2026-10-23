using System;
using System.Text;

namespace SdfBattle
{
    /// <summary>
    /// A word typed blind, with no field to type it in: every character the keyboard sends is fed in, and <see cref="Feed"/> says when
    /// the last ones spell the code (any case; whatever came before is ignored, so it can be typed in the middle of playing).
    /// </summary>
    public sealed class CheatCode
    {
        /// <summary>Your team gets <see cref="SkipPoints"/> captures, which wins a shooter level; in the boss fight, Prime is beaten.</summary>
        public const string Skip = "cheatcheat";

        public const int SkipPoints = 3;

        private readonly string code;
        private readonly StringBuilder typed = new StringBuilder();

        public CheatCode(string code)
        {
            if (string.IsNullOrEmpty(code))
                throw new ArgumentException("A cheat code needs letters", nameof(code));
            this.code = code.ToLowerInvariant();
        }

        /// <summary>One typed character; true when it completes the code (which then starts over).</summary>
        public bool Feed(char c)
        {
            typed.Append(char.ToLowerInvariant(c));
            if (typed.Length > code.Length)
                typed.Remove(0, typed.Length - code.Length);
            if (typed.ToString() != code)
                return false;
            typed.Clear();
            return true;
        }
    }
}
