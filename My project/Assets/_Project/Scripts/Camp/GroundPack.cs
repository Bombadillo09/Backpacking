using System.Collections.Generic;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// Your backpack, set down on the ground. Look at it to put it back on or take the tent out of it.
    /// While it's down, the backpack screen only opens when you're beside it. Made by <see cref="PackHandling"/>.
    /// </summary>
    public class GroundPack : MonoBehaviour, IInteractable
    {
        public string DisplayName => "Your backpack";

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            PackHandling handling = PackHandling.Current;
            if (handling == null)
                return;
            options.Add(new InteractionOption("Put your pack on", handling.PickUp));
            if (interactor.Backpack.HasStove)
                options.Add(new InteractionOption("Take out the stove and set it up", () => interactor.Placer.BeginPlacement(CampItem.Stove)));
            if (interactor.Backpack.HasChair && interactor.Backpack.ChairInPack)
                options.Add(new InteractionOption("Take out the camp chair", () => interactor.Placer.BeginPlacement(CampItem.Chair)));
            if (interactor.Backpack.HasTent)
                options.Add(new InteractionOption("Take out the tent bag", () => handling.TakeOutTent(),
                    handling.TentBag != null ? "The tent bag is already out" : null));
            else if (handling.TentBag != null)
                options.Add(new InteractionOption("Pack the tent bag away", handling.PackTentBag, handling.TentBagProblem()));
        }

        /// <summary>Tints the bag panels (children named "Bag...") to the hiker's pack colour.</summary>
        public void SetColour(Color colour)
        {
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", colour);
            foreach (Renderer part in GetComponentsInChildren<Renderer>())
                if (part.name.StartsWith("Bag"))
                    part.SetPropertyBlock(tint);
        }
    }
}
