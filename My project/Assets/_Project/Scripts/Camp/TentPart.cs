using System.Collections.Generic;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// Something inside a pitched tent you can look at while you're in it, the doorway or your bedding: it offers
    /// the tent's inside options. From outside, the tent's own collider is in the way, so these are only reached
    /// from within.
    /// </summary>
    public class TentPart : MonoBehaviour, IInteractable
    {
        public Tent tent;
        public string partName = "Tent";

        public string DisplayName => partName;

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            if (tent != null && tent.PlayerInside)
                tent.GetInsideOptions(interactor, options);
        }
    }
}
