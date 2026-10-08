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
            Background.Ranger => "Years outdoors in all weathers. Handles the cold better (+3 °C) and the rain is half as likely to beat your matches. Brings an old machete from home.",
            Background.Angler => "Never without a rod: brings a telescopic rod from home, has 40% longer to react to a bite, and loses fewer fish.",
            Background.Ultralight => "Every gram counts. Walks 8% faster, and already owns a 40 L ultralight pack, so there's more money for the rest.",
            Background.Forager => "Knows what's good to eat. Picks half as many berries again, is half as likely to get sick from untreated water or risky food, and brings 3 wire snares from home.",
            _ => "A balanced all-rounder who brings an old fleece from home. A good place to start.",
        };

        /// <summary>
        /// The person from the roster who looks the part, for a man or a woman: picking a background in the
        /// creator switches to them (you can still step to anyone else).
        /// </summary>
        public static string DefaultHiker(Background background, bool female) => (background, female) switch
        {
            (Background.Ranger, false) => "Male_Adult_05",
            (Background.Ranger, true) => "Female_Adult_04",
            (Background.Angler, false) => "Male_Adult_07",
            (Background.Angler, true) => "Female_Adult_14",
            (Background.Ultralight, false) => "Male_Adult_18",
            (Background.Ultralight, true) => "Female_Adult_12",
            (Background.Forager, false) => "Wood_Male_01",
            (Background.Forager, true) => "Female_Adult_07",
            (_, false) => "Male_Adult_04",
            (_, true) => "Female_Adult_17",
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

        /// <summary>
        /// The one thing each background brings from home, applied once when a new trip starts: into the truck bed,
        /// or onto your back for a pack.
        /// </summary>
        public static void ApplyStartingKit(Background background, Backpack wearer, Backpack truckBed)
        {
            switch (background)
            {
                case Background.Ranger:
                    truckBed.AddMachete();
                    break;
                case Background.Angler:
                    truckBed.AddFishingRod();
                    break;
                case Background.Ultralight:
                    wearer.SetPack(PackModel.Ultralight40);
                    break;
                case Background.Forager:
                    for (int i = 0; i < 3; i++)
                        truckBed.AddSnare();
                    break;
                default:
                    Trade.ShopCatalog.Get(Trade.ShopItemId.Fleece).ApplyTo(truckBed);
                    break;
            }
        }
    }
}
