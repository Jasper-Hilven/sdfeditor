using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The kill feed in the top right: the latest eliminations, newest at the bottom, each fading out after a few seconds; your own
    /// stand out. Draws into the entries under it (prefab instances), hiding those it has nothing for.
    /// </summary>
    public sealed class GuiKillFeed : GuiView
    {
        public GuiKillFeedEntry[] entries = new GuiKillFeedEntry[0];
        public float showSeconds = 5;
        public float fadeSeconds = 1;

        protected override void Render(FakeMatch m)
        {
            var first = Mathf.Max(0, m.feed.Count - entries.Length);
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null)
                    continue;
                var k = first + i;
                var visible = k < m.feed.Count;
                if (visible)
                {
                    var kill = m.feed[k];
                    var alpha = 1 - Mathf.Clamp01((kill.age - showSeconds) / fadeSeconds);
                    visible = alpha > 0;
                    if (visible)
                        entry.Show(m, kill, alpha);
                }
                GuiText.Show(entry, visible);
            }
        }
    }
}
