using System.IO;
using UnityEditor;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Runs the in-game performance benchmark (see UI.Benchmark): enters Play mode, which runs it and leaves Play
    /// mode again; results go to Logs/benchmark.log. "run:Backpacking.EditorTools.Benchmark.Run" (an optional
    /// label after a space isn't supported by run:, so the label comes from Temp/backpacking-benchmark-label).
    /// </summary>
    [InitializeOnLoad]
    public static class Benchmark
    {
        const string LabelPath = "Temp/backpacking-benchmark-label";

        // Back in the editor after a run, the project's own run-in-background setting goes back as it was.
        static Benchmark() => EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode || !EditorPrefs.HasKey(UI.Benchmark.RunInBackgroundKey))
                return;
            PlayerSettings.runInBackground = EditorPrefs.GetBool(UI.Benchmark.RunInBackgroundKey);
            EditorPrefs.DeleteKey(UI.Benchmark.RunInBackgroundKey);
        };

        public static string Run()
        {
            string label = File.Exists(LabelPath) ? File.ReadAllText(LabelPath).Trim() : "benchmark";
            File.WriteAllText(UI.Benchmark.RequestPath, label);
            // Lets the benchmark read CPU and GPU frame times separately.
            PlayerSettings.enableFrameTimingStats = true;
            // Keep the game running while the editor isn't focused, from the very first frame; it's put back once
            // Play mode ends. (Only noted once, in case a run starts before the last one's been put back.)
            if (!EditorPrefs.HasKey(UI.Benchmark.RunInBackgroundKey))
                EditorPrefs.SetBool(UI.Benchmark.RunInBackgroundKey, PlayerSettings.runInBackground);
            PlayerSettings.runInBackground = true;
            EditorApplication.EnterPlaymode();
            return $"Entering Play mode for benchmark \"{label}\"; results in {UI.Benchmark.LogPath}.";
        }
    }
}
