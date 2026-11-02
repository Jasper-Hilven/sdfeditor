using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// The build's scene list (tickets 32.4, 52): Boot first (build index 0), then the seven levels. The showcases (Mushrooms, GUI, SpecialEffects) are in development builds only, so the web download
    /// does not carry them. Scenes that are editor-only (Home) never go in. The Build Settings list holds the development list with the
    /// showcases disabled, so a builder that takes the enabled scenes (the web build, the CrazySDK build window) builds the release; the
    /// build button of the Build window uses the right one for what it builds.
    /// </summary>
    [InitializeOnLoad]
    public static class LevelBuildScenes
    {
        public const string Folder = "Assets/Scenes";
        public static readonly string[] Showcases = { "Mushrooms", "GUI", "SpecialEffects" };
        // The battle levels the game plays (ticket 41: the arena and Home are gone). Any of them can be the first (GameLoop's rotation)
        public static readonly string[] Levels = { "Meadow", "Fungal", "Chessboard", "Toybox", "Kitchen", "Bathroom", "Computer" };
        public static readonly string[] EditorOnly = { };

        static LevelBuildScenes()
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
            var names = new List<string> { "Boot" };
            names.AddRange(Levels);
            if (development)
                names.AddRange(Showcases);
            return names.Distinct()
                .Where(name => !EditorOnly.Contains(name))
                .Select(name => $"{Folder}/{name}.unity")
                .Where(path => File.Exists(path));
        }

        /// <summary>
        /// The scenes of the streamed web build (ticket 52): Boot alone. Every level, the Meadow too, is an AssetBundle
        /// (<see cref="StreamedBundles"/>), and the first one is fetched behind the Loading screen.
        /// </summary>
        public static IEnumerable<string> PlayerPaths() => Paths(false).Where(path => StreamLayout.IsPlayerScene(Path.GetFileNameWithoutExtension(path)));

        /// <summary>The release scenes the streamed web build leaves out of the player: all the levels.</summary>
        public static IEnumerable<string> StreamedPaths()
        {
            var player = PlayerPaths().ToList();
            return Paths(false).Where(path => !player.Contains(path));
        }

        [MenuItem("Tools/SDF Campaign/Update Build Scene List")]
        public static void Update()
        {
            var release = Paths(false).ToList();
            EditorBuildSettings.scenes = Paths(true).Select(path => new EditorBuildSettingsScene(path, release.Contains(path))).ToArray();
            Debug.Log("SDF Campaign: build scenes " + string.Join(", ", EditorBuildSettings.scenes.Select(s => Path.GetFileNameWithoutExtension(s.path))));
        }
    }
}
