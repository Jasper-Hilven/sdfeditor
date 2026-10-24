using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// The web build: WebGL 2 of the campaign's release scenes (Boot and the arenas; no showcases, <see cref="SdfCampaign.Editor.CampaignBuildScenes"/>), into Unity/Builds/Web, on the integrated GPU /
    /// web quality level (<see cref="RenderPipelineSetup.Web"/>).
    /// <list type="bullet">
    /// <item>Tools > SDF Mushrooms > Build Web, or Build and Run Web, which serves it locally and opens the browser.</item>
    /// <item>Tools > SDF Mushrooms > Build Web for CrazyGames (or <c>-crazygames</c> in batch mode): the upload for the portal, without the
    /// decompression fallback, which the portal refuses (its .unityweb files); its servers send the Brotli files' Content-Encoding themselves.
    /// That build does not run from a plain local server.</item>
    /// <item>Without a window: <c>Unity -batchmode -quit -projectPath Unity -buildTarget WebGL -executeMethod
    /// SdfMushrooms.Editor.WebBuild.Build</c> (the "Unity Web Build" run configuration), which exits with an error if the build fails.
    /// <c>-scene Assets/Scenes/Meadow.unity</c> (repeatable) builds those scenes instead of the release's, the first one starting.</item>
    /// </list>
    /// The release is streamed (ticket 38): the player carries Boot and the first arena only (the initial download), and every later arena
    /// and comic is an AssetBundle in <c>Builds/Web/StreamingAssets/Bundles</c> (CrazyGames serves only that folder of an upload besides Build) (<see cref="SdfCampaign.Editor.StreamedBundles"/>), fetched as the campaign goes there.
    /// <c>-nostream</c> builds every release scene into the player instead (one big download, as before).
    /// <para>Compressed with Brotli, with the loader able to decompress it itself, so any static file server shows it (e.g.
    /// <c>python3 -m http.server -d Unity/Builds/Web</c>), not only one that sends the Content-Encoding header.</para>
    /// </summary>
    public static class WebBuild
    {
        public const string OutputFolder = "Builds/Web";

        [MenuItem("Tools/SDF Mushrooms/Build Web")]
        private static void BuildFromMenu() => Run(BuildOptions.None);

        [MenuItem("Tools/SDF Mushrooms/Build and Run Web")]
        private static void BuildAndRunFromMenu() => Run(BuildOptions.AutoRunPlayer);

        [MenuItem("Tools/SDF Mushrooms/Build Web for CrazyGames")]
        private static void BuildForCrazyGamesFromMenu() => Run(BuildOptions.None, crazyGames: true);

        public static void Build()
        {
            if (!Run(BuildOptions.None, System.Environment.GetCommandLineArgs().Contains("-crazygames")))
                EditorApplication.Exit(1);
        }

        private static bool Run(BuildOptions options, bool crazyGames = false)
        {
            RenderPipelineSetup.Ensure();
            MushroomBuilder.BuildStale();

            // WebGL 2 only (WebGPU is still experimental), Brotli (ticket 37; a good tenth smaller than gzip) with the loader's own decompression as a
            // fallback, but for CrazyGames, which refuses the fallback's files
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = !crazyGames;
            // No .unityweb files of an earlier build with the fallback left in the upload
            var built = Path.Combine(OutputFolder, "Build");
            if (crazyGames && Directory.Exists(built))
                Directory.Delete(built, true);

            var args = System.Environment.GetCommandLineArgs();
            var chosen = args.Select((a, i) => a == "-scene" && i + 1 < args.Length ? args[i + 1] : null).Where(a => a != null).ToArray();
            // The release is streamed (ticket 38): the player carries Boot and the first arena, the other arenas are bundles next to it.
            // -scene builds only the scenes named; -nostream puts every release scene in the player as before.
            var stream = chosen.Length == 0 && !args.Contains("-nostream");
            var scenes = chosen.Length > 0 ? chosen
                : (stream ? SdfCampaign.Editor.CampaignBuildScenes.PlayerPaths() : SdfCampaign.Editor.CampaignBuildScenes.Paths(false)).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("SDF mushrooms: no scenes in the build settings to build for the web");
                return false;
            }
            // The bundles are built first (the player lists them), the player next, the bundles are copied into its folder last
            using (var streamed = stream ? new SdfCampaign.Editor.StreamedBundles(BuildTarget.WebGL) : null)
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = OutputFolder,
                    target = BuildTarget.WebGL,
                    options = options,
                });
                var summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"SDF mushrooms: web build {summary.result.ToString().ToLowerInvariant()} with {summary.totalErrors} errors");
                    return false;
                }
                if (streamed != null)
                {
                    streamed.CopyTo(OutputFolder);
                    Debug.Log($"SDF mushrooms: {streamed.Manifest.bundles.Length} bundles ({streamed.Manifest.bundles.Sum(b => b.size) / (1024 * 1024)} MB) written to {Path.GetFullPath(Path.Combine(OutputFolder, SdfCampaign.Editor.StreamedBundles.StreamedContentFolder))}");
                }
                return Finish(report.summary, scenes);
            }
        }

        private static bool Finish(BuildSummary summary, string[] scenes)
        {
            Debug.Log($"SDF mushrooms: web build{(PlayerSettings.WebGL.decompressionFallback ? "" : " for CrazyGames (no decompression fallback)")} of {string.Join(", ", scenes)} written to {Path.GetFullPath(OutputFolder)} " +
                      $"({summary.totalSize / (1024 * 1024)} MB) in {summary.totalTime.TotalSeconds:0} s");
            return true;
        }
    }
}
