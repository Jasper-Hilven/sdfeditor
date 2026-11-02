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
    /// does not carry them. Scenes that are editor-only (Home) never go in. The Build Settings list holds the development list with the
    /// showcases disabled, so a builder that takes the enabled scenes (the web build, the CrazySDK build window) builds the release; the
    /// build button of the Build window uses the right one for what it builds.
    /// </summary>
    [InitializeOnLoad]
    public static class CampaignBuildScenes
    {
        public const string Folder = "Assets/Scenes";
        public static readonly string[] Showcases = { "Mushrooms", "GUI", "SpecialEffects" };
        // The battle levels the game plays (ticket 41: the arena and Home are gone); the first one is the Meadow, which Boot starts
        public static readonly string[] Levels = { "Meadow", "Fungal", "Chessboard", "Toybox", "Kitchen", "Bathroom", "Computer" };
        public static readonly string[] EditorOnly = { };

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
        /// The scenes of the streamed web build (ticket 38): Boot and the first battle's scene only, which the player carries; every other
        /// arena is an AssetBundle (<see cref="StreamedBundles"/>).
        /// </summary>
        public static IEnumerable<string> PlayerPaths()
        {
            var first = GameLoop.FirstLevel;
            return Paths(false).Where(path => Path.GetFileNameWithoutExtension(path) is var name && (name == "Boot" || name == first));
        }

        /// <summary>The release scenes the streamed web build leaves out of the player: the arenas after the first.</summary>
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
