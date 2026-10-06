using System;
using System.Collections.Generic;
using Backpacking.Survival;

namespace Backpacking.Trade
{
    public enum ShopItemId
    {
        GasCanister,
        Matches,
        TrailMix,
        DehydratedMeal,
        Snare,
        WaterBladder,
        WaterFilter,
        FishingRod,
        WoolHatAndGloves,
        InsulatedPants,
        WinterSleepingBag,
        FourSeasonTent,
    }

    /// <summary>Something a trading post can sell: what it costs and what it does to the backpack.</summary>
    public sealed class ShopItem
    {
        public readonly string Name;
        public readonly string Description;
        public readonly int BasePrice;
        /// <summary>True for one-off gear upgrades rather than supplies.</summary>
        public readonly bool IsGear;
        readonly Func<Backpack, string> problem;
        readonly Action<Backpack> apply;

        public ShopItem(string name, string description, int basePrice, bool isGear, Action<Backpack> apply,
            Func<Backpack, string> problem = null)
        {
            Name = name;
            Description = description;
            BasePrice = basePrice;
            IsGear = isGear;
            this.apply = apply;
            this.problem = problem;
        }

        /// <summary>Why this can't be bought (already owned, etc.), ignoring price. Null if it can.</summary>
        public string Problem(Backpack backpack) => problem?.Invoke(backpack);

        public void ApplyTo(Backpack backpack) => apply(backpack);
    }

    /// <summary>
    /// Every item trading posts can stock. Kept in code for the prototype; these become data assets
    /// along with food when the full item system arrives.
    /// </summary>
    public static class ShopCatalog
    {
        const string HatName = "Wool hat & gloves";
        const string PantsName = "Insulated pants";
        const string WinterBagName = "Winter down bag";
        const string FourSeasonTentName = "4-season mountain tent";

        static readonly Dictionary<ShopItemId, ShopItem> items = new()
        {
            [ShopItemId.GasCanister] = new ShopItem("Gas canister", "230 g of stove fuel.", 12, false,
                backpack => backpack.AddGas(230f)),
            [ShopItemId.Matches] = new ShopItem("Waterproof matches", "A box of 12.", 3, false,
                backpack => backpack.AddMatches(12)),
            [ShopItemId.TrailMix] = new ShopItem("Trail mix", "A handful of energy. Never spoils.", 3, false,
                backpack => backpack.AddFood(FoodKind.TrailMix)),
            [ShopItemId.DehydratedMeal] = new ShopItem("Dehydrated meal", "Add boiling water. Very filling. Never spoils.", 8, false,
                backpack => backpack.AddFood(FoodKind.TrailMeal)),
            [ShopItemId.Snare] = new ShopItem("Wire snare", "Catches rabbits while you're away.", 6, false,
                backpack => backpack.AddSnare()),

            [ShopItemId.WaterBladder] = new ShopItem("3 L water bladder", "Replaces your 2 L bottle.", 18, true,
                backpack => backpack.SetWaterCapacity(3f),
                backpack => backpack.WaterCapacity >= 3f ? "You already carry 3 L" : null),
            [ShopItemId.WaterFilter] = new ShopItem("Squeeze water filter", "Water from lakes and streams is safe straight away.", 35, true,
                backpack => backpack.AddWaterFilter(),
                backpack => backpack.HasWaterFilter ? "Already owned" : null),
            [ShopItemId.FishingRod] = new ShopItem("Telescopic fishing rod", "More time to react to bites, and far fewer fish lost.", 30, true,
                backpack => backpack.AddFishingRod(),
                backpack => backpack.HasGoodRod ? "Already owned" : null),
            [ShopItemId.WoolHatAndGloves] = new ShopItem(HatName, "+3 °C. Light, and worth every gram on cold nights.", 20, true,
                backpack => backpack.AddGarment(HatName, 3f),
                backpack => backpack.HasGarment(HatName) ? "Already owned" : null),
            [ShopItemId.InsulatedPants] = new ShopItem(PantsName, "+4 °C. Synthetic fill, warm even when damp.", 45, true,
                backpack => backpack.AddGarment(PantsName, 4f),
                backpack => backpack.HasGarment(PantsName) ? "Already owned" : null),
            [ShopItemId.WinterSleepingBag] = new ShopItem(WinterBagName, "Comfortable down to −12 °C. Replaces your bag.", 120, true,
                backpack => backpack.SetSleepingBag(WinterBagName, -12f),
                backpack => backpack.SleepingBagComfort <= -12f ? "Already owned" : null),
            [ShopItemId.FourSeasonTent] = new ShopItem(FourSeasonTentName, "Adds +9 °C when sleeping in it (your tent adds +5). Replaces your tent.", 150, true,
                backpack => backpack.SetTent(FourSeasonTentName, 9f),
                backpack => backpack.TentShelter >= 9f ? "Already owned" : null),
        };

        public static ShopItem Get(ShopItemId id) => items[id];

        public const int PeltValue = 10;
    }
}
