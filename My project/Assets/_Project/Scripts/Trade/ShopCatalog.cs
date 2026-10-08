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
        LeatherBoots,
        MountaineeringBoots,
        Antibiotics,
        // New items go at the end: trading posts store their stock by this number.
        TwoPersonTent,
        FoamMat,
        InflatableMat,
        Bandages,
        CampChair,
        HuntingBow,
        Arrows,
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
        const string TwoPersonTentName = "2-person dome tent";
        const string FoamMatName = "Closed-cell foam mat";
        const string AirMatName = "Insulated air mat";
        const string LeatherBootsName = "Leather hiking boots";
        const string MountainBootsName = "Mountaineering boots";

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
            [ShopItemId.Bandages] = new ShopItem("Bandages", "A pack of 3, for cuts.", 4, false,
                backpack => backpack.AddBandages(3)),
            [ShopItemId.CampChair] = new ShopItem("Lightweight camp chair",
                "Folds into a sack the size of a water bottle. Poles first, then the seat. Sitting in it, your feet rest 60% faster than on the ground. 0.9 kg.", 40, true,
                backpack => backpack.AddChair(),
                backpack => backpack.HasChair ? "Already owned" : null),
            [ShopItemId.Arrows] = new ShopItem("Arrows", "Six carbon arrows with broadheads. Arrows can be found and shot again, but they break on rock.", 9, false,
                backpack => backpack.AddArrows(6),
                backpack => backpack.HasBow ? null : "You need a bow first"),
            [ShopItemId.HuntingBow] = new ShopItem("Takedown recurve bow",
                "For hunting deer and rabbits. Hold to draw, aim, release to shoot; arrows drop over distance. Comes with 6 arrows. 0.9 kg.", 70, true,
                backpack =>
                {
                    backpack.AddBow();
                    backpack.AddArrows(6);
                },
                backpack => backpack.HasBow ? "Already owned" : null),
            [ShopItemId.Antibiotics] = new ShopItem("Antibiotics", "One course. Clears an infection in a few hours.", 25, false,
                backpack => backpack.AddAntibiotics()),

            [ShopItemId.WaterBladder] = new ShopItem("3 L water bladder", "Replaces your 2 L bottle.", 18, true,
                backpack => backpack.SetWaterCapacity(3f),
                backpack => backpack.WaterCapacity >= 3f ? "You already carry 3 L" : null),
            [ShopItemId.WaterFilter] = new ShopItem("Squeeze water filter", "Water from lakes and streams is safe straight away.", 35, true,
                backpack => backpack.AddWaterFilter(),
                backpack => backpack.HasWaterFilter ? "Already owned" : null),
            [ShopItemId.FishingRod] = new ShopItem("Telescopic fishing rod", "More time to react to bites, and far fewer fish lost.", 30, true,
                backpack => backpack.AddFishingRod(),
                backpack => backpack.HasGoodRod ? "Already owned" : null),
            [ShopItemId.WoolHatAndGloves] = new ShopItem(HatName, "+3 °C, 0.1 kg. Worth every gram on cold nights.", 20, true,
                backpack => backpack.AddGarment(HatName, 3f, 0.1f),
                backpack => backpack.HasGarment(HatName) ? "Already owned" : null),
            [ShopItemId.InsulatedPants] = new ShopItem(PantsName, "+4 °C, 0.4 kg. Synthetic fill, warm even when damp.", 45, true,
                backpack => backpack.AddGarment(PantsName, 4f, 0.4f),
                backpack => backpack.HasGarment(PantsName) ? "Already owned" : null),
            [ShopItemId.WinterSleepingBag] = new ShopItem(WinterBagName, "Comfortable down to −12 °C. 1.6 kg (yours is 1.0). Replaces your bag.", 120, true,
                backpack => backpack.SetSleepingBag(WinterBagName, -12f, 1.6f),
                backpack => backpack.SleepingBagComfort <= -12f ? "Already owned" : null),
            [ShopItemId.TwoPersonTent] = new ShopItem(TwoPersonTentName,
                "A freestanding dome with two crossing poles. +5 °C when sleeping (the 1-person tent is +4), room for your pack inside. 1.8 kg. Replaces your tent.", 90, true,
                backpack => backpack.SetTent(TwoPersonTentName, Camp.TentModel.TwoPerson, 5f, 1.8f),
                backpack => backpack.TentModel >= Camp.TentModel.TwoPerson ? "Your tent is already this good" : TentAwayProblem(backpack)),
            [ShopItemId.FourSeasonTent] = new ShopItem(FourSeasonTentName,
                "Three poles and guy lines: stands up to storms. +9 °C when sleeping, room for your pack inside. 2.6 kg. Replaces your tent.", 150, true,
                backpack => backpack.SetTent(FourSeasonTentName, Camp.TentModel.FourSeason, 9f, 2.6f),
                backpack => backpack.TentModel >= Camp.TentModel.FourSeason ? "Already owned" : TentAwayProblem(backpack)),
            [ShopItemId.FoamMat] = new ShopItem(FoamMatName,
                "Cheap and tough. Keeps the cold ground off you (+3 °C asleep) and you sleep better: 25% more energy back. 0.4 kg.", 15, true,
                backpack => backpack.SetMat(FoamMatName, 3f, 0.4f, 1.25f),
                backpack => backpack.MatRecovery >= 1.25f ? "You already have a mat this good" : null),
            [ShopItemId.InflatableMat] = new ShopItem(AirMatName,
                "Thick and warm (+6 °C asleep): you wake far more rested, 50% more energy back. 0.5 kg. Replaces your mat.", 55, true,
                backpack => backpack.SetMat(AirMatName, 6f, 0.5f, 1.5f),
                backpack => backpack.MatRecovery >= 1.5f ? "Already owned" : null),
            [ShopItemId.LeatherBoots] = new ShopItem(LeatherBootsName,
                "Stiff, well-fitted boots. Your feet tire about a third slower than in worn boots. Replaces your boots.", 55, true,
                backpack => backpack.SetBoots(LeatherBootsName, 0.8f, false, 0f),
                backpack => backpack.BootsStrain <= 0.8f ? "Your boots are already this good" : null),
            [ShopItemId.MountaineeringBoots] = new ShopItem(MountainBootsName,
                "Waterproof and insulated (+2 °C). Your feet tire about 40% slower, and stay dry in the rain. Replaces your boots.", 110, true,
                backpack => backpack.SetBoots(MountainBootsName, 0.7f, true, 2f),
                backpack => backpack.BootsStrain <= 0.7f ? "Already owned" : null),
        };

        public static ShopItem Get(ShopItemId id) => items[id];

        /// <summary>A tent can only be traded in while it's packed away in your backpack.</summary>
        static string TentAwayProblem(Backpack backpack) => backpack.HasTent ? null : "Pack your tent away first";

        public const int PeltValue = 10;
        public const int HideValue = 40;
    }
}
