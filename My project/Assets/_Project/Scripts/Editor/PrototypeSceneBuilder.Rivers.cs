using System.Collections.Generic;
using System.IO;
using Backpacking.Camp;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Rivers and streams. A river runs down out of the mountains into the lake at Valley Crossing, and out of the
    /// lake again off the edge of the map; streams come down off the high ground into them. Each finds the easy way
    /// downhill across the land (a cheapest path that hates climbing, and keeps off the road and the lots), its water
    /// only ever runs downhill, and its channel is cut into the ground with banks either side: through a rise, that's
    /// a little gorge. The water is real: a surface you see down into, a trigger to wade into (it slows you and
    /// soaks you), a source to fill your bottle from. Where the trail crosses, there's a footbridge.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        const float RiverPathCell = 20f;
        const float RiverSampleSpacing = 4f;

        sealed class River
        {
            public string Name;
            public float HalfWidth, Depth, Bank, Drop, Flow;
            /// <summary>World (x, z) points from source to mouth, about 4 m apart, and the water surface height at each.</summary>
            public List<Vector2> Points = new();
            public List<float> Surface = new();
            /// <summary>How wide each bank is at each point: wider where the channel is cut deep, so it's a valley, not a slot.</summary>
            public List<float> Banks = new();
            /// <summary>Where it starts and ends, if a lake: its water level holds the river's.</summary>
            public float? FromLake, IntoLake;
            /// <summary>The river a stream runs into, and where along it.</summary>
            public River IntoRiver;
            public int JoinsAt;
        }

        sealed class Bridge
        {
            public Vector2 Centre, Along;
            public float Length, Surface;
        }

        static readonly List<River> currentRivers = new();
        static readonly List<Bridge> currentBridges = new();
        /// <summary>Distance to the nearest water's edge of any river or stream (negative in the water), on the trail's grid.</summary>
        static Trail riverEdges;

        /// <summary>How far a world (x, z) is from the edge of the nearest river or stream: negative in the water.</summary>
        static float RiverEdgeDistance(Vector2 world) => riverEdges == null ? float.MaxValue : riverEdges.DistanceAt(world);

        static float RiverEdgeDistance(float u, float v)
        {
            float half = TerrainSize / 2f;
            return RiverEdgeDistance(new Vector2(u * TerrainSize - half, v * TerrainSize - half));
        }

        static bool NearBridge(Vector2 world, float margin)
        {
            foreach (Bridge bridge in currentBridges)
                if (Vector2.Distance(world, bridge.Centre) < bridge.Length / 2f + margin)
                    return true;
            return false;
        }

        // ---------- Planning the courses ----------

        /// <summary>Lays out where the rivers and streams run (not yet cut into the ground). After the trail is planned.</summary>
        static void PlanRivers(float[,] heights, RouteLayout route)
        {
            currentRivers.Clear();
            currentBridges.Clear();
            riverEdges = null;
            var grid = new RiverGrid(heights, route);
            float half = TerrainSize / 2f;

            // The valley lake at Valley Crossing gathers the river; find it among the route's lakes.
            RouteStop valleyStop = route.Stops.Find(stop => stop.Name == "Valley Crossing");
            int valleyLake = -1;
            for (int i = 0; i < route.Lakes.Count && valleyStop != null; i++)
                if (Vector2.Distance(new Vector2(route.Lakes[i].Centre.x, route.Lakes[i].Centre.z),
                        new Vector2(valleyStop.U * TerrainSize - half, valleyStop.V * TerrainSize - half)) < 200f)
                    valleyLake = i;
            if (valleyLake < 0)
                return;
            Lake lake = route.Lakes[valleyLake];
            var lakeCentre = new Vector2(lake.Centre.x, lake.Centre.z);

            // The river: from the lowest ground high in the mountains, off the trail, down into the lake.
            Vector2Int? source = grid.LowestIn(0.83f, 0.9f, 0.3f, 0.75f, 180f);
            if (source != null)
            {
                List<Vector2> course = grid.Cheapest(source.Value, cell => Vector2.Distance(grid.World(cell), lakeCentre) < lake.Radius - 4f, valleyLake);
                if (course != null)
                    currentRivers.Add(Course("River", course, heights, route, halfWidth: 5.5f, depth: 1.0f, bank: 9f, drop: 0.9f, flow: 0.7f,
                        intoLake: lake));
            }
            // And out of the lake again, west or east off the edge of the map.
            {
                Vector2Int start = grid.Cell(lakeCentre);
                List<Vector2> course = grid.Cheapest(start, cell => cell.x <= 1 || cell.x >= grid.Size - 2, valleyLake);
                if (course != null)
                    currentRivers.Add(Course("River", course, heights, route, halfWidth: 6.5f, depth: 1.1f, bank: 9f, drop: 0.9f, flow: 0.6f,
                        fromLake: lake));
            }

            // Streams: off the hills on either side, down into the rivers.
            var random = new System.Random(Seed + 31);
            var mainRivers = new List<River>(currentRivers);
            int streams = 0;
            for (int attempt = 0; attempt < 400 && streams < 6 && mainRivers.Count > 0; attempt++)
            {
                var cell = new Vector2Int(random.Next(4, grid.Size - 4), random.Next(Mathf.RoundToInt(grid.Size * 0.2f), Mathf.RoundToInt(grid.Size * 0.82f)));
                Vector2 world = grid.World(cell);
                if (grid.Blocked(cell) || TrailDistance(new Vector3(world.x, 0f, world.y)) < 120f)
                    continue;
                // High enough above a river some way off, but not too far.
                (River river, int index, float distance) = NearestRiverPoint(mainRivers, world);
                if (distance < 280f || distance > 950f || grid.Height(cell) < SampleHeightMetres(heights, river.Points[index]) + 18f)
                    continue;
                bool crowded = false;
                foreach (River other in currentRivers)
                    if (other.IntoRiver != null && Vector2.Distance(other.Points[0], world) < 400f)
                        crowded = true;
                if (crowded)
                    continue;
                List<Vector2> course = grid.Cheapest(cell, target => NearestRiverPoint(mainRivers, grid.World(target)).distance < 10f, -1);
                if (course == null || course.Count < 8)
                    continue;
                (River into, int joins, _) = NearestRiverPoint(mainRivers, course[^1]);
                River stream = Course("Stream", course, heights, route, halfWidth: 2.2f, depth: 0.45f, bank: 4.5f, drop: 0.45f, flow: 1f);
                if (stream == null)
                    continue;
                stream.IntoRiver = into;
                stream.JoinsAt = joins;
                currentRivers.Add(stream);
                streams++;
            }
        }

        static (River river, int index, float distance) NearestRiverPoint(List<River> rivers, Vector2 world)
        {
            (River river, int index, float distance) best = (null, 0, float.MaxValue);
            foreach (River river in rivers)
                for (int i = 0; i < river.Points.Count; i += 2)
                {
                    float distance = Vector2.Distance(river.Points[i], world);
                    if (distance < best.distance)
                        best = (river, i, distance);
                }
            return best;
        }

        /// <summary>A river along a cheapest path: smoothed, given a wander, resampled, and trimmed where it meets lakes.</summary>
        static River Course(string name, List<Vector2> path, float[,] heights, RouteLayout route, float halfWidth, float depth, float bank, float drop, float flow,
            Lake? fromLake = null, Lake? intoLake = null)
        {
            // Round off the grid's corners.
            for (int pass = 0; pass < 3; pass++)
            {
                var smoothed = new List<Vector2> { path[0] };
                for (int i = 0; i < path.Count - 1; i++)
                {
                    smoothed.Add(Vector2.Lerp(path[i], path[i + 1], 0.25f));
                    smoothed.Add(Vector2.Lerp(path[i], path[i + 1], 0.75f));
                }
                smoothed.Add(path[^1]);
                path = smoothed;
            }
            var river = new River { Name = name, HalfWidth = halfWidth, Depth = depth, Bank = bank, Drop = drop, Flow = flow };
            List<(Vector2 position, Vector2 forward, float along)> samples = ResampleTrail(path, RiverSampleSpacing);
            float seed = path[0].x * 0.013f;
            foreach ((Vector2 position, Vector2 forward, float along) in samples)
            {
                // A gentle meander either side of the course.
                Vector2 side = new(-forward.y, forward.x);
                Vector2 point = position + side * ((Mathf.PerlinNoise(seed, along / 90f) * 2f - 1f) * halfWidth * 1.6f);
                // Out of the lake it starts from, into the lake it ends in: only the stretch between.
                bool inLake = false;
                foreach (Lake lake in route.Lakes)
                    if (Vector2.Distance(point, new Vector2(lake.Centre.x, lake.Centre.z)) < lake.Radius - 3f)
                        inLake = true;
                if (inLake)
                {
                    if (river.Points.Count == 0)
                        continue;
                    break;
                }
                river.Points.Add(point);
            }
            if (river.Points.Count < 6)
                return null;
            if (fromLake != null)
                river.FromLake = fromLake.Value.Centre.y;
            if (intoLake != null)
                river.IntoLake = intoLake.Value.Centre.y;
            return river;
        }

        /// <summary>
        /// A coarse grid over the map for finding where water would run: moving costs the distance, a lot more
        /// for every metre climbed, more over high ground; the road, the lots, the trading posts and other lakes are
        /// off limits, and crossing the trail costs a little (so rivers cross it squarely, not run along it).
        /// </summary>
        sealed class RiverGrid
        {
            public readonly int Size;
            readonly float[] height;
            readonly bool[] blocked;
            readonly float[] trailCost;
            readonly int[] lakeOf;

            public RiverGrid(float[,] heights, RouteLayout route)
            {
                Size = Mathf.CeilToInt(TerrainSize / RiverPathCell);
                height = new float[Size * Size];
                blocked = new bool[Size * Size];
                trailCost = new float[Size * Size];
                lakeOf = new int[Size * Size];
                int resolution = heights.GetLength(0);
                for (int z = 0; z < Size; z++)
                for (int x = 0; x < Size; x++)
                {
                    int i = z * Size + x;
                    Vector2 world = World(new Vector2Int(x, z));
                    float half = TerrainSize / 2f;
                    float u = (world.x + half) / TerrainSize, v = (world.y + half) / TerrainSize;
                    height[i] = heights[Mathf.Clamp(Mathf.RoundToInt(v * (resolution - 1)), 0, resolution - 1),
                        Mathf.Clamp(Mathf.RoundToInt(u * (resolution - 1)), 0, resolution - 1)] * TerrainHeight;
                    var world3 = new Vector3(world.x, 0f, world.y);
                    blocked[i] = RoadDistance(world3) < RoadHalfWidth + 30f || InsideLot(world) > -40f || NearPlace(world) > 0f;
                    foreach (RouteStop stop in route.Stops)
                        if (Vector2.Distance(world, new Vector2(stop.U * TerrainSize - half, stop.V * TerrainSize - half)) < 45f)
                            blocked[i] = true;
                    trailCost[i] = TrailDistance(world3) < 25f ? 350f : 0f;
                    lakeOf[i] = -1;
                    for (int l = 0; l < route.Lakes.Count; l++)
                        if (Vector2.Distance(world, new Vector2(route.Lakes[l].Centre.x, route.Lakes[l].Centre.z)) < route.Lakes[l].Radius + 25f)
                            lakeOf[i] = l;
                }
            }

            public Vector2 World(Vector2Int cell) => new(cell.x * RiverPathCell + RiverPathCell / 2f - TerrainSize / 2f, cell.y * RiverPathCell + RiverPathCell / 2f - TerrainSize / 2f);

            public Vector2Int Cell(Vector2 world) => new(Mathf.Clamp(Mathf.FloorToInt((world.x + TerrainSize / 2f) / RiverPathCell), 0, Size - 1),
                Mathf.Clamp(Mathf.FloorToInt((world.y + TerrainSize / 2f) / RiverPathCell), 0, Size - 1));

            public float Height(Vector2Int cell) => height[cell.y * Size + cell.x];
            public bool Blocked(Vector2Int cell) => blocked[cell.y * Size + cell.x] || lakeOf[cell.y * Size + cell.x] >= 0;

            /// <summary>The lowest open cell in a box of the map (normalised), at least <paramref name="offTrail"/> m from the trail.</summary>
            public Vector2Int? LowestIn(float vMin, float vMax, float uMin, float uMax, float offTrail)
            {
                Vector2Int? best = null;
                float lowest = float.MaxValue;
                for (int z = Mathf.RoundToInt(vMin * Size); z < Mathf.RoundToInt(vMax * Size); z++)
                for (int x = Mathf.RoundToInt(uMin * Size); x < Mathf.RoundToInt(uMax * Size); x++)
                {
                    var cell = new Vector2Int(x, z);
                    Vector2 world = World(cell);
                    if (Blocked(cell) || TrailDistance(new Vector3(world.x, 0f, world.y)) < offTrail || Height(cell) >= lowest)
                        continue;
                    lowest = Height(cell);
                    best = cell;
                }
                return best;
            }

            /// <summary>The cheapest way from <paramref name="start"/> to any cell <paramref name="done"/> accepts, as world points; null if there's none.</summary>
            public List<Vector2> Cheapest(Vector2Int start, System.Func<Vector2Int, bool> done, int ownLake)
            {
                var cost = new float[Size * Size];
                var from = new int[Size * Size];
                for (int i = 0; i < cost.Length; i++)
                {
                    cost[i] = float.MaxValue;
                    from[i] = -1;
                }
                var open = new MinHeap();
                int first = start.y * Size + start.x;
                cost[first] = 0f;
                open.Push(first, 0f);
                int end = -1;
                while (open.Count > 0)
                {
                    (int at, float atCost) = open.Pop();
                    if (atCost > cost[at])
                        continue;
                    var cell = new Vector2Int(at % Size, at / Size);
                    if (at != first && done(cell))
                    {
                        end = at;
                        break;
                    }
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;
                        int nx = cell.x + dx, nz = cell.y + dz;
                        if (nx < 0 || nz < 0 || nx >= Size || nz >= Size)
                            continue;
                        int next = nz * Size + nx;
                        // Off limits: the road and lots, and lakes other than this river's own.
                        if (blocked[next] && !done(new Vector2Int(nx, nz)))
                            continue;
                        if (lakeOf[next] >= 0 && lakeOf[next] != ownLake && !done(new Vector2Int(nx, nz)))
                            continue;
                        float step = (dx != 0 && dz != 0 ? 1.414f : 1f) * RiverPathCell;
                        float climb = Mathf.Max(0f, height[next] - height[at]);
                        float stepCost = step * (1f + height[next] / 120f) + climb * 350f + trailCost[next];
                        float total = atCost + stepCost;
                        if (total < cost[next])
                        {
                            cost[next] = total;
                            from[next] = at;
                            open.Push(next, total);
                        }
                    }
                }
                if (end < 0)
                    return null;
                var path = new List<Vector2>();
                for (int at = end; at >= 0; at = from[at])
                    path.Add(World(new Vector2Int(at % Size, at / Size)));
                path.Reverse();
                return path;
            }
        }

        /// <summary>A binary heap of (index, cost), cheapest first.</summary>
        sealed class MinHeap
        {
            readonly List<(int index, float cost)> items = new();
            public int Count => items.Count;

            public void Push(int index, float cost)
            {
                items.Add((index, cost));
                int i = items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (items[parent].cost <= items[i].cost)
                        break;
                    (items[parent], items[i]) = (items[i], items[parent]);
                    i = parent;
                }
            }

            public (int index, float cost) Pop()
            {
                (int index, float cost) top = items[0];
                items[0] = items[^1];
                items.RemoveAt(items.Count - 1);
                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1, right = left + 1, smallest = i;
                    if (left < items.Count && items[left].cost < items[smallest].cost)
                        smallest = left;
                    if (right < items.Count && items[right].cost < items[smallest].cost)
                        smallest = right;
                    if (smallest == i)
                        break;
                    (items[smallest], items[i]) = (items[i], items[smallest]);
                    i = smallest;
                }
                return top;
            }
        }

        // ---------- Cutting the channels ----------

        static float SampleHeightMetres(float[,] heights, Vector2 world)
        {
            int resolution = heights.GetLength(0);
            float half = TerrainSize / 2f;
            float fx = (world.x + half) / TerrainSize * (resolution - 1), fz = (world.y + half) / TerrainSize * (resolution - 1);
            int x = Mathf.Clamp(Mathf.FloorToInt(fx), 0, resolution - 2), z = Mathf.Clamp(Mathf.FloorToInt(fz), 0, resolution - 2);
            float tx = Mathf.Clamp01(fx - x), tz = Mathf.Clamp01(fz - z);
            float bottom = Mathf.Lerp(heights[z, x], heights[z, x + 1], tx), top = Mathf.Lerp(heights[z + 1, x], heights[z + 1, x + 1], tx);
            return Mathf.Lerp(bottom, top, tz) * TerrainHeight;
        }

        /// <summary>
        /// Works out each river's water level (always downhill, held by the lakes it joins and the river a stream
        /// joins) and cuts its channel and banks into the heights. Then finds where the trail crosses, for bridges.
        /// </summary>
        static void CarveRivers(float[,] heights, RouteLayout route)
        {
            if (currentRivers.Count == 0)
                return;
            // Rivers first, so streams can meet them at their level.
            currentRivers.Sort((a, b) => (a.IntoRiver == null ? 0 : 1).CompareTo(b.IntoRiver == null ? 0 : 1));
            foreach (River river in currentRivers)
            {
                river.Surface.Clear();
                float level = float.MaxValue;
                foreach (Vector2 point in river.Points)
                {
                    level = Mathf.Min(level, SampleHeightMetres(heights, point) - river.Drop);
                    river.Surface.Add(level);
                }
                if (river.FromLake != null)
                    for (int i = 0; i < river.Surface.Count; i++)
                        river.Surface[i] = Mathf.Min(river.Surface[i], river.FromLake.Value);
                // Never below where it's going.
                float floor = river.IntoLake ?? (river.IntoRiver != null ? river.IntoRiver.Surface[Mathf.Clamp(river.JoinsAt, 0, river.IntoRiver.Surface.Count - 1)] : float.MinValue);
                for (int i = 0; i < river.Surface.Count; i++)
                    river.Surface[i] = Mathf.Max(river.Surface[i], floor);
                // Ease the steps between level stretches into rapids, keeping it running downhill.
                for (int pass = 0; pass < 3; pass++)
                    for (int i = 1; i < river.Surface.Count - 1; i++)
                        river.Surface[i] = Mathf.Clamp((river.Surface[i - 1] + river.Surface[i] * 2f + river.Surface[i + 1]) / 4f, river.Surface[i + 1], river.Surface[i - 1]);
                // Where it cuts through a rise, its sides lie back at about 35°.
                river.Banks.Clear();
                for (int i = 0; i < river.Points.Count; i++)
                    river.Banks.Add(Mathf.Max(river.Bank, (SampleHeightMetres(heights, river.Points[i]) - river.Surface[i]) * 1.45f));
                for (int pass = 0; pass < 4; pass++)
                    for (int i = 1; i < river.Banks.Count - 1; i++)
                        river.Banks[i] = Mathf.Max(river.Banks[i], (river.Banks[i - 1] + river.Banks[i + 1]) / 2f * 0.9f);
            }

            int resolution = heights.GetLength(0);
            float metresPerSample = TerrainSize / (resolution - 1);
            float halfSize = TerrainSize / 2f;
            var nearest = new Dictionary<int, (float distance, float surface, float bank, River river)>();
            foreach (River river in currentRivers)
            {
                float widest = 0f;
                foreach (float bank in river.Banks)
                    widest = Mathf.Max(widest, bank);
                float reach = river.HalfWidth + widest;
                int cells = Mathf.CeilToInt(reach / metresPerSample) + 1;
                for (int i = 0; i < river.Points.Count - 1; i++)
                {
                    Vector2 a = river.Points[i], b = river.Points[i + 1];
                    Vector2 ab = b - a;
                    float lengthSquared = Mathf.Max(ab.sqrMagnitude, 0.0001f);
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) + halfSize) / metresPerSample) - cells);
                    int x1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + halfSize) / metresPerSample) + cells);
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) + halfSize) / metresPerSample) - cells);
                    int z1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + halfSize) / metresPerSample) + cells);
                    for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var sample = new Vector2(x * metresPerSample - halfSize, z * metresPerSample - halfSize);
                        float t = Mathf.Clamp01(Vector2.Dot(sample - a, ab) / lengthSquared);
                        float distance = Vector2.Distance(sample, a + ab * t);
                        if (distance > reach)
                            continue;
                        int key = z * resolution + x;
                        // The nearest water's edge wins (a stream's bank doesn't fill in the river).
                        float edge = distance - river.HalfWidth;
                        if (nearest.TryGetValue(key, out var known) && known.distance - known.river.HalfWidth <= edge)
                            continue;
                        float bankHere = Mathf.Lerp(river.Banks[i], river.Banks[i + 1], t);
                        if (distance > river.HalfWidth + bankHere)
                            continue;
                        nearest[key] = (distance, Mathf.Lerp(river.Surface[i], river.Surface[i + 1], t), bankHere, river);
                    }
                }
            }

            foreach ((int key, (float distance, float surface, float bank, River river)) in nearest)
            {
                int x = key % resolution, z = key / resolution;
                var sample = new Vector2(x * metresPerSample - halfSize, z * metresPerSample - halfSize);
                // The lakes shape their own shores.
                bool inLake = false;
                foreach (Lake lake in route.Lakes)
                    if (Vector2.Distance(sample, new Vector2(lake.Centre.x, lake.Centre.z)) < lake.Radius + 2f)
                        inLake = true;
                float ground = heights[z, x] * TerrainHeight;
                float shaped;
                if (distance < river.HalfWidth)
                {
                    float across = distance / river.HalfWidth;
                    shaped = Mathf.Min(ground, surface - river.Depth * (1f - across * across) - 0.08f);
                }
                else
                {
                    // Banks: from just above the water at the edge, back up to the land (or down to it).
                    float t = Mathf.SmoothStep(0f, 1f, (distance - river.HalfWidth) / bank);
                    shaped = Mathf.Lerp(surface + 0.3f, ground, t);
                    if (inLake)
                        shaped = Mathf.Min(shaped, ground);
                }
                heights[z, x] = Mathf.Clamp01(shaped / TerrainHeight);
            }

            BuildRiverEdgeField();
            FindBridges();
        }

        static void BuildRiverEdgeField()
        {
            float half = TerrainSize / 2f;
            var field = new Trail { Size = Mathf.CeilToInt(TerrainSize / TrailFieldCell) + 1 };
            field.Distance = new float[field.Size * field.Size];
            const float Reach = 40f;
            for (int i = 0; i < field.Distance.Length; i++)
                field.Distance[i] = Reach;
            int cells = Mathf.CeilToInt((Reach + 8f) / TrailFieldCell);
            foreach (River river in currentRivers)
                for (int i = 0; i < river.Points.Count - 1; i++)
                {
                    Vector2 a = river.Points[i], b = river.Points[i + 1];
                    Vector2 ab = b - a;
                    float lengthSquared = Mathf.Max(ab.sqrMagnitude, 0.0001f);
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) + half) / TrailFieldCell) - cells);
                    int x1 = Mathf.Min(field.Size - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + half) / TrailFieldCell) + cells);
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) + half) / TrailFieldCell) - cells);
                    int z1 = Mathf.Min(field.Size - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + half) / TrailFieldCell) + cells);
                    for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var cell = new Vector2(x * TrailFieldCell - half, z * TrailFieldCell - half);
                        float t = Mathf.Clamp01(Vector2.Dot(cell - a, ab) / lengthSquared);
                        float edge = Vector2.Distance(cell, a + ab * t) - river.HalfWidth;
                        int index = z * field.Size + x;
                        if (edge < field.Distance[index])
                            field.Distance[index] = edge;
                    }
                }
            riverEdges = field;
        }

        /// <summary>Where the trail's path crosses a river or stream: a footbridge goes there, square to the trail.</summary>
        static void FindBridges()
        {
            if (currentTrail == null)
                return;
            foreach (River river in currentRivers)
                for (int i = 0; i < river.Points.Count - 1; i++)
                for (int j = 0; j < currentTrail.Points.Count - 1; j++)
                {
                    Vector2 a = river.Points[i], b = river.Points[i + 1], c = currentTrail.Points[j], d = currentTrail.Points[j + 1];
                    if (!SegmentsCross(a, b, c, d, out Vector2 at))
                        continue;
                    Vector2 along = (d - c).normalized;
                    // Square to the water as near as the trail allows: long enough to clear both banks.
                    Vector2 flow = (b - a).normalized;
                    float squareness = Mathf.Max(0.4f, Mathf.Abs(along.x * flow.y - along.y * flow.x));
                    if (currentBridges.Exists(bridge => Vector2.Distance(bridge.Centre, at) < 15f))
                        continue;
                    currentBridges.Add(new Bridge
                    {
                        Centre = at,
                        Along = along,
                        Length = (river.HalfWidth * 2f + 5f) / squareness,
                        Surface = Mathf.Lerp(river.Surface[i], river.Surface[i + 1], Vector2.Distance(a, at) / Mathf.Max(0.01f, Vector2.Distance(a, b))),
                    });
                }
        }

        static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 at)
        {
            at = default;
            Vector2 r = b - a, s = d - c;
            float denominator = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denominator) < 1e-5f)
                return false;
            float t = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / denominator;
            float u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / denominator;
            if (t < 0f || t > 1f || u < 0f || u > 1f)
                return false;
            at = a + r * t;
            return true;
        }

        // ---------- The water, the triggers and the bridges ----------

        /// <summary>Lays the rivers' water surfaces, wading triggers and footbridges in the scene (after the terrain exists).</summary>
        static void CreateRivers(Terrain terrain)
        {
            if (currentRivers.Count == 0)
                return;
            var root = new GameObject("Rivers").transform;
            Material water = GetOrCreateRiverMaterial();
            EnsureFolder(GeneratedFolder + "/Rivers");
            int chunkIndex = 0;
            foreach (River river in currentRivers)
            {
                var riverRoot = new GameObject(river.Name).transform;
                riverRoot.SetParent(root, false);

                // The surface, in chunks of 50 points (200 m) so the far ones are culled.
                const int ChunkPoints = 50;
                float along = 0f;
                for (int start = 0; start < river.Points.Count - 1; start += ChunkPoints)
                {
                    int end = Mathf.Min(river.Points.Count - 1, start + ChunkPoints);
                    Mesh mesh = BuildRiverStrip(river, start, end, ref along);
                    string path = $"{GeneratedFolder}/Rivers/Water {chunkIndex++}.asset";
                    AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(mesh, path);
                    var chunk = new GameObject("Water");
                    chunk.transform.SetParent(riverRoot, false);
                    chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = chunk.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = water;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    var flowing = chunk.AddComponent<World.FlowingWater>();
                    SetFloat(flowing, "speed", river.Flow);
                }

                // Wading triggers every 12 m, each tilted down its stretch so the surface height is right along it.
                for (int i = 0; i < river.Points.Count - 1; i += 3)
                {
                    int j = Mathf.Min(river.Points.Count - 1, i + 3);
                    var a = new Vector3(river.Points[i].x, river.Surface[i], river.Points[i].y);
                    var b = new Vector3(river.Points[j].x, river.Surface[j], river.Points[j].y);
                    Vector3 forward = b - a;
                    if (forward.sqrMagnitude < 0.01f)
                        continue;
                    var stretch = new GameObject(river.Name);
                    stretch.transform.SetParent(riverRoot, false);
                    stretch.transform.SetPositionAndRotation((a + b) / 2f, Quaternion.LookRotation(forward, Vector3.up));
                    var trigger = stretch.AddComponent<BoxCollider>();
                    trigger.isTrigger = true;
                    trigger.size = new Vector3(river.HalfWidth * 2f + 1.2f, 0.12f, forward.magnitude + 1.5f);
                    var source = stretch.AddComponent<WaterSource>();
                    SetString(source, "displayName", river.Name);
                    SetBool(source, "flowing", true);
                }
            }
            foreach (Bridge bridge in currentBridges)
                CreateFootbridge(bridge, terrain, root);
        }

        /// <summary>The water's surface for one stretch: a ribbon a little wider than the channel, its edges tucked into the banks.</summary>
        static Mesh BuildRiverStrip(River river, int start, int end, ref float along)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float half = river.HalfWidth + 0.7f;
            for (int i = start; i <= end; i++)
            {
                Vector2 previous = river.Points[Mathf.Max(0, i - 1)], next = river.Points[Mathf.Min(river.Points.Count - 1, i + 1)];
                Vector2 forward = (next - previous).normalized;
                Vector2 side = new(-forward.y, forward.x);
                if (i > start)
                    along += Vector2.Distance(river.Points[i - 1], river.Points[i]);
                float y = river.Surface[i];
                Vector2 left = river.Points[i] + side * half, right = river.Points[i] - side * half;
                vertices.Add(new Vector3(left.x, y, left.y));
                vertices.Add(new Vector3(right.x, y, right.y));
                // Across 0–1 per 4 m of width, down the river one repeat per 6 m.
                uvs.Add(new Vector2(0f, along / 6f));
                uvs.Add(new Vector2(half * 2f / 4f, along / 6f));
                if (i > start)
                {
                    int b = vertices.Count - 4;
                    triangles.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }
            var mesh = new Mesh { name = $"{river.Name} water" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A log footbridge: two stringers, a deck of planks resting on them and a handrail each side.</summary>
        static void CreateFootbridge(Bridge bridge, Terrain terrain, Transform parent)
        {
            Vector3 Ground(Vector2 at) => new(at.x, terrain.SampleHeight(new Vector3(at.x, 0f, at.y)) + terrain.transform.position.y, at.y);
            Vector2 from2 = bridge.Centre - bridge.Along * (bridge.Length / 2f), to2 = bridge.Centre + bridge.Along * (bridge.Length / 2f);
            Vector3 from = Ground(from2) + Vector3.up * 0.08f, to = Ground(to2) + Vector3.up * 0.08f;
            // Clear of the water in the middle.
            float lift = Mathf.Max(0f, bridge.Surface + 0.55f - (from.y + to.y) / 2f);
            from.y += lift;
            to.y += lift;
            var root = new GameObject("Footbridge");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation((from + to) / 2f, Quaternion.LookRotation(to - from, Vector3.up));
            float length = Vector3.Distance(from, to);
            Material timber = GetOrCreateMaterial("BridgeTimber", new Color(0.4f, 0.29f, 0.19f));
            Material log = GetOrCreateMaterial("Wood", new Color(0.33f, 0.22f, 0.13f));
            const float width = 1.4f;
            // The deck walks as one solid piece; the planks are its look.
            var deck = new GameObject("Deck");
            deck.transform.SetParent(root.transform, false);
            var walk = deck.AddComponent<BoxCollider>();
            walk.center = new Vector3(0f, -0.04f, 0f);
            walk.size = new Vector3(width, 0.1f, length);
            for (float z = -length / 2f + 0.12f; z < length / 2f; z += 0.26f)
                AddVisual(PrimitiveType.Cube, root.gameObject, new Vector3(0f, -0.04f, z), Quaternion.Euler(0f, Random.Range(-2f, 2f), 0f), new Vector3(width, 0.06f, 0.23f), timber);
            foreach (float side in new[] { -0.55f, 0.55f })
            {
                AddVisual(PrimitiveType.Cylinder, root.gameObject, new Vector3(side, -0.17f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.22f, length / 2f + 0.3f, 0.22f), log);
                for (float z = -length / 2f + 0.2f; z <= length / 2f; z += Mathf.Max(1.5f, length / 4f))
                    AddVisual(PrimitiveType.Cylinder, root.gameObject, new Vector3(side * 1.18f, 0.45f, z), Quaternion.identity, new Vector3(0.07f, 0.5f, 0.07f), log);
                AddVisual(PrimitiveType.Cylinder, root.gameObject, new Vector3(side * 1.18f, 0.9f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.06f, length / 2f, 0.06f), log);
            }
        }

        /// <summary>Clear, dark water, see-through near the edge, with ripples drawn along the flow (FlowingWater moves them).</summary>
        static Material GetOrCreateRiverMaterial()
        {
            string path = $"{GeneratedFolder}/River Water.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", new Color(0.1f, 0.2f, 0.22f, 0.78f));
            material.SetFloat("_Smoothness", 0.93f);
            material.SetFloat("_Metallic", 0f);
            material.SetTexture("_BumpMap", GetOrCreateRippleTexture());
            material.SetFloat("_BumpScale", 0.45f);
            material.EnableKeyword("_NORMALMAP");
            // Transparent, alpha blended, not writing depth (and not the milky look: see URP's specular preserve).
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_BlendModePreserveSpecular", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A tileable normal map of small ripples, longer down the flow than across it.</summary>
        static Texture2D GetOrCreateRippleTexture()
        {
            string path = $"{GeneratedFolder}/WaterRipples.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;
            const int size = 256;
            var heights = new float[size, size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                heights[y, x] = TileableValueNoise(u, v, 8) * 0.5f + TileableValueNoise(u, v, 16) * 0.3f + TileableValueNoise(u, v, 32) * 0.2f;
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = heights[y, (x + 1) % size] - heights[y, (x + size - 1) % size];
                float dy = heights[(y + 1) % size, x] - heights[(y + size - 1) % size, x];
                Vector3 normal = new Vector3(-dx * 6f, -dy * 3f, 1f).normalized;
                texture.SetPixel(x, y, new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f));
            }
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.NormalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
