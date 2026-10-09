using System.Collections.Generic;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.Vehicles
{
    /// <summary>Which part of the truck you're looking at.</summary>
    public enum PickupPartKind
    {
        Door,
        Tailgate,
    }

    /// <summary>
    /// A door or the tailgate of the pickup, each with its one thing to do, so a single press of E does it: the
    /// door gets you in to drive, the tailgate opens the truck bed (to pack, or see what's in it).
    /// </summary>
    public class PickupPart : MonoBehaviour, IInteractable
    {
        [SerializeField] Pickup pickup;
        [SerializeField] PickupPartKind kind;

        public string DisplayName => kind == PickupPartKind.Door ? "Truck door" : "Tailgate";

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            if (pickup == null || pickup.Driving)
                return;
            if (kind == PickupPartKind.Door)
                pickup.AddDoorOption(options);
            else
                pickup.AddBedOption(interactor, options);
        }
    }
}
