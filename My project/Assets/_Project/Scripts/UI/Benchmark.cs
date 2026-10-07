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

        /// <summary>
        /// Logs how many of each animal are about, and renders the one nearest the player of each kind (with a
        /// camera of its own, a couple of metres off) to Temp/shot-wildlife-*.png.
        /// </summary>
        static void ShootWildlife()
        {
            Transform player = FindAnyObjectByType<FirstPersonController>().transform;
            var report = new StringBuilder("Wildlife:");
            void Shoot<T>(string kind, float distance) where T : Component
            {
                T[] all = FindObjectsByType<T>(FindObjectsSortMode.None);
                report.Append($" {kind} {all.Length},");
                T nearest = all.OrderBy(a => (a.transform.position - player.position).sqrMagnitude).FirstOrDefault();
                if (nearest == null)
                    return;
                Vector3 target = nearest.transform.position + Vector3.up * distance * 0.12f;
                var go = new GameObject("Wildlife Camera");
                var camera = go.AddComponent<Camera>();
                camera.nearClipPlane = 0.02f;
                camera.fieldOfView = 35f;
                Vector3 side = nearest.transform.right;
                go.transform.position = target + (side * 0.8f + nearest.transform.forward * 0.6f + Vector3.up * 0.35f).normalized * distance;
                go.transform.LookAt(target);
                var texture = new RenderTexture(900, 600, 24);
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                var image = new Texture2D(900, 600, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 900, 600), 0, 0);
                RenderTexture.active = null;
                File.WriteAllBytes($"Temp/shot-wildlife-{kind}.png", image.EncodeToPNG());
                camera.targetTexture = null;
                Destroy(texture);
                Destroy(image);
                Destroy(go);
                report.Append($" (nearest {Vector3.Distance(nearest.transform.position, player.position):0} m)");
            }
            Shoot<Wildlife.SmallAnimalRig>("rabbit", 1.4f);
            Shoot<Wildlife.Squirrel>("squirrel", 1.4f);
            Shoot<Wildlife.Songbird>("bird", 1.2f);
            Shoot<Wildlife.Butterfly>("butterfly", 0.5f);
            Shoot<Wildlife.BirdFlock>("flock", 6f);
            Debug.Log(report.ToString());
        }

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
                if (label.Contains("storm") || label.Contains("fog") || label.Contains("snow"))
                {
                    var weather = FindAnyObjectByType<World.WeatherSystem>();
                    if (label.Contains("storm"))
                        weather.Force(World.WeatherKind.Storm);
                    else if (label.Contains("fog"))
                        weather.Force(World.WeatherKind.Fog);
                    else
                        weather.Force(World.WeatherKind.Rain, coldSnap: true, snowMetres: 700f);
                    // Let the light, fog and snow settle (the snow repaints over many frames).
                    float settle = Time.realtimeSinceStartup + 8f;
                    while (Time.realtimeSinceStartup < settle)
                        yield return null;
                    if (label.Contains("storm") && !label.Contains("wind"))
                    {
                        FindAnyObjectByType<World.LightningStorm>().StrikeNow(600f);
                        // Into the brightest blink of the flicker.
                        float flash = Time.realtimeSinceStartup + 0.13f;
                        while (Time.realtimeSinceStartup < flash)
                            yield return null;
                    }
                    ScreenCapture.CaptureScreenshot($"Temp/{label.Replace(' ', '-')}.png");
                    if (label.Contains("wind"))
                    {
                        // A second frame a moment later: the trees should have moved between the two.
                        float later = Time.realtimeSinceStartup + 0.6f;
                        while (Time.realtimeSinceStartup < later)
                            yield return null;
                        ScreenCapture.CaptureScreenshot($"Temp/{label.Replace(' ', '-')}-later.png");
                    }
                    for (int i = 0; i < 5; i++)
                        yield return null;
                    Finish();
                    yield break;
                }
                if (label.Contains("wildlife"))
                {
                    // Let them get on with things for a while, then photograph the nearest of each kind.
                    float until = Time.realtimeSinceStartup + 6f;
                    while (Time.realtimeSinceStartup < until)
                        yield return null;
                    ShootWildlife();
                    Finish();
                    yield break;
                }
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
