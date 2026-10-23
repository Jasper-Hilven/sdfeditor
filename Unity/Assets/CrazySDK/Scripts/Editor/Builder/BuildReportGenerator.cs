using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

#if UNITY_6000_0_OR_NEWER
namespace CrazyGames
{
    [System.Serializable]
    public class BuildOptimization
    {
        public string optimization;
        public string initialValue;
        public string buildValue;

        public BuildOptimization(string optimization, string initialValue, string buildValue)
        {
            this.optimization = optimization;
            this.initialValue = initialValue;
            this.buildValue = buildValue;
        }
    }

    /// <summary>
    /// Outcome of one of the secondary builds.
    /// A Release Build carries on when a secondary build fails, so the report has to say whether a file is missing because it was never asked for, or because its build failed.
    /// </summary>
    [System.Serializable]
    public class SecondaryArtifact
    {
        public string type;
        public string status;
        public string error;

        public const string STATUS_OK = "ok";
        public const string STATUS_NOT_REQUESTED = "not_requested";
        public const string STATUS_FAILED = "failed";

        public static SecondaryArtifact Ok(string type)
        {
            return new SecondaryArtifact { type = type, status = STATUS_OK };
        }

        public static SecondaryArtifact NotRequested(string type)
        {
            return new SecondaryArtifact { type = type, status = STATUS_NOT_REQUESTED };
        }

        public static SecondaryArtifact Failed(string type, string error)
        {
            return new SecondaryArtifact
            {
                type = type,
                status = STATUS_FAILED,
                error = error,
            };
        }
    }

    [System.Serializable]
    public class BuildData
    {
        public string buildDateTime;
        public string buildScriptVersion;
        public string sdkVersion;
        public string unityVersion;
        public string buildVariant;
        public AdditionalBuildOptions additionalOptions;
        public string buildResult;
        public double totalTime;
        public List<BuildOptimization> buildOptimizations;
        public List<GeneratedFile> generatedFiles;
        public List<SecondaryArtifact> secondaryArtifacts;
    }

    [System.Serializable]
    public class GeneratedFile
    {
        public string filename;
        public string type;
        public ulong uncompressedSize;
        public ulong size;
    }

    /// <summary>
    /// Generates the report that will be uploaded to Developer Portal, contains information about the build.
    /// </summary>
    public class BuildReportGenerator
    {
        private const string BUILD_SCRIPT_VERSION = "1.5.0";

        public const string TYPE_DATA_ASTC = "data_astc";
        public const string TYPE_WASM_MAX_512MB = "wasm_max_512mb";
        public const string TYPE_WASM_MAX_1024MB = "wasm_max_1024mb";

        public void GenerateReport(
            BuildReport report,
            BuildVariant variant,
            AdditionalBuildOptions additionalOptions,
            string buildPath,
            double totalTimeSeconds,
            List<BuildOptimization> buildOptimizations,
            string astcDataFileName,
            LimitedMemoryFiles limitedMemoryFiles,
            List<SecondaryArtifact> secondaryArtifacts
        )
        {
            var generatedFiles = GetMainFiles(report);
            if (!string.IsNullOrEmpty(astcDataFileName))
            {
                var path = Path.Combine(buildPath, "Build", astcDataFileName);
                generatedFiles.Add(
                    new GeneratedFile
                    {
                        filename = astcDataFileName,
                        type = TYPE_DATA_ASTC,
                        uncompressedSize = GetUncompressedSize(path, astcDataFileName),
                        size = (ulong)new FileInfo(path).Length,
                    }
                );
            }

            // limited memory files will be missing if the game doesn't support mobile devices, or if their build failed
            if (limitedMemoryFiles != null)
            {
                if (limitedMemoryFiles.max512mb != null)
                {
                    var path512 = Path.Combine(buildPath, "Build", limitedMemoryFiles.max512mb);
                    generatedFiles.Add(
                        new GeneratedFile
                        {
                            filename = limitedMemoryFiles.max512mb,
                            type = TYPE_WASM_MAX_512MB,
                            uncompressedSize = GetUncompressedSize(path512, limitedMemoryFiles.max512mb),
                            size = (ulong)new FileInfo(path512).Length,
                        }
                    );
                }
                if (limitedMemoryFiles.max1024mb != null)
                {
                    var path1024 = Path.Combine(buildPath, "Build", limitedMemoryFiles.max1024mb);
                    generatedFiles.Add(
                        new GeneratedFile
                        {
                            filename = limitedMemoryFiles.max1024mb,
                            type = TYPE_WASM_MAX_1024MB,
                            uncompressedSize = GetUncompressedSize(path1024, limitedMemoryFiles.max1024mb),
                            size = (ulong)new FileInfo(path1024).Length,
                        }
                    );
                }
            }

            var buildData = new BuildData
            {
                buildDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                buildScriptVersion = BUILD_SCRIPT_VERSION,
                sdkVersion = CrazySDK.Version,
                unityVersion = Application.unityVersion,
                buildVariant = variant.ToString(),
                additionalOptions = additionalOptions,
                buildResult = report.summary.result.ToString(),
                totalTime = totalTimeSeconds,
                buildOptimizations = buildOptimizations,
                generatedFiles = generatedFiles,
                secondaryArtifacts = secondaryArtifacts,
            };

            var json = JsonUtility.ToJson(buildData, true);
            var reportPath = Path.Combine(buildPath, "crazygames_build_report.json");

            File.WriteAllText(reportPath, json);
        }

        /// <summary>
        /// Finds the main file ending in <paramref name="suffix"/> (i.e ".data.br")
        /// Unity 6.6 leaves the files it writes into the Build folder out of the report, so when the report doesn't mention one we read the Build folder instead
        /// </summary>
        public static string FindMainFile(BuildReport report, string suffix)
        {
            foreach (var file in report.GetFiles())
            {
                if (IsMainFile(Path.GetFileName(file.path), suffix))
                {
                    return file.path;
                }
            }

            var buildDir = Path.Combine(report.summary.outputPath, "Build");
            if (!Directory.Exists(buildDir))
            {
                throw new FileNotFoundException($"The build report didn't list a {suffix} file and the {buildDir} folder is missing.");
            }

            var matches = Directory.GetFiles(buildDir).Where(path => IsMainFile(Path.GetFileName(path), suffix)).ToList();
            if (matches.Count == 0)
            {
                throw new FileNotFoundException($"The build report didn't list a {suffix} file and there isn't one in {buildDir} either.");
            }
            if (matches.Count > 1)
            {
                Debug.LogWarning($"Found {matches.Count} {suffix} files in {buildDir}, using {Path.GetFileName(matches[0])}.");
            }

            Debug.Log($"The build report didn't list a {suffix} file, using {matches[0]} from the build folder instead.");
            return matches[0];
        }

        private static bool IsMainFile(string filename, string suffix)
        {
            return !string.IsNullOrEmpty(filename)
                && !IsSecondaryFile(filename)
                && filename.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Files copied into the Build folder from the secondary builds.
        /// They are uploaded, but they belong to a specific class ofdevices, so the Developer Portal leaves them out of the total size.
        /// </summary>
        public static bool IsSecondaryFile(string filename)
        {
            return (
                    filename.StartsWith("astc_", StringComparison.OrdinalIgnoreCase)
                    && filename.EndsWith(".data.br", StringComparison.OrdinalIgnoreCase)
                )
                || (
                    filename.StartsWith("max_", StringComparison.OrdinalIgnoreCase)
                    && filename.EndsWith(".wasm.br", StringComparison.OrdinalIgnoreCase)
                );
        }

        public static List<GeneratedFile> GetMainFiles(BuildReport report)
        {
            var files = new List<GeneratedFile>();

            // initially, these are the files we are interested in
            var filePatterns = new Dictionary<string, string>
            {
                { ".data.br", "data" },
                { ".data", "data" },
                { ".framework.js.br", "framework" },
                { ".framework.js", "framework" },
                { ".loader.js", "loader" },
                { ".wasm.br", "wasm" },
                { ".wasm", "wasm" },
            };

            foreach (var file in report.GetFiles())
            {
                AddMainFile(files, filePatterns, file.path, file.size);
            }

            // Unity 6.6 doesn't list the main files in the report, so add whatever it left out from the build folder
            var buildDir = Path.Combine(report.summary.outputPath, "Build");
            if (Directory.Exists(buildDir))
            {
                foreach (var path in Directory.GetFiles(buildDir))
                {
                    var filename = Path.GetFileName(path);
                    if (files.Any(f => string.Equals(f.filename, filename, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    AddMainFile(files, filePatterns, path, (ulong)new FileInfo(path).Length);
                }
            }

            return files.OrderBy(f => f.type).ToList();
        }

        private static void AddMainFile(List<GeneratedFile> files, Dictionary<string, string> filePatterns, string path, ulong size)
        {
            var filename = Path.GetFileName(path);
            if (IsSecondaryFile(filename))
            {
                return;
            }

            foreach (var pattern in filePatterns)
            {
                if (filename.EndsWith(pattern.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var generatedFile = new GeneratedFile
                    {
                        filename = filename,
                        type = pattern.Value,
                        uncompressedSize = GetUncompressedSize(path, filename),
                        size = size,
                    };
                    files.Add(generatedFile);
                    break;
                }
            }
        }

        private static ulong GetUncompressedSize(string filePath, string filename)
        {
            if (filename.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using (var compressedFile = File.OpenRead(filePath))
                    using (var brotli = new BrotliStream(compressedFile, CompressionMode.Decompress))
                    using (var memoryStream = new MemoryStream())
                    {
                        brotli.CopyTo(memoryStream);
                        return (ulong)memoryStream.Length;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Could not get uncompressed size for {filename}: {e.Message}");
                    return (ulong)new FileInfo(filePath).Length;
                }
            }

            return (ulong)new FileInfo(filePath).Length;
        }
    }
}
#endif
