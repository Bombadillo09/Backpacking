using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Player
{
    /// <summary>
    /// Lets menus and timed activities take control away from the player. Each system locks with
    /// itself as the owner and unlocks when done, so overlapping locks don't release each other.
    /// </summary>
    public static class PlayerControlLock
    {
        static readonly HashSet<object> movementOwners = new();
        static readonly HashSet<object> cursorOwners = new();

        /// <summary>Movement, looking and jumping are disabled.</summary>
        public static bool MovementLocked => movementOwners.Count > 0;

        /// <summary>A menu needs the mouse cursor free.</summary>
        public static bool CursorNeeded => cursorOwners.Count > 0;

        public static void Lock(object owner, bool needsCursor)
        {
            movementOwners.Add(owner);
            if (needsCursor)
                cursorOwners.Add(owner);
        }

        public static void Unlock(object owner)
        {
            if (movementOwners.Remove(owner))
                lastReleaseFrame = Time.frameCount;
            cursorOwners.Remove(owner);
        }

        static int lastReleaseFrame = -1;

        /// <summary>
        /// A lock was released this frame. The key press that closed one screen shouldn't also open another,
        /// so input handlers skip this frame.
        /// </summary>
        public static bool JustReleased => Time.frameCount == lastReleaseFrame;

        public static bool IsLockedBy(object owner) => movementOwners.Contains(owner);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            movementOwners.Clear();
            cursorOwners.Clear();
            lastReleaseFrame = -1;
        }
    }
}
