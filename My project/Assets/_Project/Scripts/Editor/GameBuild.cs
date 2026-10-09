using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// A plain Windows build of the game to play outside the editor (Builds/Backpacking/Backpacking.exe).
    /// With the editor open: "run:Backpacking.EditorTools.GameBuild.BuildWindows".
    /// </summary>
    public static class GameBuild
    {
        public const string BuildPath = "Builds/Backpacking/Backpacking.exe";

        [MenuItem("Backpacking/Build Windows Game")]
        public static string BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Prototype.unity" },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            return summary.result == BuildResult.Succeeded
                ? $"Built {BuildPath} in {summary.totalTime.TotalMinutes:0.0} min ({summary.totalSize / 1e6:0} MB)."
                : $"Build {summary.result}: {summary.totalErrors} errors. See the editor log.";
        }
    }
}
