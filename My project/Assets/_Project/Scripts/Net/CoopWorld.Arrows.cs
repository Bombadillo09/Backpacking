using System.Collections.Generic;
using Backpacking.Hunting;
using Unity.Netcode;
using UnityEngine;

namespace Backpacking.Net
{
    /// <summary>
    /// Arrows: one loosed here flies in everyone's game (as a ghost there: it hurts nothing, the hit is this game's
    /// to decide, see the wildlife part), lands where it landed here, and whoever picks it up gets it; it's gone
    /// from everyone's game. Numbered by whoever shot it.
    /// </summary>
    public partial class CoopWorld
    {
        readonly Dictionary<int, Arrow> arrows = new();
        int nextArrow;

        void SpawnArrows()
        {
            Arrow.Loosed += OnLoosed;
            Arrow.Settled += OnSettled;
            Arrow.PickedUp += OnPickedUp;
        }

        void DespawnArrows()
        {
            Arrow.Loosed -= OnLoosed;
            Arrow.Settled -= OnSettled;
            Arrow.PickedUp -= OnPickedUp;
            arrows.Clear();
        }

        void OnLoosed(Arrow arrow)
        {
            arrow.NetId = (int)((NetworkManager.LocalClientId + 1) << 20) | ++nextArrow;
            arrows[arrow.NetId] = arrow;
            LoosedRpc(arrow.NetId, arrow.transform.position, arrow.Velocity);
        }

        [Rpc(SendTo.NotMe)]
        void LoosedRpc(int id, Vector3 from, Vector3 velocity) => arrows[id] = Arrow.ShootGhost(from, velocity, id);

        void OnSettled(Arrow arrow, bool lying)
        {
            if (arrow.NetId == 0)
                return;
            if (!lying)
                arrows.Remove(arrow.NetId);
            SettledRpc(arrow.NetId, lying, arrow.transform.position, arrow.transform.rotation);
        }

        [Rpc(SendTo.NotMe)]
        void SettledRpc(int id, bool lying, Vector3 position, Quaternion rotation)
        {
            if (!arrows.TryGetValue(id, out Arrow arrow) || arrow == null)
                return;
            if (lying)
                arrow.SettleAt(position, rotation);
            else
            {
                arrows.Remove(id);
                arrow.Vanish();
            }
        }

        void OnPickedUp(Arrow arrow)
        {
            if (arrow.NetId == 0)
                return;
            arrows.Remove(arrow.NetId);
            PickedUpRpc(arrow.NetId);
        }

        [Rpc(SendTo.NotMe)]
        void PickedUpRpc(int id)
        {
            if (arrows.TryGetValue(id, out Arrow arrow) && arrow != null)
                arrow.Vanish();
            arrows.Remove(id);
        }

        /// <summary>An arrow by its number (for the co-op test).</summary>
        public Arrow ArrowOf(int id) => arrows.TryGetValue(id, out Arrow arrow) ? arrow : null;
    }
}
