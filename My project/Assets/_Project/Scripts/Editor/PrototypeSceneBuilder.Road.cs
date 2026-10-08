using System.Collections.Generic;
using Backpacking.Navigation;
using Backpacking.Trade;
using Backpacking.Trip;
using Backpacking.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// The drive in: your home at the west end of a gravel road through the woods, the outdoor store
    /// partway along, and a small car park at the trailhead where the road ends. The road is levelled into
    /// the hills, kept clear of trees and laid as a gravel strip, the same way the trail is.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        const float RoadHalfWidth = 2.6f;
        /// <summary>How far either side of the gravel the carved ground blends back into the hills.</summary>
        const float RoadShoulder = 10f;
        const float RoadTreeClearance = RoadHalfWidth + 2.2f;
        /// <summary>Dense woods this far either side of the road.</summary>
        const float RoadWoodsWidth = 80f;
        /// <summary>The road's rise and fall is the ground height averaged this far ahead and behind.</summary>
        const float RoadProfileReach = 40f;
        const float RoadPointSpacing = 8f;
        const float RoadTextureTile = 4f;
        const string GravelFolder = Root + "/Art/Ground/gravel_road";

        /// <summary>A level, gravelled lot by the road: home, the store, the trailhead parking.</summary>
        class Place
        {
            public string Name;
            /// <summary>World (x, z).</summary>
            public Vector2 Centre;
            public float LotRadius;
            /// <summary>Open ground around the lot, where the woods give way.</summary>
            public float MeadowRadius;
            /// <summary>Flat direction the road runs through the lot (toward the trailhead).</summary>
            public Vector2 RoadDirection = Vector2.right;
        }

        /// <summary>The road for the scene being built, as a path like the trail.</summary>
        static Trail currentRoad;
        static Place home, store, parking;

        static IEnumerable<Place> Places()
        {
            if (home != null)
                yield return home;
            if (store != null)
                yield return store;
            if (parking != null)
                yield return parking;
        }

        static float RoadDistance(float u, float v)
        {
            if (currentRoad == null)
                return float.MaxValue;
            float half = TerrainSize / 2f;
            return currentRoad.DistanceAt(new Vector2(u * TerrainSize - half, v * TerrainSize - half));
        }

        static float RoadDistance(Vector3 world) =>
            currentRoad == null ? float.MaxValue : currentRoad.DistanceAt(new Vector2(world.x, world.z));

        /// <summary>How far a world (x, z) is inside the nearest lot's edge: positive inside, negative outside.</summary>
        static float InsideLot(Vector2 world)
        {
            float inside = float.MinValue;
            foreach (Place place in Places())
                inside = Mathf.Max(inside, place.LotRadius - Vector2.Distance(world, place.Centre));
            return inside;
        }

        static float InsideLot(float u, float v)
        {
            float half = TerrainSize / 2f;
            return InsideLot(new Vector2(u * TerrainSize - half, v * TerrainSize - half));
        }

        /// <summary>
        /// Distance to trodden ground for the plants: the trail, the road and the lots, measured so each is
        /// bare across its whole width (grass and brush start <see cref="TrailBareWidth"/> past the result's zero).
        /// </summary>
        static float TroddenDistance(float u, float v)
        {
            float road = RoadDistance(u, v) - (RoadHalfWidth + 1.2f - TrailBareWidth);
            float lot = -InsideLot(u, v) + TrailBareWidth - 1f;
            return Mathf.Min(TrailDistance(u, v), Mathf.Min(road, lot));
        }

        /// <summary>0–1: how much a meadow opens up around the lots, so they sit in clearings.</summary>
        static float NearPlace(Vector2 world)
        {
            float near = 0f;
            foreach (Place place in Places())
                near = Mathf.Max(near, 1f - Mathf.InverseLerp(place.MeadowRadius * 0.6f, place.MeadowRadius, Vector2.Distance(world, place.Centre)));
            return near;
        }

        /// <summary>0–1 gravel paint under the road and on the lots.</summary>
        static float RoadGround(float u, float v)
        {
            float road = 1f - Mathf.InverseLerp(RoadHalfWidth, RoadHalfWidth + 3f, RoadDistance(u, v));
            float lot = Mathf.InverseLerp(-3f, 1f, InsideLot(u, v));
            return 0.9f * Mathf.Max(road, lot);
        }

        // ---------- Planning ----------

        /// <summary>
        /// Picks the places, levels their lots and carves the road between them into the heights.
        /// Runs after the route is planned (it uses the first stop) and before the trail.
        /// </summary>
        static void PlanDrive(float[,] heights, RouteLayout route)
        {
            float half = TerrainSize / 2f;
            Vector2 Normalised(float u, float v) => new(u * TerrainSize - half, v * TerrainSize - half);
            RouteStop trailhead = route.Stops[0];

            home = new Place { Name = "Home", Centre = Normalised(0.2f, 0.046f), LotRadius = 14f, MeadowRadius = 45f };
            store = new Place { Name = TripLog.Outfitter, Centre = Normalised(0.355f, 0.05f), LotRadius = 24f, MeadowRadius = 60f };
            parking = new Place { Name = "Trailhead parking", Centre = Normalised(trailhead.U, trailhead.V) + new Vector2(0f, -32f), LotRadius = 13f, MeadowRadius = 40f };

            foreach (Place place in Places())
                FlattenAround(heights, (place.Centre.x + half) / TerrainSize, (place.Centre.y + half) / TerrainSize, place.LotRadius);

            currentRoad = PlanRoad();
            CarveRoad(heights, currentRoad);
            // Carving smooths the road's heights over a long stretch, so level the lots again where it crosses them.
            foreach (Place place in Places())
                FlattenAround(heights, (place.Centre.x + half) / TerrainSize, (place.Centre.y + half) / TerrainSize, place.LotRadius);
        }

        /// <summary>A gently winding line from home, through the store's lot, to the trailhead parking.</summary>
        static Trail PlanRoad()
        {
            var road = new Trail();
            Place[] stops = { home, store, parking };
            for (int leg = 0; leg < stops.Length - 1; leg++)
            {
                Vector2 a = stops[leg].Centre, b = stops[leg + 1].Centre;
                float length = Vector2.Distance(a, b);
                Vector2 side = new Vector2(-(b - a).y, (b - a).x) / length;
                int steps = Mathf.Max(2, Mathf.CeilToInt(length / RoadPointSpacing));
                // Roads wander less than footpaths: long, easy bends.
                float amplitude = Mathf.Min(40f, length * 0.05f);
                for (int k = leg == 0 ? 0 : 1; k <= steps; k++)
                {
                    float t = k / (float)steps;
                    float wander = Mathf.PerlinNoise(leg * 5.7f + 20.5f, t * length / 350f) * 2f - 1f;
                    road.Points.Add(Vector2.Lerp(a, b, t) + side * (wander * amplitude * Mathf.Sin(Mathf.PI * t)));
                }
            }
            for (int pass = 0; pass < 3; pass++)
                for (int i = 1; i < road.Points.Count - 1; i++)
                    road.Points[i] = (road.Points[i - 1] + road.Points[i] * 2f + road.Points[i + 1]) / 4f;

            // Which way the road runs through each lot, for facing the buildings.
            foreach (Place place in stops)
                place.RoadDirection = RoadDirectionNear(road.Points, place.Centre);

            BuildDistanceField(road);
            return road;
        }

        static Vector2 RoadDirectionNear(List<Vector2> points, Vector2 at)
        {
            int nearest = 0;
            for (int i = 1; i < points.Count; i++)
                if ((points[i] - at).sqrMagnitude < (points[nearest] - at).sqrMagnitude)
                    nearest = i;
            Vector2 a = points[Mathf.Max(0, nearest - 2)], b = points[Mathf.Min(points.Count - 1, nearest + 2)];
            return (b - a).normalized;
        }

        /// <summary>
        /// Levels a bed for the road: flat across, rising and falling with the ground's average along it,
        /// blending back into the hills over the shoulder.
        /// </summary>
        static void CarveRoad(float[,] heights, Trail road)
        {
            int resolution = heights.GetLength(0);
            float half = TerrainSize / 2f;
            float metresPerSample = TerrainSize / (resolution - 1);
            const float spacing = 2f;
            List<(Vector2 position, Vector2 forward, float along)> samples = ResampleTrail(road.Points, spacing);

            var ground = new float[samples.Count];
            for (int i = 0; i < samples.Count; i++)
                ground[i] = SampleHeights(heights, samples[i].position);
            var profile = new float[samples.Count];
            int window = Mathf.RoundToInt(RoadProfileReach / spacing);
            for (int i = 0; i < samples.Count; i++)
            {
                // Weighted toward the middle, so the grade changes smoothly.
                float sum = 0f, weights = 0f;
                for (int j = Mathf.Max(0, i - window); j <= Mathf.Min(samples.Count - 1, i + window); j++)
                {
                    float weight = 1f - Mathf.Abs(j - i) / (window + 1f);
                    sum += ground[j] * weight;
                    weights += weight;
                }
                profile[i] = sum / weights;
            }

            float reach = RoadHalfWidth + RoadShoulder;
            int cellReach = Mathf.CeilToInt(reach / metresPerSample) + 1;
            var nearest = new Dictionary<int, (float distance, float height)>();
            for (int i = 0; i < samples.Count - 1; i++)
            {
                Vector2 a = samples[i].position, b = samples[i + 1].position;
                Vector2 ab = b - a;
                float lengthSquared = Mathf.Max(ab.sqrMagnitude, 0.0001f);
                int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) + half) / metresPerSample) - cellReach);
                int x1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + half) / metresPerSample) + cellReach);
                int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) + half) / metresPerSample) - cellReach);
                int z1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + half) / metresPerSample) + cellReach);
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var cell = new Vector2(x * metresPerSample - half, z * metresPerSample - half);
                    float t = Mathf.Clamp01(Vector2.Dot(cell - a, ab) / lengthSquared);
                    float distance = Vector2.Distance(cell, a + ab * t);
                    if (distance > reach)
                        continue;
                    int key = z * resolution + x;
                    if (!nearest.TryGetValue(key, out var best) || distance < best.distance)
                        nearest[key] = (distance, Mathf.Lerp(profile[i], profile[i + 1], t));
                }
            }

            foreach (KeyValuePair<int, (float distance, float height)> entry in nearest)
            {
                int z = entry.Key / resolution, x = entry.Key % resolution;
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RoadHalfWidth + 1f, reach, entry.Value.distance));
                heights[z, x] = Mathf.Lerp(entry.Value.height, heights[z, x], blend);
            }
        }

        /// <summary>Bilinear height (0–1) of the heightmap under a world (x, z).</summary>
        static float SampleHeights(float[,] heights, Vector2 world)
        {
            int resolution = heights.GetLength(0);
            float half = TerrainSize / 2f;
            float fx = Mathf.Clamp((world.x + half) / TerrainSize * (resolution - 1), 0f, resolution - 1.001f);
            float fz = Mathf.Clamp((world.y + half) / TerrainSize * (resolution - 1), 0f, resolution - 1.001f);
            int x = Mathf.FloorToInt(fx), z = Mathf.FloorToInt(fz);
            float tx = fx - x, tz = fz - z;
            float bottom = Mathf.Lerp(heights[z, x], heights[z, x + 1], tx);
            float top = Mathf.Lerp(heights[z + 1, x], heights[z + 1, x + 1], tx);
            return Mathf.Lerp(bottom, top, tz);
        }

        // ---------- Building ----------

        /// <summary>The gravel road and lots, your home, the outdoor store and the trailhead parking.</summary>
        static void CreateDrive(Terrain terrain)
        {
            if (currentRoad == null || currentRoad.Points.Count < 2)
                return;

            string folder = GeneratedFolder + "/Road";
            AssetDatabase.DeleteAsset(folder);
            EnsureFolder(folder);
            Material gravel = GetOrCreateGravelMaterial();

            var root = new GameObject("Road");
            var path = root.AddComponent<RoadPath>();
            var points = new Vector3[currentRoad.Points.Count];
            for (int i = 0; i < points.Length; i++)
                points[i] = OnGround(terrain, currentRoad.Points[i]);
            SetVector3Array(path, "points", points);
            SetRoadPlaces(path, terrain);

            List<(Vector2 position, Vector2 forward, float along)> samples = ResampleTrail(currentRoad.Points, TrailStripSpacing);
            const int perChunk = 400;
            int chunk = 0;
            for (int start = 0; start < samples.Count - 1; start += perChunk)
            {
                int end = Mathf.Min(samples.Count - 1, start + perChunk);
                Mesh mesh = BuildTrailStrip(terrain, samples, start, end, RoadStyle);
                mesh.name = $"Road{chunk}";
                AssetDatabase.CreateAsset(mesh, $"{folder}/Road{chunk}.asset");
                AddGroundMesh($"Road {chunk}", mesh, gravel, root.transform);
                chunk++;
            }
            foreach (Place place in Places())
            {
                Mesh lot = BuildLotMesh(terrain, place);
                AssetDatabase.CreateAsset(lot, $"{folder}/{place.Name} lot.asset");
                AddGroundMesh($"{place.Name} lot", lot, gravel, root.transform);
            }

            CreateHome(home, terrain, root.transform);
            CreateStore(store, terrain, root.transform);
            CreateTrailheadParking(parking, terrain, root.transform);
            Debug.Log($"Laid {samples.Count * TrailStripSpacing / 1000f:0.0} km of road.");
        }

        static void SetRoadPlaces(RoadPath path, Terrain terrain) =>
            Modify(path, "places", property =>
            {
                property.arraySize = 2;
                (string name, Place place)[] labelled = { ("Home", home), ("Trailhead parking", parking) };
                for (int i = 0; i < labelled.Length; i++)
                {
                    SerializedProperty entry = property.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("name").stringValue = labelled[i].name;
                    entry.FindPropertyRelative("position").vector3Value = OnGround(terrain, labelled[i].place.Centre);
                }
            });

        static Vector3 OnGround(Terrain terrain, Vector2 xz)
        {
            var world = new Vector3(xz.x, 0f, xz.y);
            world.y = terrain.SampleHeight(world) + terrain.transform.position.y;
            return world;
        }

        static void AddGroundMesh(string name, Mesh mesh, Material material, Transform parent)
        {
            var piece = new GameObject(name);
            piece.transform.SetParent(parent, false);
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = piece.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        static readonly StripStyle RoadStyle = new()
        {
            Across = new[] { -0.5f, -0.33f, -0.17f, 0f, 0.17f, 0.33f, 0.5f },
            // A slight crown, so rain would run off.
            Lift = new[] { 0.03f, 0.05f, 0.06f, 0.065f, 0.06f, 0.05f, 0.03f },
            WidthMin = RoadHalfWidth * 2f * 0.95f,
            WidthMax = RoadHalfWidth * 2f * 1.05f,
            Ragged = 0.5f,
            Tile = RoadTextureTile,
        };

        /// <summary>A round gravel pad following the ground, with a ragged edge.</summary>
        static Mesh BuildLotMesh(Terrain terrain, Place place)
        {
            const int rings = 8, segments = 48;
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            void AddVertex(Vector2 xz, float lift)
            {
                var world = new Vector3(xz.x, 0f, xz.y);
                world.y = terrain.SampleHeight(world) + origin.y + lift;
                vertices.Add(world);
                float u = Mathf.Clamp01((world.x - origin.x) / data.size.x), v = Mathf.Clamp01((world.z - origin.z) / data.size.z);
                normals.Add(data.GetInterpolatedNormal(u, v));
                uvs.Add(xz / RoadTextureTile);
            }

            AddVertex(place.Centre, 0.06f);
            for (int ring = 1; ring <= rings; ring++)
            for (int s = 0; s < segments; s++)
            {
                float angle = s * Mathf.PI * 2f / segments;
                float radius = place.LotRadius * ring / rings;
                if (ring == rings)
                    radius += (Mathf.PerlinNoise(s * 0.6f, place.Centre.x * 0.01f) - 0.5f) * 1.6f;
                AddVertex(place.Centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, ring == rings ? 0.03f : 0.06f);
            }
            for (int s = 0; s < segments; s++)
                triangles.AddRange(new[] { 0, 1 + (s + 1) % segments, 1 + s });
            for (int ring = 1; ring < rings; ring++)
            for (int s = 0; s < segments; s++)
            {
                int inner = 1 + (ring - 1) * segments, outer = 1 + ring * segments;
                int a = inner + s, b = inner + (s + 1) % segments, c = outer + s, d = outer + (s + 1) % segments;
                triangles.AddRange(new[] { a, b, c });
                triangles.AddRange(new[] { b, d, c });
            }

            var mesh = new Mesh { name = $"{place.Name} lot" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Lit gravel from the downloaded Poly Haven texture, or plain grey gravel without it.</summary>
        static Material GetOrCreateGravelMaterial()
        {
            Material material = GetOrCreateMaterial("RoadGravel", new Color(0.62f, 0.6f, 0.57f), 0.08f);
            var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>($"{GravelFolder}/gravel_road_diffuse.jpg");
            string normalPath = $"{GravelFolder}/gravel_road_normal.jpg";
            if (AssetImporter.GetAtPath(normalPath) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            material.SetTexture("_BaseMap", diffuse);
            material.SetTexture("_BumpMap", normal);
            if (normal != null)
                material.EnableKeyword("_NORMALMAP");
            else
                material.DisableKeyword("_NORMALMAP");
            material.SetColor("_BaseColor", diffuse != null ? new Color(0.9f, 0.88f, 0.85f) : new Color(0.62f, 0.6f, 0.57f));
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A building root on the ground at a lot, at <paramref name="offset"/> metres along (x) and beside (y) the road, facing <paramref name="facing"/>.</summary>
        static GameObject PlaceBuilding(string name, Place place, Vector2 offset, Vector2 facing, Terrain terrain, Transform parent)
        {
            Vector2 along = place.RoadDirection, side = new(-along.y, along.x);
            Vector2 xz = place.Centre + along * offset.x + side * offset.y;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(OnGround(terrain, xz), Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y)));
            return root;
        }

        // ---------- Home ----------

        /// <summary>
        /// A one-room timber cabin at the end of the road: bed, table, kitchen counter and a lamp. The door faces
        /// down the road (local +Z).
        /// </summary>
        static void CreateHome(Place place, Terrain terrain, Transform parent)
        {
            Vector2 along = place.RoadDirection;
            GameObject cabin = PlaceBuilding("Home", place, new Vector2(-9f, 0f), along, terrain, parent);
            AddSaveId(cabin, "home");

            Material walls = GetOrCreateMaterial("CabinWalls", new Color(0.5f, 0.36f, 0.24f));
            Material floor = GetOrCreateMaterial("CabinFloor", new Color(0.62f, 0.47f, 0.32f));
            Material roof = GetOrCreateMaterial("RoofShingles", new Color(0.25f, 0.22f, 0.2f));
            Material glass = GetOrCreateMaterial("WindowGlass", new Color(0.12f, 0.16f, 0.2f), 0.9f);
            Material trim = GetOrCreateMaterial("CabinTrim", new Color(0.88f, 0.85f, 0.78f));
            Material bedding = GetOrCreateMaterial("Bedding", new Color(0.85f, 0.84f, 0.8f));
            Material blanket = GetOrCreateMaterial("Blanket", new Color(0.55f, 0.15f, 0.12f));
            Material rug = GetOrCreateMaterial("Rug", new Color(0.3f, 0.4f, 0.5f));
            Material metal = GetOrCreateMaterial("DarkMetal", new Color(0.18f, 0.18f, 0.19f), 0.5f);

            const float width = 6f, depth = 5f, floorTop = 0.3f, wallHeight = 2.6f, wall = 0.15f;
            const float doorX = 0.8f, doorWidth = 1.1f, doorHeight = 2.1f;
            BuildRoom(cabin, width, depth, floorTop, wallHeight, wall, doorX, doorWidth, doorHeight, walls, floor);
            // Step up to the door.
            AddSolid(PrimitiveType.Cube, cabin, new Vector3(doorX, 0.08f, depth / 2f + 0.4f), Quaternion.identity, new Vector3(1.6f, 0.16f, 0.6f), floor);

            // Pitched roof along the cabin's width, with gable ends.
            float top = floorTop + wallHeight;
            const float rise = 1.1f;
            float halfSpan = depth / 2f + 0.1f;
            float slope = Mathf.Atan2(rise, halfSpan) * Mathf.Rad2Deg;
            float slab = Mathf.Sqrt(halfSpan * halfSpan + rise * rise) + 0.35f;
            AddVisual(PrimitiveType.Cube, cabin, new Vector3(0f, top + rise / 2f + 0.06f, halfSpan / 2f), Quaternion.Euler(slope, 0f, 0f), new Vector3(width + 0.6f, 0.12f, slab), roof);
            AddVisual(PrimitiveType.Cube, cabin, new Vector3(0f, top + rise / 2f + 0.06f, -halfSpan / 2f), Quaternion.Euler(-slope, 0f, 0f), new Vector3(width + 0.6f, 0.12f, slab), roof);
            AddGable(cabin, -width / 2f + wall / 2f, top, halfSpan, rise, wall, walls);
            AddGable(cabin, width / 2f - wall / 2f, top, halfSpan, rise, wall, walls);

            // Windows: one each side of the door, one in each side wall, one at the back.
            float frontWall = depth / 2f - wall / 2f, sideWall = width / 2f - wall / 2f;
            AddWindow(cabin, new Vector3(-1.6f, floorTop + 1.5f, frontWall), 0f, new Vector2(1.1f, 0.9f), glass, trim);
            AddWindow(cabin, new Vector3(-sideWall, floorTop + 1.5f, 0.6f), 90f, new Vector2(1f, 0.9f), glass, trim);
            AddWindow(cabin, new Vector3(sideWall, floorTop + 1.5f, -0.4f), 90f, new Vector2(1f, 0.9f), glass, trim);
            AddWindow(cabin, new Vector3(1.4f, floorTop + 1.5f, -frontWall), 0f, new Vector2(1.2f, 0.9f), glass, trim);

            // Bed in the back-left corner.
            var bed = new Vector3(-width / 2f + 0.65f, floorTop, -depth / 2f + 1.15f);
            AddSolid(PrimitiveType.Cube, cabin, bed + new Vector3(0f, 0.18f, 0f), Quaternion.identity, new Vector3(1f, 0.36f, 2f), floor);
            AddVisual(PrimitiveType.Cube, cabin, bed + new Vector3(0f, 0.44f, 0f), Quaternion.identity, new Vector3(0.95f, 0.18f, 1.95f), bedding);
            AddVisual(PrimitiveType.Cube, cabin, bed + new Vector3(0f, 0.54f, 0.25f), Quaternion.identity, new Vector3(0.98f, 0.05f, 1.4f), blanket);
            AddVisual(PrimitiveType.Cube, cabin, bed + new Vector3(0f, 0.58f, -0.75f), Quaternion.identity, new Vector3(0.6f, 0.1f, 0.35f), bedding);

            // Table and chair on the right, kitchen counter along the back wall.
            var table = new Vector3(1.7f, floorTop, -0.2f);
            AddSolid(PrimitiveType.Cube, cabin, table + new Vector3(0f, 0.74f, 0f), Quaternion.identity, new Vector3(1.2f, 0.05f, 0.8f), floor);
            foreach (Vector2 leg in new[] { new Vector2(-0.52f, -0.32f), new Vector2(0.52f, -0.32f), new Vector2(-0.52f, 0.32f), new Vector2(0.52f, 0.32f) })
                AddVisual(PrimitiveType.Cube, cabin, table + new Vector3(leg.x, 0.36f, leg.y), Quaternion.identity, new Vector3(0.06f, 0.72f, 0.06f), floor);
            var chair = table + new Vector3(-0.2f, 0f, 0.75f);
            AddSolid(PrimitiveType.Cube, cabin, chair + new Vector3(0f, 0.45f, 0f), Quaternion.identity, new Vector3(0.45f, 0.05f, 0.45f), floor);
            AddVisual(PrimitiveType.Cube, cabin, chair + new Vector3(0f, 0.22f, 0f), Quaternion.identity, new Vector3(0.4f, 0.44f, 0.4f), floor);
            AddVisual(PrimitiveType.Cube, cabin, chair + new Vector3(0f, 0.75f, 0.2f), Quaternion.identity, new Vector3(0.45f, 0.55f, 0.05f), floor);
            AddSolid(PrimitiveType.Cube, cabin, new Vector3(1.9f, floorTop + 0.45f, -depth / 2f + 0.4f), Quaternion.identity, new Vector3(1.8f, 0.9f, 0.6f), trim);
            AddVisual(PrimitiveType.Cube, cabin, new Vector3(1.5f, floorTop + 0.92f, -depth / 2f + 0.4f), Quaternion.identity, new Vector3(0.5f, 0.04f, 0.4f), metal);
            AddVisual(PrimitiveType.Cube, cabin, new Vector3(-0.4f, floorTop + 0.005f, 0.4f), Quaternion.identity, new Vector3(2f, 0.01f, 1.4f), rug);

            AddLamp(cabin, new Vector3(0f, top - 0.3f, 0f), 7f, 1.6f);

            // A mailbox by the road.
            Vector2 side = new(-along.y, along.x);
            Vector2 mailboxXZ = place.Centre + along * 4f + side * (RoadHalfWidth + 1.2f);
            var mailbox = new GameObject("Mailbox");
            mailbox.transform.SetParent(parent, false);
            mailbox.transform.SetPositionAndRotation(OnGround(terrain, mailboxXZ), Quaternion.LookRotation(new Vector3(-side.x, 0f, -side.y)));
            AddSolid(PrimitiveType.Cube, mailbox, new Vector3(0f, 0.55f, 0f), Quaternion.identity, new Vector3(0.1f, 1.1f, 0.1f), floor);
            AddVisual(PrimitiveType.Cube, mailbox, new Vector3(0f, 1.2f, 0.05f), Quaternion.identity, new Vector3(0.25f, 0.25f, 0.5f), metal);
        }

        /// <summary>
        /// Floor slab and four walls with a doorway in the front (+Z) wall; the walls stand on the floor.
        /// Everything is solid.
        /// </summary>
        static void BuildRoom(GameObject root, float width, float depth, float floorTop, float wallHeight, float wall,
            float doorX, float doorWidth, float doorHeight, Material walls, Material floor)
        {
            AddSolid(PrimitiveType.Cube, root, new Vector3(0f, floorTop / 2f, 0f), Quaternion.identity, new Vector3(width + 0.2f, floorTop, depth + 0.2f), floor);
            float y = floorTop + wallHeight / 2f;
            AddSolid(PrimitiveType.Cube, root, new Vector3(0f, y, -depth / 2f + wall / 2f), Quaternion.identity, new Vector3(width, wallHeight, wall), walls);
            AddSolid(PrimitiveType.Cube, root, new Vector3(-width / 2f + wall / 2f, y, 0f), Quaternion.identity, new Vector3(wall, wallHeight, depth), walls);
            AddSolid(PrimitiveType.Cube, root, new Vector3(width / 2f - wall / 2f, y, 0f), Quaternion.identity, new Vector3(wall, wallHeight, depth), walls);

            // Front wall in three pieces around the doorway.
            float front = depth / 2f - wall / 2f;
            float doorLeft = doorX - doorWidth / 2f, doorRight = doorX + doorWidth / 2f;
            float leftWidth = doorLeft + width / 2f, rightWidth = width / 2f - doorRight;
            AddSolid(PrimitiveType.Cube, root, new Vector3(-width / 2f + leftWidth / 2f, y, front), Quaternion.identity, new Vector3(leftWidth, wallHeight, wall), walls);
            AddSolid(PrimitiveType.Cube, root, new Vector3(width / 2f - rightWidth / 2f, y, front), Quaternion.identity, new Vector3(rightWidth, wallHeight, wall), walls);
            float lintel = wallHeight - doorHeight;
            AddSolid(PrimitiveType.Cube, root, new Vector3(doorX, floorTop + doorHeight + lintel / 2f, front), Quaternion.identity, new Vector3(doorWidth, lintel, wall), walls);
        }

        /// <summary>
        /// A triangular gable end: a square turned 45° and squashed to the roof's rise. Its lower half
        /// hides inside the wall below.
        /// </summary>
        static void AddGable(GameObject root, float x, float baseY, float halfSpan, float rise, float thickness, Material material)
        {
            var squash = new GameObject("Gable");
            squash.transform.SetParent(root.transform, false);
            squash.transform.localPosition = new Vector3(x, baseY, 0f);
            squash.transform.localScale = new Vector3(1f, rise / halfSpan, 1f);
            float side = halfSpan * Mathf.Sqrt(2f);
            AddVisual(PrimitiveType.Cube, squash, Vector3.zero, Quaternion.Euler(45f, 0f, 0f), new Vector3(thickness, side, side), material);
        }

        /// <summary>
        /// A dark pane with a pale frame, standing just proud of both faces of a wall up to 0.24 m thick.
        /// <paramref name="centre"/> is on the wall's centre line; <paramref name="yaw"/> 90 for side walls.
        /// </summary>
        static void AddWindow(GameObject root, Vector3 centre, float yaw, Vector2 size, Material glass, Material frame)
        {
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            AddVisual(PrimitiveType.Cube, root, centre, rotation, new Vector3(size.x, size.y, 0.26f), glass);
            // Frame: sill, head and a centre bar.
            AddVisual(PrimitiveType.Cube, root, centre, rotation, new Vector3(size.x + 0.14f, 0.07f, 0.28f), frame);
            AddVisual(PrimitiveType.Cube, root, centre + Vector3.up * (size.y / 2f + 0.035f), rotation, new Vector3(size.x + 0.14f, 0.07f, 0.28f), frame);
            AddVisual(PrimitiveType.Cube, root, centre - Vector3.up * (size.y / 2f + 0.035f), rotation, new Vector3(size.x + 0.14f, 0.07f, 0.28f), frame);
        }

        static void AddLamp(GameObject root, Vector3 position, float range, float intensity)
        {
            var lampObject = new GameObject("Lamp");
            lampObject.transform.SetParent(root.transform, false);
            lampObject.transform.localPosition = position;
            var lamp = lampObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = range;
            lamp.intensity = intensity;
            lamp.color = new Color(1f, 0.85f, 0.65f);
            lamp.shadows = LightShadows.None;
            AddVisual(PrimitiveType.Sphere, lampObject, Vector3.zero, Quaternion.identity, Vector3.one * 0.22f,
                GetOrCreateEmissiveMaterial("LampGlow", new Color(1f, 0.85f, 0.6f)));
        }

        static Material GetOrCreateEmissiveMaterial(string materialName, Color colour)
        {
            Material material = GetOrCreateMaterial(materialName, colour, 0.3f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", colour * 2f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------- The outdoor store ----------

        /// <summary>
        /// A timber-clad store north of the road with big front windows and a sign. The counter inside is the
        /// vendor; shelves of gear line the walls. It's the first trading post, and where rescues bring you.
        /// </summary>
        static void CreateStore(Place place, Terrain terrain, Transform parent)
        {
            Vector2 along = place.RoadDirection, side = new(-along.y, along.x);
            GameObject building = PlaceBuilding(TripLog.Outfitter, place, new Vector2(0f, 12f), -side, terrain, parent);

            Material walls = GetOrCreateMaterial("StoreWalls", new Color(0.32f, 0.38f, 0.3f));
            Material floor = GetOrCreateMaterial("StoreFloor", new Color(0.55f, 0.45f, 0.33f));
            Material roof = GetOrCreateMaterial("RoofShingles", new Color(0.25f, 0.22f, 0.2f));
            Material glass = GetOrCreateMaterial("WindowGlass", new Color(0.12f, 0.16f, 0.2f), 0.9f);
            Material trim = GetOrCreateMaterial("CabinTrim", new Color(0.88f, 0.85f, 0.78f));
            Material timber = GetOrCreateMaterial("Timber", new Color(0.42f, 0.3f, 0.19f));
            Material shopkeeper = GetOrCreateMaterial("Shopkeeper", new Color(0.55f, 0.3f, 0.2f));
            Material skin = GetOrCreateMaterial("Skin", new Color(0.85f, 0.66f, 0.5f));
            Material[] goods =
            {
                GetOrCreateMaterial("GoodsOrange", new Color(0.85f, 0.42f, 0.12f)), GetOrCreateMaterial("GoodsBlue", new Color(0.15f, 0.3f, 0.55f)),
                GetOrCreateMaterial("GoodsGreen", new Color(0.25f, 0.45f, 0.2f)), GetOrCreateMaterial("GoodsRed", new Color(0.6f, 0.15f, 0.12f)),
                GetOrCreateMaterial("GoodsGrey", new Color(0.45f, 0.45f, 0.47f)), GetOrCreateMaterial("GoodsYellow", new Color(0.85f, 0.7f, 0.2f)),
            };

            const float width = 12f, depth = 9f, floorTop = 0.3f, wallHeight = 3.4f, wall = 0.2f;
            BuildRoom(building, width, depth, floorTop, wallHeight, wall, 0f, 1.8f, 2.4f, walls, floor);
            AddSolid(PrimitiveType.Cube, building, new Vector3(0f, 0.08f, depth / 2f + 0.5f), Quaternion.identity, new Vector3(3f, 0.16f, 0.8f), floor);
            float top = floorTop + wallHeight;
            AddVisual(PrimitiveType.Cube, building, new Vector3(0f, top + 0.12f, 0.2f), Quaternion.identity, new Vector3(width + 0.8f, 0.25f, depth + 1.2f), roof);
            // Porch roof over the door.
            AddVisual(PrimitiveType.Cube, building, new Vector3(0f, 2.95f, depth / 2f + 0.9f), Quaternion.Euler(-8f, 0f, 0f), new Vector3(4f, 0.1f, 1.8f), roof);

            // Big shop windows either side of the door.
            AddWindow(building, new Vector3(-3.5f, floorTop + 1.6f, depth / 2f - wall / 2f), 0f, new Vector2(3.6f, 1.8f), glass, trim);
            AddWindow(building, new Vector3(3.5f, floorTop + 1.6f, depth / 2f - wall / 2f), 0f, new Vector2(3.6f, 1.8f), glass, trim);

            // The sign board above the windows.
            AddVisual(PrimitiveType.Cube, building, new Vector3(0f, top + 0.75f, depth / 2f + 0.05f), Quaternion.identity, new Vector3(8.5f, 1.1f, 0.15f), timber);
            AddSignText(building, TripLog.Outfitter.ToUpperInvariant(), new Vector3(0f, top + 0.75f, depth / 2f + 0.14f), 0.075f);

            // The counter is the vendor. It's its own object so walls and shelves don't open the shop.
            var counter = new GameObject("Counter");
            counter.transform.SetParent(building.transform, false);
            counter.transform.localPosition = new Vector3(0f, floorTop, -depth / 2f + 2.2f);
            AddSolid(PrimitiveType.Cube, counter, new Vector3(0f, 0.5f, 0f), Quaternion.identity, new Vector3(3.4f, 1f, 0.7f), timber);
            AddVisual(PrimitiveType.Capsule, counter, new Vector3(0f, 0.9f, -0.9f), Quaternion.identity, new Vector3(0.5f, 0.75f, 0.5f), shopkeeper);
            AddVisual(PrimitiveType.Sphere, counter, new Vector3(0f, 1.85f, -0.9f), Quaternion.identity, Vector3.one * 0.32f, skin);
            AddVisual(PrimitiveType.Cube, counter, new Vector3(1.1f, 1.12f, 0f), Quaternion.identity, new Vector3(0.4f, 0.24f, 0.3f), goods[4]);
            var vendor = counter.AddComponent<Vendor>();
            AddSaveId(counter, $"vendor-{TripLog.Outfitter}");
            SetString(vendor, "vendorName", TripLog.Outfitter);
            SetFloat(vendor, "priceMultiplier", OutfitterStock.PriceMultiplier);
            SetFloat(vendor, "sellRate", OutfitterStock.SellRate);
            SetStock(vendor, OutfitterStock.Stock);

            // Shelving along both side walls, stocked with boxes and bundles of gear.
            var random = new System.Random(Seed + 11);
            foreach (float x in new[] { -width / 2f + 0.55f, width / 2f - 0.55f })
            for (int unit = 0; unit < 3; unit++)
            {
                var shelf = new Vector3(x, floorTop, -depth / 2f + 1.6f + unit * 2.1f);
                AddSolid(PrimitiveType.Cube, building, shelf + new Vector3(0f, 1.1f, 0f), Quaternion.identity, new Vector3(0.7f, 2.2f, 1.9f), timber);
                for (int level = 0; level < 4; level++)
                for (int item = 0; item < 3; item++)
                {
                    float size = 0.25f + (float)random.NextDouble() * 0.2f;
                    var spot = shelf + new Vector3(-Mathf.Sign(x) * 0.4f, 0.3f + level * 0.5f + size / 2f, -0.6f + item * 0.6f);
                    AddVisual(random.NextDouble() < 0.3 ? PrimitiveType.Cylinder : PrimitiveType.Cube, building, spot, Quaternion.Euler(0f, (float)random.NextDouble() * 20f, 0f),
                        new Vector3(size, size * (random.NextDouble() < 0.5 ? 1f : 0.6f), size), goods[random.Next(goods.Length)]);
                }
            }
            // A display table of packs in the middle of the floor.
            AddSolid(PrimitiveType.Cube, building, new Vector3(0f, floorTop + 0.4f, 1f), Quaternion.identity, new Vector3(2.4f, 0.8f, 1.2f), timber);
            for (int i = 0; i < 3; i++)
                AddVisual(PrimitiveType.Capsule, building, new Vector3(-0.75f + i * 0.75f, floorTop + 1.15f, 1f), Quaternion.identity, new Vector3(0.42f, 0.38f, 0.3f), goods[i]);

            AddLamp(building, new Vector3(-2.5f, top - 0.4f, 0f), 9f, 1.8f);
            AddLamp(building, new Vector3(2.5f, top - 0.4f, 0f), 9f, 1.8f);

            // On the map as a trading post. You know your local store, so it counts as visited, which also
            // makes it the rescue team's drop-off.
            var pointObject = new GameObject($"{TripLog.Outfitter} (point)");
            pointObject.transform.SetParent(parent, false);
            pointObject.transform.position = OnGround(terrain, place.Centre + side * 6f);
            var point = pointObject.AddComponent<NavigationPoint>();
            SetString(point, "displayName", TripLog.Outfitter);
            SetEnum(point, "kind", (int)NavigationPointKind.TradingPost);
            SetBool(point, "visited", true);
            AddSaveId(pointObject, $"point-{TripLog.Outfitter}");
        }

        static void AddSignText(GameObject root, string text, Vector3 position, float characterSize)
        {
            var signObject = new GameObject("Sign");
            signObject.transform.SetParent(root.transform, false);
            // Text meshes face -Z; turn it to read from in front of the building.
            signObject.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));
            var sign = signObject.AddComponent<TextMesh>();
            sign.text = text;
            sign.anchor = TextAnchor.MiddleCenter;
            sign.alignment = TextAlignment.Center;
            sign.characterSize = characterSize;
            sign.fontSize = 48;
            sign.color = new Color(0.95f, 0.9f, 0.75f);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            sign.font = font;
            signObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        // ---------- Trailhead parking ----------

        /// <summary>
        /// A notice board at the head of the trail, and a row of posts across the trail's mouth that let hikers
        /// through but not trucks.
        /// </summary>
        static void CreateTrailheadParking(Place place, Terrain terrain, Transform parent)
        {
            Material timber = GetOrCreateMaterial("Timber", new Color(0.42f, 0.3f, 0.19f));
            Material roof = GetOrCreateMaterial("RoofShingles", new Color(0.25f, 0.22f, 0.2f));
            Material board = GetOrCreateMaterial("NoticeBoard", new Color(0.2f, 0.3f, 0.22f));

            // The trail leaves the lot to the north, toward the first cairn.
            Vector2 north = Vector2.up, east = Vector2.right;
            Vector2 mouth = place.Centre + north * (place.LotRadius - 1f);
            var posts = new GameObject("Trail Posts");
            posts.transform.SetParent(parent, false);
            for (int i = -3; i <= 3; i++)
            {
                Vector3 position = OnGround(terrain, mouth + east * (i * 1.4f));
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = "Post";
                post.transform.SetParent(posts.transform, false);
                post.transform.position = position + Vector3.up * 0.45f;
                post.transform.localScale = new Vector3(0.22f, 0.45f, 0.22f);
                post.GetComponent<Renderer>().sharedMaterial = timber;
            }

            Vector2 kioskXZ = mouth + east * 6.5f;
            var kiosk = new GameObject("Trailhead Kiosk");
            kiosk.transform.SetParent(parent, false);
            kiosk.transform.SetPositionAndRotation(OnGround(terrain, kioskXZ), Quaternion.LookRotation(Vector3.back));
            AddSolid(PrimitiveType.Cube, kiosk, new Vector3(-1f, 1.2f, 0f), Quaternion.identity, new Vector3(0.15f, 2.4f, 0.15f), timber);
            AddSolid(PrimitiveType.Cube, kiosk, new Vector3(1f, 1.2f, 0f), Quaternion.identity, new Vector3(0.15f, 2.4f, 0.15f), timber);
            AddVisual(PrimitiveType.Cube, kiosk, new Vector3(0f, 1.55f, 0f), Quaternion.identity, new Vector3(1.9f, 1.1f, 0.08f), board);
            AddVisual(PrimitiveType.Cube, kiosk, new Vector3(0f, 2.5f, 0f), Quaternion.Euler(-12f, 0f, 0f), new Vector3(2.4f, 0.08f, 0.8f), roof);
            AddSignText(kiosk, $"TRAILHEAD\n\n{TripLog.Destination}\nvia the trail north", new Vector3(0f, 1.6f, 0.05f), 0.035f);
        }
    }
}
