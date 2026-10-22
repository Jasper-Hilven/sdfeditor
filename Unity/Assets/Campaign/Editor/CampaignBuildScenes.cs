using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// The build's scene list, written from the campaign's steps (ticket 32.4): Boot first (build index 0), then each step's scene that
    /// exists, once, in campaign order. The showcases (Mushrooms, GUI, SpecialEffects) are in development builds only, so the web download
    /// does not carry them. Scenes that are editor-only (Home) never go in. The Build Settings list is kept at the development list; the
    /// build button of the Build window uses the right one for what it builds.
    /// </summary>
    [InitializeOnLoad]
    public static class CampaignBuildScenes
    {
        public const string Folder = "Assets/Scenes";
        public static readonly string[] Showcases = { "Mushrooms", "GUI", "SpecialEffects" };
        // Home is 31's intro room, played as comic panels in the viewer, not a scene the game loads
        public static readonly string[] EditorOnly = { "Home" };

        static CampaignBuildScenes()
        {
            BuildPlayerWindow.RegisterBuildPlayerHandler(options =>
            {
                options.scenes = Paths(options.options.HasFlag(BuildOptions.Development)).ToArray();
                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
            });
        }

        /// <summary>The scene paths of a release (<paramref name="development"/> false) or development build.</summary>
        public static IEnumerable<string> Paths(bool development)
        {
            var names = new List<string> { CampaignRunner.BootScene };
            foreach (var step in CampaignDefinition.Default().Steps)
                names.Add(step.scene);
            if (development)
                names.AddRange(Showcases);
            return names.Distinct()
                .Where(name => !EditorOnly.Contains(name))
                .Select(name => $"{Folder}/{name}.unity")
                .Where(path => File.Exists(path));
        }

        [MenuItem("Tools/SDF Campaign/Update Build Scene List")]
        public static void Update()
        {
            EditorBuildSettings.scenes = Paths(true).Select(path => new EditorBuildSettingsScene(path, true)).ToArray();
            Debug.Log("SDF Campaign: build scenes " + string.Join(", ", EditorBuildSettings.scenes.Select(s => Path.GetFileNameWithoutExtension(s.path))));
        }
    }
}
