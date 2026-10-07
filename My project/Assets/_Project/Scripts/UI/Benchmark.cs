#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Backpacking.Character;
using Backpacking.Navigation;
using Backpacking.Player;
using Unity.Profiling;
using UnityEngine;

namespace Backpacking.UI
{
    /// <summary>
    /// An automatic performance run, editor only. When Temp/backpacking-benchmark-request exists as Play mode
    /// starts, it skips the title, starts a trip, stands at a few spots along the route turning slowly through a
    /// full circle at each, records frame time, CPU main thread, draw calls, triangles and garbage per frame,
    /// writes them to Logs/benchmark.log and leaves Play mode. Started by the editor's Benchmark.Run.
    /// </summary>
    public class Benchmark : MonoBehaviour
    {
        public const string RequestPath = "Temp/backpacking-benchmark-request";
        public const string LogPath = "Logs/benchmark.log";
        /// <summary>Where the editor keeps the project's own run-in-background setting during a run.</summary>
        public const string RunInBackgroundKey = "Backpacking.Benchmark.RunInBackground";

        /// <summary>Puts the project's run-in-background setting back and leaves Play mode.</summary>
        static void Finish()
        {
            UnityEditor.PlayerSettings.runInBackground = UnityEditor.EditorPrefs.GetBool(RunInBackgroundKey, false);
            UnityEditor.EditorApplication.ExitPlaymode();
        }
        const float WarmupSeconds = 2.5f, TurnSeconds = 8f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void MaybeStart()
        {
            if (File.Exists(RequestPath))
                new GameObject("Benchmark").AddComponent<Benchmark>();
        }

        IEnumerator Start()
        {
            string label = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath);
            yield return null;

            // Keep running when the editor isn't the focused window.
            Application.runInBackground = true;
            // Experiments named in the label switch things off, to see what they cost.
            Terrain terrain = Terrain.activeTerrain;
            foreach (string token in label.Split(' '))
            {
                if (token.StartsWith("tree-") && float.TryParse(token.Substring(5), out float distance))
                    terrain.treeDistance = distance;
                if (token.StartsWith("lodbias-") && float.TryParse(token.Substring(8), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float bias))
                    QualitySettings.lodBias = bias;
            }
            if (label.Contains("no-trees"))
                terrain.treeDistance = 0f;
            if (label.Contains("no-plants"))
                terrain.detailObjectDistance = 0f;
            if (label.Contains("no-shadows"))
                foreach (Light light in FindObjectsByType<Light>())
                    light.shadows = LightShadows.None;
            if (label.Contains("no-objects"))
                foreach (MeshRenderer renderer in FindObjectsByType<MeshRenderer>())
                    if (renderer.GetComponentInParent<Terrain>() == null && renderer.GetComponentInParent<FirstPersonController>() == null)
                        renderer.enabled = false;

            var menus = FindAnyObjectByType<GameMenus>();
            menus.BeginBenchmarkTrip();
            yield return null;

            // "shot backpack": just open the backpack screen and capture the game view.
            if (label.StartsWith("shot"))
            {
                for (int i = 0; i < 30; i++)
                    yield return null;
                if (label.Contains("backpack"))
                    FindAnyObjectByType<BackpackView>().Show();
                for (int i = 0; i < 10; i++)
                    yield return null;
                ScreenCapture.CaptureScreenshot($"Temp/{label.Replace(' ', '-')}.png");
                for (int i = 0; i < 5; i++)
                    yield return null;
                Finish();
                yield break;
            }

            var player = FindAnyObjectByType<FirstPersonController>();
            var controller = player.GetComponent<CharacterController>();
            var spots = new List<(string name, Vector3 position)> { ("Trailhead (start)", player.transform.position) };
            List<NavigationPoint> route = NavigationPoint.All.OrderBy(point => Vector3.Distance(point.transform.position, player.transform.position)).ToList();
            if (route.Count > 2)
                spots.Add(($"Woods near {route[route.Count / 2].DisplayName}", route[route.Count / 2].transform.position + new Vector3(25f, 0f, 15f)));
            if (route.Count > 0)
                spots.Add(($"{route[^1].DisplayName} (far end)", route[^1].transform.position + new Vector3(-10f, 0f, 10f)));

            var report = new StringBuilder($"=== {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} {label} ({Screen.width}x{Screen.height})\n");
            foreach ((string name, Vector3 position) in spots)
            {
                // Stand on the ground there.
                Vector3 at = position;
                if (Physics.Raycast(at + Vector3.up * 400f, Vector3.down, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
                    at = hit.point + Vector3.up * 0.1f;
                controller.enabled = false;
                player.transform.position = at;
                controller.enabled = true;

                for (float t = 0f; t < WarmupSeconds; t += Time.unscaledDeltaTime)
                    yield return null;

                using var mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
                using var renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Render Thread", 1);
                using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                using var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                using var triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                using var shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
                using var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                var frames = new List<float>();
                double main = 0, render = 0, batch = 0, pass = 0, tris = 0, shadows = 0, garbage = 0, cpu = 0, gpu = 0;
                int timed = 0;
                var timings = new FrameTiming[1];
                float yaw = player.transform.eulerAngles.y;
                for (float t = 0f; t < TurnSeconds; t += Time.unscaledDeltaTime)
                {
                    player.transform.rotation = Quaternion.Euler(0f, yaw + 360f * t / TurnSeconds, 0f);
                    yield return null;
                    frames.Add(Time.unscaledDeltaTime * 1000f);
                    main += mainThread.LastValue / 1e6;
                    render += renderThread.LastValue / 1e6;
                    batch += batches.LastValue;
                    pass += setPass.LastValue;
                    tris += triangles.LastValue;
                    shadows += shadowCasters.LastValue;
                    garbage += gc.LastValue;
                    // CPU and GPU time apart (the main-thread figure includes waiting for the GPU).
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0)
                    {
                        cpu += timings[0].cpuMainThreadFrameTime;
                        gpu += timings[0].gpuFrameTime;
                        timed++;
                    }
                }
                int n = Mathf.Max(1, frames.Count);
                frames.Sort();
                report.AppendLine($"{name}: {frames.Average():0.0} ms/frame ({1000f / frames.Average():0} fps), worst 1% {frames[(int)(frames.Count * 0.99f)]:0.0} ms | " +
                                  $"main thread {main / n:0.0} ms, render thread {render / n:0.0} ms | " +
                                  (timed > 0 ? $"CPU {cpu / timed:0.0} ms, GPU {gpu / timed:0.0} ms | " : "no GPU timing | ") + $"batches {batch / n:0}, set-pass {pass / n:0}, " +
                                  $"triangles {tris / n / 1e6:0.00} M, shadow casters {shadows / n:0} | garbage {garbage / n / 1024:0.0} KB/frame");
            }
            File.AppendAllText(LogPath, report.ToString());
            Debug.Log(report.ToString());
            Finish();
        }
    }
}
#endif
