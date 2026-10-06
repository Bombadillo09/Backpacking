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
        const float TreeGridSpacing = 11f;
        const int DetailResolution = 1024;

        // Layer order in the terrain's splatmap.
        const int GrassLayer = 0, DirtLayer = 1, RockLayer = 2, SnowLayer = 3, ForestFloorLayer = 4, AlpineLayer = 5;

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
            forest = Mathf.Max(forest, valley * 0.75f) * belowTreeline * (1f - cliffs);
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
            data.terrainLayers = new[] { art.grass, art.dirt, art.rock, art.snow, art.forestFloor, art.alpineMeadow };
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
            var splat = new float[res, res, 6];
            var weights = new float[6];

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
                float forestFloor = biome.Forest * (1f - rock) * (1f - alpine);
                float dirt = Mathf.InverseLerp(0.6f, 0.8f, patchNoise) * biome.Meadow * (1f - rock) * 0.8f;

                weights[GrassLayer] = Mathf.Max(0f, 1f - rock - snow - alpine - forestFloor - dirt);
                weights[DirtLayer] = dirt;
                weights[RockLayer] = rock;
                weights[SnowLayer] = snow;
                weights[ForestFloorLayer] = forestFloor;
                weights[AlpineLayer] = alpine;

                float total = 0f;
                foreach (float weight in weights)
                    total += weight;
                for (int layer = 0; layer < 6; layer++)
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
            for (int cz = 0; cz < cells; cz++)
            for (int cx = 0; cx < cells; cx++)
            {
                // One chance per grid cell, jittered so trees don't line up.
                float u = (cx + (float)random.NextDouble()) / cells;
                float v = (cz + (float)random.NextDouble()) / cells;
                Biome biome = SampleBiome(data, u, v);
                if (random.NextDouble() > biome.Forest * 0.55f * art.treeDensity)
                    continue;
                if (InClearing(u, v, route, data))
                    continue;

                int[] pool = random.NextDouble() < biome.Valley ? valley
                    : random.NextDouble() < biome.ConiferShare ? conifers
                    : lowland;
                float size = 0.75f + (float)random.NextDouble() * 0.6f;
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

        static void GrowGrass(TerrainData data, RouteLayout route, BiomeArtSettings art)
        {
            data.SetDetailResolution(DetailResolution, 32);
            data.detailPrototypes = new[]
            {
                new DetailPrototype
                {
                    prototypeTexture = art.grassTexture,
                    renderMode = DetailRenderMode.Grass,
                    healthyColor = art.grassHealthy,
                    dryColor = art.grassDry,
                    minWidth = 0.5f,
                    maxWidth = 1f,
                    minHeight = 0.35f,
                    maxHeight = 0.75f,
                    noiseSpread = 0.4f,
                },
            };

            var density = new int[DetailResolution, DetailResolution];
            for (int z = 0; z < DetailResolution; z++)
            for (int x = 0; x < DetailResolution; x++)
            {
                float u = x / (DetailResolution - 1f), v = z / (DetailResolution - 1f);
                float steepness = data.GetSteepness(u, v);
                float height01 = data.GetInterpolatedHeight(u, v) / data.size.y;
                Biome biome = SampleBiome(u, v, height01, steepness);
                float bare = Mathf.Max(Mathf.InverseLerp(28f, 40f, steepness), Mathf.InverseLerp(0.6f, 0.7f, height01));
                float amount = (biome.Meadow * 7f + biome.Forest * 1.5f + biome.Alpine * 3f) * (1f - bare) * art.grassDensity;
                if (amount > 0.5f && InLake(u, v, route))
                    amount = 0f;
                density[z, x] = Mathf.Clamp(Mathf.RoundToInt(amount), 0, 16);
            }
            data.SetDetailLayer(0, 0, 0, density);
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
