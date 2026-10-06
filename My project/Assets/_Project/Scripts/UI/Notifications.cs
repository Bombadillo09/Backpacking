using System;
using UnityEngine;

namespace Backpacking.UI
{
    /// <summary>Short messages for the player ("The fire has burned out"). The HUD displays them.</summary>
    public static class Notifications
    {
        public static event Action<string> Posted;

        public static void Post(string message) => Posted?.Invoke(message);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Posted = null;
    }
}
