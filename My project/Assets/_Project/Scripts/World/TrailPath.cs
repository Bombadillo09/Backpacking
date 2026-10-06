using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.World
{
    /// <summary>The footpath linking the route's stops, as a line of points. The map draws it.</summary>
    public class TrailPath : MonoBehaviour
    {
        [SerializeField] Vector3[] points = System.Array.Empty<Vector3>();

        public IReadOnlyList<Vector3> Points => points;
    }
}
