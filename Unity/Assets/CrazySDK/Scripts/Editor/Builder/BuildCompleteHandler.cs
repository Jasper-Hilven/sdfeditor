using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

#if UNITY_6000_0_OR_NEWER
namespace CrazyGames
{
    public class BuildCompleteHandler : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        /// <summary>
        /// When a build is copleted, if this is a release WebGL build, we generate a report summary and open the Analyzer tab if needed.
        /// This applies to both builds made from the CrazyGames SDK, but also to builds made from the Unity Editor.
        /// </summary>
        public void OnPostprocessBuild(BuildReport report)
        {
#pragma warning disable UDR0005
            // ignore unsubscribe warning, not relevant here as delayCall is called only once
            EditorApplication.delayCall += () =>
            {
                if (report.summary.outputPath.EndsWith(Builder.INTERMEDIATE_BUILD_SUFFIX))
                {
                    // these are secondary builds for getting ASTC compression or limited max memory size, so ignore them
                    return;
                }

                // this runs after the whole Release Build has completed: need to take the release state before anything below returns early, otherwise a failed release leaves it behind for the next build to pick up
                var release = ReleaseFor(report);
                Builder.ClearCurrentRelease();

                if (report.summary.result != BuildResult.Succeeded)
                {
                    return;
                }

                if (report.summary.options.HasFlag(BuildOptions.Development))
                {
                    return;
                }

                if (report.summary.platform != BuildTarget.WebGL)
                {
                    return;
                }

                if (release != null && release.failed)
                {
                    // this build succeeded but the Release Build around it did not, so there is no complete build to describe
                    Debug.LogWarning("The release build did not finish, so the Analyzer still shows the results of the previous one.");
                    return;
                }

                GenerateReportSummary(report, release);
                GoToAnalysisIfNeeded();
            };
#pragma warning restore UDR0005
        }

        /// <summary>
        /// The release this build belongs to, or null when the build was started from the Unity Editor instead of the SDK.
        /// </summary>
        private static ReleaseBuildState ReleaseFor(BuildReport report)
        {
            var release = Builder.CurrentRelease;
            if (release == null || !IsSamePath(release.outputPath, report.summary.outputPath))
            {
                return null;
            }
            return release;
        }

        private static bool IsSamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }
            try
            {
                var fullA = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar);
                var fullB = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar);
                return string.Equals(fullA, fullB, System.StringComparison.OrdinalIgnoreCase);
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private void GenerateReportSummary(BuildReport report, ReleaseBuildState release)
        {
            // store a summary of the build report, to be used by the Analyzer
            var assets = report
                .packedAssets.SelectMany(p => p.contents)
                // ordering is important before grouping, to ensure we keep the largest file for each sourceAssetPath, or there may be duplicates with 0 size
                .OrderByDescending(f => f.packedSize)
                .GroupBy(p => p.sourceAssetPath)
                .Select(g => g.First())
                .Where(f => !string.IsNullOrEmpty(f.sourceAssetPath))
                .ToList();

            var mainFiles = BuildReportGenerator.GetMainFiles(report);
            var reportSummary = new BuildReportSummary()
            {
                buildDateISO = System.DateTime.Now.ToString("o"),
                // report.summary.totalTime only covers the main build, the release also contains the secondary ones
                durationSeconds =
                    release != null && release.durationSeconds > 0 ? release.durationSeconds : report.summary.totalTime.TotalSeconds,
                totalSize = GetUploadSize(report.summary.outputPath),
                packagedFiles = assets.Select(a => new PackagedFileSummary { path = a.sourceAssetPath, size = a.packedSize }).ToList(),
                initialLoadSize = mainFiles.Aggregate(0UL, (acc, f) => acc + f.size),
            };
            var reportSummaryJson = JsonUtility.ToJson(reportSummary, true);
            var outputPath = Path.Combine("Library", Builder.SUMMARY_FILE_NAME);
            File.WriteAllText(outputPath, reportSummaryJson);
        }

        /// <summary>
        /// Size of the build folder, without the files copied in from the secondary builds.
        /// Developer Portal measures the upload the same way this does.
        /// </summary>
        private static ulong GetUploadSize(string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath) || !Directory.Exists(outputPath))
            {
                return 0;
            }

            ulong size = 0;
            foreach (var path in Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories))
            {
                if (BuildReportGenerator.IsSecondaryFile(Path.GetFileName(path)))
                {
                    continue;
                }
                size += (ulong)new FileInfo(path).Length;
            }
            return size;
        }

        private static void GoToAnalysisIfNeeded()
        {
            // open analyzer tab and run analysis if agreed to
            if (EditorPrefs.GetBool(AnalyzeTab.AUTO_SHOW_AND_RUN_KEY, true))
            {
                var window = BuildWindow.ShowWindow(1);
                (window.tabs[1] as AnalyzeTab).Analyze();
            }
        }
    }
}
#endif
