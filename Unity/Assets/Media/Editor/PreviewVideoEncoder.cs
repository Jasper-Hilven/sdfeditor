using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace SdfMedia.Editor
{
    /// <summary>
    /// Stitches the frames <see cref="PreviewVideoRecorder"/> wrote into the three preview videos with ffmpeg, one per video shape:
    /// <c>Media/preview-landscape.mp4</c> (1920x1080), <c>preview-portrait.mp4</c> (1080x1620, 2:3) and <c>preview-square.mp4</c>
    /// (1080x1080). A video is the shape's cover image held for <see cref="HoldSeconds"/> (when <c>Media/Covers/cover-&lt;shape&gt;.png</c>
    /// exists; the cover is scaled until it fills the video and the overflow cropped from the middle, never padded, so no bars) followed by
    /// the four clips back to back, 30 frames a second: H.264 in the yuv420p pixel format with the BT.709 colour tags, no audio track, the
    /// moov atom up front for streaming. The bit rate is capped at <see cref="MaxRateMegabits"/> Mbit/s, so a video of at most 20 seconds
    /// stays well under the 50 MB limit whatever the picture. Afterwards each video is probed: its length must be 15 to 20 seconds, its
    /// size at most 50 MB, and a black frame detector must find nothing; anything else is logged as a warning.
    /// <para>
    /// The command line is one ffmpeg call per video, with the cover (a looped still, 0.5 s) and the clips' numbered PNGs
    /// (<c>0000.png</c> on, <c>-start_number 0</c>) as inputs, a filter graph that gives every input the video's size, 30 fps, the
    /// BT.709 matrix and yuv420p, and a <c>concat</c> of them; see <see cref="BuildArguments"/>.
    /// </para>
    /// </summary>
    public static class PreviewVideoEncoder
    {
        /// <summary>The ffmpeg to run: Homebrew's, else the one on the PATH.</summary>
        public static string FfmpegPath => File.Exists("/opt/homebrew/bin/ffmpeg") ? "/opt/homebrew/bin/ffmpeg" : "ffmpeg";

        /// <summary>ffprobe, next to ffmpeg when that is Homebrew's.</summary>
        public static string FfprobePath => File.Exists("/opt/homebrew/bin/ffprobe") ? "/opt/homebrew/bin/ffprobe" : "ffprobe";

        public const int Fps = 30;

        /// <summary>Seconds the cover is held at the start.</summary>
        public const float HoldSeconds = 0.5f;

        /// <summary>The cap on the video bit rate, megabits a second: 14 Mbit/s for 20 seconds is 35 MB.</summary>
        public const int MaxRateMegabits = 14;

        public const long MaxBytes = 50L * 1024 * 1024;
        public const double MinSeconds = 15, MaxSeconds = 20;

        /// <summary>The repo's Media folder (next to the Unity project's).</summary>
        public static string MediaFolder => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "..", "Media"));

        /// <summary>Where the frames of a shape go: <c>Media/Video/&lt;width&gt;x&lt;height&gt;/clipN</c>, N from 1.</summary>
        public static string ClipFolder(PreviewVideoRecorder.Shape shape, int clip) =>
            Path.Combine(MediaFolder, "Video", $"{shape.width}x{shape.height}", "clip" + clip);

        public static string CoverPath(PreviewVideoRecorder.Shape shape)
        {
            // The cover at the video's own size when CoverCapture made one (portrait and square), else the cover itself
            var video = Path.Combine(MediaFolder, "Covers", $"cover-{shape.name}-video.png");
            return File.Exists(video) ? video : Path.Combine(MediaFolder, "Covers", $"cover-{shape.name}.png");
        }

        public static string OutputPath(PreviewVideoRecorder.Shape shape) => Path.Combine(MediaFolder, $"preview-{shape.name}.mp4");

        /// <summary>Encodes the three videos from the recorded frames; false if one failed. Shows a progress bar.</summary>
        public static bool EncodeAll()
        {
            var ok = true;
            try
            {
                var shapes = PreviewVideoRecorder.Shapes;
                for (var i = 0; i < shapes.Length; i++)
                {
                    EditorUtility.DisplayProgressBar("Preview videos", $"Encoding {shapes[i].name}", (float)i / shapes.Length);
                    ok &= Encode(shapes[i]);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return ok;
        }

        /// <summary>The clips of a shape that have frames, as their folders in order.</summary>
        private static List<string> Clips(PreviewVideoRecorder.Shape shape)
        {
            var clips = new List<string>();
            for (var clip = 1; clip <= PreviewVideoRecorder.Arenas.Length; clip++)
            {
                var folder = ClipFolder(shape, clip);
                if (Directory.Exists(folder) && File.Exists(Path.Combine(folder, "0000.png")))
                    clips.Add(folder);
                else
                    Debug.LogWarning($"SDF Media: no frames in {folder}; the {shape.name} video goes without that clip");
            }
            return clips;
        }

        /// <summary>Encodes one shape's video; false when there is nothing to encode or ffmpeg failed.</summary>
        public static bool Encode(PreviewVideoRecorder.Shape shape)
        {
            var clips = Clips(shape);
            if (clips.Count == 0)
            {
                Debug.LogError($"SDF Media: no clips recorded for the {shape.name} video");
                return false;
            }
            var cover = CoverPath(shape);
            var hasCover = File.Exists(cover);
            if (!hasCover)
                Debug.LogWarning($"SDF Media: {cover} is missing; the {shape.name} video starts without the cover hold");
            var output = OutputPath(shape);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            var arguments = BuildArguments(shape, hasCover ? cover : null, clips, output);
            if (!Run(FfmpegPath, arguments, out _, out var errors))
            {
                Debug.LogError($"SDF Media: ffmpeg failed for the {shape.name} video:\n{errors}\n{FfmpegPath} {string.Join(" ", arguments.Select(a => a.Contains(' ') || a.Contains('[') ? "\"" + a + "\"" : a))}");
                return false;
            }
            Verify(shape, output);
            return true;
        }

        /// <summary>
        /// The ffmpeg arguments for a video: the cover first when there is one (<c>-loop 1 -framerate 30 -t 0.5</c>), each clip's frames
        /// next (<c>-framerate 30 -start_number 0 -i clipN/%04d.png</c>), the filter graph and the encoder settings. Every input is
        /// brought to the video's size (the cover scaled to fill and cropped, the clips already are that size), a square pixel, 30 fps,
        /// the BT.709 matrix, limited range and yuv420p before the <c>concat</c>.
        /// </summary>
        public static List<string> BuildArguments(PreviewVideoRecorder.Shape shape, string cover, IReadOnlyList<string> clipFolders, string output)
        {
            var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
            var graph = new StringBuilder();
            var inputs = 0;
            const string Convert = "fps=30,scale=out_color_matrix=bt709:out_range=tv,format=yuv420p";
            if (cover != null)
            {
                arguments.AddRange(new[] { "-loop", "1", "-framerate", "30", "-t", HoldSeconds.ToString("0.###", CultureInfo.InvariantCulture), "-i", cover });
                graph.Append($"[0:v]scale={shape.width}:{shape.height}:force_original_aspect_ratio=increase,crop={shape.width}:{shape.height},setsar=1,{Convert}[v0];");
                inputs++;
            }
            foreach (var folder in clipFolders)
            {
                arguments.AddRange(new[] { "-framerate", "30", "-start_number", "0", "-i", Path.Combine(folder, "%04d.png") });
                graph.Append($"[{inputs}:v]setsar=1,{Convert}[v{inputs}];");
                inputs++;
            }
            for (var i = 0; i < inputs; i++)
                graph.Append($"[v{i}]");
            graph.Append($"concat=n={inputs}:v=1:a=0[v]");
            arguments.AddRange(new[]
            {
                "-filter_complex", graph.ToString(), "-map", "[v]", "-an",
                "-c:v", "libx264", "-preset", "slow", "-crf", "20", "-maxrate", MaxRateMegabits + "M", "-bufsize", MaxRateMegabits * 2 + "M",
                "-pix_fmt", "yuv420p", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv",
                "-r", "30", "-movflags", "+faststart", output,
            });
            return arguments;
        }

        /// <summary>Checks a finished video's length, size and black frames and logs the result.</summary>
        private static void Verify(PreviewVideoRecorder.Shape shape, string path)
        {
            var size = new FileInfo(path).Length;
            double seconds = 0;
            if (Run(FfprobePath, new List<string> { "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", path }, out var probe, out _))
                double.TryParse(probe.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
            var problems = new List<string>();
            if (size > MaxBytes)
                problems.Add($"{size / 1048576f:0.0} MB is over the 50 MB limit");
            if (seconds < MinSeconds || seconds > MaxSeconds)
                problems.Add($"{seconds:0.0} s is outside 15 to 20 seconds");
            // Anything dark for a thirtieth of a second: a black frame or a fade from black
            Run(FfmpegPath, new List<string> { "-hide_banner", "-nostats", "-i", path, "-vf", "blackdetect=d=0.03:pix_th=0.04", "-an", "-f", "null", "-" }, out _, out var detect);
            var black = detect.Split('\n').Where(l => l.Contains("black_start")).ToList();
            if (black.Count > 0)
                problems.Add($"black frames found ({black.Count}): {black[0].Trim()}");
            var line = $"SDF Media: {shape.name} video {path}: {seconds:0.0} s, {size / 1048576f:0.0} MB";
            if (problems.Count == 0)
                Debug.Log(line + ", no black frames");
            else
                Debug.LogWarning(line + "; " + string.Join("; ", problems));
        }

        /// <summary>Runs a program to its end, with its output and error text; false for a non-zero exit or one that would not start.</summary>
        private static bool Run(string program, IEnumerable<string> arguments, out string output, out string errors)
        {
            var info = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in arguments)
                info.ArgumentList.Add(argument);
            var outText = new StringBuilder();
            var errText = new StringBuilder();
            try
            {
                using var process = new Process { StartInfo = info };
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (outText) outText.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errText) errText.AppendLine(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                // The second wait lets the readers drain
                process.WaitForExit();
                output = outText.ToString();
                errors = errText.ToString();
                return process.ExitCode == 0;
            }
            catch (Exception exception)
            {
                output = "";
                errors = $"{program}: {exception.Message}";
                return false;
            }
        }
    }
}
