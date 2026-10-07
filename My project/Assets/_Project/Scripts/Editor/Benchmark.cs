using System.IO;
using UnityEditor;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Runs the in-game performance benchmark (see UI.Benchmark): enters Play mode, which runs it and leaves Play
    /// mode again; results go to Logs/benchmark.log. "run:Backpacking.EditorTools.Benchmark.Run" (an optional
    /// label after a space isn't supported by run:, so the label comes from Temp/backpacking-benchmark-label).
    /// </summary>
    public static class Benchmark
    {
        const string LabelPath = "Temp/backpacking-benchmark-label";

        public static string Run()
        {
            string label = File.Exists(LabelPath) ? File.ReadAllText(LabelPath).Trim() : "benchmark";
            File.WriteAllText(UI.Benchmark.RequestPath, label);
            // Lets the benchmark read CPU and GPU frame times separately.
            PlayerSettings.enableFrameTimingStats = true;
            EditorApplication.EnterPlaymode();
            return $"Entering Play mode for benchmark \"{label}\"; results in {UI.Benchmark.LogPath}.";
        }
    }
}
