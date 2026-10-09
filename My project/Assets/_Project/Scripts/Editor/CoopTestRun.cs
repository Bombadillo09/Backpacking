using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// The co-op check (see UI.CoopTest) needs two games: this editor hosts in Play mode, and a development build
    /// joins as the guest. <see cref="BuildGuest"/> makes the build (Builds/CoopTest/Backpacking.exe); start it with
    /// -coop-test-guest, then <see cref="Run"/> here (or the other way round: the guest keeps trying for a while).
    /// With the editor open: "run:Backpacking.EditorTools.CoopTestRun.BuildGuest" and "run:...CoopTestRun.Run".
    /// </summary>
    public static class CoopTestRun
    {
        const string ScenePath = "Assets/_Project/Scenes/Prototype.unity";
        public const string BuildPath = "Builds/CoopTest/Backpacking.exe";

        public static string Run()
        {
            File.WriteAllText(UI.CoopTest.RequestPath, "run");
            if (!EditorPrefs.HasKey(UI.Benchmark.RunInBackgroundKey))
                EditorPrefs.SetBool(UI.Benchmark.RunInBackgroundKey, PlayerSettings.runInBackground);
            PlayerSettings.runInBackground = true;
            if (SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.EnterPlaymode();
            return "Entering Play mode to host the co-op test; results in Logs/cooptest-host.log.";
        }

        [MenuItem("Backpacking/Co-op/Build Test Copy")]
        public static string BuildGuest()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            return summary.result == BuildResult.Succeeded
                ? $"Built {BuildPath} in {summary.totalTime.TotalMinutes:0.0} min ({summary.totalSize / 1e6:0} MB)."
                : $"Build {summary.result}: {summary.totalErrors} errors. See the editor log.";
        }
    }
}
