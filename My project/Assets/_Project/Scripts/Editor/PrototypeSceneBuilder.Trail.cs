using System.Collections.Generic;
using System.IO;
using Backpacking.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// The footpath: a winding trail linking every stop on the route, running through dense woods with a
    /// few glades. The terrain is too coarse to paint a footpath (about 5 m per texel), so the trail is a
    /// thin strip of dirt laid on the ground, over a soft worn band painted into the terrain.
    /// Trees keep a few metres back so the canopy closes overhead, and the path itself is clear of brush.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        const float TrailPointSpacing = 12f;
        const float TrailFieldCell = 2.5f;
        const float TrailFieldReach = 160f;
        /// <summary>Tree trunks stand at least this far from the middle of the path.</summary>
        const float TrailTreeClearance = 2.6f;
        /// <summary>Dense woods this far either side of the path.</summary>
        const float TrailWoodsWidth = 110f;
        /// <summary>Open ground around trading posts, in metres.</summary>
        const float PostMeadowRadius = 70f;
        const float TrailStripSpacing = 0.75f;
        const float TrailTextureTile = 1.5f;

        class Trail
        {
            /// <summary>World (x, z) points along the path, about 12 m apart.</summary>
            public readonly List<Vector2> Points = new();
            public readonly List<Vector2> Posts = new();
            public float[] Distance;
            public int Size;

            /// <summary>Distance in metres from a world (x, z) to the path, smoothly interpolated.</summary>
            public float DistanceAt(Vector2 world)
            {
                float half = TerrainSize / 2f;
                float fx = (world.x + half) / TrailFieldCell, fz = (world.y + half) / TrailFieldCell;
                int x = Mathf.Clamp(Mathf.FloorToInt(fx), 0, Size - 2), z = Mathf.Clamp(Mathf.FloorToInt(fz), 0, Size - 2);
                float tx = Mathf.Clamp01(fx - x), tz = Mathf.Clamp01(fz - z);
                float bottom = Mathf.Lerp(Distance[z * Size + x], Distance[z * Size + x + 1], tx);
                float top = Mathf.Lerp(Distance[(z + 1) * Size + x], Distance[(z + 1) * Size + x + 1], tx);
                return Mathf.Lerp(bottom, top, tz);
            }
        }

        /// <summary>The trail for the scene being built; the biome and planting code read it.</summary>
        static Trail currentTrail;

        /// <summary>Distance from a normalised map position to the trail, or a long way if there's no trail.</summary>
        static float TrailDistance(float u, float v)
        {
            if (currentTrail == null)
                return float.MaxValue;
            float half = TerrainSize / 2f;
            return currentTrail.DistanceAt(new Vector2(u * TerrainSize - half, v * TerrainSize - half));
        }

        static float TrailDistance(Vector3 world) =>
            currentTrail == null ? float.MaxValue : currentTrail.DistanceAt(new Vector2(world.x, world.z));

        /// <summary>
        /// How much the trail turns the land around it into woods, 0–1: full forest near the path, with
        /// glades here and there and open ground around the trading posts.
        /// </summary>
        static float TrailWoods(float u, float v)
        {
            if (currentTrail == null)
                return 0f;
            float half = TerrainSize / 2f;
            var world = new Vector2(u * TerrainSize - half, v * TerrainSize - half);
            float band = 1f - Mathf.InverseLerp(TrailWoodsWidth * 0.6f, TrailWoodsWidth, currentTrail.DistanceAt(world));
            if (band <= 0f)
                return 0f;
            float glade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.72f, Mathf.PerlinNoise(u * 55f + 17f, v * 55f + 71f)));
            float nearPost = 0f;
            foreach (Vector2 post in currentTrail.Posts)
                nearPost = Mathf.Max(nearPost, 1f - Mathf.InverseLerp(PostMeadowRadius * 0.6f, PostMeadowRadius, Vector2.Distance(world, post)));
            return band * 0.95f * (1f - glade) * (1f - nearPost);
        }

        // ---------- Planning ----------

        static Trail PlanTrail(RouteLayout route)
        {
            var trail = new Trail();
            float half = TerrainSize / 2f;
            Vector2 StopXZ(RouteStop stop) => new(stop.U * TerrainSize - half, stop.V * TerrainSize - half);
            foreach (RouteStop stop in route.Stops)
                if (stop.Kind == Navigation.NavigationPointKind.TradingPost)
                    trail.Posts.Add(StopXZ(stop));

            for (int leg = 0; leg < route.Stops.Count - 1; leg++)
            {
                Vector2 a = StopXZ(route.Stops[leg]), b = StopXZ(route.Stops[leg + 1]);
                float length = Vector2.Distance(a, b);
                Vector2 side = new Vector2(-(b - a).y, (b - a).x) / length;
                int steps = Mathf.Max(2, Mathf.CeilToInt(length / TrailPointSpacing));
                // Wander more on long legs, but meet each cairn head-on.
                float amplitude = Mathf.Min(45f, length * 0.06f);
                for (int k = leg == 0 ? 0 : 1; k <= steps; k++)
                {
                    float t = k / (float)steps;
                    float wander = (Mathf.PerlinNoise(leg * 7.3f + 0.5f, t * length / 160f) * 2f - 1f)
                                   + 0.35f * (Mathf.PerlinNoise(leg * 3.1f + 9f, t * length / 45f) * 2f - 1f);
                    Vector2 point = Vector2.Lerp(a, b, t) + side * (wander * amplitude * Mathf.Sin(Mathf.PI * t));
                    trail.Points.Add(AroundLakes(point, route));
                }
            }

            // Ease out the corners, keeping the ends in place.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 1; i < trail.Points.Count - 1; i++)
                    trail.Points[i] = (trail.Points[i - 1] + trail.Points[i] * 2f + trail.Points[i + 1]) / 4f;

            BuildDistanceField(trail);
            return trail;
        }

        /// <summary>Pushes a point out to the shore if it's in a lake.</summary>
        static Vector2 AroundLakes(Vector2 point, RouteLayout route)
        {
            foreach (Lake lake in route.Lakes)
            {
                var centre = new Vector2(lake.Centre.x, lake.Centre.z);
                Vector2 offset = point - centre;
                float keepOut = lake.Radius + 5f;
                if (offset.magnitude < keepOut)
                    point = centre + (offset.sqrMagnitude > 0.01f ? offset.normalized : Vector2.right) * keepOut;
            }
            return point;
        }

        static void BuildDistanceField(Trail trail)
        {
            float half = TerrainSize / 2f;
            trail.Size = Mathf.CeilToInt(TerrainSize / TrailFieldCell) + 1;
            trail.Distance = new float[trail.Size * trail.Size];
            for (int i = 0; i < trail.Distance.Length; i++)
                trail.Distance[i] = TrailFieldReach;

            int reach = Mathf.CeilToInt(TrailFieldReach / TrailFieldCell);
            for (int i = 0; i < trail.Points.Count - 1; i++)
            {
                Vector2 a = trail.Points[i], b = trail.Points[i + 1];
                Vector2 ab = b - a;
                float lengthSquared = Mathf.Max(ab.sqrMagnitude, 0.0001f);
                int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) + half) / TrailFieldCell) - reach);
                int x1 = Mathf.Min(trail.Size - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + half) / TrailFieldCell) + reach);
                int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) + half) / TrailFieldCell) - reach);
                int z1 = Mathf.Min(trail.Size - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + half) / TrailFieldCell) + reach);
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var cell = new Vector2(x * TrailFieldCell - half, z * TrailFieldCell - half);
                    float t = Mathf.Clamp01(Vector2.Dot(cell - a, ab) / lengthSquared);
                    float distance = Vector2.Distance(cell, a + ab * t);
                    int index = z * trail.Size + x;
                    if (distance < trail.Distance[index])
                        trail.Distance[index] = distance;
                }
            }
        }

        // ---------- The dirt strip ----------

        /// <summary>Lays the path on the ground as strips of dirt, and records it for the map.</summary>
        static void CreateTrail(Terrain terrain, BiomeArtSettings art)
        {
            if (currentTrail == null || currentTrail.Points.Count < 2)
                return;

            string folder = GeneratedFolder + "/Trail";
            AssetDatabase.DeleteAsset(folder);
            EnsureFolder(folder);
            Material dirt = GetOrCreateTrailMaterial(art);

            var root = new GameObject("Trail");
            var path = root.AddComponent<TrailPath>();
            var points = new Vector3[currentTrail.Points.Count];
            for (int i = 0; i < points.Length; i++)
            {
                var world = new Vector3(currentTrail.Points[i].x, 0f, currentTrail.Points[i].y);
                world.y = terrain.SampleHeight(world) + terrain.transform.position.y;
                points[i] = world;
            }
            SetVector3Array(path, "points", points);

            List<(Vector2 position, Vector2 forward, float along)> samples = ResampleTrail(currentTrail.Points, TrailStripSpacing);
            const int perChunk = 500;
            int chunk = 0;
            for (int start = 0; start < samples.Count - 1; start += perChunk)
            {
                int end = Mathf.Min(samples.Count - 1, start + perChunk);
                Mesh mesh = BuildTrailStrip(terrain, samples, start, end);
                mesh.name = $"Trail{chunk}";
                AssetDatabase.CreateAsset(mesh, $"{folder}/Trail{chunk}.asset");

                var piece = new GameObject($"Trail {chunk}");
                piece.transform.SetParent(root.transform, false);
                piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                var meshRenderer = piece.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = dirt;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                chunk++;
            }
            Debug.Log($"Laid {samples.Count * TrailStripSpacing / 1000f:0.0} km of trail.");
        }

        /// <summary>Points every <paramref name="spacing"/> metres along a smooth curve through the trail points.</summary>
        static List<(Vector2, Vector2, float)> ResampleTrail(List<Vector2> points, float spacing)
        {
            var dense = new List<Vector2>();
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 p0 = points[Mathf.Max(0, i - 1)], p1 = points[i], p2 = points[i + 1], p3 = points[Mathf.Min(points.Count - 1, i + 2)];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / (spacing * 0.5f)));
                for (int s = 0; s < steps; s++)
                    dense.Add(CatmullRom(p0, p1, p2, p3, s / (float)steps));
            }
            dense.Add(points[^1]);

            var samples = new List<(Vector2, Vector2, float)>();
            float along = 0f, nextAt = 0f;
            for (int i = 0; i < dense.Count - 1; i++)
            {
                float length = Vector2.Distance(dense[i], dense[i + 1]);
                Vector2 forward = length > 0.0001f ? (dense[i + 1] - dense[i]) / length : Vector2.up;
                while (nextAt <= along + length)
                {
                    samples.Add((Vector2.Lerp(dense[i], dense[i + 1], (nextAt - along) / Mathf.Max(length, 0.0001f)), forward, nextAt));
                    nextAt += spacing;
                }
                along += length;
            }
            return samples;
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>
        /// A strip four vertices wide following the ground, so it bends over cross-slopes. Its width wanders
        /// and its edges are ragged, like a worn path.
        /// </summary>
        static Mesh BuildTrailStrip(Terrain terrain, List<(Vector2 position, Vector2 forward, float along)> samples, int start, int end)
        {
            float[] across = { -0.5f, -0.17f, 0.17f, 0.5f };
            float[] lift = { 0.025f, 0.04f, 0.04f, 0.025f };
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int i = start; i <= end; i++)
            {
                (Vector2 position, Vector2 forward, float along) = samples[i];
                var side = new Vector2(forward.y, -forward.x);
                float width = Mathf.Lerp(0.85f, 1.35f, Mathf.PerlinNoise(along * 0.03f, 4.2f));
                for (int k = 0; k < across.Length; k++)
                {
                    float offset = across[k] * width;
                    // Ragged edges.
                    if (k == 0 || k == across.Length - 1)
                        offset += (Mathf.PerlinNoise(along * 0.45f, k * 13.7f) - 0.5f) * 0.3f;
                    Vector2 xz = position + side * offset;
                    var world = new Vector3(xz.x, 0f, xz.y);
                    world.y = terrain.SampleHeight(world) + origin.y + lift[k];
                    vertices.Add(world);
                    float u = Mathf.Clamp01((world.x - origin.x) / data.size.x), v = Mathf.Clamp01((world.z - origin.z) / data.size.z);
                    normals.Add(data.GetInterpolatedNormal(u, v));
                    uvs.Add(new Vector2(offset / TrailTextureTile + 0.5f, along / TrailTextureTile));
                }
                if (i == start)
                    continue;
                int row = vertices.Count - across.Length, previous = row - across.Length;
                for (int k = 0; k < across.Length - 1; k++)
                {
                    triangles.AddRange(new[] { previous + k, row + k, previous + k + 1 });
                    triangles.AddRange(new[] { previous + k + 1, row + k, row + k + 1 });
                }
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Lit dirt using the terrain's own dirt texture, so the path matches the ground around it.</summary>
        static Material GetOrCreateTrailMaterial(BiomeArtSettings art)
        {
            Material material = GetOrCreateMaterial("TrailDirt", new Color(0.8f, 0.75f, 0.68f), 0.05f);
            if (art.dirt != null)
            {
                material.SetTexture("_BaseMap", art.dirt.diffuseTexture);
                material.SetTexture("_BumpMap", art.dirt.normalMapTexture);
                if (art.dirt.normalMapTexture != null)
                    material.EnableKeyword("_NORMALMAP");
                else
                    material.DisableKeyword("_NORMALMAP");
            }
            // Worn earth is a shade darker and browner than loose dirt.
            material.SetColor("_BaseColor", new Color(0.8f, 0.75f, 0.68f));
            EditorUtility.SetDirty(material);
            return material;
        }

        static void SetVector3Array(Object target, string fieldName, Vector3[] values) =>
            Modify(target, fieldName, property =>
            {
                property.arraySize = values.Length;
                for (int i = 0; i < values.Length; i++)
                    property.GetArrayElementAtIndex(i).vector3Value = values[i];
            });
    }
}
