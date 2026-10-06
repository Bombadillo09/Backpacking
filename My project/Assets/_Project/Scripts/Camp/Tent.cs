using System.Collections.Generic;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>A pitched tent: sleep in it, or pack it back into the backpack.</summary>
    public class Tent : MonoBehaviour, IInteractable
    {
        [SerializeField] float packUpMinutes = 10f;

        public string DisplayName => "Tent";

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            options.Add(new InteractionOption("Sleep", () => interactor.Activity.Sleep(inTent: true)));
            options.Add(new InteractionOption($"Pack up tent ({packUpMinutes:0} min)", () =>
                interactor.Activity.Begin("Packing up tent", packUpMinutes, () =>
                {
                    interactor.Backpack.HasTent = true;
                    Destroy(gameObject);
                })));
        }
    }
}
