#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Backpacking.Character;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Trade;
using Backpacking.Trip;
using Backpacking.Vehicles;
using UnityEngine;

namespace Backpacking.UI
{
    /// <summary>
    /// An automatic check of a new trip's run-up to the trail, editor only. When Temp/backpacking-arrivaltest-request
    /// exists as Play mode starts, it starts a fresh trip (at home, nothing but money), gets in the truck, drives to
    /// the store, buys a kit through the shop screen's own delivery, packs it through the packing screen, drives to
    /// the trailhead and steps out, checking the arrival guide's stages, the journal and that nothing threw.
    /// Results go to Logs/arrivaltest.log; lines starting FAIL are problems. Then it leaves Play mode (and quits a
    /// batch-mode editor). Started by the editor's ArrivalTestRun.
    /// </summary>
    public class ArrivalTest : MonoBehaviour
    {
        public const string RequestPath = "Temp/backpacking-arrivaltest-request";
        const string LogPath = "Logs/arrivaltest.log";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        readonly StringBuilder log = new();
        int failures, errors;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void MaybeStart()
        {
            if (File.Exists(RequestPath))
                new GameObject("Arrival Test").AddComponent<ArrivalTest>();
        }

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string message, string stack, LogType type)
        {
            if (type is LogType.Exception or LogType.Error && !message.StartsWith("[ArrivalTest]"))
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
            log.AppendLine($"=== Arrival test {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
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
            var menus = FindAnyObjectByType<GameMenus>();
            var player = FindAnyObjectByType<FirstPersonController>();
            var backpack = player.GetComponent<Backpack>();
            var interactor = player.GetComponent<Interactor>();
            Pickup truck = Pickup.Current;
            var guide = FindAnyObjectByType<ArrivalGuide>();
            Vendor store = FindObjectsByType<Vendor>().FirstOrDefault(vendor => vendor.DeliversToTruck);
            var road = FindAnyObjectByType<World.RoadPath>();
            Check(truck != null && truck.Bed != null && guide != null && store != null && road != null, "the truck, its bed, the guide, the store and the road exist");
            if (truck == null || guide == null || store == null)
                yield break;

            // A fresh trip, as the character creator starts one.
            var profile = new CharacterProfile { name = "Tester", background = Background.Ranger };
            typeof(GameMenus).GetMethod("BeginTrip", Private, null, new[] { typeof(CharacterProfile) }, null).Invoke(menus, new object[] { profile });
            yield return new WaitForSeconds(0.5f);
            Check(!backpack.HasPack && backpack.Money == ArrivalGuide.StartingMoney, $"at home with no pack and ${backpack.Money}");
            Check(guide.Phase == ArrivalPhase.AtHome, $"the guide says: at home ({guide.Phase})");
            Check(truck.Bed.HasMachete, "the ranger's machete from home is in the truck bed");
            Check(Vector3.Distance(player.transform.position, truck.transform.position) < 20f, "the truck is parked just outside");

            // Get in and drive (teleported) to the store's lot, then get out.
            truck.GetIn();
            yield return new WaitForSeconds(0.5f);
            Check(player.Mounted && PlayerControlLock.MovementLocked, "in the driver's seat, walking locked");
            Vector3 storeLot = store.transform.position + (road.NearestOnRoad(store.transform.position) - store.transform.position) * 0.9f;
            Quaternion along = truck.transform.rotation;
            truck.RestoreState(new PickupState { position = storeLot + Vector3.up * 1f, rotation = along, driving = true });
            yield return new WaitForSeconds(2f);
            Check(player.Mounted, "still driving after moving the truck");
            Check(Vector3.Distance(player.transform.position, truck.transform.position) < 3f, "riding along in the cab");
            typeof(Pickup).GetMethod("TryGetOut", Private).Invoke(truck, null);
            yield return new WaitForSeconds(1f);
            Check(!player.Mounted && !PlayerControlLock.MovementLocked, "got out of the truck");
            // Walk up to the store's door.
            Teleport(player, store.transform.position + store.transform.forward * 3f);
            yield return new WaitForSeconds(1f);
            Check(guide.Phase == ArrivalPhase.Shopping, $"the guide says: shopping ({guide.Phase})");

            // The shop screen, and its own delivery to the truck bed.
            interactor.Shop.Open(store);
            yield return new WaitForSeconds(0.5f);
            Check(interactor.Shop.IsOpen, "the shop screen opens");
            var delivery = (Backpack)typeof(ShopView).GetProperty("Delivery", Private).GetValue(interactor.Shop);
            Check(delivery == truck.Bed, "purchases are delivered to the truck bed");
            int spent = 0;
            foreach (ShopItemId id in new[]
                     {
                         ShopItemId.TrekkingPack, ShopItemId.OnePersonTent, ShopItemId.SummerBag, ShopItemId.FoamMat, ShopItemId.Stove,
                         ShopItemId.GasCanister, ShopItemId.WaterBottle, ShopItemId.Matches, ShopItemId.RainShell, ShopItemId.DehydratedMeal,
                         ShopItemId.DehydratedMeal, ShopItemId.TrailMix, ShopItemId.TrailMix, ShopItemId.HikingBoots,
                     })
            {
                ShopItem item = ShopCatalog.Get(id);
                int price = store.PriceOf(id);
                string problem = item.Problem(backpack, delivery);
                if (problem != null || !backpack.TrySpendMoney(price))
                {
                    Check(false, $"couldn't buy {item.Name}: {problem ?? "not enough money"}");
                    continue;
                }
                item.ApplyTo(item.Worn ? backpack : delivery);
                spent += price;
            }
            log.AppendLine($"      spent ${spent}, ${backpack.Money} left");
            Check(backpack.HasPack && backpack.BootsName == "Trail hiking boots", "wearing the new pack and boots");
            interactor.Shop.Close();
            yield return new WaitForSeconds(0.3f);

            // Out to the truck: the guide moves on to packing.
            Teleport(player, truck.transform.position + truck.transform.right * -2f - truck.transform.forward * 3f);
            yield return new WaitForSeconds(1f);
            Check(guide.Phase == ArrivalPhase.Packing, $"the guide says: pack ({guide.Phase})");
            var options = new System.Collections.Generic.List<InteractionOption>();
            truck.GetOptions(interactor, options);
            InteractionOption packOption = options.FirstOrDefault(option => option.Label.StartsWith("Pack your backpack"));
            Check(packOption.Label != null && packOption.Enabled, $"the truck offers: {string.Join(" / ", options.Select(option => option.Label))}");
            packOption.Execute?.Invoke();
            yield return new WaitForSeconds(0.5f);
            Check(PackingView.Current != null && PackingView.Current.IsOpen, "the packing screen opens");
            MethodInfo packFromTruck = typeof(PackingView).GetMethod("PackFromTruck", Private);
            foreach (PackItem item in truck.Bed.Contents().ToList())
            {
                packFromTruck.Invoke(PackingView.Current, new object[] { item.Key, true });
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            log.AppendLine($"      packed: {backpack.TotalWeight:0.0} kg, {Backpack.BalanceWord(backpack.Balance)} ({backpack.Balance * 100f:0}%), "
                           + $"{truck.Bed.Contents().Count} kinds left in the truck");
            Check(truck.Bed.Contents().Count == 0, "everything packed from the truck bed");
            Check(backpack.OwnsTent && backpack.HasSleepingBag && backpack.OwnsStove && backpack.WaterCapacity > 0f && backpack.HasMachete, "tent, bag, stove, bottle and machete are in the pack");
            PackingView.Current.Close();
            yield return new WaitForSeconds(0.3f);

            // Drive on to the trailhead.
            truck.GetIn();
            yield return new WaitForSeconds(1f);
            Check(guide.Phase == ArrivalPhase.Driving, $"the guide says: drive to the trailhead ({guide.Phase})");
            Vector3 parking = road.Points[road.Points.Count - 1];
            truck.RestoreState(new PickupState { position = parking + Vector3.up * 1f, rotation = Quaternion.LookRotation(Vector3.forward), driving = true });
            yield return new WaitForSeconds(2f);
            typeof(Pickup).GetMethod("TryGetOut", Private).Invoke(truck, null);
            yield return new WaitForSeconds(1.5f);
            Check(!player.Mounted, "out of the truck at the trailhead");
            Check(guide.Phase == ArrivalPhase.OnTheTrail, $"the guide says: on the trail ({guide.Phase})");
            var tutorial = FindAnyObjectByType<Tutorial>();
            Check(tutorial != null && tutorial.IsRunning, "the tutorial starts at the trailhead");
            if (TripLog.Current != null)
                log.AppendLine("      journal:\n        " + string.Join("\n        ", TripLog.Current.CaptureState().entries.Select(entry => entry.text)));
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

        void Finish()
        {
            Check(errors == 0, $"no errors or exceptions in the log ({errors})");
            log.AppendLine(failures == 0 ? "PASSED" : $"{failures} FAILED");
            File.AppendAllText(LogPath, log + "\n");
            Debug.Log($"[ArrivalTest] {(failures == 0 ? "PASSED" : $"{failures} FAILED")}; see {LogPath}");
            UnityEditor.EditorApplication.isPlaying = false;
            if (Application.isBatchMode)
                UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}
#endif
