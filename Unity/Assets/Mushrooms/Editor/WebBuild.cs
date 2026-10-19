using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// The web build: WebGL 2 of the scenes in the build settings (the mushroom scene), into Unity/Builds/Web, on the integrated GPU /
    /// web quality level (<see cref="RenderPipelineSetup.Web"/>).
    /// <list type="bullet">
    /// <item>Tools > SDF Mushrooms > Build Web, or Build and Run Web, which serves it locally and opens the browser.</item>
    /// <item>Without a window: <c>Unity -batchmode -quit -projectPath Unity -buildTarget WebGL -executeMethod
    /// SdfMushrooms.Editor.WebBuild.Build</c> (the "Unity Web Build" run configuration), which exits with an error if the build fails.</item>
    /// </list>
    /// Compressed with gzip, with the loader able to decompress it itself, so any static file server shows it (e.g.
    /// <c>python3 -m http.server -d Unity/Builds/Web</c>), not only one that sends the Content-Encoding header.
    /// </summary>
    public static class WebBuild
    {
        public const string OutputFolder = "Builds/Web";

        [MenuItem("Tools/SDF Mushrooms/Build Web")]
        private static void BuildFromMenu() => Run(BuildOptions.None);

        [MenuItem("Tools/SDF Mushrooms/Build and Run Web")]
        private static void BuildAndRunFromMenu() => Run(BuildOptions.AutoRunPlayer);

        public static void Build()
        {
            if (!Run(BuildOptions.None))
                EditorApplication.Exit(1);
        }

        private static bool Run(BuildOptions options)
        {
            RenderPipelineSetup.Ensure();
            MushroomBuilder.BuildStale();

            // WebGL 2 only (WebGPU is still experimental), gzip with the loader's own decompression as a fallback
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("SDF mushrooms: no scenes in the build settings to build for the web");
                return false;
            }
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
            Debug.Log($"SDF mushrooms: web build of {string.Join(", ", scenes)} written to {Path.GetFullPath(OutputFolder)} " +
                      $"({summary.totalSize / (1024 * 1024)} MB) in {summary.totalTime.TotalSeconds:0} s");
            return true;
        }
    }
}
