using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// Whose camp gear this is, on a trip with friends. Gear without one is yours. Someone else's tent, chair, stove or
    /// snare can be used (sit in it, cook on it, shelter in it) but only its owner packs it away.
    /// </summary>
    public class CampOwner : MonoBehaviour
    {
        /// <summary>The owner's key (<see cref="LocalKey"/> in their game).</summary>
        public string key;
        public string ownerName;

        /// <summary>This player's own key, the same every time they play (set by co-op at start-up).</summary>
        public static string LocalKey { get; set; } = "";

        /// <summary>Marks <paramref name="gear"/> as belonging to someone (an empty key or this player's: yours).</summary>
        public static void Set(GameObject gear, string ownerKey, string name)
        {
            if (gear == null)
                return;
            var owner = gear.GetComponent<CampOwner>();
            if (owner == null)
                owner = gear.AddComponent<CampOwner>();
            owner.key = ownerKey ?? "";
            owner.ownerName = name ?? "";
        }

        /// <summary>The owner's key, or empty if it's this player's.</summary>
        public static string KeyOf(GameObject gear)
        {
            var owner = gear != null ? gear.GetComponent<CampOwner>() : null;
            return owner == null || string.IsNullOrEmpty(owner.key) || owner.key == LocalKey ? "" : owner.key;
        }

        /// <summary>True if it's a friend's, with their name for "It's Sam's tent".</summary>
        public static bool IsOthers(GameObject gear, out string name)
        {
            var owner = gear != null ? gear.GetComponent<CampOwner>() : null;
            bool others = owner != null && !string.IsNullOrEmpty(owner.key) && owner.key != LocalKey;
            name = others ? string.IsNullOrEmpty(owner.ownerName) ? "a friend" : owner.ownerName : null;
            return others;
        }

        /// <summary>Why this player can't pack it away ("It's Sam's: only they can pack it away"), or null.</summary>
        public static string PackProblem(GameObject gear) => IsOthers(gear, out string name) ? $"It's {name}'s: only they can pack it away" : null;
    }
}
