using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.World
{
    /// <summary>What a pin on the map and compass marks.</summary>
    public enum PinKind
    {
        /// <summary>A friend on a co-op trip, where they are now.</summary>
        Friend,
        /// <summary>Somewhere someone pointed out (see UI.Pings).</summary>
        Ping,
    }

    /// <summary>One pin: a friend, or a pinged spot.</summary>
    public struct MapPin
    {
        public string id, label;
        public PinKind kind;
        public Vector3 position;
        public Color colour;
    }

    /// <summary>
    /// Pins for the map and compass beyond the fixed places: friends on a co-op trip (kept up to date by co-op) and
    /// spots pinged by anyone. Network-agnostic, like <see cref="OtherHikers"/>.
    /// </summary>
    public static class MapPins
    {
        static readonly List<MapPin> all = new();

        public static IReadOnlyList<MapPin> All => all;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        public static void Set(MapPin pin)
        {
            for (int i = 0; i < all.Count; i++)
                if (all[i].id == pin.id)
                {
                    all[i] = pin;
                    return;
                }
            all.Add(pin);
        }

        public static void Remove(string id) => all.RemoveAll(pin => pin.id == id);

        public static void RemoveAll(PinKind kind) => all.RemoveAll(pin => pin.kind == kind);
    }
}
