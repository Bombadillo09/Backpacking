using System;
using System.Collections.Generic;
using Backpacking.Interaction;
using Backpacking.Survival;
using Backpacking.UI;

namespace Backpacking.Camp
{
    /// <summary>The boiling, cooking and smoking options shared by the campfire and the stove.</summary>
    public static class CampCooking
    {
        public const float MealWaterLitres = 0.4f;
        const float MealSatiety = 35f;
        const float MealHydration = 8f;
        const float CookBaseMinutes = 8f;
        const float CookMinutesPerPiece = 4f;
        public const float SmokingHours = 3f;

        static readonly Func<FoodInfo, FoodKind?> Cookable = info => info.CooksInto;
        static readonly Func<FoodInfo, FoodKind?> Smokable = info => info.SmokesInto;

        /// <param name="minutesPerLitre">How long this heat source takes to boil a litre.</param>
        /// <param name="mealMinutes">How long a trail meal takes.</param>
        /// <param name="canSmoke">Whether food can be smoked here (only over a fire).</param>
        /// <param name="fuelProblem">Returns why a task of the given length can't run, or null if it can.</param>
        /// <param name="useFuel">Spends fuel for a task of the given length, when it starts.</param>
        public static void AddOptions(Interactor interactor, List<InteractionOption> options,
            float minutesPerLitre, float mealMinutes, bool canSmoke, Func<float, string> fuelProblem, Action<float> useFuel)
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

            string mealProblem = backpack.CountFood(FoodKind.TrailMeal) <= 0 ? "No dehydrated meals left"
                : backpack.TotalWater < MealWaterLitres ? $"Needs {MealWaterLitres:0.0} L of water"
                : fuelProblem(mealMinutes);
            options.Add(new InteractionOption($"Cook dehydrated meal ({mealMinutes:0} min)", () =>
            {
                backpack.TryTakeFood(FoodKind.TrailMeal);
                backpack.TryUseCookingWater(MealWaterLitres);
                useFuel(mealMinutes);
                interactor.Activity.Begin("Cooking", mealMinutes, () =>
                {
                    interactor.Vitals.Eat(MealSatiety);
                    interactor.Vitals.Drink(MealHydration);
                    Notifications.Post("A hot meal. You feel much better.");
                });
            }, mealProblem));

            // With friends round the fire: a meal each for everyone here, cooked together.
            List<World.OtherHiker> friends = World.OtherHikers.Within(interactor.transform.position, 8f);
            if (friends.Count > 0 && World.OtherHikers.Feed != null)
            {
                int meals = friends.Count + 1;
                float minutes = mealMinutes + 2f * friends.Count;
                string shareProblem = backpack.CountFood(FoodKind.TrailMeal) < meals ? $"Needs {meals} dehydrated meals"
                    : backpack.TotalWater < MealWaterLitres * meals ? $"Needs {MealWaterLitres * meals:0.0} L of water"
                    : fuelProblem(minutes);
                options.Add(new InteractionOption($"Cook a meal for everyone here ({meals} meals, {minutes:0} min)", () =>
                {
                    for (int i = 0; i < meals; i++)
                        backpack.TryTakeFood(FoodKind.TrailMeal);
                    backpack.TryUseCookingWater(MealWaterLitres * meals);
                    useFuel(minutes);
                    interactor.Activity.Begin("Cooking for everyone", minutes, () =>
                    {
                        interactor.Vitals.Eat(MealSatiety);
                        interactor.Vitals.Drink(MealHydration);
                        foreach (World.OtherHiker friend in friends)
                            World.OtherHikers.Feed?.Invoke(friend.id, MealSatiety, MealHydration);
                        Notifications.Post("Hot food all round.");
                    });
                }, shareProblem));
            }

            int raw = backpack.CountConvertible(Cookable);
            float cookMinutes = CookBaseMinutes + raw * CookMinutesPerPiece;
            options.Add(new InteractionOption($"Cook fish & meat ({raw} pieces, {cookMinutes:0} min)", () =>
            {
                useFuel(cookMinutes);
                interactor.Activity.Begin("Cooking fish & meat", cookMinutes, () =>
                {
                    int cooked = backpack.ConvertFood(Cookable);
                    Notifications.Post($"Cooked {cooked} pieces. Eat within a day or smoke them for the trail.");
                });
            }, raw == 0 ? "Nothing raw to cook" : fuelProblem(cookMinutes)));

            if (!canSmoke)
                return;

            int smokable = backpack.CountConvertible(Smokable);
            float smokeMinutes = SmokingHours * 60f;
            options.Add(new InteractionOption($"Smoke fish & meat for the trail ({smokable} pieces, {SmokingHours:0} h)", () =>
                interactor.Activity.Begin("Smoking fish & meat", smokeMinutes, () =>
                {
                    int smoked = backpack.ConvertFood(Smokable);
                    Notifications.Post($"Smoked {smoked} pieces. They'll keep for a week or more.");
                }), smokable == 0 ? "No fish or meat to smoke" : fuelProblem(smokeMinutes)));
        }
    }
}
