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
        /// <summary>What a trading post pays for one, in dollars, before its own rate. 0 means they won't buy it.</summary>
        public readonly int Value;
        /// <summary>Kilograms per piece.</summary>
        public readonly float Weight;

        public FoodInfo(string name, float weight, float satiety = 0f, float hydration = 0f, float spoilHours = 0f,
            float sicknessChance = 0f, string notEdibleReason = null, FoodKind? cooksInto = null, FoodKind? smokesInto = null,
            int value = 0)
        {
            Weight = weight;
            Value = value;
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
            // Smoking dries food out, so smoked fish and jerky weigh far less than fresh.
            [FoodKind.TrailMix] = new FoodInfo("Trail mix", 0.1f, satiety: 8f, value: 1),
            [FoodKind.TrailMeal] = new FoodInfo("Dehydrated meal", 0.15f, notEdibleReason: "Cook it with water on a stove or fire", value: 3),
            [FoodKind.Berries] = new FoodInfo("Wild berries", 0.03f, satiety: 4f, hydration: 2f, spoilHours: 72f, value: 1),
            [FoodKind.RawFish] = new FoodInfo("Raw trout", 0.35f, satiety: 6f, spoilHours: 12f, sicknessChance: 0.5f,
                cooksInto: FoodKind.CookedFish, smokesInto: FoodKind.SmokedFish, value: 2),
            [FoodKind.CookedFish] = new FoodInfo("Cooked trout", 0.3f, satiety: 18f, hydration: 2f, spoilHours: 24f,
                smokesInto: FoodKind.SmokedFish, value: 3),
            [FoodKind.SmokedFish] = new FoodInfo("Smoked trout", 0.12f, satiety: 15f, spoilHours: 168f, value: 6),
            [FoodKind.RabbitCarcass] = new FoodInfo("Rabbit (uncleaned)", 1.1f, spoilHours: 24f, notEdibleReason: "Clean it first", value: 4),
            [FoodKind.RawMeat] = new FoodInfo("Raw rabbit meat", 0.4f, satiety: 8f, spoilHours: 12f, sicknessChance: 0.6f,
                cooksInto: FoodKind.CookedMeat, smokesInto: FoodKind.Jerky, value: 2),
            [FoodKind.CookedMeat] = new FoodInfo("Cooked rabbit", 0.35f, satiety: 25f, hydration: 2f, spoilHours: 24f,
                smokesInto: FoodKind.Jerky, value: 4),
            [FoodKind.Jerky] = new FoodInfo("Rabbit jerky", 0.12f, satiety: 20f, spoilHours: 240f, value: 8),
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

        public FoodItem() { }

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
