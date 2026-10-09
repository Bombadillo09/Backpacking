using Backpacking.UI;
using Unity.Netcode;
using UnityEngine;

namespace Backpacking.Net
{
    /// <summary>Pings: what anyone points out (see UI.Pings) shows in everyone's game.</summary>
    public partial class CoopWorld
    {
        void SpawnPings() => Pings.Placed += OnPinged;

        void DespawnPings()
        {
            Pings.Placed -= OnPinged;
            World.MapPins.RemoveAll(World.PinKind.Ping);
        }

        void OnPinged(Vector3 position, string label) => PingRpc(position, label, LocalName());

        [Rpc(SendTo.NotMe)]
        void PingRpc(Vector3 position, string label, string who)
        {
            if (Pings.Current != null)
                Pings.Current.Add(who, label, position);
        }
    }
}
