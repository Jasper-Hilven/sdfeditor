using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui.Tests
{
    /// <summary>
    /// Ticket 57: on a touch device every button of the menus is about 64 px tall at 800x450 (154 in the 1920x1080 layout), through
    /// the screen's GuiTouchLayout, and the three power buttons are not over the joystick's ground at the lower left. Reads the built
    /// prefabs through serialized properties (the tests do not see the game's assembly).
    /// </summary>
    public sealed class TouchMenuTests
    {
        const string Screens = "Assets/GUI/Prefabs/Screens";
        const float Thumb = 150; // "about 64 px": 154 built, a little give

        sealed class Entry
        {
            public RectTransform target;
            public bool hide, move, resize;
            public Vector2 position, size;
            public float scale;
        }

        static GameObject Load(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Screens}/{name}.prefab");

        static Dictionary<Transform, Entry> Entries(GameObject prefab)
        {
            var map = new Dictionary<Transform, Entry>();
            foreach (var behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != "GuiTouchLayout")
                    continue;
                var list = new SerializedObject(behaviour).FindProperty("entries");
                for (var i = 0; i < list.arraySize; i++)
                {
                    var e = list.GetArrayElementAtIndex(i);
                    var entry = new Entry
                    {
                        target = (RectTransform)e.FindPropertyRelative("target").objectReferenceValue,
                        hide = e.FindPropertyRelative("hide").boolValue,
                        move = e.FindPropertyRelative("move").boolValue,
                        resize = e.FindPropertyRelative("resize").boolValue,
                        position = e.FindPropertyRelative("position").vector2Value,
                        size = e.FindPropertyRelative("size").vector2Value,
                        scale = e.FindPropertyRelative("scale").floatValue,
                    };
                    if (entry.target != null)
                        map[entry.target] = entry;
                }
            }
            return map;
        }

        static float TouchHeight(RectTransform rect, Dictionary<Transform, Entry> entries)
        {
            var height = rect.sizeDelta.y;
            var scale = rect.localScale.y;
            if (entries.TryGetValue(rect, out var e))
            {
                if (e.resize)
                    height = e.size.y;
                if (e.scale > 0)
                    scale *= e.scale;
            }
            return height * scale;
        }

        [TestCase("Pause")]
        [TestCase("Confirm")]
        [TestCase("Settings")]
        [TestCase("Victory")]
        [TestCase("Defeat")]
        public void EveryButtonIsThumbSizedOnATouchDevice(string screen)
        {
            var prefab = Load(screen);
            Assert.IsNotNull(prefab, "run SdfGui.Editor.GuiScene.BuildInBatch");
            var entries = Entries(prefab);
            var small = new List<string>();
            foreach (var button in prefab.GetComponentsInChildren<Button>(true))
            {
                var rect = (RectTransform)button.transform;
                // A selector's arrows grow with the selector (checked with the rows), not on their own
                if (button.GetComponentsInParent<MonoBehaviour>(true).Any(m => m != null && m.GetType().Name == "GuiSelector"))
                    continue;
                var height = TouchHeight(rect, entries);
                if (height < Thumb)
                    small.Add($"{screen} / {rect.name}: {height}");
            }
            Assert.IsEmpty(small, "buttons under " + Thumb + " units tall on touch:\n" + string.Join("\n", small));
        }

        [Test]
        public void SettingsRowsAndTheirControlsGrowOnATouchDevice()
        {
            var prefab = Load("Settings");
            var entries = Entries(prefab);
            var rows = prefab.GetComponentsInChildren<Transform>(true).Where(t => t.parent != null && t.parent.name == "Content").ToList();
            Assert.AreEqual(12, rows.Count);
            foreach (var row in rows)
            {
                Assert.IsTrue(entries.TryGetValue(row, out var e), $"row {row.name} has a touch layout");
                if (e.hide)
                    continue;
                Assert.GreaterOrEqual(e.size.y, 100, row.name);
                var control = row.Cast<Transform>().FirstOrDefault(c => c.name != "Label");
                Assert.IsNotNull(control, row.name);
                Assert.IsTrue(entries.TryGetValue(control, out var ce) && ce.scale >= 1.4f, $"{row.name}'s control is scaled up");
            }
            var mouse = rows.First(r => r.name == "Mouse sensitivity");
            Assert.IsTrue(entries[mouse].hide, "the mouse row is hidden on touch");
        }

        [Test]
        public void ThePowerButtonsAreNotOverTheJoysticksGround()
        {
            var prefab = Load("Touch Controls");
            Assert.IsNotNull(prefab);
            var found = 0;
            foreach (var behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != "TouchButton")
                    continue;
                var kind = new SerializedObject(behaviour).FindProperty("kind");
                if (kind.enumNames[kind.enumValueIndex] != "Power")
                    continue;
                found++;
                var rect = (RectTransform)behaviour.transform;
                // Anchored at the right edge: the whole button is in the right half (1920 / 2 = 960 from the left)
                Assert.AreEqual(1f, rect.anchorMin.x, behaviour.name);
                Assert.LessOrEqual(-rect.anchoredPosition.x + rect.sizeDelta.x / 2, 960 - 100, behaviour.name + " reaches into the left half");
            }
            Assert.AreEqual(3, found);
        }
    }
}
