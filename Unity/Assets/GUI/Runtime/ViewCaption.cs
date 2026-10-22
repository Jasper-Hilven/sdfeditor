using System;

namespace SdfGui
{
    /// <summary>
    /// The caption box's words (ticket 32.7): one line at a time, who says it, how visible it is, and how far a held skip has got. Written
    /// by whoever plays a scene before or inside a match (the campaign's beat player and warm-up) and drawn by <see cref="GuiCaption"/>;
    /// an empty line shows nothing.
    /// </summary>
    [Serializable]
    public sealed class ViewCaption
    {
        public string speaker = "";
        public string line = "";
        [UnityEngine.Tooltip("0 to 1: faded in and out by the player of the scene")]
        public float alpha = 1;
        [UnityEngine.Tooltip("0 to 1: how far the skip hold has got; the bar shows when above 0")]
        public float skip;
        [UnityEngine.Tooltip("The small hint at the screen's corner, like Hold SPACE to skip; empty for none")]
        public string hint = "";

        /// <summary>There is a line to show.</summary>
        public bool Visible => !string.IsNullOrEmpty(line) && alpha > 0.001f;

        public void Set(string speaker, string line, float alpha = 1, float skip = 0, string hint = "")
        {
            this.speaker = speaker ?? "";
            this.line = line ?? "";
            this.alpha = alpha;
            this.skip = skip;
            this.hint = hint ?? "";
        }

        public void Clear() => Set("", "", 0, 0, "");
    }
}
