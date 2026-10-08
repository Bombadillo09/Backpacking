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

    /// <summary>
    /// The gravel road from home past the outdoor store to the trailhead, as a line of points, plus the
    /// places along it. The map draws it.
    /// </summary>
    public class RoadPath : MonoBehaviour
    {
        [SerializeField] Vector3[] points = Array.Empty<Vector3>();
        [SerializeField] RoadPlace[] places = Array.Empty<RoadPlace>();

        public IReadOnlyList<Vector3> Points => points;
        public IReadOnlyList<RoadPlace> Places => places;

        /// <summary>Horizontal distance in metres from a position to the middle of the road.</summary>
        public float DistanceTo(Vector3 position)
        {
            var at = new Vector2(position.x, position.z);
            float nearest = float.MaxValue;
            for (int i = 0; i < points.Length - 1; i++)
            {
                var a = new Vector2(points[i].x, points[i].z);
                var b = new Vector2(points[i + 1].x, points[i + 1].z);
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(at - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
                nearest = Mathf.Min(nearest, Vector2.Distance(at, a + ab * t));
            }
            return nearest;
        }
    }
}
