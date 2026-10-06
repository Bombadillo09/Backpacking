using System;
using System.Collections.Generic;
using Backpacking.UI;
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
    /// This is a simple fixed set of supplies for the prototype; a general item system comes later.
    /// </summary>
    public class Backpack : MonoBehaviour
    {
        [SerializeField] Vitals vitals;

        [Header("Shelter & Cooking")]
        [SerializeField] bool hasTent = true;
        [SerializeField] bool hasStove = true;
        [SerializeField, Min(0f)] float gasGrams = 230f;
        [SerializeField, Min(0)] int matches = 12;
        [SerializeField, Min(0)] int firewood;

        [Header("Food")]
        [SerializeField, Min(0)] int snacks = 4;
        [SerializeField, Min(0)] int trailMeals = 3;

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

        [Header("Consumables")]
        [SerializeField] float sipLitres = 0.25f;
        [Tooltip("Hydration gained per litre drunk.")]
        [SerializeField] float hydrationPerLitre = 40f;
        [SerializeField] float snackSatiety = 8f;
        [Tooltip("Chance that a sip of untreated water makes you sick.")]
        [SerializeField, Range(0f, 1f)] float untreatedSicknessChance = 0.15f;
        [SerializeField] float sicknessHours = 12f;

        public bool HasTent { get => hasTent; set => hasTent = value; }
        public bool HasStove { get => hasStove; set => hasStove = value; }
        public float GasGrams => gasGrams;
        public int Matches => matches;
        public int Firewood => firewood;
        public int Snacks => snacks;
        public int TrailMeals => trailMeals;
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

        public void ToggleGarment(Garment garment) => garment.worn = !garment.worn;

        public void AddFirewood(int amount) => firewood += amount;

        public bool TryUseFirewood(int amount) => TrySpend(ref firewood, amount);
        public bool TryUseMatch() => TrySpend(ref matches, 1);
        public bool TryUseTrailMeal() => TrySpend(ref trailMeals, 1);

        public bool TryUseGas(float grams)
        {
            if (gasGrams < grams)
                return false;
            gasGrams -= grams;
            return true;
        }

        /// <summary>Tops the bottle up with untreated water. Returns the litres added.</summary>
        public float FillFromSource()
        {
            float added = FreeWaterSpace;
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

        /// <summary>Drinking from a lake or stream: hydrating, but it might make you sick.</summary>
        public void DrinkUntreatedDirectly(float litres)
        {
            vitals.Drink(litres * hydrationPerLitre);
            if (Random.value < untreatedSicknessChance)
            {
                vitals.MakeSick(sicknessHours);
                Notifications.Post("Your stomach doesn't feel right...");
            }
        }

        public void EatSnack()
        {
            if (TrySpend(ref snacks, 1))
                vitals.Eat(snackSatiety);
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
