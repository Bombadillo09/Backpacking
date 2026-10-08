namespace Backpacking.Survival
{
    /// <summary>Which backpack you carry. Saves store this by number: new models go at the end.</summary>
    public enum PackModel
    {
        None,
        Ultralight40,
        Trekking55,
        Expedition70,
    }

    /// <summary>What a backpack model holds and how it carries.</summary>
    public sealed class PackInfo
    {
        public string Name;
        public string Description;
        /// <summary>Total volume, in litres: the main compartment plus the lid and side pockets.</summary>
        public float Litres;
        public float LidLitres;
        public float PocketLitres;
        /// <summary>How many things can be strapped on the outside.</summary>
        public int Straps;
        /// <summary>The empty pack, in kilograms.</summary>
        public float Weight;
        /// <summary>Up to this load (kg) it carries comfortably; the hip belt and frame take the weight.</summary>
        public float ComfortableLoad;
        /// <summary>Above this (kg) you're overloaded.</summary>
        public float MaxLoad;
        /// <summary>Size of the pack on your back compared with the 55 L trekking pack.</summary>
        public float VisualScale;

        public float MainLitres => Litres - LidLitres - PocketLitres;
    }

    public static class PackModels
    {
        static readonly PackInfo none = new()
        {
            Name = "No pack",
            Description = "Without a pack you can only carry what you wear.",
            ComfortableLoad = 6f,
            MaxLoad = 10f,
            VisualScale = 1f,
        };

        static readonly PackInfo ultralight = new()
        {
            Name = "Ultralight 40 L pack",
            Description = "A frameless roll-top: very light, but no lid and only two straps outside, and it gets uncomfortable over about 10 kg.",
            Litres = 40f,
            LidLitres = 0f,
            PocketLitres = 3f,
            Straps = 2,
            Weight = 0.6f,
            ComfortableLoad = 10f,
            MaxLoad = 18f,
            VisualScale = 0.88f,
        };

        static readonly PackInfo trekking = new()
        {
            Name = "Trekking 55 L pack",
            Description = "An internal-frame pack with a padded hip belt, a lid pocket and side pockets: comfortable up to about 15 kg.",
            Litres = 55f,
            LidLitres = 5f,
            PocketLitres = 3f,
            Straps = 3,
            Weight = 1.4f,
            ComfortableLoad = 15f,
            MaxLoad = 25f,
            VisualScale = 1f,
        };

        static readonly PackInfo expedition = new()
        {
            Name = "Expedition 70 L pack",
            Description = "A heavy-duty frame pack for big loads: comfortable up to about 19 kg, with room for winter gear, but heavy itself.",
            Litres = 70f,
            LidLitres = 6f,
            PocketLitres = 4f,
            Straps = 4,
            Weight = 2.2f,
            ComfortableLoad = 19f,
            MaxLoad = 30f,
            VisualScale = 1.12f,
        };

        public static PackInfo Get(PackModel model) => model switch
        {
            PackModel.Ultralight40 => ultralight,
            PackModel.Trekking55 => trekking,
            PackModel.Expedition70 => expedition,
            _ => none,
        };
    }
}
