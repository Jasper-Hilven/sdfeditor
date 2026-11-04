using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace SdfMushrooms
{
    /// <summary>
    /// Ticket 58's measuring: with <c>perflog</c> in the page's address, a line to the console every <see cref="Every"/> seconds with the
    /// level, quality level, render scale, the average and 95th percentile frame time and the memory the player holds. Off otherwise (no
    /// object, no cost); safe in a release build.
    /// </summary>
    public sealed class PerfLog : MonoBehaviour
    {
        public const float Every = 10;

        private readonly List<float> frames = new List<float>();
        private float time;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url) || url.IndexOf("perflog", System.StringComparison.OrdinalIgnoreCase) < 0)
                return;
            // perfquality=N: that quality level (0 the best) instead of the start's, for comparing levels
            var at = url.IndexOf("perfquality=", System.StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && int.TryParse(url.Substring(at + 12, 1), out var forced) && forced < QualitySettings.names.Length)
                QualitySettings.SetQualityLevel(forced, true);
            var host = new GameObject("Perf Log") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(host);
            host.AddComponent<PerfLog>();
        }

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;
            frames.Add(dt);
            time += dt;
            if (time < Every)
                return;
            frames.Sort();
            var sum = 0f;
            foreach (var f in frames)
                sum += f;
            var average = sum / frames.Count;
            var p95 = frames[Mathf.Min(frames.Count - 1, Mathf.FloorToInt(frames.Count * 0.95f))];
            var names = QualitySettings.names;
            var level = QualitySettings.GetQualityLevel();
            var scale = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset != null
                ? UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset.renderScale : 0;
            Debug.Log($"PerfLog: level {SceneManager.GetActiveScene().name}, quality {names[level]}, scale {scale:0.00}, " +
                      $"frames {frames.Count}, avg {average * 1000:0.0} ms, p95 {p95 * 1000:0.0} ms, " +
                      $"reserved {Profiler.GetTotalReservedMemoryLong() / 1048576} MB, allocated {Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB, " +
                      $"managed {System.GC.GetTotalMemory(false) / 1048576} MB, screen {Screen.width}x{Screen.height}");
            frames.Clear();
            time = 0;
        }
    }
}
