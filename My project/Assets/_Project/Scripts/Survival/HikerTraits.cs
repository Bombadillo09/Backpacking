using UnityEngine;

namespace Backpacking.Survival
{
    /// <summary>
    /// Bonuses from the hiker's background, read by the systems they affect. All values are neutral
    /// (no effect) until a background is applied.
    /// </summary>
    public static class HikerTraits
    {
        /// <summary>Extra °C of warmth, as if wearing another layer.</summary>
        public static float InsulationBonus { get; set; }
        /// <summary>Multiplies the chance the rain puts out a match.</summary>
        public static float FireFailFactor { get; set; } = 1f;
        /// <summary>Multiplies how long you have to react to a bite.</summary>
        public static float BiteWindowFactor { get; set; } = 1f;
        /// <summary>Added to the chance a hooked fish stays on.</summary>
        public static float LandChanceBonus { get; set; }
        /// <summary>Multiplies berries picked.</summary>
        public static float ForageFactor { get; set; } = 1f;
        /// <summary>Multiplies the chance of getting sick from untreated water or risky food.</summary>
        public static float SicknessFactor { get; set; } = 1f;
        /// <summary>Multiplies walking speed.</summary>
        public static float SpeedFactor { get; set; } = 1f;

        public static void Reset()
        {
            InsulationBonus = 0f;
            FireFailFactor = 1f;
            BiteWindowFactor = 1f;
            LandChanceBonus = 0f;
            ForageFactor = 1f;
            SicknessFactor = 1f;
            SpeedFactor = 1f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reset();
    }
}
