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
        [Tooltip("Kilograms.")]
        public float weight;
        [Tooltip("Keeps rain off while worn.")]
        public bool waterproof;
        public bool worn;

        public Garment() { }

        public Garment(string name, float insulation, float weight, bool worn, bool waterproof = false)
        {
            this.name = name;
            this.insulation = insulation;
            this.weight = weight;
            this.worn = worn;
            this.waterproof = waterproof;
        }
    }

    /// <summary>Everything in the backpack, in a form that can be written to a save file.</summary>
    [Serializable]
    public class BackpackState
    {
        public int money, pelts;
        public string tentName;
        public float tentShelter, tentWeight;
        public bool hasTent, hasStove;
        public float gasGrams;
        public int matches, firewood;
        public bool hasFishingKit, hasGoodRod, hasWaterFilter;
        public int snares;
        public float waterCapacity, safeWater, untreatedWater;
        public string sleepingBagName;
        public float sleepingBagComfort, sleepingBagWeight;
        public List<Garment> clothing = new();
        public List<FoodItem> food = new();
    }

    /// <summary>
    /// Everything the player carries, and how heavy it is. The inspector values are the starting kit.
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
        [SerializeField] float tentWeight = 1.8f;
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
        [SerializeField] float sleepingBagWeight = 1f;

        [Header("Clothing")]
        [SerializeField] List<Garment> clothing = new()
        {
            new Garment("Merino base layer", 3f, 0.2f, true),
            new Garment("Fleece", 6f, 0.45f, false),
            new Garment("Rain shell", 2f, 0.3f, false, waterproof: true),
            new Garment("Down jacket", 10f, 0.35f, false),
        };

        [Header("Drinking")]
        [SerializeField] float sipLitres = 0.25f;
        [Tooltip("Hydration gained per litre drunk.")]
        [SerializeField] float hydrationPerLitre = 40f;
        [Tooltip("Chance that a sip of untreated water makes you sick.")]
        [SerializeField, Range(0f, 1f)] float untreatedSicknessChance = 0.15f;
        [SerializeField] float sicknessHours = 12f;

        [Header("Weight (kg)")]
        [Tooltip("The pack itself, plus small things like a knife and first-aid kit.")]
        [SerializeField] float packWeight = 1.3f;
        [SerializeField] float stoveWeight = 0.35f;
        [Tooltip("Empty weight of each 230 g gas canister.")]
        [SerializeField] float canisterWeight = 0.15f;
        [SerializeField] float firewoodWeight = 0.8f;
        [SerializeField] float fishingKitWeight = 0.1f;
        [SerializeField] float fishingRodWeight = 0.4f;
        [SerializeField] float snareWeight = 0.05f;
        [SerializeField] float filterWeight = 0.1f;
        [SerializeField] float peltWeight = 0.3f;
        [Tooltip("Up to this weight you move freely.")]
        [SerializeField] float comfortableLoad = 15f;
        [Tooltip("Above this you're overloaded: very slow and unable to sprint.")]
        [SerializeField] float maxLoad = 25f;
        [SerializeField, Range(0.1f, 1f)] float speedAtMaxLoad = 0.65f;
        [SerializeField, Range(0.1f, 1f)] float overloadedSpeed = 0.45f;
        [Tooltip("Extra food, water and energy used while walking at max load (0.6 = 60% more).")]
        [SerializeField] float exertionAtMaxLoad = 0.6f;

        List<FoodItem> food = new();
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
        public float ComfortableLoad => comfortableLoad;
        public float MaxLoad => maxLoad;

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

        public bool WearingWaterproof => clothing.Exists(garment => garment.worn && garment.waterproof);

        void Awake()
        {
            foreach (FoodStack stack in startingFood)
                AddFood(stack.kind, stack.count);
        }

        void Update() => UpdateSpoilage();

        // ---------- Weight ----------

        /// <summary>Total kilograms carried, including worn clothing.</summary>
        public float TotalWeight
        {
            get
            {
                float weight = packWeight + sleepingBagWeight + TotalWater;
                if (hasTent)
                    weight += tentWeight;
                if (hasStove)
                    weight += stoveWeight;
                if (gasGrams > 0f)
                    weight += gasGrams / 1000f + Mathf.Ceil(gasGrams / 230f) * canisterWeight;
                weight += matches * 0.002f + firewood * firewoodWeight + snares * snareWeight + pelts * peltWeight;
                if (hasFishingKit)
                    weight += hasGoodRod ? fishingRodWeight : fishingKitWeight;
                if (hasWaterFilter)
                    weight += filterWeight;
                foreach (Garment garment in clothing)
                    weight += garment.weight;
                foreach (FoodItem item in food)
                    weight += item.Info.Weight;
                return weight;
            }
        }

        public bool IsOverloaded => TotalWeight > maxLoad;

        /// <summary>How much the load slows walking: 1 when comfortable, lower when heavy.</summary>
        public float LoadSpeedMultiplier
        {
            get
            {
                float weight = TotalWeight;
                if (weight > maxLoad)
                    return overloadedSpeed;
                return Mathf.Lerp(1f, speedAtMaxLoad, Mathf.InverseLerp(comfortableLoad, maxLoad, weight));
            }
        }

        /// <summary>Extra effort of walking with this load: 1 when comfortable, more when heavy.</summary>
        public float LoadExertionMultiplier =>
            1f + exertionAtMaxLoad * Mathf.Clamp01((TotalWeight - comfortableLoad) / (maxLoad - comfortableLoad)) * (IsOverloaded ? 1.5f : 1f);

        // ---------- Money ----------

        public void AddMoney(int amount) => money += amount;
        public bool TrySpendMoney(int amount) => TrySpend(ref money, amount);
        public bool TryTakePelt() => TrySpend(ref pelts, 1);

        // ---------- Gear & fuel ----------

        public void ToggleGarment(Garment garment) => garment.worn = !garment.worn;

        public bool HasGarment(string garmentName) => clothing.Exists(garment => garment.name == garmentName);

        /// <summary>Adds a new clothing layer, worn straight away.</summary>
        public void AddGarment(string garmentName, float insulation, float weight) =>
            clothing.Add(new Garment(garmentName, insulation, weight, true));

        public void SetSleepingBag(string bagName, float comfort, float weight)
        {
            sleepingBagName = bagName;
            sleepingBagComfort = comfort;
            sleepingBagWeight = weight;
        }

        public void SetTent(string newTentName, float shelter, float weight)
        {
            tentName = newTentName;
            tentShelter = shelter;
            tentWeight = weight;
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

        public void PourOutUntreatedWater() => untreatedWater = 0f;

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

        // ---------- Saving ----------

        public BackpackState CaptureState() => new()
        {
            money = money,
            pelts = pelts,
            tentName = tentName,
            tentShelter = tentShelter,
            tentWeight = tentWeight,
            hasTent = hasTent,
            hasStove = hasStove,
            gasGrams = gasGrams,
            matches = matches,
            firewood = firewood,
            hasFishingKit = hasFishingKit,
            hasGoodRod = hasGoodRod,
            hasWaterFilter = hasWaterFilter,
            snares = snares,
            waterCapacity = waterCapacity,
            safeWater = safeWater,
            untreatedWater = untreatedWater,
            sleepingBagName = sleepingBagName,
            sleepingBagComfort = sleepingBagComfort,
            sleepingBagWeight = sleepingBagWeight,
            clothing = new List<Garment>(clothing),
            food = new List<FoodItem>(food),
        };

        public void RestoreState(BackpackState state)
        {
            money = state.money;
            pelts = state.pelts;
            tentName = state.tentName;
            tentShelter = state.tentShelter;
            tentWeight = state.tentWeight;
            hasTent = state.hasTent;
            hasStove = state.hasStove;
            gasGrams = state.gasGrams;
            matches = state.matches;
            firewood = state.firewood;
            hasFishingKit = state.hasFishingKit;
            hasGoodRod = state.hasGoodRod;
            hasWaterFilter = state.hasWaterFilter;
            snares = state.snares;
            waterCapacity = state.waterCapacity;
            safeWater = state.safeWater;
            untreatedWater = state.untreatedWater;
            sleepingBagName = state.sleepingBagName;
            sleepingBagComfort = state.sleepingBagComfort;
            sleepingBagWeight = state.sleepingBagWeight;
            clothing = new List<Garment>(state.clothing);
            food = new List<FoodItem>(state.food);
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
