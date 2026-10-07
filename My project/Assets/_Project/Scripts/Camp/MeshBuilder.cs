using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// Small helper for building gear meshes in code: lofted shells from rings of points, grids over a surface,
    /// round tubes and flat straps along paths. UVs are in metres times a tiling factor, so fabric textures keep a
    /// real-world scale across parts.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> vertices = new();
        readonly List<Vector2> uvs = new();
        readonly List<int> triangles = new();

        /// <summary>Texture repeats per metre.</summary>
        public float Tiling = 2f;

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A surface through rings of points (each the same count, going round the same way). Closed rings wrap round;
        /// UVs run along each ring by distance, and from ring to ring by distance too.
        /// </summary>
        public void Loft(IReadOnlyList<Vector3[]> rings, bool closed, bool flip = false)
        {
            int count = rings[0].Length, start = vertices.Count;
            int columns = closed ? count + 1 : count;
            float[] along = new float[columns];
            float v = 0f;
            for (int r = 0; r < rings.Count; r++)
            {
                if (r > 0)
                    v += Vector3.Distance(Centre(rings[r]), Centre(rings[r - 1])) + 0.001f;
                float u = 0f;
                for (int i = 0; i < columns; i++)
                {
                    Vector3 point = rings[r][i % count];
                    if (i > 0)
                        u += Vector3.Distance(point, rings[r][(i - 1) % count]);
                    vertices.Add(point);
                    uvs.Add(new Vector2(u, v) * Tiling);
                    along[i] = u;
                }
            }
            for (int r = 0; r < rings.Count - 1; r++)
            for (int i = 0; i < columns - 1; i++)
            {
                int a = start + r * columns + i, b = a + 1, c = a + columns, d = c + 1;
                if (flip)
                    triangles.AddRange(new[] { a, b, c, b, d, c });
                else
                    triangles.AddRange(new[] { a, c, b, b, c, d });
            }
        }

        static Vector3 Centre(Vector3[] ring)
        {
            Vector3 sum = Vector3.zero;
            foreach (Vector3 point in ring)
                sum += point;
            return sum / ring.Length;
        }

        /// <summary>A fan closing a ring (a cap): every point joined to the ring's centre, nudged by <paramref name="bulge"/>.</summary>
        public void Cap(Vector3[] ring, Vector3 bulge, bool flip = false)
        {
            int centre = vertices.Count;
            Vector3 middle = Centre(ring) + bulge;
            vertices.Add(middle);
            uvs.Add(new Vector2(middle.x, middle.z) * Tiling);
            for (int i = 0; i < ring.Length; i++)
            {
                vertices.Add(ring[i]);
                uvs.Add(new Vector2(ring[i].x, ring[i].z) * Tiling);
            }
            for (int i = 0; i < ring.Length; i++)
            {
                int a = centre + 1 + i, b = centre + 1 + (i + 1) % ring.Length;
                if (flip)
                    triangles.AddRange(new[] { centre, b, a });
                else
                    triangles.AddRange(new[] { centre, a, b });
            }
        }

        /// <summary>A round tube along a path, closed at both ends.</summary>
        public void Tube(IReadOnlyList<Vector3> path, float radius, int sides = 10)
        {
            var rings = new List<Vector3[]>();
            Vector3 up = Vector3.up;
            for (int k = 0; k < path.Count; k++)
            {
                Vector3 forward = (path[Mathf.Min(k + 1, path.Count - 1)] - path[Mathf.Max(k - 1, 0)]).normalized;
                Vector3 side = Vector3.Cross(forward, Mathf.Abs(Vector3.Dot(forward, up)) > 0.9f ? Vector3.right : up).normalized;
                Vector3 normal = Vector3.Cross(side, forward);
                var ring = new Vector3[sides];
                for (int s = 0; s < sides; s++)
                {
                    float angle = s / (float)sides * Mathf.PI * 2f;
                    ring[s] = path[k] + (side * Mathf.Cos(angle) + normal * Mathf.Sin(angle)) * radius;
                }
                rings.Add(ring);
            }
            Loft(rings, closed: true, flip: true);
            // Ends capped from both sides: cheap, and right whichever way the path runs.
            foreach (bool flip in new[] { false, true })
            {
                Cap(rings[0], Vector3.zero, flip);
                Cap(rings[^1], Vector3.zero, flip);
            }
        }

        /// <summary>A smooth ellipsoid (rounded body part), turned by <paramref name="rotation"/>.</summary>
        public void Ellipsoid(Vector3 centre, Vector3 radii, Quaternion rotation, int segments = 16)
        {
            var rings = new List<Vector3[]>();
            int stacks = Mathf.Max(4, segments / 2);
            for (int j = 1; j < stacks; j++)
            {
                float lat = Mathf.Lerp(-Mathf.PI / 2f, Mathf.PI / 2f, j / (float)stacks);
                var ring = new Vector3[segments];
                for (int i = 0; i < segments; i++)
                {
                    float lon = i / (float)segments * Mathf.PI * 2f;
                    var unit = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    ring[i] = centre + rotation * Vector3.Scale(unit, radii);
                }
                rings.Add(ring);
            }
            Loft(rings, closed: true);
            Cap(rings[0], rotation * new Vector3(0f, -radii.y * 0.07f, 0f), flip: true);
            Cap(rings[^1], rotation * new Vector3(0f, radii.y * 0.07f, 0f));
        }

        /// <summary>
        /// A flat strap (webbing or padding) along a path, its face turned toward <paramref name="outward"/> at each
        /// point; a box in section, <paramref name="width"/> by <paramref name="thickness"/>.
        /// </summary>
        public void Strap(IReadOnlyList<Vector3> path, System.Func<int, Vector3> outward, float width, float thickness)
        {
            var rings = new List<Vector3[]>();
            for (int k = 0; k < path.Count; k++)
            {
                Vector3 forward = (path[Mathf.Min(k + 1, path.Count - 1)] - path[Mathf.Max(k - 1, 0)]).normalized;
                Vector3 normal = Vector3.ProjectOnPlane(outward(k), forward).normalized;
                Vector3 side = Vector3.Cross(forward, normal).normalized;
                Vector3 p = path[k];
                rings.Add(new[]
                {
                    p + side * width / 2f + normal * thickness, p - side * width / 2f + normal * thickness,
                    p - side * width / 2f, p + side * width / 2f,
                });
            }
            Loft(rings, closed: true, flip: true);
            foreach (bool flip in new[] { false, true })
            {
                Cap(rings[0], Vector3.zero, flip);
                Cap(rings[^1], Vector3.zero, flip);
            }
        }

        /// <summary>A box (for buckles and the like), centred at <paramref name="centre"/> with the given axes and half-sizes.</summary>
        public void Box(Vector3 centre, Vector3 right, Vector3 up, Vector3 forward, Vector3 half)
        {
            Vector3 X = right * half.x, Y = up * half.y, Z = forward * half.z;
            Vector3[] bottom = { centre - X - Y - Z, centre + X - Y - Z, centre + X - Y + Z, centre - X - Y + Z };
            Vector3[] top = { centre - X + Y - Z, centre + X + Y - Z, centre + X + Y + Z, centre - X + Y + Z };
            Loft(new[] { bottom, top }, closed: true, flip: true);
            Cap(bottom, Vector3.zero);
            Cap(top, Vector3.zero, flip: true);
        }
    }
}
