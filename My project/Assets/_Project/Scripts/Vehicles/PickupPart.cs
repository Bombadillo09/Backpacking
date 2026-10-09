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
    /// door gets you in (to drive, or to ride along in co-op), the tailgate opens the truck bed (to pack, or see what's in it).
    /// </summary>
    public class PickupPart : MonoBehaviour, IInteractable
    {
        [SerializeField] Pickup pickup;
        [SerializeField] PickupPartKind kind;
        [Tooltip("For a door: -1 on the left (driver's) side, 1 on the right.")]
        [SerializeField] int side;

        public string DisplayName => kind == PickupPartKind.Door ? "Truck door" : "Tailgate";

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            if (pickup == null || pickup.Aboard)
                return;
            if (kind == PickupPartKind.Door)
                pickup.AddDoorOption(options, side);
            else
                pickup.AddBedOption(interactor, options);
        }
    }
}
