using System.Collections.Generic;
using Backpacking.Interaction;
using Backpacking.UI;
using UnityEngine;

namespace Backpacking.World
{
    /// <summary>
    /// An outdoor tap (the spigot on the side of the outdoor store): fill your water bottle with clean water before you
    /// set off, or have a drink. Tap water is safe, so it needs no boiling or filtering.
    /// </summary>
    public class WaterTap : MonoBehaviour, IInteractable
    {
        [SerializeField] float fillMinutes = 1f;
        [SerializeField] float drinkLitres = 0.3f;

        public string DisplayName => "Water tap";

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            var backpack = interactor.Backpack;
            float room = backpack.WaterCapacity - backpack.SafeWater;
            string problem = backpack.WaterCapacity <= 0f ? "You have no water bottle. The store sells them"
                : room <= 0.01f ? "Your bottle is full of clean water"
                : null;
            string label = backpack.UntreatedWater > 0.01f
                ? $"Pour out the untreated water and fill your bottle with clean water ({backpack.WaterCapacity:0.0} L)"
                : $"Fill your water bottle (+{room:0.0} L, clean)";
            options.Add(new InteractionOption(label, () =>
                interactor.Activity.Begin("Filling your bottle at the tap", fillMinutes, () =>
                {
                    float added = backpack.FillWithTapWater();
                    Notifications.Post($"Filled up: {backpack.SafeWater:0.0} L of clean water ({added:0.0} L added).", 3f);
                }), problem));
            options.Add(new InteractionOption("Drink from the tap", () => backpack.DrinkTapWater(drinkLitres)));
        }
    }
}
