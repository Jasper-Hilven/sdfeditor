using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

#if UNITY_6000_0_OR_NEWER
namespace CrazyGames
{
    public class Builder
    {
        private WebGLCompressionFormat _initialCompressionFormat;
        private WebGLExceptionSupport _initialExceptionSupport;
        private WebGLDebugSymbolMode _initialDebugSymbolMode;
        private bool _initialDataCaching;
        private bool _initialNameFilesAsHashes;
        private bool _initialStripEngineCode;
        private UnityEditor.WebGL.WasmCodeOptimization _initialCodeOptimization;
        private Il2CppCodeGeneration _initialIl2CppCodeGeneration;
        private bool _initialShowSplashScreen;
        private bool _initialShowUnityLogo;
        private bool _initialWasm2023;
        private bool _initialWebAssemblyTable;
        private bool _initialWebAssemblyBigInt;
        private WebGLTextureSubtarget _initialWebGLTextureSubtarget;
        private int _initialMaximumMemorySize;

        public const string SUMMARY_FILE_NAME = "CGReleaseBuildReportSummary-v1.json";
        public const string INTERMEDIATE_BUILD_SUFFIX = "_cg_intermediate_build";

        /// <summary>
        /// Holds the files taken from the secondary builds until the build that gets uploaded has been made
        /// </summary>
        private const string STAGING_SUFFIX = "_cg_staging";

        /// <summary>
        /// The release currently being built, or the last one, until the Analyzer consumes it.
        /// The post process callback is handed the report of a single build.
        /// The build that gets uploaded is the last of the four, so this is how it knows when the release started and whether anything in it failed.
        /// </summary>
        public static ReleaseBuildState CurrentRelease { get; private set; }

        public static void ClearCurrentRelease()
        {
            CurrentRelease = null;
        }

        public void DoDevelopmentBuild()
        {
            Build(BuildVariant.Development, null);
        }

        public void DoReleaseBuild(AdditionalBuildOptions additionalOptions)
        {
            Build(BuildVariant.Release, additionalOptions);
        }

        private void Build(BuildVariant selectedVariant, AdditionalBuildOptions additionalOptions)
        {
            var startTime = System.DateTime.Now;

            var scenePaths = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();

            if (scenePaths.Length == 0)
            {
                Debug.LogError("There aren't any enabled scenes in the build settings. Please add at least one scene.");
                return;
            }

            var buildPath = GetBuildPath(selectedVariant);
            if (selectedVariant == BuildVariant.Release)
            {
                CurrentRelease = new ReleaseBuildState { outputPath = buildPath };
            }

            var buildOptions = new BuildPlayerOptions
            {
                scenes = scenePaths,
                locationPathName = buildPath,
                target = BuildTarget.WebGL,
                options = GetBuildOptions(selectedVariant),
            };
            if (Directory.Exists(buildOptions.locationPathName))
            {
                Directory.Delete(buildOptions.locationPathName, true);
            }

            StoreInitialSettings();
            var buildOptimizations = ConfigureWebGLSettings(selectedVariant);
            var optimizationsDebugString = string.Join(
                "\n",
                buildOptimizations.Select(o => $"{o.optimization}: {o.initialValue} => {o.buildValue}")
            );

            var stagingPath = buildPath + STAGING_SUFFIX;
            var mainBuildSucceeded = false;
            try
            {
                // The secondary builds are made first, into their own folder, with what we need from them kept in the staging folder
                // The build that gets uploaded is made last.
                SecondaryBuildResult astcResult = null;
                SecondaryBuildResult max512Result = null;
                SecondaryBuildResult max1024Result = null;
                if (selectedVariant == BuildVariant.Release)
                {
                    DeleteDirectory(stagingPath);
                    Directory.CreateDirectory(stagingPath);
                    astcResult = DoASTCBuild(buildOptions, stagingPath);
                    if (additionalOptions.supportsMobile)
                    {
                        max512Result = DoBuildWithMaxMemorySize(512, buildOptions, stagingPath);
                        max1024Result = DoBuildWithMaxMemorySize(1024, buildOptions, stagingPath);
                    }
                }

                EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.DXT;
                var report = BuildPipeline.BuildPlayer(buildOptions);
                mainBuildSucceeded = report.summary.result == BuildResult.Succeeded;

                if (mainBuildSucceeded)
                {
                    Debug.Log(
                        $"Build succeeded in {report.summary.totalTime.TotalMinutes:F1} minutes\nWith these settings:\n{optimizationsDebugString}"
                    );
                    if (selectedVariant == BuildVariant.Release)
                    {
                        var secondaryArtifacts = new List<SecondaryArtifact>();
                        var astcDataFileName = CollectSecondaryFile(
                            BuildReportGenerator.TYPE_DATA_ASTC,
                            astcResult,
                            stagingPath,
                            buildPath,
                            secondaryArtifacts
                        );
                        var limitedMemoryFiles = new LimitedMemoryFiles
                        {
                            max512mb = CollectSecondaryFile(
                                BuildReportGenerator.TYPE_WASM_MAX_512MB,
                                max512Result,
                                stagingPath,
                                buildPath,
                                secondaryArtifacts
                            ),
                            max1024mb = CollectSecondaryFile(
                                BuildReportGenerator.TYPE_WASM_MAX_1024MB,
                                max1024Result,
                                stagingPath,
                                buildPath,
                                secondaryArtifacts
                            ),
                        };

                        if (!HasAllMainFiles(buildPath))
                        {
                            // Without the main files the folder cannot be uploaded: cannot leave a report behind saying otherwise
                            MarkReleaseFailed();
                            return;
                        }

                        CurrentRelease.durationSeconds = (DateTime.Now - startTime).TotalSeconds;
                        var reportGenerator = new BuildReportGenerator();
                        reportGenerator.GenerateReport(
                            report,
                            selectedVariant,
                            additionalOptions,
                            buildPath,
                            CurrentRelease.durationSeconds,
                            buildOptimizations,
                            astcDataFileName,
                            limitedMemoryFiles,
                            secondaryArtifacts
                        );
                    }
                }
                else
                {
                    Debug.LogError(
                        $"Build failed! {report.summary.totalTime.TotalMinutes:F1} minutes\nWith these settings:\n{optimizationsDebugString}"
                    );
                    foreach (var step in report.steps)
                    {
                        foreach (var message in step.messages.Where(m => m.type == LogType.Error))
                        {
                            Debug.LogError($"Build Error: {message.content}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MarkReleaseFailed();
                Debug.LogError($"Build failed with exception: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                if (selectedVariant == BuildVariant.Release && !mainBuildSucceeded)
                {
                    // the post process callback only runs for a successful build, so nothing else would clear the release state
                    // which means that a later build to the same folder from the Unity Editor would be taken for this failed release
                    ClearCurrentRelease();
                }
                DeleteDirectory(stagingPath);
                RestoreWebGLSettings();
                AssetDatabase.SaveAssets(); // restore changed PlayerSettings
            }
        }

        /// <summary>
        /// Does a second build with ASTC compression format, and keeps its data file in the staging folder, prefixed with "astc_".
        /// The Release Build carries on when this fails, without an ASTC data file.
        /// </summary>
        private SecondaryBuildResult DoASTCBuild(BuildPlayerOptions originalBuildOptions, string stagingPath)
        {
            var initialWebGLBuildSubtarget = EditorUserBuildSettings.webGLBuildSubtarget;
            try
            {
                EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.ASTC;
                return DoSecondaryBuild(originalBuildOptions, stagingPath, ".data.br", "astc_");
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "Could not produce the ASTC data file, so this build won't have one and mobile players will download the "
                        + $"desktop textures instead. Please report this to CrazyGames. Cause: {e.Message}"
                );
                return SecondaryBuildResult.Failed(e.Message);
            }
            finally
            {
                EditorUserBuildSettings.webGLBuildSubtarget = initialWebGLBuildSubtarget;
            }
        }

        /// <summary>
        /// Does a build with the given max memory size, and keeps its wasm file in the staging folder, prefixed with "max_{memory}mb_".
        /// The Release Build carries on when this fails, without that file.
        /// </summary>
        private SecondaryBuildResult DoBuildWithMaxMemorySize(int memorySize, BuildPlayerOptions originalBuildOptions, string stagingPath)
        {
            var initialMaximumMemorySize = PlayerSettings.WebGL.maximumMemorySize;
            try
            {
                PlayerSettings.WebGL.maximumMemorySize = memorySize;
                return DoSecondaryBuild(originalBuildOptions, stagingPath, ".wasm.br", "max_" + memorySize + "mb_");
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"Could not produce the {memorySize}MB memory limited wasm file, so this build won't have one and crashes on "
                        + $"low memory devices will be harder to detect. Please report this to CrazyGames. Cause: {e.Message}"
                );
                return SecondaryBuildResult.Failed(e.Message);
            }
            finally
            {
                PlayerSettings.WebGL.maximumMemorySize = initialMaximumMemorySize;
            }
        }

        /// <summary>
        /// Makes a build into the intermediate folder and copies the file ending in <paramref name="suffix"/> into the staging
        /// folder under <paramref name="prefix"/>, so it survives the builds that come after this one.
        /// </summary>
        private SecondaryBuildResult DoSecondaryBuild(
            BuildPlayerOptions originalBuildOptions,
            string stagingPath,
            string suffix,
            string prefix
        )
        {
            var buildOptions = originalBuildOptions;
            buildOptions.locationPathName += INTERMEDIATE_BUILD_SUFFIX;
            // reset options, don't need autorun, and don't clean cache for quicker build
            buildOptions.options = BuildOptions.None;
            try
            {
                DeleteDirectory(buildOptions.locationPathName);

                var report = BuildPipeline.BuildPlayer(buildOptions);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new Exception($"the build finished with result {report.summary.result}");
                }

                var from = BuildReportGenerator.FindMainFile(report, suffix);
                var fileName = prefix + Path.GetFileName(from);
                File.Copy(from, Path.Combine(stagingPath, fileName), true);
                return SecondaryBuildResult.Ok(fileName);
            }
            finally
            {
                DeleteDirectory(buildOptions.locationPathName);
            }
        }

        /// <summary>
        /// Moves a staged file into the build folder that gets uploaded, and records how that artifact ended up.
        /// Returns its file name, or null when there isn't one.
        /// </summary>
        private static string CollectSecondaryFile(
            string artifactType,
            SecondaryBuildResult result,
            string stagingPath,
            string buildPath,
            List<SecondaryArtifact> secondaryArtifacts
        )
        {
            if (result == null)
            {
                secondaryArtifacts.Add(SecondaryArtifact.NotRequested(artifactType));
                return null;
            }
            if (result.fileName == null)
            {
                secondaryArtifacts.Add(SecondaryArtifact.Failed(artifactType, result.error));
                return null;
            }

            try
            {
                File.Copy(Path.Combine(stagingPath, result.fileName), Path.Combine(buildPath, "Build", result.fileName), true);
                secondaryArtifacts.Add(SecondaryArtifact.Ok(artifactType));
                return result.fileName;
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not copy {result.fileName} into the build folder, so this build won't have it: {e.Message}");
                secondaryArtifacts.Add(SecondaryArtifact.Failed(artifactType, e.Message));
                return null;
            }
        }

        /// <summary>
        /// Whether the build folder still holds the files every game needs. Unity deleting them out from under us is the kind of
        /// thing that should stop a release rather than produce a folder that looks fine and cannot be uploaded.
        /// </summary>
        private static bool HasAllMainFiles(string buildPath)
        {
            var buildDir = Path.Combine(buildPath, "Build");
            var fileNames = Directory.Exists(buildDir)
                ? Directory.GetFiles(buildDir).Select(Path.GetFileName).Where(name => !BuildReportGenerator.IsSecondaryFile(name)).ToList()
                : new List<string>();

            var required = new Dictionary<string, string[]>
            {
                { "data", new[] { ".data.br", ".data" } },
                { "framework", new[] { ".framework.js.br", ".framework.js" } },
                { "loader", new[] { ".loader.js" } },
                { "wasm", new[] { ".wasm.br", ".wasm" } },
            };
            var missing = required
                .Where(entry =>
                    !fileNames.Any(name => entry.Value.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                )
                .Select(entry => entry.Key)
                .ToList();
            if (missing.Count == 0)
            {
                return true;
            }

            Debug.LogError(
                $"The build folder is missing its {string.Join(", ", missing)} file(s), so this build cannot be uploaded. "
                    + "Please run the release build again and report this to CrazyGames if it keeps happening."
            );
            return false;
        }

        /// <summary>
        /// Deletes an intermediate build folder. Never throws, so it is safe to call while cleaning up after another failure.
        /// </summary>
        private static void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not delete {path}, you can remove it by hand: {e.Message}");
            }
        }

        private static void MarkReleaseFailed()
        {
            if (CurrentRelease != null)
            {
                CurrentRelease.failed = true;
            }
        }

        private BuildOptions GetBuildOptions(BuildVariant variant)
        {
            switch (variant)
            {
                case BuildVariant.Development:
                    return BuildOptions.Development | BuildOptions.AutoRunPlayer; // to start the game in browser;
                case BuildVariant.Release:
                    return BuildOptions.AutoRunPlayer // to start the game in browser
#if !UNITY_6000_4_OR_NEWER
                        // to avoid sometimes packedAssets being empty
                        // however Unity 6.4 has an issue and the build fails if CleanBuildCache is used (internal build error, backend has requested build program 6 times)
                        // but CleanBuildCache doesn't seems necessary for 6.4 anyways, as packedAssets was always populated there
                        | BuildOptions.CleanBuildCache
#endif
                    ;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(variant), variant, "Unknown build variant");
            }
        }

        private void StoreInitialSettings()
        {
            _initialCompressionFormat = PlayerSettings.WebGL.compressionFormat;
            _initialExceptionSupport = PlayerSettings.WebGL.exceptionSupport;
            _initialDebugSymbolMode = PlayerSettings.WebGL.debugSymbolMode;
            _initialDataCaching = PlayerSettings.WebGL.dataCaching;
            _initialNameFilesAsHashes = PlayerSettings.WebGL.nameFilesAsHashes;
            _initialStripEngineCode = PlayerSettings.stripEngineCode;
            _initialIl2CppCodeGeneration = PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.WebGL);
            _initialCodeOptimization = UnityEditor.WebGL.UserBuildSettings.codeOptimization;
            _initialShowSplashScreen = PlayerSettings.SplashScreen.show;
            _initialShowUnityLogo = PlayerSettings.SplashScreen.showUnityLogo;
            _initialWebAssemblyBigInt = PlayerSettings.WebGL.webAssemblyBigInt;
            _initialWebAssemblyTable = PlayerSettings.WebGL.webAssemblyTable;
            _initialWasm2023 = PlayerSettings.WebGL.wasm2023;
            _initialWebGLTextureSubtarget = EditorUserBuildSettings.webGLBuildSubtarget;
            _initialMaximumMemorySize = PlayerSettings.WebGL.maximumMemorySize;
        }

        private List<BuildOptimization> ConfigureWebGLSettings(BuildVariant variant)
        {
            var optimizations = new List<BuildOptimization>();

            try
            {
                var webglTarget = NamedBuildTarget.WebGL;

                var buildIl2CppCodeGeneration = Il2CppCodeGeneration.OptimizeSize;
                optimizations.Add(
                    new BuildOptimization(
                        "il2CppCodeGeneration",
                        _initialIl2CppCodeGeneration.ToString(),
                        buildIl2CppCodeGeneration.ToString()
                    )
                );
                PlayerSettings.SetIl2CppCodeGeneration(webglTarget, buildIl2CppCodeGeneration);

                optimizations.Add(new BuildOptimization("showSplashScreen", _initialShowSplashScreen.ToString(), "False"));
                PlayerSettings.SplashScreen.show = false;

                optimizations.Add(new BuildOptimization("showUnityLogo", _initialShowUnityLogo.ToString(), "False"));
                PlayerSettings.SplashScreen.showUnityLogo = false;

                switch (variant)
                {
                    case BuildVariant.Development:
                        ApplyDevelopmentSettings(optimizations);
                        break;
                    case BuildVariant.Release:
                        ApplyReleaseSettings(optimizations);
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not configure WebGL: {e.Message}");
            }

            return optimizations;
        }

        private void ApplyDevelopmentSettings(List<BuildOptimization> optimizations)
        {
            var buildCompression = WebGLCompressionFormat.Disabled;
            optimizations.Add(
                new BuildOptimization("compressionFormat", _initialCompressionFormat.ToString(), buildCompression.ToString())
            );
            PlayerSettings.WebGL.compressionFormat = buildCompression;

            var buildExceptionSupport = WebGLExceptionSupport.FullWithStacktrace;
            optimizations.Add(
                new BuildOptimization("exceptionSupport", _initialExceptionSupport.ToString(), buildExceptionSupport.ToString())
            );
            PlayerSettings.WebGL.exceptionSupport = buildExceptionSupport;

            var buildDebugSymbols = WebGLDebugSymbolMode.External;
            optimizations.Add(new BuildOptimization("debugSymbolMode", _initialDebugSymbolMode.ToString(), buildDebugSymbols.ToString()));
            PlayerSettings.WebGL.debugSymbolMode = buildDebugSymbols;

            optimizations.Add(new BuildOptimization("dataCaching", _initialDataCaching.ToString(), "False"));
            PlayerSettings.WebGL.dataCaching = false;

            optimizations.Add(new BuildOptimization("nameFilesAsHashes", _initialNameFilesAsHashes.ToString(), "False"));
            PlayerSettings.WebGL.nameFilesAsHashes = false;

            optimizations.Add(new BuildOptimization("stripEngineCode", _initialStripEngineCode.ToString(), "False"));
            PlayerSettings.stripEngineCode = false;

            var buildCodeOptimization = UnityEditor.WebGL.WasmCodeOptimization.BuildTimes;
            optimizations.Add(
                new BuildOptimization("codeOptimization", _initialCodeOptimization.ToString(), buildCodeOptimization.ToString())
            );
            UnityEditor.WebGL.UserBuildSettings.codeOptimization = buildCodeOptimization;
        }

        private void ApplyReleaseSettings(List<BuildOptimization> optimizations)
        {
            var buildCompression = WebGLCompressionFormat.Brotli;
            optimizations.Add(
                new BuildOptimization("compressionFormat", _initialCompressionFormat.ToString(), buildCompression.ToString())
            );
            PlayerSettings.WebGL.compressionFormat = buildCompression;

            if (_initialExceptionSupport == WebGLExceptionSupport.None)
            {
                // if exception support is set to None, which is even better than ExplicitlyThrownExceptionsOnly, we keep it as is
                optimizations.Add(
                    new BuildOptimization("exceptionSupport", _initialExceptionSupport.ToString(), _initialExceptionSupport.ToString())
                );
            }
            else
            {
                var buildExceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
                optimizations.Add(
                    new BuildOptimization("exceptionSupport", _initialExceptionSupport.ToString(), buildExceptionSupport.ToString())
                );
                PlayerSettings.WebGL.exceptionSupport = buildExceptionSupport;
            }

            var buildDebugSymbols = WebGLDebugSymbolMode.Off;
            optimizations.Add(new BuildOptimization("debugSymbolMode", _initialDebugSymbolMode.ToString(), buildDebugSymbols.ToString()));
            PlayerSettings.WebGL.debugSymbolMode = buildDebugSymbols;

            optimizations.Add(new BuildOptimization("dataCaching", _initialDataCaching.ToString(), "True"));
            PlayerSettings.WebGL.dataCaching = true;

            optimizations.Add(new BuildOptimization("nameFilesAsHashes", _initialNameFilesAsHashes.ToString(), "True"));
            PlayerSettings.WebGL.nameFilesAsHashes = true;

            optimizations.Add(new BuildOptimization("stripEngineCode", _initialStripEngineCode.ToString(), "True"));
            PlayerSettings.stripEngineCode = true;

            var buildCodeOptimization = UnityEditor.WebGL.WasmCodeOptimization.DiskSizeLTO;
            optimizations.Add(
                new BuildOptimization("codeOptimization", _initialCodeOptimization.ToString(), buildCodeOptimization.ToString())
            );
            UnityEditor.WebGL.UserBuildSettings.codeOptimization = buildCodeOptimization;

            optimizations.Add(new BuildOptimization("webAssemblyBigInt", _initialWebAssemblyBigInt.ToString(), "True"));
            PlayerSettings.WebGL.webAssemblyBigInt = true;

            optimizations.Add(new BuildOptimization("webAssemblyTable", _initialWebAssemblyTable.ToString(), "True"));
            PlayerSettings.WebGL.webAssemblyTable = true;

            optimizations.Add(new BuildOptimization("wasm2023", _initialWasm2023.ToString(), "True"));
            PlayerSettings.WebGL.wasm2023 = true;
        }

        private void RestoreWebGLSettings()
        {
            try
            {
                PlayerSettings.WebGL.compressionFormat = _initialCompressionFormat;
                PlayerSettings.WebGL.exceptionSupport = _initialExceptionSupport;
                PlayerSettings.WebGL.debugSymbolMode = _initialDebugSymbolMode;
                PlayerSettings.WebGL.dataCaching = _initialDataCaching;
                PlayerSettings.WebGL.nameFilesAsHashes = _initialNameFilesAsHashes;
                PlayerSettings.stripEngineCode = _initialStripEngineCode;
                PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, _initialIl2CppCodeGeneration);
                UnityEditor.WebGL.UserBuildSettings.codeOptimization = _initialCodeOptimization;
                PlayerSettings.SplashScreen.show = _initialShowSplashScreen;
                PlayerSettings.SplashScreen.showUnityLogo = _initialShowUnityLogo;
                PlayerSettings.WebGL.webAssemblyBigInt = _initialWebAssemblyBigInt;
                PlayerSettings.WebGL.webAssemblyTable = _initialWebAssemblyTable;
                PlayerSettings.WebGL.wasm2023 = _initialWasm2023;
                EditorUserBuildSettings.webGLBuildSubtarget = _initialWebGLTextureSubtarget;
                PlayerSettings.WebGL.maximumMemorySize = _initialMaximumMemorySize;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not restore WebGL settings: {e.Message}");
            }
        }

        private string GetBuildPath(BuildVariant buildVariant)
        {
            var folder = buildVariant == BuildVariant.Development ? "CrazyGamesDevelopment" : "CrazyGamesRelease";
            return Path.Combine("Builds", folder);
        }
    }

    public enum BuildVariant
    {
        Development,
        Release,
    }

    /// <summary>
    /// A staged file from one of the secondary builds, or the reason there isn't one.
    /// </summary>
    public class SecondaryBuildResult
    {
        public string fileName;
        public string error;

        public static SecondaryBuildResult Ok(string fileName)
        {
            return new SecondaryBuildResult { fileName = fileName };
        }

        public static SecondaryBuildResult Failed(string error)
        {
            return new SecondaryBuildResult { error = error };
        }
    }

    /// <summary>
    /// What the Analyzer needs to know about a release once every build in it is over.
    /// </summary>
    public class ReleaseBuildState
    {
        public string outputPath;
        public bool failed;

        /// <summary>
        /// How long the Release Build took, measured when it ended.
        /// The Analyzer can't determine this by itself, because it is only called on the next editor tick, and the editor can sit idle for before that happens.
        /// </summary>
        public double durationSeconds;
    }

    public class LimitedMemoryFiles
    {
        public string max512mb;
        public string max1024mb;
    }

    [System.Serializable]
    public class AdditionalBuildOptions
    {
        public bool supportsMobile;
    }
}
#endif
