using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Survival
{
    /// <summary>Something that warms the air around it, like a campfire. Only active while enabled.</summary>
    public class HeatSource : MonoBehaviour
    {
        static readonly List<HeatSource> active = new();

        [Tooltip("°C added right next to the source.")]
        [SerializeField] float maxWarmth = 18f;
        [Tooltip("Distance in metres at which the warmth fades to nothing.")]
        [SerializeField] float radius = 5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => active.Clear();

        void OnEnable() => active.Add(this);
        void OnDisable() => active.Remove(this);

        /// <summary>Total extra warmth (°C) from all active sources at a position.</summary>
        public static float WarmthAt(Vector3 position)
        {
            float total = 0f;
            foreach (HeatSource source in active)
            {
                float distance = Vector3.Distance(position, source.transform.position);
                float falloff = 1f - Mathf.Clamp01(distance / source.radius);
                total += source.maxWarmth * falloff * falloff;
            }
            return total;
        }
    }
}
