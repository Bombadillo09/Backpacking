using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Runs the automatic check of life in the tent (see UI.TentTest): enters Play mode, which pitches a tent,
    /// crawls in, lays out the bed and crawls out, then leaves Play mode; results in
    /// Logs/tenttest.log. With the editor open: "run:Backpacking.EditorTools.TentTestRun.Run". From the
    /// command line (no -quit; the test quits when done): -batchmode -executeMethod Backpacking.EditorTools.TentTestRun.Run.
    /// </summary>
    public static class TentTestRun
    {
        const string ScenePath = "Assets/_Project/Scenes/Prototype.unity";

        public static string Run()
        {
            File.WriteAllText(UI.TentTest.RequestPath, "run");
            if (!EditorPrefs.HasKey(UI.Benchmark.RunInBackgroundKey))
                EditorPrefs.SetBool(UI.Benchmark.RunInBackgroundKey, PlayerSettings.runInBackground);
            PlayerSettings.runInBackground = true;
            if (SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.EnterPlaymode();
            return "Entering Play mode for the tent test; results in Logs/tenttest.log.";
        }
    }
}
