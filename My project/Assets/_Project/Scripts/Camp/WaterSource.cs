using System.Collections.Generic;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>A lake or stream. Water from here is untreated until boiled.</summary>
    public class WaterSource : MonoBehaviour, IInteractable
    {
        [SerializeField] string displayName = "Lake";
        [SerializeField] float fillMinutes = 2f;
        [SerializeField] float drinkLitres = 0.25f;

        public string DisplayName => displayName;

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            var backpack = interactor.Backpack;
            float space = backpack.FreeWaterSpace;
            string treatment = backpack.HasWaterFilter ? "filtered" : "untreated";
            options.Add(new InteractionOption($"Fill water bottle (+{space:0.0} L {treatment})", () =>
                interactor.Activity.Begin("Filling water bottle", fillMinutes, () => backpack.FillFromSource()),
                space <= 0.01f ? "Bottle is full" : null));
            options.Add(new InteractionOption($"Drink straight from the water ({treatment})", () =>
                backpack.DrinkFromSource(drinkLitres)));
            // The rod lives in the pack, like the rest of the camp gear.
            PackHandling pack = PackHandling.Current;
            options.Add(new InteractionOption("Go fishing", interactor.Fishing.Begin,
                !backpack.HasFishingKit ? "You need a fishing kit"
                : pack != null && backpack.IsWorn ? "Take your pack off to get your fishing kit out"
                : pack != null && !pack.CanReachPack ? "Your fishing kit is in your pack, back there"
                : null));
        }
    }
}
