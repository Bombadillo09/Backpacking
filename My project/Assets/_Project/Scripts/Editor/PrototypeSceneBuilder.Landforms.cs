using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Landmarks to look at from the trail: rounded hills, flat-topped rock mesas with cliff walls and a slope of
    /// scree at their feet, and long cliff bands where the land steps up. They stand off to either side of the trail
    /// (never on it, the road, the lots, a lake or a river), within sight of it. Their steep faces are rock, and big
    /// boulders are set into them so they read as crags rather than steep grass.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        const int HillCount = 26, MesaCount = 12, CliffBandCount = 12;
        const int CliffRockLimit = 900;

        /// <summary>Raises the landforms in the heights. After the trail and the rivers' courses are planned, before the channels are cut.</summary>
        static void ShapeLandforms(float[,] heights, RouteLayout route)
        {
            if (currentTrail == null || currentTrail.Points.Count < 2)
                return;
            var random = new System.Random(Seed + 41);
            float Random01() => (float)random.NextDouble();
            int resolution = heights.GetLength(0);
            float metresPerSample = TerrainSize / (resolution - 1);
            float half = TerrainSize / 2f;

            // A spot off to one side of the trail, clear of everything by its size plus a margin.
            bool Spot(float size, out Vector2 centre)
            {
                centre = default;
                Vector2 point = currentTrail.Points[random.Next(currentTrail.Points.Count)];
                int index = currentTrail.Points.IndexOf(point);
                Vector2 next = currentTrail.Points[Mathf.Min(currentTrail.Points.Count - 1, index + 1)];
                Vector2 forward = (next - point).sqrMagnitude > 0.01f ? (next - point).normalized : Vector2.up;
                Vector2 side = new(-forward.y, forward.x);
                centre = point + side * ((Random01() < 0.5f ? -1f : 1f) * (size + 70f + Random01() * 420f));
                float u = (centre.x + half) / TerrainSize, v = (centre.y + half) / TerrainSize;
                if (u is < 0.04f or > 0.96f || v is < 0.04f or > 0.96f)
                    return false;
                return Clear(centre, size + 45f, route);
            }

            void Raise(Vector2 centre, float reach, System.Func<Vector2, float> metres)
            {
                int cx = Mathf.RoundToInt((centre.x + half) / metresPerSample), cz = Mathf.RoundToInt((centre.y + half) / metresPerSample);
                int cells = Mathf.CeilToInt(reach / metresPerSample);
                for (int z = Mathf.Max(0, cz - cells); z <= Mathf.Min(resolution - 1, cz + cells); z++)
                for (int x = Mathf.Max(0, cx - cells); x <= Mathf.Min(resolution - 1, cx + cells); x++)
                {
                    var sample = new Vector2(x * metresPerSample - half, z * metresPerSample - half);
                    float add = metres(sample);
                    if (add > 0f)
                        heights[z, x] = Mathf.Clamp01(heights[z, x] + add / TerrainHeight);
                }
            }

            // Rounded hills, lumpy rather than perfect domes.
            for (int i = 0, tries = 0; i < HillCount && tries < HillCount * 20; tries++)
            {
                float radius = 90f + Random01() * 170f, height = 14f + Random01() * 38f;
                if (!Spot(radius * 1.3f, out Vector2 centre))
                    continue;
                float seed = Random01() * 100f;
                Raise(centre, radius * 1.6f, sample =>
                {
                    float d = Vector2.Distance(sample, centre) / radius;
                    float lumps = 0.8f + 0.4f * Mathf.PerlinNoise(sample.x * 0.012f + seed, sample.y * 0.012f + seed);
                    return height * Mathf.Exp(-d * d * 2.2f) * lumps;
                });
                i++;
            }

            // Mesas: a flat rocky top on cliff walls, its outline ragged, scree heaped at the foot.
            for (int i = 0, tries = 0; i < MesaCount && tries < MesaCount * 20; tries++)
            {
                float radius = 40f + Random01() * 70f, height = 14f + Random01() * 26f;
                if (!Spot(radius * 1.5f, out Vector2 centre))
                    continue;
                float seed = Random01() * 100f, wall = 5f + Random01() * 4f;
                Raise(centre, radius * 1.6f + 30f, sample =>
                {
                    Vector2 offset = sample - centre;
                    float angle = Mathf.Atan2(offset.y, offset.x);
                    // The edge wanders in and out round the mesa.
                    float edge = radius * (0.85f + 0.3f * Mathf.PerlinNoise(Mathf.Cos(angle) * 1.6f + seed, Mathf.Sin(angle) * 1.6f + seed));
                    float d = offset.magnitude;
                    float top = 1f - Mathf.SmoothStep(0f, 1f, (d - edge) / wall);
                    float scree = 0.28f * (1f - Mathf.SmoothStep(0f, 1f, (d - edge - wall) / 26f));
                    float crown = 1f + 0.06f * (Mathf.PerlinNoise(sample.x * 0.05f + seed, sample.y * 0.05f) - 0.5f);
                    return height * Mathf.Max(top * crown, scree);
                });
                i++;
            }

            // Cliff bands: the land steps up along a line, a rock wall on the low side, easing back down behind.
            for (int i = 0, tries = 0; i < CliffBandCount && tries < CliffBandCount * 20; tries++)
            {
                float length = 140f + Random01() * 260f, height = 10f + Random01() * 22f;
                if (!Spot(length * 0.6f, out Vector2 centre))
                    continue;
                float heading = Random01() * Mathf.PI;
                var along = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading));
                var across = new Vector2(-along.y, along.x);
                float seed = Random01() * 100f, wall = 5f + Random01() * 4f;
                Raise(centre, length * 0.75f + 180f, sample =>
                {
                    Vector2 offset = sample - centre;
                    float a = Vector2.Dot(offset, along), c = Vector2.Dot(offset, across);
                    // A wavering face, fading out at each end.
                    c += (Mathf.PerlinNoise(a * 0.015f + seed, seed) - 0.5f) * 30f;
                    float ends = 1f - Mathf.SmoothStep(0f, 1f, (Mathf.Abs(a) - length / 2f) / 70f);
                    float step = Mathf.SmoothStep(0f, 1f, (c + wall / 2f) / wall);
                    float behind = 1f - Mathf.SmoothStep(0f, 1f, (c - 40f) / 140f);
                    float scree = 0.22f * (1f - Mathf.SmoothStep(0f, 1f, (-c - wall) / 22f)) * (c < 0f ? 1f : 0f);
                    return height * ends * Mathf.Max(step * behind, scree);
                });
                i++;
            }
        }

        /// <summary>Clear of the trail, the road, the lots, the stops, the lakes and the rivers by <paramref name="margin"/> metres.</summary>
        static bool Clear(Vector2 centre, float margin, RouteLayout route)
        {
            float half = TerrainSize / 2f;
            if (DistanceToPolyline(centre, currentTrail.Points) < margin)
                return false;
            if (currentRoad != null && DistanceToPolyline(centre, currentRoad.Points) < margin)
                return false;
            if (InsideLot(centre) > -margin)
                return false;
            foreach (RouteStop stop in route.Stops)
                if (Vector2.Distance(centre, new Vector2(stop.U * TerrainSize - half, stop.V * TerrainSize - half)) < margin + 30f)
                    return false;
            foreach (Lake lake in route.Lakes)
                if (Vector2.Distance(centre, new Vector2(lake.Centre.x, lake.Centre.z)) < margin + lake.Radius)
                    return false;
            foreach (River river in currentRivers)
                if (DistanceToPolyline(centre, river.Points) < margin + river.HalfWidth + river.Bank)
                    return false;
            return true;
        }

        static float DistanceToPolyline(Vector2 point, List<Vector2> line)
        {
            float best = float.MaxValue;
            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector2 a = line[i], ab = line[i + 1] - a;
                float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
                best = Mathf.Min(best, Vector2.Distance(point, a + ab * t));
            }
            return best;
        }

        /// <summary>
        /// Big boulders set into the steep faces within sight of the trail (the cliffs of the mesas and bands, the
        /// gorges, the mountain crags), so they look like rock outcrops. The terrain paints them rock already.
        /// </summary>
        static void DressCliffs(Terrain terrain, RouteLayout route, BiomeArtSettings art)
        {
            if (IsEmpty(art.boulders) || currentTrail == null)
                return;
            var random = new System.Random(Seed + 43);
            var parent = new GameObject("Crags").transform;
            TerrainData data = terrain.terrainData;
            float half = TerrainSize / 2f;
            const float Step = 7f;
            int placed = 0;
            for (float z = -half + Step; z < half - Step && placed < CliffRockLimit; z += Step)
            for (float x = -half + Step; x < half - Step && placed < CliffRockLimit; x += Step)
            {
                float u = (x + half) / TerrainSize, v = (z + half) / TerrainSize;
                float steepness = data.GetSteepness(u, v);
                if (steepness < 40f || random.NextDouble() > 0.3)
                    continue;
                var world = new Vector2(x + ((float)random.NextDouble() - 0.5f) * Step, z + ((float)random.NextDouble() - 0.5f) * Step);
                // In sight of the trail, not on it, and out of the water.
                float fromTrail = DistanceToPolyline(world, currentTrail.Points);
                if (fromTrail > 800f || fromTrail < 12f || RiverEdgeDistance(world) < 2f)
                    continue;
                var position = new Vector3(world.x, 0f, world.y);
                if (TooCloseToFeature(position, route, terrain))
                    continue;
                float scale = Mathf.Lerp(2.4f, 6f, (float)random.NextDouble());
                // Half buried in the slope, leaning with it, pushed back into the face so it doesn't hang off it.
                Vector3 normal = data.GetInterpolatedNormal(u, v);
                position -= new Vector3(normal.x, 0f, normal.z) * (0.45f * scale);
                position.y = terrain.SampleHeight(position) + terrain.transform.position.y - 0.35f * scale;
                Quaternion lean = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, normal, 0.6f)) * Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                var rock = (GameObject)PrefabUtility.InstantiatePrefab(art.boulders[random.Next(art.boulders.Length)], parent);
                rock.transform.SetPositionAndRotation(position, lean);
                rock.transform.localScale = new Vector3(scale, scale * Mathf.Lerp(0.6f, 1.1f, (float)random.NextDouble()), scale);
                placed++;
            }
        }
    }
}
