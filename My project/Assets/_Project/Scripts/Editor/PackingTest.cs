using System.IO;
using System.Text;
using Backpacking.Survival;
using Backpacking.Trade;
using Backpacking.Vehicles;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Exercises buying and packing without Play mode, on the built scene's own player pack and truck bed:
    /// empties the kit, buys a starter kit at the store into the truck bed, packs it, rearranges it, and checks
    /// capacity, balance, swaps and the save round trip. Writes Logs/packingtest.log; lines starting FAIL are
    /// problems. Run with -executeMethod Backpacking.EditorTools.PackingTest.RunBatch, or the AutoRebuild
    /// request "run:Backpacking.EditorTools.PackingTest.Run". The scene isn't saved afterwards.
    /// </summary>
    public static class PackingTest
    {
        const string LogPath = "Logs/packingtest.log";

        public static void RunBatch() => Debug.Log(Run());

        public static string Run()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Prototype.unity", OpenSceneMode.Single);
            var log = new StringBuilder($"=== Packing test {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            int failures = 0;
            void Check(bool ok, string what)
            {
                log.AppendLine((ok ? "ok    " : "FAIL  ") + what);
                if (!ok)
                    failures++;
            }

            var player = Object.FindAnyObjectByType<Player.FirstPersonController>();
            var pack = player != null ? player.GetComponent<Backpack>() : null;
            var truck = Object.FindAnyObjectByType<Pickup>();
            Backpack bed = truck != null ? truck.Bed : null;
            if (pack == null || bed == null)
                return Write("FAIL  no player pack or truck bed in the scene; rebuild it first.");
            var store = System.Array.Find(Object.FindObjectsByType<Vendor>(), vendor => vendor.DeliversToTruck);
            Check(store != null, "the outdoor store delivers to the truck");
            Check(bed.IsTruckBed && !pack.IsTruckBed, "the truck bed has no vitals; the player's pack does");

            pack.EmptyKit(Trip.ArrivalGuide.StartingMoney, streetClothes: true);
            bed.EmptyKit(0, streetClothes: false);
            Check(!pack.HasPack && pack.Contents().Count == 0, $"a new trip starts with no pack and nothing to pack ({pack.Contents().Count} things)");
            Check(pack.Money == Trip.ArrivalGuide.StartingMoney, $"and ${pack.Money}");

            int Buy(ShopItemId id)
            {
                ShopItem item = ShopCatalog.Get(id);
                int price = store != null ? store.PriceOf(id) : item.BasePrice;
                string problem = item.Problem(pack, bed);
                if (problem != null || !pack.TrySpendMoney(price))
                {
                    log.AppendLine($"      couldn't buy {item.Name}: {problem ?? "not enough money"}");
                    return 0;
                }
                item.ApplyTo(item.Worn ? pack : bed);
                return price;
            }

            // A cheap but complete kit.
            int spent = 0;
            foreach (ShopItemId id in new[]
                     {
                         ShopItemId.TrekkingPack, ShopItemId.OnePersonTent, ShopItemId.SummerBag, ShopItemId.FoamMat, ShopItemId.Stove,
                         ShopItemId.GasCanister, ShopItemId.GasCanister, ShopItemId.WaterBottle, ShopItemId.Matches, ShopItemId.RainShell,
                         ShopItemId.Fleece, ShopItemId.Bandages, ShopItemId.DehydratedMeal, ShopItemId.DehydratedMeal, ShopItemId.DehydratedMeal,
                         ShopItemId.TrailMix, ShopItemId.TrailMix, ShopItemId.TrailMix, ShopItemId.Machete,
                     })
                spent += Buy(id);
            log.AppendLine($"      spent ${spent}, ${pack.Money} left");
            Check(pack.PackModel == PackModel.Trekking55, "the pack is worn straight out of the shop");
            Check(Buy(ShopItemId.OnePersonTent) == 0, "can't buy a second tent while one is in the truck");
            Check(Buy(ShopItemId.Stove) == 0, "nor a second stove");
            Check(bed.HasGarment("Fleece") && !bed.Clothing[0].worn, "clothes bought go into the truck bed, not worn");
            Check(bed.OwnsTent && bed.HasTent && bed.OwnsStove && bed.WaterCapacity >= 2f, "tent, stove and bottle are in the truck bed");
            log.AppendLine($"      truck bed: {Describe(bed)}");

            // Pack everything, each where it usually goes.
            int moved = 0;
            for (int guard = 0; guard < 200; guard++)
            {
                PackItem next = bed.Contents().Find(item => pack.BestZoneFor(item) != null);
                if (next == null)
                    break;
                PackZone zone = pack.BestZoneFor(next).Value;
                string problem = bed.MoveOne(next.Key, pack);
                Check(problem == null, $"moved one {next.Name} to the {zone}{(problem != null ? $": {problem}" : "")}");
                pack.SetZone(next.Key, zone);
                moved++;
            }
            log.AppendLine($"      packed {moved} things; left in the truck: {Describe(bed)}");
            log.AppendLine($"      pack: {Describe(pack)}");
            Check(bed.Contents().Count == 0, "everything fitted in the trekking pack");
            Check(pack.Overfull <= 0f, $"nothing's over-full ({pack.Overfull:0.0} L)");
            Check(pack.ZoneOf(pack.Contents().Find(item => item.Key == "mat")) == PackZone.Straps, "the foam mat is strapped outside");
            Check(pack.ZoneOf(pack.Contents().Find(item => item.Key == "gas")) == PackZone.Core, "gas canisters go in the core");
            Check(!pack.HasMachete || pack.OnHotbar(new HotbarSlot(HotbarKind.Machete)), "the machete (if bought) is on the hotbar once packed");
            Check(pack.OnHotbar(new HotbarSlot(HotbarKind.Water)), "and so is the water bottle");
            float good = pack.Balance;
            log.AppendLine($"      balance as packed: {good * 100f:0}% ({Backpack.BalanceWord(good)}), weight {pack.TotalWeight:0.0} kg");

            // Pack badly: everything heavy in the lid and on the straps.
            foreach (PackItem item in pack.Contents())
                if (item.Heavy || item.Key == "tent")
                    pack.SetZone(item.Key, item.Strappable ? PackZone.Straps : PackZone.Lid);
            float bad = pack.Balance;
            log.AppendLine($"      balance with the heavy things up top and outside: {bad * 100f:0}% ({Backpack.BalanceWord(bad)})");
            Check(bad < good - 0.05f, "packing heavy things badly lowers the balance");

            // Capacity: a big tent and the winter bag in the ultralight pack.
            pack.SetPack(PackModel.Ultralight40);
            log.AppendLine($"      in the ultralight pack: over-full by {pack.Overfull:0.0} L, lid capacity {pack.CapacityOf(PackZone.Lid):0}");
            Check(pack.ZoneOf(pack.Contents().Find(item => item.Key == "matches")) != PackZone.Lid, "no lid on the ultralight pack: the matches go in the top");
            pack.SetPack(PackModel.Trekking55);

            // Swaps: a better bag from the truck replaces the one in the pack, which goes to the truck.
            pack.AddMoney(200);
            Buy(ShopItemId.ThreeSeasonBag);
            Check(bed.HasSleepingBag && bed.SleepingBagComfort <= -1f, "a better bag can still be bought (into the truck)");
            string swap = bed.MoveOne("sleepingbag", pack);
            Check(swap == null && pack.SleepingBagComfort <= -1f && bed.SleepingBagComfort >= 5f, "moving it into the pack swaps it with the summer bag");

            // Clothes: wear the fleece straight from the truck.
            pack.MoveOne("garment-Fleece", bed);
            Check(bed.Wear("Fleece", pack) == null && System.Linq.Enumerable.Any(pack.Clothing, garment => garment.name == "Fleece" && garment.worn), "the fleece can be worn straight from the truck");

            // Food keeps its freshness when moved.
            bed.AddFood(FoodKind.RawFish);
            Check(bed.MoveOne("food-RawFish", pack) == null && pack.CountFood(FoodKind.RawFish) == 1, "food moves between the truck and the pack");

            // Saves: the pack's model, zones and contents survive a round trip.
            BackpackState saved = pack.CaptureState();
            string json = JsonUtility.ToJson(saved);
            var loaded = JsonUtility.FromJson<BackpackState>(json);
            Backpack copy = new GameObject("Copy").AddComponent<Backpack>();
            copy.RestoreState(loaded);
            Check(copy.PackModel == pack.PackModel && Describe(copy, zones: true) == Describe(pack, zones: true), "a saved pack loads the same");
            Object.DestroyImmediate(copy.gameObject);
            var old = JsonUtility.FromJson<BackpackState>("{\"money\":10,\"hasStove\":true,\"tentName\":\"Old tent\",\"hasTent\":true}");
            Check(old.packModel == (int)PackModel.Trekking55 && old.ownsStove, "an old save without packs carries the trekking pack and owns its stove");

            log.AppendLine(failures == 0 ? "PASSED" : $"{failures} FAILED");
            return Write(log.ToString());
        }

        static string Describe(Backpack container, bool zones = false)
        {
            var text = new StringBuilder();
            foreach (PackItem item in container.Contents())
                text.Append($"{item.Name}×{item.Count}@{(container.IsTruckBed && !zones ? "-" : container.ZoneOf(item).ToString())} ");
            return text.Length > 0 ? text.ToString() : "(empty)";
        }

        static string Write(string text)
        {
            File.AppendAllText(LogPath, text + "\n");
            return text;
        }
    }
}
