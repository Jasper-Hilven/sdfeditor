using System;
using System.Collections.Generic;
using System.Linq;
using SDFPipeline.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SdfStory.Editor
{
    /// <summary>
    /// Tools > SDF Story > Render Comic... (ticket 34): one row per comic of content/story/ (the intro, then the beats by name) with its panel
    /// count and where its panels go, a Render button for it, and a field to render only some of its panels ("2" or "1,3"). The comics are
    /// read again each time the window opens or is refreshed, so a new story file shows up without a code change.
    /// </summary>
    public sealed class StoryComicWindow : EditorWindow
    {
        private readonly List<(string comic, int panels, string problem)> comics = new List<(string, int, string)>();
        private readonly Dictionary<string, string> only = new Dictionary<string, string>();
        private Vector2 scroll;

        public static void Open()
        {
            var window = GetWindow<StoryComicWindow>(true, "Render Comic");
            window.minSize = new Vector2(520, 160);
            window.Refresh();
        }

        private void Refresh()
        {
            comics.Clear();
            foreach (var comic in StoryCapture.Comics())
            {
                try
                {
                    comics.Add((comic, StoryFile.Load(StoryFile.PathIn(StoryCapture.ContentFolder, comic)).Panels.Count, null));
                }
                catch (Exception exception) when (exception is System.IO.InvalidDataException || exception is System.IO.IOException)
                {
                    comics.Add((comic, 0, exception.Message));
                }
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Renders a comic of content/story/ into its folder. Panels: empty for all, or numbers such as 2 or 1,3.", MessageType.None);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var (comic, panels, problem) in comics)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(comic, EditorStyles.boldLabel, GUILayout.Width(140));
                    if (problem != null)
                    {
                        EditorGUILayout.LabelField(problem, EditorStyles.wordWrappedMiniLabel);
                        continue;
                    }
                    EditorGUILayout.LabelField($"{panels} panels to {StoryCapture.FolderOf(comic)}", EditorStyles.miniLabel);
                    only.TryGetValue(comic, out var numbers);
                    only[comic] = EditorGUILayout.TextField(numbers ?? "", GUILayout.Width(60));
                    if (GUILayout.Button("Render", GUILayout.Width(70)))
                    {
                        Render(comic, only[comic]);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh"))
                    Refresh();
                if (GUILayout.Button("Render all"))
                {
                    StoryCapture.RenderFromMenu();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static void Render(string comic, string numbers)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            int[] panels = null;
            if (!string.IsNullOrWhiteSpace(numbers))
            {
                try
                {
                    panels = numbers.Split(',').Select(n => int.Parse(n.Trim())).ToArray();
                }
                catch (FormatException)
                {
                    Debug.LogError($"SDF story: '{numbers}' is not a list of panel numbers such as 2 or 1,3");
                    return;
                }
            }
            StoryCapture.RenderPanels(comic, panels, StoryCapture.FolderOf(comic), false);
        }
    }
}
