using Backpacking.Survival;

namespace Backpacking.Character
{
    /// <summary>What each background means: a name, a description, its lasting traits and its starting kit.</summary>
    public static class Backgrounds
    {
        public static readonly Background[] All =
        {
            Background.WeekendHiker, Background.Ranger, Background.Angler, Background.Ultralight, Background.Forager,
        };

        public static string Name(Background background) => background switch
        {
            Background.Ranger => "Ranger",
            Background.Angler => "Angler",
            Background.Ultralight => "Ultralight Hiker",
            Background.Forager => "Forager",
            _ => "Weekend Hiker",
        };

        public static string Description(Background background) => background switch
        {
            Background.Ranger => "Years outdoors in all weathers. Handles the cold better (+3 °C), the rain is half as likely to beat your matches, and you carry 8 extra matches.",
            Background.Angler => "Never without a rod. Starts with the telescopic rod, has 40% longer to react to a bite, and loses fewer fish.",
            Background.Ultralight => "Every gram counts. Walks 8% faster with a pack 0.8 kg lighter, but starts with one meal less, no fishing kit, and trail runners that tire the feet faster than boots.",
            Background.Forager => "Knows what's good to eat. Picks half as many berries again, is half as likely to get sick from untreated water or risky food, and carries 2 extra snares.",
            _ => "A balanced all-rounder with the standard kit. A good place to start.",
        };

        /// <summary>Lasting bonuses. Applied on every start and load.</summary>
        public static void ApplyTraits(Background background)
        {
            HikerTraits.Reset();
            switch (background)
            {
                case Background.Ranger:
                    HikerTraits.InsulationBonus = 3f;
                    HikerTraits.FireFailFactor = 0.5f;
                    break;
                case Background.Angler:
                    HikerTraits.BiteWindowFactor = 1.4f;
                    HikerTraits.LandChanceBonus = 0.05f;
                    break;
                case Background.Ultralight:
                    HikerTraits.SpeedFactor = 1.08f;
                    break;
                case Background.Forager:
                    HikerTraits.ForageFactor = 1.5f;
                    HikerTraits.SicknessFactor = 0.5f;
                    break;
            }
        }

        /// <summary>Changes to the standard kit. Applied once, when a new trip starts.</summary>
        public static void ApplyStartingKit(Background background, Backpack backpack)
        {
            switch (background)
            {
                case Background.Ranger:
                    backpack.AddMatches(8);
                    break;
                case Background.Angler:
                    backpack.AddFishingRod();
                    break;
                case Background.Ultralight:
                    backpack.ReducePackWeight(0.8f);
                    backpack.TryTakeFood(FoodKind.TrailMeal);
                    backpack.RemoveFishingKit();
                    backpack.SetBoots("Trail runners", 1.3f, false, 0f);
                    break;
                case Background.Forager:
                    backpack.AddSnare();
                    backpack.AddSnare();
                    break;
            }
        }
    }
}
