using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Runs the automatic check of a new trip's run-up to the trail (see UI.ArrivalTest): enters Play mode, which
    /// goes from home to the store, packs and drives to the trailhead, then leaves Play mode; results in
    /// Logs/arrivaltest.log. With the editor open: "run:Backpacking.EditorTools.ArrivalTestRun.Run". From the
    /// command line (no -quit; the test quits when done): -batchmode -executeMethod Backpacking.EditorTools.ArrivalTestRun.Run.
    /// </summary>
    public static class ArrivalTestRun
    {
        const string ScenePath = "Assets/_Project/Scenes/Prototype.unity";

        public static string Run()
        {
            File.WriteAllText(UI.ArrivalTest.RequestPath, "run");
            if (!EditorPrefs.HasKey(UI.Benchmark.RunInBackgroundKey))
                EditorPrefs.SetBool(UI.Benchmark.RunInBackgroundKey, PlayerSettings.runInBackground);
            PlayerSettings.runInBackground = true;
            if (SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.EnterPlaymode();
            return "Entering Play mode for the arrival test; results in Logs/arrivaltest.log.";
        }
    }
}
