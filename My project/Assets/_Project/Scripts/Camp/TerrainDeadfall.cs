using System.Collections.Generic;
using Backpacking.Interaction;
using Backpacking.UI;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// The fallen sticks and branches scattered over the forest floor are terrain details, which can't be picked
    /// up one by one. Looking at the ground where they lie offers to gather them: a piece of firewood or two,
    /// and they're gone from that spot. Added to the terrain by <see cref="GroundClearing"/>.
    /// </summary>
    public class TerrainDeadfall : MonoBehaviour, IInteractable
    {
        public GroundClearing Clearing { get; set; }

        public string DisplayName => "Fallen sticks";

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            if (Clearing == null)
                return;
            Vector3 spot = interactor.AimPoint;
            int sticks = Clearing.DeadfallNear(spot);
            if (sticks <= 0)
                return;
            int pieces = Mathf.Clamp(sticks, 1, 2);
            options.Add(new InteractionOption(pieces == 1 ? "Gather sticks for firewood" : $"Gather sticks for firewood ({pieces})", () =>
            {
                Clearing.GatherDeadfall(spot);
                interactor.Backpack.AddFirewood(pieces);
                Notifications.Post(pieces == 1 ? "You gather an armful of dry sticks." : $"You gather dry sticks: {pieces} pieces of firewood.", 2.5f);
            }));
        }
    }
}
