using System;
using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Navigation
{
    /// <summary>
    /// A named destination in the world. It appears on the map and reports when the player reaches it.
    /// Later these will host vendors.
    /// </summary>
    public class NavigationPoint : MonoBehaviour
    {
        static readonly List<NavigationPoint> all = new();

        /// <summary>Every active point in the scene.</summary>
        public static IReadOnlyList<NavigationPoint> All => all;

        /// <summary>Raised the first time the player comes within range of a point.</summary>
        public static event Action<NavigationPoint> Arrived;

        [SerializeField] string displayName = "Point";
        [Tooltip("Horizontal distance in metres that counts as arriving.")]
        [SerializeField] float arrivalRadius = 12f;
        [SerializeField] bool visited;

        Transform player;

        public string DisplayName => displayName;
        public bool Visited => visited;

        // Statics survive entering Play Mode when domain reload is turned off, so clear them explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            all.Clear();
            Arrived = null;
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        void Start()
        {
            GameObject playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null)
                player = playerObject.transform;
        }

        void Update()
        {
            if (visited || player == null)
                return;

            Vector3 offset = player.position - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= arrivalRadius * arrivalRadius)
            {
                visited = true;
                Arrived?.Invoke(this);
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = visited ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, arrivalRadius);
        }
    }
}
