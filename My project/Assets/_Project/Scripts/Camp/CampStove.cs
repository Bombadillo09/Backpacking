using System.Collections.Generic;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>A canister stove: quick, reliable cooking that burns gas from the backpack.</summary>
    public class CampStove : MonoBehaviour, IInteractable
    {
        [SerializeField] float minutesPerLitre = 5f;
        [SerializeField] float mealMinutes = 10f;
        [SerializeField] float gasGramsPerMinute = 1.2f;

        public string DisplayName => "Camp stove";

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            var backpack = interactor.Backpack;
            CampCooking.AddOptions(interactor, options, minutesPerLitre, mealMinutes, canSmoke: false,
                minutes => backpack.GasGrams < minutes * gasGramsPerMinute ? "Not enough gas" : null,
                minutes => backpack.TryUseGas(minutes * gasGramsPerMinute));

            options.Add(new InteractionOption("Pack up stove", () =>
            {
                backpack.HasStove = true;
                Destroy(gameObject);
            }));
        }
    }
}
