using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Biomes: which parts of the map are meadow, forest, valley woodland or alpine, and the trees, grass
    /// and ground textures that go with each. Art comes from <see cref="BiomeArtSettings"/>; empty slots get
    /// generated placeholders. Also builds the rain effect.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        const string BiomeArtPath = Root + "/Settings/BiomeArt.asset";
        const string NatureFolder = GeneratedFolder + "/Nature";
        const float TreeGridSpacing = 5f;
        const int DetailResolution = 1024;

        // Layer order in the terrain's splatmap.
        const int GrassLayer = 0, DirtLayer = 1, RockLayer = 2, SnowLayer = 3, ForestFloorLayer = 4, AlpineLayer = 5,
            LeafLitterLayer = 6, NeedleLitterLayer = 7, LayerCount = 8;

        /// <summary>How much of each biome a spot is, all 0–1.</summary>
        struct Biome
        {
            /// <summary>Tree cover.</summary>
            public float Forest;
            /// <summary>Share of the trees that are conifers.</summary>
            public float ConiferShare;
            /// <summary>Valley-floor woodland.</summary>
            public float Valley;
            /// <summary>Above the treeline.</summary>
            public float Alpine;
            /// <summary>Open, grassy lowland.</summary>
            public float Meadow;
        }

        static Biome SampleBiome(float u, float v, float height01, float steepness)
        {
            float patches = Fbm(u * 7f + 311f, v * 7f + 173f, 4);
            float belowTreeline = 1f - Mathf.InverseLerp(0.42f, 0.56f, height01);
            float alpine = Mathf.InverseLerp(0.5f, 0.6f, height01);
            float valley = (1f - Mathf.Clamp01(Mathf.Abs(u - ValleyCentre(v)) / 0.04f)) * (1f - Mathf.InverseLerp(0.3f, 0.42f, height01));
            float cliffs = Mathf.InverseLerp(30f, 42f, steepness);

            float forest = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.58f, patches));
            // The trail runs through dense woods.
            forest = Mathf.Max(Mathf.Max(forest, valley * 0.75f), TrailWoods(u, v)) * belowTreeline * (1f - cliffs);
            return new Biome
            {
                Forest = forest,
                ConiferShare = Mathf.InverseLerp(0.12f, 0.38f, height01) * (1f - valley),
                Valley = valley,
                Alpine = alpine,
                Meadow = (1f - forest) * belowTreeline,
            };
        }

        static Biome SampleBiome(TerrainData data, float u, float v) =>
            SampleBiome(u, v, data.GetInterpolatedHeight(u, v) / data.size.y, data.GetSteepness(u, v));

        /// <summary>Ground textures, trees and grass for the whole terrain.</summary>
        static void DressTerrain(TerrainData data, RouteLayout route, BiomeArtSettings art)
        {
            data.terrainLayers = new[]
            {
                art.grass, art.dirt, art.rock, art.snow, art.forestFloor, art.alpineMeadow, art.leafLitter, art.needleLitter,
            };
            EditorUtility.DisplayProgressBar("Building prototype scene", "Painting ground...", 0.4f);
            data.SetAlphamaps(0, 0, GenerateBiomeSplatmap(data));
            EditorUtility.DisplayProgressBar("Building prototype scene", "Planting trees...", 0.6f);
            PlantTrees(data, route, art);
            EditorUtility.DisplayProgressBar("Building prototype scene", "Growing grass...", 0.8f);
            GrowGrass(data, route, art);
        }

        static float[,,] GenerateBiomeSplatmap(TerrainData data)
        {
            int res = data.alphamapResolution;
            var splat = new float[res, res, LayerCount];
            var weights = new float[LayerCount];

            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float u = x / (res - 1f), v = z / (res - 1f);
                float steepness = data.GetSteepness(u, v);
                float height01 = data.GetInterpolatedHeight(u, v) / data.size.y;
                Biome biome = SampleBiome(u, v, height01, steepness);
                float patchNoise = Mathf.PerlinNoise(u * 120f + 7f, v * 120f + 3f);

                float rock = Mathf.InverseLerp(28f, 40f, steepness);
                float snow = Mathf.InverseLerp(0.6f, 0.7f, height01) * (1f - rock);
                float alpine = biome.Alpine * (1f - snow) * (1f - rock);
                float forestGround = biome.Forest * (1f - rock) * (1f - alpine);
                float dirt = Mathf.InverseLerp(0.6f, 0.8f, patchNoise) * biome.Meadow * (1f - rock) * 0.8f;

                // Litter drifts into patches about 15 m across: needles under conifers, leaves under broadleaf.
                float litterNoise = Mathf.PerlinNoise(u * 330f + 91f, v * 330f + 57f);
                float litter = forestGround * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.65f, litterNoise));

                weights[GrassLayer] = Mathf.Max(0f, 1f - rock - snow - alpine - forestGround - dirt);
                // A soft worn band under the trail's dirt strip.
                float worn = 0.8f * (1f - Mathf.InverseLerp(1f, 4.5f, TrailDistance(u, v)));
                weights[DirtLayer] = dirt + worn;
                weights[RockLayer] = rock;
                weights[SnowLayer] = snow;
                weights[ForestFloorLayer] = forestGround - litter;
                weights[AlpineLayer] = alpine;
                weights[LeafLitterLayer] = litter * (1f - biome.ConiferShare);
                weights[NeedleLitterLayer] = litter * biome.ConiferShare;

                float total = 0f;
                foreach (float weight in weights)
                    total += weight;
                for (int layer = 0; layer < LayerCount; layer++)
                    splat[z, x, layer] = weights[layer] / total;
            }
            return splat;
        }

        static void PlantTrees(TerrainData data, RouteLayout route, BiomeArtSettings art)
        {
            var prototypes = new List<TreePrototype>();
            int[] lowland = AddTreePrototypes(prototypes, art.lowlandTrees);
            int[] conifers = AddTreePrototypes(prototypes, art.conifers);
            int[] valley = AddTreePrototypes(prototypes, art.valleyTrees);
            data.treePrototypes = prototypes.ToArray();

            var random = new System.Random(Seed + 3);
            var trees = new List<TreeInstance>();
            int cells = Mathf.FloorToInt(TerrainSize / TreeGridSpacing);
            float cellArea = TreeGridSpacing * TreeGridSpacing;
            float half = TerrainSize / 2f;
            for (int cz = 0; cz < cells; cz++)
            for (int cx = 0; cx < cells; cx++)
            {
                // One chance per grid cell, jittered so trees don't line up.
                float u = (cx + (float)random.NextDouble()) / cells;
                float v = (cz + (float)random.NextDouble()) / cells;
                Biome biome = SampleBiome(data, u, v);
                if (biome.Forest < 0.03f)
                    continue;

                // Dense where you walk, thinner far away to keep the tree count manageable.
                float routeDistance = DistanceToRoute(new Vector2(u * TerrainSize - half, v * TerrainSize - half), route);
                float nearRoute = 1f - Mathf.InverseLerp(art.denseForestWidth * 0.6f, art.denseForestWidth, routeDistance);
                float perHundred = Mathf.Lerp(art.remoteForestDensity, art.forestDensity, nearRoute);
                // Real forests grow in clumps with small gaps between, roughly 30 m across.
                float clumping = Mathf.Lerp(0.3f, 1.7f, Mathf.PerlinNoise(u * 170f + 51f, v * 170f + 13f));
                float chance = Mathf.Pow(biome.Forest, 1.3f) * perHundred * cellArea / 100f * clumping * art.treeDensity;
                if (random.NextDouble() > chance)
                    continue;
                if (InClearing(u, v, route, data))
                    continue;
                // Keep trunks off the path; the branches still meet overhead.
                if (TrailDistance(u, v) < TrailTreeClearance)
                    continue;

                int[] pool = random.NextDouble() < biome.Valley ? valley
                    : random.NextDouble() < biome.ConiferShare ? conifers
                    : lowland;
                float size = RandomTreeAge(random) * art.treeScale;
                trees.Add(new TreeInstance
                {
                    prototypeIndex = pool[random.Next(pool.Length)],
                    position = new Vector3(u, data.GetInterpolatedHeight(u, v) / data.size.y, v),
                    widthScale = size * (0.9f + (float)random.NextDouble() * 0.2f),
                    heightScale = size,
                    rotation = (float)random.NextDouble() * Mathf.PI * 2f,
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }
            data.SetTreeInstances(trees.ToArray(), snapToHeightmap: true);
            Debug.Log($"Planted {trees.Count:N0} trees.");
        }

        /// <summary>Size multiplier for a tree: mostly mature, with some saplings and a few old giants.</summary>
        static float RandomTreeAge(System.Random random)
        {
            double roll = random.NextDouble();
            float t = (float)random.NextDouble();
            if (roll < 0.15)
                return Mathf.Lerp(0.3f, 0.55f, t);
            if (roll < 0.88)
                return Mathf.Lerp(0.8f, 1.15f, t);
            return Mathf.Lerp(1.2f, 1.5f, t);
        }

        /// <summary>Distance in metres from a world position (x, z) to the nearest leg of the route.</summary>
        static float DistanceToRoute(Vector2 world, RouteLayout route)
        {
            float half = TerrainSize / 2f;
            float nearest = float.MaxValue;
            for (int i = 0; i < route.Stops.Count - 1; i++)
            {
                Vector2 a = new Vector2(route.Stops[i].U, route.Stops[i].V) * TerrainSize - new Vector2(half, half);
                Vector2 b = new Vector2(route.Stops[i + 1].U, route.Stops[i + 1].V) * TerrainSize - new Vector2(half, half);
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(world - a, ab) / ab.sqrMagnitude);
                nearest = Mathf.Min(nearest, Vector2.Distance(world, a + ab * t));
            }
            return nearest;
        }

        static int[] AddTreePrototypes(List<TreePrototype> prototypes, GameObject[] prefabs)
        {
            var indices = new int[prefabs.Length];
            for (int i = 0; i < prefabs.Length; i++)
            {
                indices[i] = prototypes.Count;
                prototypes.Add(new TreePrototype { prefab = prefabs[i] });
            }
            return indices;
        }

        /// <summary>Keeps trees off lakes, trading posts, cairns and the start.</summary>
        static bool InClearing(float u, float v, RouteLayout route, TerrainData data)
        {
            float half = TerrainSize / 2f;
            var world = new Vector2(u * TerrainSize - half, v * TerrainSize - half);
            foreach (Lake lake in route.Lakes)
                if (Vector2.Distance(world, new Vector2(lake.Centre.x, lake.Centre.z)) < lake.Radius + 6f)
                    return true;
            for (int i = 0; i < route.Stops.Count; i++)
            {
                RouteStop stop = route.Stops[i];
                var stopWorld = new Vector2(stop.U * TerrainSize - half, stop.V * TerrainSize - half);
                float clearing = i == 0 ? 35f : stop.Vendor != null ? 26f : 12f;
                if (Vector2.Distance(world, stopWorld) < clearing)
                    return true;
            }
            return false;
        }

        /// <summary>Detail layers that count as brush, for <see cref="Camp.GroundClearing"/>. Set by <see cref="GrowGrass"/>.</summary>
        static int[] brushDetailLayers = System.Array.Empty<int>();

        /// <summary>Grass everywhere it grows, plus optional ground plants for forest floors and meadows.</summary>
        static void GrowGrass(TerrainData data, RouteLayout route, BiomeArtSettings art)
        {
            // Small patches: each one is a single mesh, and dense grass would overflow larger ones.
            data.SetDetailResolution(DetailResolution, 16);
            data.wavingGrassStrength = art.grassWaveStrength;
            data.wavingGrassSpeed = art.grassWaveSpeed;
            data.wavingGrassAmount = art.grassWaveAmount;
            // Densities below are plants per detail cell.
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

            var prototypes = new List<DetailPrototype>();
            var plantLayers = new List<(int layer, PlantGroup group)>();
            var grassTextures = new List<Texture2D> { art.grassTexture };
            if (art.extraGrassTextures != null)
                grassTextures.AddRange(art.extraGrassTextures);
            AddBillboardPrototypes(prototypes, plantLayers, grassTextures, PlantGroup.Grass, art.grassWidth, art.grassHeight,
                art.grassHealthy, art.grassDry);
            // Flowers keep their own colours, just a touch of dryness.
            AddBillboardPrototypes(prototypes, plantLayers, art.flowerTextures, PlantGroup.Flowers, art.flowerWidth, art.flowerHeight,
                Color.white, new Color(0.9f, 0.85f, 0.7f));
            AddPlantPrototypes(prototypes, plantLayers, art.forestFloorPlants, PlantGroup.ForestFloor, 0.7f, 1.3f);
            AddPlantPrototypes(prototypes, plantLayers, art.meadowPlants, PlantGroup.Meadow, 0.7f, 1.3f);
            AddPlantPrototypes(prototypes, plantLayers, art.understoryShrubs, PlantGroup.Understory, 0.7f, 1.3f);
            // Shrink full-size logs and rocks down to branches, sticks and stones.
            AddPlantPrototypes(prototypes, plantLayers, art.forestDebris, PlantGroup.Debris, 0.2f, 0.5f);
            AddPlantPrototypes(prototypes, plantLayers, art.forestStones, PlantGroup.Stones, 0.08f, 0.25f);
            data.detailPrototypes = prototypes.ToArray();
            // Shrubs, deadfall and forest-floor plants have to be cleared before pitching a tent.
            brushDetailLayers = plantLayers
                .FindAll(entry => entry.group is PlantGroup.ForestFloor or PlantGroup.Understory or PlantGroup.Debris)
                .ConvertAll(entry => entry.layer)
                .ToArray();

            var plants = new int[plantLayers.Count][,];
            for (int i = 0; i < plants.Length; i++)
                plants[i] = new int[DetailResolution, DetailResolution];
            var random = new System.Random(Seed + 4);
            // Each kind gets an even share of its group, so they mix rather than stack up.
            var kindsPerGroup = new Dictionary<PlantGroup, int>();
            foreach ((int _, PlantGroup group) in plantLayers)
                kindsPerGroup[group] = kindsPerGroup.TryGetValue(group, out int kinds) ? kinds + 1 : 1;

            for (int z = 0; z < DetailResolution; z++)
            for (int x = 0; x < DetailResolution; x++)
            {
                float u = x / (DetailResolution - 1f), v = z / (DetailResolution - 1f);
                float steepness = data.GetSteepness(u, v);
                float height01 = data.GetInterpolatedHeight(u, v) / data.size.y;
                Biome biome = SampleBiome(u, v, height01, steepness);
                float bare = Mathf.Max(Mathf.InverseLerp(28f, 40f, steepness), Mathf.InverseLerp(0.6f, 0.7f, height01));
                if (InLake(u, v, route))
                    bare = 1f;
                float growth = 1f - bare;
                float fromTrail = TrailDistance(u, v);

                for (int i = 0; i < plantLayers.Count; i++)
                {
                    PlantGroup group = plantLayers[i].group;
                    float share = 1f / kindsPerGroup[group];
                    // Plants per detail cell (about 24 m²) where the biome is at its fullest.
                    float where = group switch
                    {
                        PlantGroup.ForestFloor => biome.Forest * 3.2f,
                        PlantGroup.Meadow => (biome.Meadow * (1f - biome.Alpine) + biome.Forest * 0.3f) * 1.6f,
                        PlantGroup.Understory => biome.Forest * biome.Forest * 1.2f,
                        PlantGroup.Grass => biome.Meadow * 7f + biome.Forest * 1.5f + biome.Alpine * 3f,
                        // Wildflowers in open meadows and alpine pasture, a few along forest edges.
                        PlantGroup.Flowers => (biome.Meadow + biome.Alpine * 0.6f + biome.Forest * 0.1f) * art.flowerDensity,
                        PlantGroup.Debris => biome.Forest * 4f,
                        // Stones under trees, and more of them on rocky ground and above the treeline.
                        _ => (biome.Forest * 2f + biome.Alpine * 2.5f) * (1f + Mathf.InverseLerp(15f, 30f, steepness)),
                    };
                    float density = group == PlantGroup.Grass ? art.grassDensity : group == PlantGroup.Flowers ? 1f : art.plantDensity;
                    // The path is trodden bare: sparse grass, no flowers, brush or deadfall.
                    if (group == PlantGroup.Grass && fromTrail < 2.5f)
                        density *= 0.2f;
                    else if (group != PlantGroup.Grass && fromTrail < 3.5f)
                        density = 0f;
                    float amount = where * growth * share * density;
                    // Fractional amounts become an occasional plant rather than none.
                    int count = (int)amount + (random.NextDouble() < amount % 1f ? 1 : 0);
                    plants[i][z, x] = Mathf.Clamp(count, 0, MaxPlantsPerCell);
                }
            }

            for (int i = 0; i < plantLayers.Count; i++)
                data.SetDetailLayer(0, 0, plantLayers[i].layer, plants[i]);
        }

        // Per detail cell (about 24 m²) of one kind; enough for thick meadow grass.
        const int MaxPlantsPerCell = 64;

        enum PlantGroup
        {
            Grass,
            Flowers,
            ForestFloor,
            Meadow,
            Understory,
            Debris,
            Stones,
        }

        /// <summary>Flat, camera-facing plants drawn from a texture: grass tufts and wildflowers.</summary>
        static void AddBillboardPrototypes(List<DetailPrototype> prototypes, List<(int layer, PlantGroup group)> layers,
            IEnumerable<Texture2D> textures, PlantGroup group, Vector2 width, Vector2 height, Color healthy, Color dry)
        {
            if (textures == null)
                return;
            foreach (Texture2D texture in textures)
            {
                if (texture == null)
                    continue;
                layers.Add((prototypes.Count, group));
                prototypes.Add(new DetailPrototype
                {
                    prototypeTexture = texture,
                    renderMode = DetailRenderMode.Grass,
                    healthyColor = healthy,
                    dryColor = dry,
                    minWidth = width.x,
                    maxWidth = width.y,
                    minHeight = height.x,
                    maxHeight = height.y,
                    noiseSpread = 0.4f,
                });
            }
        }

        static void AddPlantPrototypes(List<DetailPrototype> prototypes, List<(int layer, PlantGroup group)> layers, GameObject[] prefabs,
            PlantGroup group, float minScale, float maxScale)
        {
            if (prefabs == null)
                return;
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || prefab.GetComponent<MeshFilter>() == null)
                    continue;
                layers.Add((prototypes.Count, group));
                prototypes.Add(new DetailPrototype
                {
                    prototype = prefab,
                    usePrototypeMesh = true,
                    useInstancing = true,
                    renderMode = DetailRenderMode.VertexLit,
                    minWidth = minScale,
                    maxWidth = maxScale,
                    minHeight = minScale,
                    maxHeight = maxScale,
                    noiseSpread = 0.3f,
                    healthyColor = Color.white,
                    dryColor = new Color(0.85f, 0.82f, 0.7f),
                });
            }
        }
        static bool InLake(float u, float v, RouteLayout route)
        {
            float half = TerrainSize / 2f;
            var world = new Vector2(u * TerrainSize - half, v * TerrainSize - half);
            foreach (Lake lake in route.Lakes)
                if (Vector2.Distance(world, new Vector2(lake.Centre.x, lake.Centre.z)) < lake.Radius)
                    return true;
            return false;
        }

        static void ApplyNatureDrawSettings(Terrain terrain, BiomeArtSettings art)
        {
            terrain.treeDistance = art.treeDrawDistance;
            // Placeholder and most pack trees have no billboard shader, so draw them as meshes all the way out.
            terrain.treeBillboardDistance = art.treeDrawDistance;
            terrain.detailObjectDistance = art.grassDrawDistance;
            terrain.detailObjectDensity = 1f;
        }

        // ---------- Art settings and placeholders ----------

        /// <summary>Loads the biome art settings, creating the asset and filling any empty slot with a placeholder.</summary>
        static BiomeArtSettings LoadBiomeArt()
        {
            EnsureFolder(Path.GetDirectoryName(BiomeArtPath).Replace('\\', '/'));
            EnsureFolder(NatureFolder);
            var art = AssetDatabase.LoadAssetAtPath<BiomeArtSettings>(BiomeArtPath);
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<BiomeArtSettings>();
                AssetDatabase.CreateAsset(art, BiomeArtPath);
            }

            if (IsEmpty(art.lowlandTrees))
                art.lowlandTrees = new[] { GetOrCreateTreePrefab("Broadleaf", BuildBroadleafMesh, "Bark", new Color(0.3f, 0.22f, 0.15f), "BroadleafLeaves", new Color(0.22f, 0.36f, 0.14f), 0.25f) };
            if (IsEmpty(art.conifers))
                art.conifers = new[] { GetOrCreateTreePrefab("Conifer", BuildConiferMesh, "Bark", new Color(0.3f, 0.22f, 0.15f), "Needles", new Color(0.1f, 0.22f, 0.12f), 0.22f) };
            if (IsEmpty(art.valleyTrees))
                art.valleyTrees = new[] { GetOrCreateTreePrefab("Birch", BuildBirchMesh, "BirchBark", new Color(0.85f, 0.83f, 0.78f), "BirchLeaves", new Color(0.4f, 0.52f, 0.2f), 0.14f) };

            // Explicit == null rather than ??=, so references to deleted assets also count as empty.
            if (art.grass == null)
                art.grass = CreateTerrainLayer("Grass", new Color(0.24f, 0.33f, 0.14f), 0.18f, 6f);
            if (art.dirt == null)
                art.dirt = CreateTerrainLayer("Dirt", new Color(0.36f, 0.29f, 0.2f), 0.2f, 5f);
            if (art.rock == null)
                art.rock = CreateTerrainLayer("Rock", new Color(0.42f, 0.41f, 0.39f), 0.25f, 10f);
            if (art.snow == null)
                art.snow = CreateTerrainLayer("Snow", new Color(0.88f, 0.9f, 0.94f), 0.06f, 12f);
            if (art.forestFloor == null)
                art.forestFloor = CreateTerrainLayer("ForestFloor", new Color(0.2f, 0.17f, 0.11f), 0.25f, 4f);
            if (art.alpineMeadow == null)
                art.alpineMeadow = CreateTerrainLayer("AlpineMeadow", new Color(0.4f, 0.4f, 0.22f), 0.2f, 7f);
            if (art.leafLitter == null)
                art.leafLitter = CreateTerrainLayer("LeafLitter", new Color(0.33f, 0.22f, 0.12f), 0.3f, 3f);
            if (art.needleLitter == null)
                art.needleLitter = CreateTerrainLayer("NeedleLitter", new Color(0.3f, 0.2f, 0.13f), 0.2f, 3f);
            if (art.grassTexture == null)
                art.grassTexture = GetOrCreateGrassTexture();

            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            return art;
        }

        static bool IsEmpty(GameObject[] prefabs)
        {
            if (prefabs == null || prefabs.Length == 0)
                return true;
            foreach (GameObject prefab in prefabs)
                if (prefab == null)
                    return true;
            return false;
        }

        /// <summary>
        /// A tree as one mesh with two submeshes (bark, foliage), so the terrain can draw it cheaply.
        /// Kept if it already exists.
        /// </summary>
        static GameObject GetOrCreateTreePrefab(string treeName, System.Func<(List<CombineInstance> bark, List<CombineInstance> foliage)> buildParts,
            string barkName, Color barkColour, string foliageName, Color foliageColour, float trunkRadius)
        {
            string prefabPath = $"{NatureFolder}/{treeName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
                return existing;

            (List<CombineInstance> barkParts, List<CombineInstance> foliageParts) = buildParts();
            var bark = new Mesh();
            bark.CombineMeshes(barkParts.ToArray(), mergeSubMeshes: true);
            var foliage = new Mesh();
            foliage.CombineMeshes(foliageParts.ToArray(), mergeSubMeshes: true);

            var mesh = new Mesh { name = treeName };
            mesh.CombineMeshes(new[]
            {
                new CombineInstance { mesh = bark, transform = Matrix4x4.identity },
                new CombineInstance { mesh = foliage, transform = Matrix4x4.identity },
            }, mergeSubMeshes: false);
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, $"{NatureFolder}/{treeName}Mesh.asset");
            Object.DestroyImmediate(bark);
            Object.DestroyImmediate(foliage);

            var root = new GameObject(treeName);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterials = new[]
            {
                GetOrCreateInstancedMaterial(barkName, barkColour),
                GetOrCreateInstancedMaterial(foliageName, foliageColour),
            };
            var trunk = root.AddComponent<CapsuleCollider>();
            trunk.radius = trunkRadius;
            trunk.height = 4f;
            trunk.center = new Vector3(0f, 2f, 0f);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Material GetOrCreateInstancedMaterial(string materialName, Color colour)
        {
            Material material = GetOrCreateMaterial(materialName, colour, 0.05f);
            material.enableInstancing = true;
            return material;
        }

        static Mesh BuiltinMesh(string name) => Resources.GetBuiltinResource<Mesh>($"{name}.fbx");

        static CombineInstance Part(Mesh mesh, Vector3 position, Vector3 scale, Quaternion? rotation = null) => new()
        {
            mesh = mesh,
            transform = Matrix4x4.TRS(position, rotation ?? Quaternion.identity, scale),
        };

        /// <summary>A spruce-like tree: tall trunk with stacked cones narrowing to a point. About 11 m tall.</summary>
        static (List<CombineInstance>, List<CombineInstance>) BuildConiferMesh()
        {
            Mesh cylinder = BuiltinMesh("Cylinder");
            var bark = new List<CombineInstance> { Part(cylinder, new Vector3(0f, 2.5f, 0f), new Vector3(0.35f, 2.5f, 0.35f)) };
            var foliage = new List<CombineInstance>();
            float[] radii = { 2.4f, 1.9f, 1.4f, 0.9f };
            float y = 1.8f;
            foreach (float radius in radii)
            {
                foliage.Add(Part(CreateConeMesh(radius, radius * 1.9f, 9), new Vector3(0f, y, 0f), Vector3.one));
                y += radius * 1.15f;
            }
            return (bark, foliage);
        }

        /// <summary>A round-canopied broadleaf tree, about 9 m tall.</summary>
        static (List<CombineInstance>, List<CombineInstance>) BuildBroadleafMesh()
        {
            Mesh cylinder = BuiltinMesh("Cylinder"), sphere = BuiltinMesh("Sphere");
            var bark = new List<CombineInstance>
            {
                Part(cylinder, new Vector3(0f, 2.2f, 0f), new Vector3(0.4f, 2.2f, 0.4f)),
                Part(cylinder, new Vector3(0.5f, 4.6f, 0.2f), new Vector3(0.2f, 1f, 0.2f), Quaternion.Euler(0f, 0f, -30f)),
            };
            var foliage = new List<CombineInstance>
            {
                Part(sphere, new Vector3(0f, 6.4f, 0f), new Vector3(5f, 4f, 5f)),
                Part(sphere, new Vector3(1.4f, 5.6f, 0.6f), new Vector3(3.2f, 2.8f, 3.2f)),
                Part(sphere, new Vector3(-1.2f, 5.8f, -0.8f), new Vector3(3.4f, 2.8f, 3.2f)),
            };
            return (bark, foliage);
        }

        /// <summary>A slender birch with a white trunk and a narrow canopy, about 10 m tall.</summary>
        static (List<CombineInstance>, List<CombineInstance>) BuildBirchMesh()
        {
            Mesh cylinder = BuiltinMesh("Cylinder"), sphere = BuiltinMesh("Sphere");
            var bark = new List<CombineInstance> { Part(cylinder, new Vector3(0f, 4f, 0f), new Vector3(0.24f, 4f, 0.24f)) };
            var foliage = new List<CombineInstance>
            {
                Part(sphere, new Vector3(0f, 7f, 0f), new Vector3(2.6f, 4.5f, 2.6f)),
                Part(sphere, new Vector3(0.4f, 5.4f, 0.3f), new Vector3(2f, 2.6f, 2f)),
            };
            return (bark, foliage);
        }

        /// <summary>A cone with its base at y = 0, pointing up.</summary>
        static Mesh CreateConeMesh(float radius, float height, int segments)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                var p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                var p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                int start = vertices.Count;
                // Side (its own vertices for faceted shading), then the base underneath.
                vertices.Add(p0);
                vertices.Add(new Vector3(0f, height, 0f));
                vertices.Add(p1);
                triangles.AddRange(new[] { start, start + 1, start + 2 });
                vertices.Add(p0);
                vertices.Add(p1);
                vertices.Add(Vector3.zero);
                triangles.AddRange(new[] { start + 3, start + 4, start + 5 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        static Texture2D GetOrCreateGrassTexture()
        {
            string path = $"{NatureFolder}/GrassTuft.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            var random = new System.Random(11);
            // Tapered blades fanning up from the bottom. White-ish so the terrain's grass colours tint it.
            for (int blade = 0; blade < 26; blade++)
            {
                float baseX = 16f + (float)random.NextDouble() * (size - 32f);
                float lean = ((float)random.NextDouble() - 0.5f) * 40f;
                float height = size * (0.55f + (float)random.NextDouble() * 0.45f);
                float width = 2.5f + (float)random.NextDouble() * 2f;
                float shade = 0.75f + (float)random.NextDouble() * 0.25f;
                for (int y = 0; y < height; y++)
                {
                    float t = y / height;
                    float centre = baseX + lean * t * t;
                    float halfWidth = width * (1f - t);
                    for (int x = Mathf.FloorToInt(centre - halfWidth); x <= Mathf.CeilToInt(centre + halfWidth); x++)
                    {
                        if (x < 0 || x >= size)
                            continue;
                        float tone = shade * Mathf.Lerp(0.7f, 1f, t);
                        pixels[y * size + x] = new Color(tone, tone, tone, 1f);
                    }
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------- Boulders and firewood models ----------

        const int BoulderCount = 500;

        /// <summary>Boulders along the route, mostly on rocky slopes and above the treeline.</summary>
        static void ScatterBoulders(Terrain terrain, RouteLayout route, BiomeArtSettings art)
        {
            if (IsEmpty(art.boulders) || art.boulderDensity <= 0f)
                return;

            var random = new System.Random(Seed + 5);
            var parent = new GameObject("Boulders").transform;
            TerrainData data = terrain.terrainData;
            float half = TerrainSize / 2f;
            int target = Mathf.RoundToInt(BoulderCount * art.boulderDensity);

            int placed = 0, attempts = 0;
            while (placed < target && attempts++ < target * 30)
            {
                Vector3 position = RandomPointNearRoute(route, random);
                float u = (position.x + half) / TerrainSize, v = (position.z + half) / TerrainSize;
                if (u is < 0.01f or > 0.99f || v is < 0.01f or > 0.99f)
                    continue;
                float steepness = data.GetSteepness(u, v);
                float height01 = data.GetInterpolatedHeight(u, v) / data.size.y;
                Biome biome = SampleBiome(u, v, height01, steepness);
                float rocky = Mathf.Max(biome.Alpine, Mathf.InverseLerp(15f, 35f, steepness));
                if (steepness > 45f || random.NextDouble() > 0.08f + 0.92f * rocky)
                    continue;
                if (TooCloseToFeature(position, route, terrain))
                    continue;

                float scale = Mathf.Lerp(0.6f, 2.8f, Mathf.Pow((float)random.NextDouble(), 2f));
                // Sink each boulder a little so it sits in the ground rather than on it.
                position.y = terrain.SampleHeight(position) + terrain.transform.position.y - 0.15f * scale;
                var boulder = (GameObject)PrefabUtility.InstantiatePrefab(art.boulders[random.Next(art.boulders.Length)], parent);
                boulder.transform.SetPositionAndRotation(position,
                    Quaternion.Euler((float)random.NextDouble() * 20f - 10f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 20f - 10f));
                boulder.transform.localScale = Vector3.one * scale;
                placed++;
            }
        }

        const int FallenLogCount = 2500;

        /// <summary>Fallen trunks lying on the forest floor along the route, settled onto the slope.</summary>
        static void ScatterFallenLogs(Terrain terrain, RouteLayout route, BiomeArtSettings art)
        {
            GameObject[] logs = IsEmpty(art.fallenLogs) ? art.firewoodModels : art.fallenLogs;
            if (IsEmpty(logs))
                return;

            var random = new System.Random(Seed + 7);
            var parent = new GameObject("Fallen Logs").transform;
            TerrainData data = terrain.terrainData;
            float half = TerrainSize / 2f;
            int target = Mathf.RoundToInt(FallenLogCount * art.treeDensity);

            int placed = 0, attempts = 0;
            while (placed < target && attempts++ < target * 30)
            {
                Vector3 position = RandomPointNearRoute(route, random);
                float u = (position.x + half) / TerrainSize, v = (position.z + half) / TerrainSize;
                if (u is < 0.01f or > 0.99f || v is < 0.01f or > 0.99f)
                    continue;
                Biome biome = SampleBiome(data, u, v);
                if (random.NextDouble() > biome.Forest * biome.Forest || data.GetSteepness(u, v) > 30f)
                    continue;
                if (TooCloseToFeature(position, route, terrain))
                    continue;

                position.y = terrain.SampleHeight(position) + terrain.transform.position.y - 0.05f;
                Vector3 ground = data.GetInterpolatedNormal(u, v);
                Quaternion lying = Quaternion.FromToRotation(Vector3.up, ground) * Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                var log = (GameObject)PrefabUtility.InstantiatePrefab(logs[random.Next(logs.Length)], parent);
                log.transform.SetPositionAndRotation(position, lying);
                log.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.6f, (float)random.NextDouble());
                placed++;
            }
        }

        /// <summary>Swaps each firewood pickup's placeholder sticks for a real wood model, resized to branch size.</summary>
        static void DressFirewood(Transform firewoodGroup, BiomeArtSettings art)
        {
            if (IsEmpty(art.firewoodModels))
                return;

            const float targetLength = 0.9f;
            var random = new System.Random(Seed + 6);
            foreach (Transform pickup in firewoodGroup)
            {
                foreach (Transform child in pickup)
                    child.gameObject.SetActive(false);

                GameObject source = art.firewoodModels[random.Next(art.firewoodModels.Length)];
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, pickup);
                model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                model.transform.localScale = Vector3.one;
                foreach (Collider collider in model.GetComponentsInChildren<Collider>())
                    Object.DestroyImmediate(collider);

                Bounds bounds = RendererBounds(model);
                float longest = Mathf.Max(bounds.size.x, bounds.size.z, 0.01f);
                float scale = targetLength / longest;
                model.transform.localScale = Vector3.one * scale;
                bounds = RendererBounds(model);
                // Rest it on the ground, then fit the pickup's collider around it.
                model.transform.position += Vector3.up * (pickup.position.y - bounds.min.y);
                bounds = RendererBounds(model);

                var box = pickup.GetComponent<BoxCollider>();
                if (box != null)
                {
                    box.center = pickup.InverseTransformPoint(bounds.center);
                    // The pickup may be rotated; size the box in its own axes.
                    Vector3 localSize = Quaternion.Inverse(pickup.rotation) * bounds.size;
                    box.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Max(Mathf.Abs(localSize.y), 0.15f), Mathf.Abs(localSize.z));
                }
            }
        }

        static Bounds RendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer meshRenderer in renderers)
                bounds.Encapsulate(meshRenderer.bounds);
            return bounds;
        }

        // ---------- Air ----------

        /// <summary>
        /// Tiny specks drifting in the air around the player, catching the light under the trees. The
        /// forest atmosphere turns them up in the woods by day.
        /// </summary>
        static ParticleSystem CreateMotes(Transform player)
        {
            var go = new GameObject("Air Motes");
            go.transform.SetParent(player, false);
            go.transform.localPosition = new Vector3(0f, 2f, 0f);

            var motes = go.AddComponent<ParticleSystem>();
            motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = motes.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSpeed = 0.05f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
            main.startColor = new Color(1f, 0.96f, 0.82f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.gravityModifier = -0.002f;

            ParticleSystem.EmissionModule emission = motes.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = motes.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(22f, 5f, 22f);

            // A slow, wandering drift.
            ParticleSystem.NoiseModule noise = motes.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.1f;

            // Fade in and out rather than popping.
            ParticleSystem.ColorOverLifetimeModule fade = motes.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            if (GraphicsSettings.currentRenderPipeline != null)
                particleRenderer.sharedMaterial = GraphicsSettings.currentRenderPipeline.defaultParticleMaterial;
            return motes;
        }

        // ---------- Rain ----------

        /// <summary>Rain falling around the player. It follows them, but drops fall in world space.</summary>
        static ParticleSystem CreateRain(Transform player)
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(player, false);
            go.transform.localPosition = new Vector3(0f, 14f, 0f);
            // Box emitters fire along local +Z; point it down.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var rain = go.AddComponent<ParticleSystem>();
            rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = rain.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 1.4f;
            main.startSpeed = 14f;
            main.startSize = 0.025f;
            main.startColor = new Color(0.75f, 0.8f, 0.85f, 0.45f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4000;

            ParticleSystem.EmissionModule emission = rain.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 40f, 0.5f);

            // Wind pushes the drops sideways; the weather sets the strength.
            ParticleSystem.VelocityOverLifetimeModule drift = rain.velocityOverLifetime;
            drift.enabled = true;
            drift.space = ParticleSystemSimulationSpace.World;
            drift.x = new ParticleSystem.MinMaxCurve(0f);
            drift.y = new ParticleSystem.MinMaxCurve(0f);
            drift.z = new ParticleSystem.MinMaxCurve(0f);

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            particleRenderer.velocityScale = 0.05f;
            particleRenderer.lengthScale = 1f;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            if (GraphicsSettings.currentRenderPipeline != null)
                particleRenderer.sharedMaterial = GraphicsSettings.currentRenderPipeline.defaultParticleMaterial;
            return rain;
        }
    }
}
