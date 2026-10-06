using System;
using UnityEngine;

namespace Backpacking.UI
{
    /// <summary>Short messages for the player ("The fire has burned out"). The HUD displays them.</summary>
    public static class Notifications
    {
        /// <summary>The message and how many seconds to show it for.</summary>
        public static event Action<string, float> Posted;

        public static void Post(string message, float seconds = 5f) => Posted?.Invoke(message, seconds);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Posted = null;
    }
}
