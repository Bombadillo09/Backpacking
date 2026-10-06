using System.Collections.Generic;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// The tent in its stuff sack, lying on the ground: out of the pack and ready to unpack where you want to
    /// pitch, or taken down and waiting to go back in. Made by <see cref="PackHandling"/>.
    /// </summary>
    public class TentBag : MonoBehaviour, IInteractable
    {
        public string DisplayName => "Tent bag";

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            PackHandling handling = PackHandling.Current;
            if (handling == null)
                return;
            options.Add(new InteractionOption("Unpack it and lay the tent out (choose where)", () =>
                interactor.Placer.BeginPlacement(CampItem.Tent)));
            options.Add(new InteractionOption("Put it back in your pack", handling.PackTentBag, handling.TentBagProblem()));
        }

        /// <summary>Colours the sack to match the tent's rainfly.</summary>
        public void SetModel(TentModel model)
        {
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", TentDesign.Of(model).Fly);
            foreach (Renderer part in GetComponentsInChildren<Renderer>())
                if (part.name.StartsWith("Sack"))
                    part.SetPropertyBlock(tint);
        }
    }
}
