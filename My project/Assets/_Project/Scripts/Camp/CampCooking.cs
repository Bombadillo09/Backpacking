using System;
using System.Collections.Generic;
using Backpacking.Interaction;
using Backpacking.Survival;
using Backpacking.UI;

namespace Backpacking.Camp
{
    /// <summary>The boiling and cooking options shared by the campfire and the stove.</summary>
    public static class CampCooking
    {
        public const float MealWaterLitres = 0.4f;
        const float MealSatiety = 35f;
        const float MealHydration = 8f;

        /// <param name="minutesPerLitre">How long this heat source takes to boil a litre.</param>
        /// <param name="mealMinutes">How long a trail meal takes.</param>
        /// <param name="fuelProblem">Returns why a task of the given length can't run, or null if it can.</param>
        /// <param name="useFuel">Spends fuel for a task of the given length, when it starts.</param>
        public static void AddOptions(Interactor interactor, List<InteractionOption> options,
            float minutesPerLitre, float mealMinutes, Func<float, string> fuelProblem, Action<float> useFuel)
        {
            Backpack backpack = interactor.Backpack;

            float litres = backpack.UntreatedWater;
            float boilMinutes = Math.Max(2f, litres * minutesPerLitre);
            string boilProblem = litres <= 0f ? "No untreated water to boil" : fuelProblem(boilMinutes);
            options.Add(new InteractionOption($"Boil water ({litres:0.0} L, {boilMinutes:0} min)", () =>
            {
                useFuel(boilMinutes);
                interactor.Activity.Begin("Boiling water", boilMinutes, () =>
                {
                    float boiled = backpack.BoilAllWater();
                    Notifications.Post($"{boiled:0.0} L of water is now safe to drink.");
                });
            }, boilProblem));

            string mealProblem = backpack.TrailMeals <= 0 ? "No trail meals left"
                : backpack.TotalWater < MealWaterLitres ? $"Needs {MealWaterLitres:0.0} L of water"
                : fuelProblem(mealMinutes);
            options.Add(new InteractionOption($"Cook trail meal ({mealMinutes:0} min)", () =>
            {
                backpack.TryUseTrailMeal();
                backpack.TryUseCookingWater(MealWaterLitres);
                useFuel(mealMinutes);
                interactor.Activity.Begin("Cooking", mealMinutes, () =>
                {
                    interactor.Vitals.Eat(MealSatiety);
                    interactor.Vitals.Drink(MealHydration);
                    Notifications.Post("A hot meal. You feel much better.");
                });
            }, mealProblem));
        }
    }
}
