using System;
using System.Collections.Generic;
using Backpacking.UI;
using Backpacking.World;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Backpacking.Survival
{
    [Serializable]
    public class Garment
    {
        public string name;
        [Tooltip("°C of warmth this layer adds when worn.")]
        public float insulation;
        public bool worn;

        public Garment(string name, float insulation, bool worn)
        {
            this.name = name;
            this.insulation = insulation;
            this.worn = worn;
        }
    }

    /// <summary>
    /// Everything the player carries. The inspector values are the starting kit.
    /// This is a fixed set of supplies plus a food list for the prototype; a general item system comes later.
    /// </summary>
    public class Backpack : MonoBehaviour
    {
        [SerializeField] Vitals vitals;
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] AmbientTemperature temperature;

        [Header("Money & Trade Goods")]
        [SerializeField, Min(0)] int money = 80;
        [SerializeField, Min(0)] int pelts;

        [Header("Shelter & Cooking")]
        [SerializeField] string tentName = "2-person backpacking tent";
        [Tooltip("°C the tent adds when sleeping in it.")]
        [SerializeField] float tentShelter = 5f;
        [SerializeField] bool hasTent = true;
        [SerializeField] bool hasStove = true;
        [SerializeField, Min(0f)] float gasGrams = 230f;
        [SerializeField, Min(0)] int matches = 12;
        [SerializeField, Min(0)] int firewood;

        [Header("Gathering")]
        [SerializeField] bool hasFishingKit = true;
        [Tooltip("A proper rod: longer to react to bites and fewer fish lost.")]
        [SerializeField] bool hasGoodRod;
        [SerializeField, Min(0)] int snares = 3;
        [Tooltip("With a filter, water from lakes and streams is safe straight away.")]
        [SerializeField] bool hasWaterFilter;

        [Header("Food")]
        [SerializeField] List<FoodStack> startingFood = new()
        {
            new FoodStack(FoodKind.TrailMix, 4),
            new FoodStack(FoodKind.TrailMeal, 3),
        };
        [Tooltip("Food keeps this much longer below 5 °C, and spoils this much faster above 20 °C.")]
        [SerializeField] float temperatureSpoilFactor = 2f;

        [Header("Water (litres)")]
        [SerializeField, Min(0.1f)] float waterCapacity = 2f;
        [SerializeField, Min(0f)] float safeWater = 1f;
        [SerializeField, Min(0f)] float untreatedWater;

        [Header("Sleeping Bag")]
        [SerializeField] string sleepingBagName = "3-season down bag";
        [Tooltip("Lowest air temperature (°C) the bag keeps you warm at.")]
        [SerializeField] float sleepingBagComfort = -1f;

        [Header("Clothing")]
        [SerializeField] List<Garment> clothing = new()
        {
            new Garment("Merino base layer", 3f, true),
            new Garment("Fleece", 6f, false),
            new Garment("Rain shell", 2f, false),
            new Garment("Down jacket", 10f, false),
        };

        [Header("Drinking")]
        [SerializeField] float sipLitres = 0.25f;
        [Tooltip("Hydration gained per litre drunk.")]
        [SerializeField] float hydrationPerLitre = 40f;
        [Tooltip("Chance that a sip of untreated water makes you sick.")]
        [SerializeField, Range(0f, 1f)] float untreatedSicknessChance = 0.15f;
        [SerializeField] float sicknessHours = 12f;

        readonly List<FoodItem> food = new();
        readonly List<string> spoiledThisFrame = new();

        public int Money => money;
        public int Pelts => pelts;
        public string TentName => tentName;
        public float TentShelter => tentShelter;
        public bool HasGoodRod => hasGoodRod;
        public bool HasWaterFilter => hasWaterFilter;
        public bool HasTent { get => hasTent; set => hasTent = value; }
        public bool HasStove { get => hasStove; set => hasStove = value; }
        public float GasGrams => gasGrams;
        public int Matches => matches;
        public int Firewood => firewood;
        public bool HasFishingKit => hasFishingKit;
        public int Snares => snares;
        public float WaterCapacity => waterCapacity;
        public float SafeWater => safeWater;
        public float UntreatedWater => untreatedWater;
        public float TotalWater => safeWater + untreatedWater;
        public float FreeWaterSpace => Mathf.Max(0f, waterCapacity - TotalWater);
        public string SleepingBagName => sleepingBagName;
        public float SleepingBagComfort => sleepingBagComfort;
        public IReadOnlyList<Garment> Clothing => clothing;
        public float SipLitres => sipLitres;

        public float ClothingInsulation
        {
            get
            {
                float total = 0f;
                foreach (Garment garment in clothing)
                    if (garment.worn)
                        total += garment.insulation;
                return total;
            }
        }

        void Awake()
        {
            foreach (FoodStack stack in startingFood)
                AddFood(stack.kind, stack.count);
        }

        void Update() => UpdateSpoilage();

        // ---------- Money ----------

        public void AddMoney(int amount) => money += amount;
        public bool TrySpendMoney(int amount) => TrySpend(ref money, amount);
        public bool TryTakePelt() => TrySpend(ref pelts, 1);

        // ---------- Gear & fuel ----------

        public void ToggleGarment(Garment garment) => garment.worn = !garment.worn;

        public bool HasGarment(string garmentName) => clothing.Exists(garment => garment.name == garmentName);

        /// <summary>Adds a new clothing layer, worn straight away.</summary>
        public void AddGarment(string garmentName, float insulation) => clothing.Add(new Garment(garmentName, insulation, true));

        public void SetSleepingBag(string bagName, float comfort)
        {
            sleepingBagName = bagName;
            sleepingBagComfort = comfort;
        }

        public void SetTent(string newTentName, float shelter)
        {
            tentName = newTentName;
            tentShelter = shelter;
        }

        public void SetWaterCapacity(float litres) => waterCapacity = Mathf.Max(waterCapacity, litres);
        public void AddWaterFilter() => hasWaterFilter = true;

        public void AddFishingRod()
        {
            hasFishingKit = true;
            hasGoodRod = true;
        }

        public void AddGas(float grams) => gasGrams += grams;
        public void AddMatches(int count) => matches += count;

        public void AddFirewood(int amount) => firewood += amount;
        public bool TryUseFirewood(int amount) => TrySpend(ref firewood, amount);
        public bool TryUseMatch() => TrySpend(ref matches, 1);
        public void AddSnare() => snares++;
        public bool TryUseSnare() => TrySpend(ref snares, 1);

        public bool TryUseGas(float grams)
        {
            if (gasGrams < grams)
                return false;
            gasGrams -= grams;
            return true;
        }

        // ---------- Food ----------

        public int CountFood(FoodKind kind)
        {
            int count = 0;
            foreach (FoodItem item in food)
                if (item.kind == kind)
                    count++;
            return count;
        }

        /// <summary>Hours until the next piece of this kind spoils, or infinity if it keeps.</summary>
        public float SoonestSpoilHours(FoodKind kind)
        {
            float soonest = float.PositiveInfinity;
            foreach (FoodItem item in food)
                if (item.kind == kind && item.Info.Spoils)
                    soonest = Mathf.Min(soonest, item.hoursLeft);
            return soonest;
        }

        public void AddFood(FoodKind kind, int count = 1)
        {
            for (int i = 0; i < count; i++)
                food.Add(new FoodItem(kind));
        }

        /// <summary>Removes the piece of this kind that would spoil soonest.</summary>
        public bool TryTakeFood(FoodKind kind)
        {
            int best = -1;
            for (int i = 0; i < food.Count; i++)
            {
                if (food[i].kind != kind)
                    continue;
                if (best < 0 || SpoilOrder(food[i]) < SpoilOrder(food[best]))
                    best = i;
            }
            if (best < 0)
                return false;
            food.RemoveAt(best);
            return true;
        }

        static float SpoilOrder(FoodItem item) => item.Info.Spoils ? item.hoursLeft : float.PositiveInfinity;

        public void Eat(FoodKind kind)
        {
            FoodInfo info = FoodCatalog.Get(kind);
            if (info.NotEdibleReason != null || !TryTakeFood(kind))
                return;

            vitals.Eat(info.Satiety);
            vitals.Drink(info.Hydration);
            if (Random.value < info.SicknessChance)
            {
                vitals.MakeSick(sicknessHours);
                Notifications.Post($"The {info.Name.ToLowerInvariant()} doesn't sit well. You feel sick.");
            }
        }

        /// <summary>Counts the food that <paramref name="transform"/> would change, e.g. everything that can be cooked.</summary>
        public int CountConvertible(Func<FoodInfo, FoodKind?> transform)
        {
            int count = 0;
            foreach (FoodItem item in food)
                if (transform(item.Info) != null)
                    count++;
            return count;
        }

        /// <summary>Turns food into its prepared form (cooked, smoked), starting it fresh. Returns how many changed.</summary>
        public int ConvertFood(Func<FoodInfo, FoodKind?> transform)
        {
            int changed = 0;
            for (int i = 0; i < food.Count; i++)
            {
                FoodKind? result = transform(food[i].Info);
                if (result == null)
                    continue;
                food[i] = new FoodItem(result.Value);
                changed++;
            }
            return changed;
        }

        /// <summary>Guts and skins a rabbit into meat and a pelt to trade.</summary>
        public bool CleanCarcass(int meatPieces)
        {
            if (!TryTakeFood(FoodKind.RabbitCarcass))
                return false;
            AddFood(FoodKind.RawMeat, meatPieces);
            pelts++;
            return true;
        }

        void UpdateSpoilage()
        {
            float hours = Time.deltaTime * timeOfDay.HoursPerSecond;
            if (hours <= 0f || food.Count == 0)
                return;

            float air = temperature.GetTemperature(transform.position);
            float rate = air < 5f ? 1f / temperatureSpoilFactor : air > 20f ? temperatureSpoilFactor : 1f;

            spoiledThisFrame.Clear();
            for (int i = food.Count - 1; i >= 0; i--)
            {
                FoodItem item = food[i];
                if (!item.Info.Spoils)
                    continue;
                item.hoursLeft -= hours * rate;
                if (item.hoursLeft > 0f)
                    continue;
                food.RemoveAt(i);
                if (!spoiledThisFrame.Contains(item.Info.Name))
                    spoiledThisFrame.Add(item.Info.Name);
            }

            foreach (string name in spoiledThisFrame)
                Notifications.Post($"Your {name.ToLowerInvariant()} has spoiled.");
        }

        // ---------- Water ----------

        /// <summary>Tops the bottle up from a lake or stream: safe if filtered, untreated otherwise. Returns the litres added.</summary>
        public float FillFromSource()
        {
            float added = FreeWaterSpace;
            if (hasWaterFilter)
                safeWater += added;
            else
                untreatedWater += added;
            return added;
        }

        /// <summary>Makes all untreated water safe. Returns the litres treated.</summary>
        public float BoilAllWater()
        {
            float boiled = untreatedWater;
            safeWater += boiled;
            untreatedWater = 0f;
            return boiled;
        }

        /// <summary>Takes water out for cooking, untreated first since it gets boiled anyway.</summary>
        public bool TryUseCookingWater(float litres)
        {
            if (TotalWater < litres)
                return false;
            float fromUntreated = Mathf.Min(untreatedWater, litres);
            untreatedWater -= fromUntreated;
            safeWater -= litres - fromUntreated;
            return true;
        }

        public void DrinkSafeWater()
        {
            float litres = Mathf.Min(sipLitres, safeWater);
            if (litres <= 0f)
                return;
            safeWater -= litres;
            vitals.Drink(litres * hydrationPerLitre);
        }

        public void DrinkUntreatedWater()
        {
            float litres = Mathf.Min(sipLitres, untreatedWater);
            if (litres <= 0f)
                return;
            untreatedWater -= litres;
            DrinkUntreatedDirectly(litres);
        }

        /// <summary>Drinking from a lake or stream: hydrating, but it might make you sick unless filtered.</summary>
        public void DrinkFromSource(float litres)
        {
            if (hasWaterFilter)
                vitals.Drink(litres * hydrationPerLitre);
            else
                DrinkUntreatedDirectly(litres);
        }

        void DrinkUntreatedDirectly(float litres)
        {
            vitals.Drink(litres * hydrationPerLitre);
            if (Random.value < untreatedSicknessChance)
            {
                vitals.MakeSick(sicknessHours);
                Notifications.Post("Your stomach doesn't feel right...");
            }
        }

        static bool TrySpend(ref int stock, int amount)
        {
            if (stock < amount)
                return false;
            stock -= amount;
            return true;
        }
    }
}
