using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Interaction
{
    /// <summary>Fallen wood lying on the ground that can be gathered for fires.</summary>
    public class FirewoodPickup : MonoBehaviour, IInteractable
    {
        [SerializeField, Min(1)] int pieces = 1;

        public string DisplayName => "Firewood";

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            options.Add(new InteractionOption(pieces == 1 ? "Pick up firewood" : $"Pick up firewood ({pieces})", () =>
            {
                interactor.Backpack.AddFirewood(pieces);
                Destroy(gameObject);
            }));
        }
    }
}
