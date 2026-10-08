using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Runs the automatic hunting check (see UI.HuntingTest): enters Play mode, which shoots at deer and rabbits,
    /// butchers a deer and leaves Play mode again; results in Logs/huntingtest.log, pictures in Temp/hunt-*.png.
    /// "run:Backpacking.EditorTools.HuntingTestRun.Run".
    /// </summary>
    public static class HuntingTestRun
    {
        const string ScenePath = "Assets/_Project/Scenes/Prototype.unity";

        public static string Run()
        {
            File.WriteAllText(UI.HuntingTest.RequestPath, "run");
            // Keep the game running while the editor isn't focused; the benchmark's hook puts the setting back after.
            if (!EditorPrefs.HasKey(UI.Benchmark.RunInBackgroundKey))
                EditorPrefs.SetBool(UI.Benchmark.RunInBackgroundKey, PlayerSettings.runInBackground);
            PlayerSettings.runInBackground = true;
            if (SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.EnterPlaymode();
            return "Entering Play mode for the hunting test; results in Logs/huntingtest.log.";
        }
    }
}
