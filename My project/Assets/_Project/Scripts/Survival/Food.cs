using System;
using System.Collections.Generic;

namespace Backpacking.Survival
{
    public enum FoodKind
    {
        TrailMix,
        TrailMeal,
        Berries,
        RawFish,
        CookedFish,
        SmokedFish,
        RabbitCarcass,
        RawMeat,
        CookedMeat,
        Jerky,
    }

    /// <summary>What a kind of food does when eaten, how long it keeps, and what it becomes when prepared.</summary>
    public sealed class FoodInfo
    {
        public readonly string Name;
        public readonly float Satiety;
        public readonly float Hydration;
        /// <summary>Game hours until it spoils at normal temperatures. 0 means it never spoils.</summary>
        public readonly float SpoilHours;
        /// <summary>Chance of getting sick from eating it as it is.</summary>
        public readonly float SicknessChance;
        /// <summary>Why it can't be eaten as it is, or null if it can.</summary>
        public readonly string NotEdibleReason;
        public readonly FoodKind? CooksInto;
        public readonly FoodKind? SmokesInto;

        public FoodInfo(string name, float satiety = 0f, float hydration = 0f, float spoilHours = 0f,
            float sicknessChance = 0f, string notEdibleReason = null, FoodKind? cooksInto = null, FoodKind? smokesInto = null)
        {
            Name = name;
            Satiety = satiety;
            Hydration = hydration;
            SpoilHours = spoilHours;
            SicknessChance = sicknessChance;
            NotEdibleReason = notEdibleReason;
            CooksInto = cooksInto;
            SmokesInto = smokesInto;
        }

        public bool Spoils => SpoilHours > 0f;
    }

    /// <summary>
    /// The food table for the prototype. These will move into data assets when the full item system arrives.
    /// </summary>
    public static class FoodCatalog
    {
        static readonly Dictionary<FoodKind, FoodInfo> table = new()
        {
            [FoodKind.TrailMix] = new FoodInfo("Trail mix", satiety: 8f),
            [FoodKind.TrailMeal] = new FoodInfo("Dehydrated meal", notEdibleReason: "Cook it with water on a stove or fire"),
            [FoodKind.Berries] = new FoodInfo("Wild berries", satiety: 4f, hydration: 2f, spoilHours: 72f),
            [FoodKind.RawFish] = new FoodInfo("Raw trout", satiety: 6f, spoilHours: 12f, sicknessChance: 0.5f,
                cooksInto: FoodKind.CookedFish, smokesInto: FoodKind.SmokedFish),
            [FoodKind.CookedFish] = new FoodInfo("Cooked trout", satiety: 18f, hydration: 2f, spoilHours: 24f,
                smokesInto: FoodKind.SmokedFish),
            [FoodKind.SmokedFish] = new FoodInfo("Smoked trout", satiety: 15f, spoilHours: 168f),
            [FoodKind.RabbitCarcass] = new FoodInfo("Rabbit (uncleaned)", spoilHours: 24f, notEdibleReason: "Clean it first"),
            [FoodKind.RawMeat] = new FoodInfo("Raw rabbit meat", satiety: 8f, spoilHours: 12f, sicknessChance: 0.6f,
                cooksInto: FoodKind.CookedMeat, smokesInto: FoodKind.Jerky),
            [FoodKind.CookedMeat] = new FoodInfo("Cooked rabbit", satiety: 25f, hydration: 2f, spoilHours: 24f,
                smokesInto: FoodKind.Jerky),
            [FoodKind.Jerky] = new FoodInfo("Rabbit jerky", satiety: 20f, spoilHours: 240f),
        };

        public static FoodInfo Get(FoodKind kind) => table[kind];

        public static IEnumerable<FoodKind> AllKinds => (FoodKind[])Enum.GetValues(typeof(FoodKind));
    }

    /// <summary>One piece of food in the backpack, with its own time left before it spoils.</summary>
    [Serializable]
    public class FoodItem
    {
        public FoodKind kind;
        public float hoursLeft;

        public FoodItem(FoodKind kind)
        {
            this.kind = kind;
            hoursLeft = FoodCatalog.Get(kind).SpoilHours;
        }

        public FoodInfo Info => FoodCatalog.Get(kind);
    }

    [Serializable]
    public struct FoodStack
    {
        public FoodKind kind;
        public int count;

        public FoodStack(FoodKind kind, int count)
        {
            this.kind = kind;
            this.count = count;
        }
    }
}
