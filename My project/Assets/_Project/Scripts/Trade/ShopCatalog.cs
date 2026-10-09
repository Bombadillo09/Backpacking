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
        // The outdoor store's starter gear.
        UltralightPack,
        TrekkingPack,
        ExpeditionPack,
        OnePersonTent,
        SummerBag,
        ThreeSeasonBag,
        Stove,
        WaterBottle,
        Machete,
        FishingKit,
        BaseLayer,
        Fleece,
        RainShell,
        DownJacket,
        HikingBoots,
    }

    /// <summary>Something a trading post can sell: what it costs and what it does to the backpack.</summary>
    public sealed class ShopItem
    {
        public readonly string Name;
        public readonly string Description;
        public readonly int BasePrice;
        /// <summary>True for one-off gear upgrades rather than supplies.</summary>
        public readonly bool IsGear;
        /// <summary>Put on in the shop (a pack, boots), rather than carried out to the truck.</summary>
        public readonly bool Worn;
        readonly Func<Backpack, string> problem;
        readonly Action<Backpack> apply;

        public ShopItem(string name, string description, int basePrice, bool isGear, Action<Backpack> apply,
            Func<Backpack, string> problem = null, bool worn = false)
        {
            Name = name;
            Description = description;
            BasePrice = basePrice;
            IsGear = isGear;
            Worn = worn;
            this.apply = apply;
            this.problem = problem;
        }

        /// <summary>Why this can't be bought (already owned, etc.), ignoring price. Null if it can.</summary>
        public string Problem(Backpack backpack) => problem?.Invoke(backpack);

        /// <summary>
        /// Why this can't be bought when it's delivered to <paramref name="delivery"/> (the truck bed) for
        /// <paramref name="wearer"/>. Gear owned in either place counts as owned; a supply's need (a bow, for
        /// arrows) met in either place is enough.
        /// </summary>
        public string Problem(Backpack wearer, Backpack delivery)
        {
            string mine = Problem(wearer);
            if (delivery == null || delivery == wearer || Worn)
                return mine;
            string there = Problem(delivery);
            if (IsGear)
                return mine ?? there;
            return mine == null || there == null ? null : mine;
        }

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
        const string OnePersonTentName = "1-person tunnel tent";
        const string SummerBagName = "Summer synthetic bag";
        const string ThreeSeasonBagName = "3-season down bag";
        const string BaseLayerName = "Merino base layer";
        const string FleeceName = "Fleece";
        const string RainShellName = "Rain shell";
        const string DownJacketName = "Down jacket";
        const string HikingBootsName = "Trail hiking boots";

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
                "A freestanding dome with two crossing poles. +5 °C when sleeping (the 1-person tent is +4), room for your pack inside. 1.8 kg. Replaces your tent.", 150, true,
                backpack => backpack.AddTent(TwoPersonTentName, Camp.TentModel.TwoPerson, 5f, 1.8f),
                backpack => backpack.OwnsTent && backpack.TentModel >= Camp.TentModel.TwoPerson ? "Your tent is already this good" : TentAwayProblem(backpack)),
            [ShopItemId.FourSeasonTent] = new ShopItem(FourSeasonTentName,
                "Three poles and guy lines: stands up to storms. +9 °C when sleeping, room for your pack inside. 2.6 kg. Replaces your tent.", 150, true,
                backpack => backpack.AddTent(FourSeasonTentName, Camp.TentModel.FourSeason, 9f, 2.6f),
                backpack => backpack.OwnsTent && backpack.TentModel >= Camp.TentModel.FourSeason ? "Already owned" : TentAwayProblem(backpack)),
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
                backpack => backpack.BootsStrain <= 0.8f ? "Your boots are already this good" : null, worn: true),
            [ShopItemId.MountaineeringBoots] = new ShopItem(MountainBootsName,
                "Waterproof and insulated (+2 °C). Your feet tire about 40% slower, and stay dry in the rain. Replaces your boots.", 110, true,
                backpack => backpack.SetBoots(MountainBootsName, 0.7f, true, 2f),
                backpack => backpack.BootsStrain <= 0.7f ? "Already owned" : null, worn: true),

            // ---------- The outdoor store's starter gear ----------

            [ShopItemId.UltralightPack] = PackFor(PackModel.Ultralight40, 70),
            [ShopItemId.TrekkingPack] = PackFor(PackModel.Trekking55, 120),
            [ShopItemId.ExpeditionPack] = PackFor(PackModel.Expedition70, 170),
            [ShopItemId.OnePersonTent] = new ShopItem(OnePersonTentName,
                "A light two-hoop tunnel: stake out the ends and it stands. +4 °C when you sleep in it. 1.2 kg.", 90, true,
                backpack => backpack.AddTent(OnePersonTentName, Camp.TentModel.OnePerson, 4f, 1.2f),
                backpack => backpack.OwnsTent ? "You have a tent" : null),
            [ShopItemId.SummerBag] = new ShopItem(SummerBagName,
                "Cheap and tough. Warm enough down to about +5 °C, so cold nights will be miserable. 0.8 kg.", 45, true,
                backpack => backpack.SetSleepingBag(SummerBagName, 5f, 0.8f),
                backpack => backpack.HasSleepingBag ? "You have a sleeping bag" : null),
            [ShopItemId.ThreeSeasonBag] = new ShopItem(ThreeSeasonBagName,
                "Light, packs small, and warm down to about −1 °C. Keep it dry: wet down is useless. 1.0 kg.", 110, true,
                backpack => backpack.SetSleepingBag(ThreeSeasonBagName, -1f, 1f),
                backpack => backpack.HasSleepingBag && backpack.SleepingBagComfort <= -1f ? "Your bag is already this warm" : null),
            [ShopItemId.Stove] = new ShopItem("Canister stove & pot",
                "Boils water and cooks a meal in minutes, rain or shine. Runs on gas canisters (sold separately). 0.35 kg.", 35, true,
                backpack => backpack.AddStove(),
                backpack => backpack.OwnsStove ? "Already owned" : null),
            [ShopItemId.WaterBottle] = new ShopItem("2 L water bottle",
                "Fill it at lakes and streams. Boil or filter the water before you drink it.", 8, true,
                backpack => backpack.AddBottle(2f),
                backpack => backpack.WaterCapacity >= 2f ? "You have a bottle" : null),
            [ShopItemId.Machete] = new ShopItem("Machete",
                "Hacks a way through thick brush and clears a campsite in the woods. 0.5 kg.", 25, true,
                backpack => backpack.AddMachete(),
                backpack => backpack.HasMachete ? "Already owned" : null),
            [ShopItemId.FishingKit] = new ShopItem("Hand line & hooks",
                "A basic fishing kit: fish bite, but you need quick hands. 0.1 kg.", 10, true,
                backpack => backpack.AddFishingKit(),
                backpack => backpack.HasFishingKit ? "You have a fishing kit" : null),
            [ShopItemId.BaseLayer] = new ShopItem(BaseLayerName, "+3 °C, 0.2 kg. Wool stays warm even when damp; cotton doesn't.", 40, true,
                backpack => backpack.AddGarment(BaseLayerName, 3f, 0.2f),
                backpack => backpack.HasGarment(BaseLayerName) ? "Already owned" : null),
            [ShopItemId.Fleece] = new ShopItem(FleeceName, "+6 °C, 0.45 kg. The warm layer for evenings in camp.", 35, true,
                backpack => backpack.AddGarment(FleeceName, 6f, 0.45f),
                backpack => backpack.HasGarment(FleeceName) ? "Already owned" : null),
            [ShopItemId.RainShell] = new ShopItem(RainShellName, "+2 °C, 0.3 kg. Keeps the rain off you. Without one, a wet day is a cold one.", 55, true,
                backpack => backpack.AddGarment(RainShellName, 2f, 0.3f, waterproof: true),
                backpack => backpack.HasGarment(RainShellName) ? "Already owned" : null),
            [ShopItemId.DownJacket] = new ShopItem(DownJacketName, "+10 °C, 0.35 kg. Very warm for its weight, but keep it out of the rain.", 90, true,
                backpack => backpack.AddGarment(DownJacketName, 10f, 0.35f),
                backpack => backpack.HasGarment(DownJacketName) ? "Already owned" : null),
            [ShopItemId.HikingBoots] = new ShopItem(HikingBootsName,
                "Proper boots: your feet tire far slower than in sneakers, and blister less. You put them on in the shop.", 60, true,
                backpack => backpack.SetBoots(HikingBootsName, 1f, false, 0f),
                backpack => backpack.BootsStrain <= 1f ? "Your boots are already this good" : null, worn: true),
        };

        static ShopItem PackFor(PackModel model, int price)
        {
            PackInfo info = PackModels.Get(model);
            string lid = info.LidLitres > 0f ? $", {info.LidLitres:0} L in the lid" : "";
            return new ShopItem(info.Name,
                $"{info.Description} Holds {info.MainLitres:0} L inside{lid}, {info.PocketLitres:0} L in the side pockets and " +
                $"{info.Straps} things strapped outside. {info.Weight:0.0} kg. You wear it out of the shop; swap packs and the store keeps your old one.",
                price, true,
                backpack => backpack.SetPack(model),
                backpack => backpack.PackModel == model ? "You're wearing it" : null, worn: true);
        }

        public static ShopItem Get(ShopItemId id) => items[id];

        /// <summary>The sections of a shop's list, in the order they're shown.</summary>
        public static readonly string[] Categories =
        {
            "Packs", "Shelter & sleeping", "Cooking & fuel", "Water", "Food", "Clothing & boots", "Tools & hunting", "First aid",
        };

        /// <summary>Which section of the shop list an item is in.</summary>
        public static string CategoryOf(ShopItemId id) => id switch
        {
            ShopItemId.UltralightPack or ShopItemId.TrekkingPack or ShopItemId.ExpeditionPack => "Packs",
            ShopItemId.OnePersonTent or ShopItemId.TwoPersonTent or ShopItemId.FourSeasonTent or ShopItemId.SummerBag
                or ShopItemId.ThreeSeasonBag or ShopItemId.WinterSleepingBag or ShopItemId.FoamMat or ShopItemId.InflatableMat
                or ShopItemId.CampChair => "Shelter & sleeping",
            ShopItemId.Stove or ShopItemId.GasCanister or ShopItemId.Matches => "Cooking & fuel",
            ShopItemId.WaterBottle or ShopItemId.WaterBladder or ShopItemId.WaterFilter => "Water",
            ShopItemId.TrailMix or ShopItemId.DehydratedMeal => "Food",
            ShopItemId.BaseLayer or ShopItemId.Fleece or ShopItemId.RainShell or ShopItemId.DownJacket or ShopItemId.WoolHatAndGloves
                or ShopItemId.InsulatedPants or ShopItemId.HikingBoots or ShopItemId.LeatherBoots or ShopItemId.MountaineeringBoots => "Clothing & boots",
            ShopItemId.Bandages or ShopItemId.Antibiotics => "First aid",
            _ => "Tools & hunting",
        };

        /// <summary>A tent can only be traded in while it's packed away in your backpack.</summary>
        static string TentAwayProblem(Backpack backpack) => backpack.OwnsTent && !backpack.HasTent ? "Pack your tent away first" : null;

        public const int PeltValue = 10;
        public const int HideValue = 40;
    }
}
