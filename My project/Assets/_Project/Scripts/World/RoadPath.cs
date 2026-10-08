using System;
using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.World
{
    /// <summary>A named spot along the road that the map labels, such as your home.</summary>
    [Serializable]
    public struct RoadPlace
    {
        public string name;
        public Vector3 position;
    }

    /// <summary>A level gravel lot beside the road: home, the store, the trailhead parking.</summary>
    [Serializable]
    public struct RoadLot
    {
        public Vector3 centre;
        public float radius;
    }

    /// <summary>
    /// The gravel road from home past the outdoor store to the trailhead, as a line of points, plus the
    /// places and lots along it. The map draws it; vehicles use it to tell how rough the ground is.
    /// </summary>
    public class RoadPath : MonoBehaviour
    {
        [SerializeField] Vector3[] points = Array.Empty<Vector3>();
        [SerializeField] RoadPlace[] places = Array.Empty<RoadPlace>();
        [SerializeField] RoadLot[] lots = Array.Empty<RoadLot>();
        [Tooltip("Metres from the middle of the road to the edge of the gravel.")]
        [SerializeField] float halfWidth = 2.6f;

        public IReadOnlyList<Vector3> Points => points;
        public IReadOnlyList<RoadPlace> Places => places;

        /// <summary>Horizontal distance in metres from a position to the middle of the road.</summary>
        public float DistanceTo(Vector3 position) => Vector2.Distance(Flat(position), Flat(NearestOnRoad(position)));

        /// <summary>The nearest point on the middle of the road.</summary>
        public Vector3 NearestOnRoad(Vector3 position)
        {
            Vector2 at = Flat(position);
            Vector3 best = points.Length > 0 ? points[0] : position;
            float nearest = float.MaxValue;
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 a = Flat(points[i]), b = Flat(points[i + 1]);
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(at - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
                float distance = Vector2.Distance(at, a + ab * t);
                if (distance < nearest)
                {
                    nearest = distance;
                    best = Vector3.Lerp(points[i], points[i + 1], t);
                }
            }
            return best;
        }

        /// <summary>
        /// Metres beyond the edge of the gravel, whether the road or a lot, and the direction (flat) back toward
        /// it. Zero on the gravel.
        /// </summary>
        public float OffRoad(Vector3 position, out Vector3 towardRoad)
        {
            Vector3 onRoad = NearestOnRoad(position);
            float beyond = Vector2.Distance(Flat(position), Flat(onRoad)) - halfWidth;
            Vector3 back = onRoad - position;
            foreach (RoadLot lot in lots)
            {
                float beyondLot = Vector2.Distance(Flat(position), Flat(lot.centre)) - lot.radius;
                if (beyondLot < beyond)
                {
                    beyond = beyondLot;
                    back = lot.centre - position;
                }
            }
            back.y = 0f;
            towardRoad = back.sqrMagnitude > 0.0001f ? back.normalized : Vector3.zero;
            return Mathf.Max(0f, beyond);
        }

        static Vector2 Flat(Vector3 position) => new(position.x, position.z);
    }
}
