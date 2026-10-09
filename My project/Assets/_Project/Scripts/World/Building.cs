using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.World
{
    /// <summary>
    /// A building you can walk into (home, the outdoor store). Its floors are boards underfoot, and inside it the
    /// weather and the woods are muffled. The inside is a box in the building's own space, set by the scene builder.
    /// </summary>
    public class Building : MonoBehaviour
    {
        [SerializeField] Vector3 interiorCentre = new(0f, 1.5f, 0f);
        [SerializeField] Vector3 interiorSize = new(8f, 3f, 7f);

        static readonly List<Building> all = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public bool Contains(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position) - interiorCentre;
            return Mathf.Abs(local.x) < interiorSize.x / 2f && Mathf.Abs(local.y) < interiorSize.y / 2f && Mathf.Abs(local.z) < interiorSize.z / 2f;
        }

        /// <summary>Inside any building.</summary>
        public static bool IsIndoors(Vector3 position)
        {
            foreach (Building building in all)
                if (building != null && building.Contains(position))
                    return true;
            return false;
        }

        /// <summary>Part of a building: its floor, a wall, the porch.</summary>
        public static bool IsPartOf(Collider collider) => collider != null && collider.GetComponentInParent<Building>() != null;
    }
}
