using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SdfGui.Tests
{
    /// <summary>
    /// Ticket 54: every player-visible text in the built GUI prefabs is at least 12 px tall at 800x450, CrazyGames' smallest window.
    /// The canvas is laid out at 1920x1080 and scaled by 450/1080, so 12 px is 28.8 in the layout: <see cref="MinReadable"/> (kept in
    /// step with <c>GuiFactory.MinReadable</c>). An auto-sizing text counts by its minimum, a <c>size=N%</c> tag in its text by N.
    /// The developer-only Gallery and Test Panel are out of scope.
    /// </summary>
    public sealed class ReadableTextTests
    {
        const float MinReadable = 29;
        const string Folder = "Assets/GUI/Prefabs";
        // "GUI" holds every screen again (each checked on its own) plus the Gallery and the Test Panel; "Level Card" is a leftover of the
        // removed level select that nothing builds or shows
        static readonly string[] Skipped = { "Gallery", "Test Panel", "GUI", "Level Card" };

        static IEnumerable<string> Prefabs() =>
            AssetDatabase.FindAssets("t:Prefab", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !Skipped.Contains(System.IO.Path.GetFileNameWithoutExtension(path)))
                .OrderBy(path => path);

        /// <summary>The size a text is drawn at, at its smallest, in the 1920x1080 layout.</summary>
        static float Smallest(TMP_Text text)
        {
            var size = text.enableAutoSizing ? text.fontSizeMin : text.fontSize;
            var percent = 100f;
            foreach (Match m in Regex.Matches(text.text ?? "", @"<size=(\d+(?:\.\d+)?)%>"))
                percent = Mathf.Min(percent, float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            return size * percent / 100f;
        }

        [Test]
        public void ThereAreBuiltPrefabsToCheck()
        {
            Assert.GreaterOrEqual(Prefabs().Count(), 10, "run SdfGui.Editor.GuiScene.BuildInBatch");
        }

        [Test]
        public void EveryTextInTheBuiltPrefabsIsAtLeastTwelvePixelsAt800x450()
        {
            var tooSmall = new List<string>();
            foreach (var path in Prefabs())
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                    if (Smallest(text) < MinReadable)
                        tooSmall.Add($"{path} / {text.transform.name}: {Smallest(text)}");
            }
            Assert.IsEmpty(tooSmall, "texts under " + MinReadable + " pt:\n" + string.Join("\n", tooSmall));
        }

        [Test]
        public void TheRuleIsTwelvePixelsAtTheSmallestWindow()
        {
            Assert.GreaterOrEqual(MinReadable * 450f / 1080f, 12f);
        }
    }
}
