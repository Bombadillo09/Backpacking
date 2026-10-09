#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Backpacking.Camp;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using UnityEngine;

namespace Backpacking.UI
{
    /// <summary>
    /// An automatic check of life inside the tent, editor only. When Temp/backpacking-tenttest-request exists as Play
    /// mode starts, it starts a trip with the full kit, pitches a tent at the trailhead, crawls in, lays out the mat and
    /// sleeping bag, takes the boots off, checks the pack and the sleep, and crawls out again. Pictures of the view
    /// inside go to Logs/SceneSnapshots/tent-*.png, results to Logs/tenttest.log (FAIL lines are problems); then it
    /// leaves Play mode (and quits a batch-mode editor). Started by the editor's TentTestRun.
    /// </summary>
    public class TentTest : MonoBehaviour
    {
        public const string RequestPath = "Temp/backpacking-tenttest-request";
        const string LogPath = "Logs/tenttest.log";

        readonly StringBuilder log = new();
        int failures, errors;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void MaybeStart()
        {
            if (File.Exists(RequestPath))
                new GameObject("Tent Test").AddComponent<TentTest>();
        }

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string message, string stack, LogType type)
        {
            if (type is LogType.Exception or LogType.Error && !message.StartsWith("[TentTest]"))
            {
                errors++;
                log.AppendLine($"ERROR {message}\n{stack}");
            }
        }

        void Check(bool ok, string what)
        {
            log.AppendLine((ok ? "ok    " : "FAIL  ") + what);
            if (!ok)
                failures++;
        }

        IEnumerator Start()
        {
            File.Delete(RequestPath);
            Application.runInBackground = true;
            log.AppendLine($"=== Tent test {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            yield return null;
            IEnumerator steps = Steps();
            while (true)
            {
                object current;
                try
                {
                    if (!steps.MoveNext())
                        break;
                    current = steps.Current;
                }
                catch (System.Exception exception)
                {
                    Check(false, $"the test itself threw: {exception}");
                    break;
                }
                yield return current;
            }
            Finish();
        }

        IEnumerator Steps()
        {
            FindAnyObjectByType<GameMenus>().BeginBenchmarkTrip();
            yield return new WaitForSeconds(0.5f);
            var player = FindAnyObjectByType<FirstPersonController>();
            var backpack = player.GetComponent<Backpack>();
            var interactor = player.GetComponent<Interactor>();
            var placer = player.GetComponent<CampPlacer>();
            var activity = player.GetComponent<PlayerActivity>();
            // The standard kit has no mat; bring one.
            if (!backpack.HasMat)
                Trade.ShopCatalog.Get(Trade.ShopItemId.FoamMat).ApplyTo(backpack);

            // Stand in the open at the trailhead parking, and pitch a tent in front of you.
            Transform parking = GameObject.Find("Road/Trailhead Parking").transform;
            Teleport(player, parking.position + new Vector3(-6f, 0f, 0f));
            player.transform.rotation = Quaternion.LookRotation(Vector3.back);
            yield return new WaitForSeconds(0.5f);
            Vector3 spot = player.transform.position + Vector3.back * 3f;
            if (Physics.Raycast(spot + Vector3.up * 5f, Vector3.down, out RaycastHit ground, 10f))
                spot = ground.point;
            GameObject instance = placer.Spawn(CampItem.Tent, spot, Quaternion.LookRotation(Vector3.forward));
            var tent = instance.GetComponent<Tent>();
            tent.Setup(backpack.TentModel, TentStage.Pitched);
            backpack.HasTent = false;
            yield return new WaitForSeconds(0.5f);
            Check(tent.IsPitched, $"a {tent.DisplayName} is pitched");

            var options = new List<InteractionOption>();
            tent.GetOptions(interactor, options);
            Check(options.Any(option => option.Label == "Crawl inside"), $"outside it offers: {string.Join(" / ", options.Select(option => option.Label))}");
            float weightBefore = backpack.TotalWeight;

            tent.CrawlIn(interactor);
            yield return new WaitForSeconds(1f);
            Check(player.Mounted && player.MountedOnGround && tent.PlayerInside, "crawled in: sitting on the tent floor");
            Check(!backpack.IsWorn, "the pack was left in the porch");
            Check(PackHandling.Current.CanReachPack, "and it's within reach from inside");
            Check(!PlayerControlLock.MovementLocked, "you can still look around and interact inside");
            Capture("tent-inside-empty");

            options.Clear();
            tent.GetInsideOptions(interactor, options);
            log.AppendLine($"      inside: {string.Join(" / ", options.Select(option => option.Enabled ? option.Label : $"{option.Label} [{option.DisabledReason}]"))}");
            InteractionOption mat = options.FirstOrDefault(option => option.Label.StartsWith("Lay out your"));
            Check(mat.Label != null && mat.Enabled, "can lay out the mat");
            mat.Execute?.Invoke();
            yield return new WaitUntil(() => !activity.IsBusy);
            options.Clear();
            tent.GetInsideOptions(interactor, options);
            InteractionOption bag = options.FirstOrDefault(option => option.Label.StartsWith("Unroll your sleeping bag"));
            Check(bag.Label != null && bag.Enabled, "can unroll the sleeping bag");
            bag.Execute?.Invoke();
            yield return new WaitUntil(() => !activity.IsBusy);
            yield return new WaitForSeconds(0.3f);
            Check(tent.MatLaidOut && tent.BagLaidOut && backpack.MatLaidOut && backpack.BagLaidOut, "mat and bag are laid out");
            Check(tent.transform.Find("Bed") != null, "and you can see them on the floor");
            Check(backpack.TotalWeight < weightBefore - 0.5f, $"they're out of the pack ({weightBefore:0.0} -> {backpack.TotalWeight:0.0} kg)");
            Check(!backpack.Contents().Any(item => item.Key is "mat" or "sleepingbag"), "and not listed as packed");
            Capture("tent-inside-bed");
            // Look round at the door.
            player.transform.localRotation = Quaternion.identity;
            yield return null;

            // Boots off inside.
            options.Clear();
            tent.GetInsideOptions(interactor, options);
            options.First(option => option.Label == "Take your boots off").Execute();
            yield return new WaitForSeconds(0.5f);
            Check(RestMode.Current.BootsOff && FindAnyObjectByType<Vitals>() != null, "boots off in the tent");

            options.Clear();
            tent.GetOptions(interactor, options);
            Check(options.Count == 0, "the tent's outside options don't show from inside");

            // Sleep in the bag on the mat, then wake straight away.
            var vitals = player.GetComponent<Vitals>();
            options.Clear();
            tent.GetInsideOptions(interactor, options);
            options.First(option => option.Label == "Get into your sleeping bag and sleep").Execute();
            yield return new WaitForSeconds(0.5f);
            Check(activity.IsSleeping && vitals.InSleepingBag && vitals.OnMat && vitals.IsSheltered, "asleep in the bag, on the mat, sheltered");
            activity.Interrupt();
            yield return new WaitForSeconds(0.5f);

            // Taking the tent down is blocked while the bed's laid out; pack it away and crawl out.
            options.Clear();
            tent.CrawlOut(player);
            yield return new WaitForSeconds(0.5f);
            Check(!player.Mounted && !RestMode.Current.BootsOff, "crawled out, boots back on");
            Check(!tent.GetComponent<BoxCollider>().bounds.Contains(player.transform.position + Vector3.up * 0.5f), "standing outside the tent");
            tent.GetOptions(interactor, options);
            InteractionOption takeDown = options.FirstOrDefault(option => option.Label.StartsWith("Take down"));
            Check(takeDown.Label != null && !takeDown.Enabled, $"can't take it down with the bed laid out ({takeDown.DisabledReason})");

            tent.CrawlIn(interactor);
            yield return new WaitForSeconds(0.5f);
            foreach (string label in new[] { "Stuff the sleeping bag back in the pack", "Roll up the mat and pack it" })
            {
                options.Clear();
                tent.GetInsideOptions(interactor, options);
                options.First(option => option.Label == label).Execute();
                yield return new WaitUntil(() => !activity.IsBusy);
            }
            Check(!tent.MatLaidOut && !backpack.BagLaidOut && Mathf.Abs(backpack.TotalWeight - weightBefore) < 0.05f, "packed the bed away again");
            tent.CrawlOut(player);
            yield return new WaitForSeconds(0.3f);
            Check(!player.Mounted, "out again");
        }

        static void Teleport(FirstPersonController player, Vector3 position)
        {
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            if (Physics.Raycast(position + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                position = hit.point + Vector3.up * 0.1f;
            player.transform.position = position;
            controller.enabled = true;
        }

        /// <summary>The player's view, rendered off screen (works in batch mode).</summary>
        public static void Capture(string name)
        {
            Camera view = Camera.main;
            if (view == null)
                return;
            Directory.CreateDirectory("Logs/SceneSnapshots");
            var target = new RenderTexture(1280, 720, 24);
            RenderTexture previous = view.targetTexture;
            view.targetTexture = target;
            view.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            view.targetTexture = previous;
            File.WriteAllBytes($"Logs/SceneSnapshots/{name}.png", image.EncodeToPNG());
            Destroy(image);
            Destroy(target);
        }

        void Finish()
        {
            Check(errors == 0, $"no errors or exceptions in the log ({errors})");
            log.AppendLine(failures == 0 ? "PASSED" : $"{failures} FAILED");
            File.AppendAllText(LogPath, log + "\n");
            Debug.Log($"[TentTest] {(failures == 0 ? "PASSED" : $"{failures} FAILED")}; see {LogPath}");
            UnityEditor.EditorApplication.isPlaying = false;
            if (Application.isBatchMode)
                UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}
#endif
