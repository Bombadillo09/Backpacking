using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.World
{
    /// <summary>Another player's hiker on a co-op trip, as far as the world needs to know: where they are and how they move.</summary>
    public struct OtherHiker
    {
        public ulong id;
        public Vector3 position;
        /// <summary>Which way they face, as a flat direction.</summary>
        public Vector3 forward;
        public float speed;
        public bool crouching;
        public bool sprinting;
        public string name;
    }

    /// <summary>
    /// The other hikers on a co-op trip (empty alone), kept up to date by co-op. Animals take fright at them, snares
    /// aren't touched while they're near, and wildlife lives around them as well as this player.
    /// </summary>
    public static class OtherHikers
    {
        static readonly List<OtherHiker> all = new();

        public static IReadOnlyList<OtherHiker> All => all;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            all.Clear();
            Feed = null;
        }

        public static void Set(OtherHiker hiker)
        {
            for (int i = 0; i < all.Count; i++)
                if (all[i].id == hiker.id)
                {
                    all[i] = hiker;
                    return;
                }
            all.Add(hiker);
        }

        public static void Remove(ulong id) => all.RemoveAll(hiker => hiker.id == id);

        public static void Clear() => all.Clear();

        /// <summary>
        /// Feeds a friend a share of a meal cooked here: (their id, food, drink). Set by co-op; null alone.
        /// </summary>
        public static System.Action<ulong, float, float> Feed;

        /// <summary>The friends within <paramref name="radius"/> metres.</summary>
        public static List<OtherHiker> Within(Vector3 position, float radius)
        {
            var near = new List<OtherHiker>();
            foreach (OtherHiker hiker in all)
                if ((hiker.position - position).sqrMagnitude < radius * radius)
                    near.Add(hiker);
            return near;
        }

        public static bool AnyWithin(Vector3 position, float radius)
        {
            foreach (OtherHiker hiker in all)
                if ((hiker.position - position).sqrMagnitude < radius * radius)
                    return true;
            return false;
        }
    }
}
