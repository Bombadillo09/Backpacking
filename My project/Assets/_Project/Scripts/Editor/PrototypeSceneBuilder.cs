using System.IO;
using System.Linq;
using Backpacking.Navigation;
using Backpacking.Player;
using Backpacking.UI;
using Backpacking.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Compass = Backpacking.Navigation.Compass;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Generates the prototype test scene: procedural terrain, sun and moon, sky, player and HUD.
    /// Run from the menu: Backpacking > Build Prototype Scene. Safe to re-run; it regenerates the scene
    /// and terrain, but keeps existing camp prefabs and materials.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        const string Root = "Assets/_Project";
        const string ScenePath = Root + "/Scenes/Prototype.unity";
        const string GeneratedFolder = Root + "/Generated";
        const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        const string VolumeProfilePath = "Assets/Settings/SampleSceneProfile.asset";

        // Terrain dimensions in metres. World height 0 is the lowest possible ground.
        const float TerrainSize = 1500f;
        const float TerrainHeight = 350f;
        const int HeightmapResolution = 513;
        const int SplatResolution = 512;
        const int Seed = 1234;

        [MenuItem("Backpacking/Build Prototype Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog(
                    "Rebuild prototype scene?",
                    $"{ScenePath} already exists and will be replaced, along with its generated terrain.",
                    "Rebuild", "Cancel"))
                return;

            EnsureFolder(Root + "/Scenes");
            EnsureFolder(GeneratedFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CampPrefabs prefabs = GetOrCreateCampPrefabs();
            Terrain terrain = CreateTerrain(out Lake lake);
            CreateLake(lake);
            Light sun = CreateDirectionalLight("Sun", Color.white, 1.3f, LightShadows.Soft);
            Light moon = CreateDirectionalLight("Moon", new Color(0.6f, 0.7f, 1f), 0.12f, LightShadows.None);
            SetUpSkyAndFog(sun);
            CreatePostProcessingVolume();

            var world = new GameObject("World Systems");
            var timeOfDay = world.AddComponent<TimeOfDay>();
            SetField(timeOfDay, "sun", sun);
            SetField(timeOfDay, "moon", moon);
            var temperature = world.AddComponent<AmbientTemperature>();
            SetField(temperature, "timeOfDay", timeOfDay);

            FirstPersonController player = CreatePlayer(terrain);
            CreateNavigationPoints(terrain, player.transform.position);

            var navigation = new GameObject("Navigation");
            var map = navigation.AddComponent<MapView>();
            SetField(map, "terrain", terrain);
            SetField(map, "player", player.transform);
            var compass = navigation.AddComponent<Compass>();
            SetField(compass, "holder", player.transform);

            var hud = new GameObject("Prototype HUD").AddComponent<PrototypeHud>();
            SetField(hud, "timeOfDay", timeOfDay);
            SetField(hud, "temperature", temperature);
            SetField(hud, "player", player);

            AddSurvivalSystems(player, timeOfDay, temperature, hud.gameObject, prefabs);
            ScatterGatherables(terrain, lake, player.transform.position, prefabs);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            Debug.Log($"Built prototype scene at {ScenePath}. Press Play to walk around.");
        }

        // ---------- Terrain ----------

        static Terrain CreateTerrain(out Lake lake)
        {
            var data = new TerrainData
            {
                // Resolution must be set before size, or Unity rescales the size.
                heightmapResolution = HeightmapResolution,
                size = new Vector3(TerrainSize, TerrainHeight, TerrainSize),
                alphamapResolution = SplatResolution,
            };
            float[,] heights = GenerateHeights(data.heightmapResolution);
            lake = CarveLake(heights);
            data.SetHeights(0, 0, heights);

            TerrainLayer[] layers =
            {
                CreateTerrainLayer("Grass", new Color(0.24f, 0.33f, 0.14f), 0.18f, 6f),
                CreateTerrainLayer("Dirt", new Color(0.36f, 0.29f, 0.2f), 0.2f, 5f),
                CreateTerrainLayer("Rock", new Color(0.42f, 0.41f, 0.39f), 0.25f, 10f),
                CreateTerrainLayer("Snow", new Color(0.88f, 0.9f, 0.94f), 0.06f, 12f),
            };
            data.terrainLayers = layers;
            data.SetAlphamaps(0, 0, GenerateSplatmap(data));

            string dataPath = GeneratedFolder + "/PrototypeTerrain.asset";
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.position = new Vector3(-TerrainSize / 2f, 0f, -TerrainSize / 2f);

            var terrain = go.GetComponent<Terrain>();
            if (GraphicsSettings.currentRenderPipeline != null)
                terrain.materialTemplate = GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial;
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 400f;
            return terrain;
        }

        /// <summary>
        /// Rolling hills in the south, a winding valley through the middle,
        /// and a ridged mountain range rising toward the north (+Z).
        /// </summary>
        static float[,] GenerateHeights(int resolution)
        {
            var random = new System.Random(Seed);
            float ox = random.Next(0, 10000), oz = random.Next(0, 10000);
            var heights = new float[resolution, resolution];

            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                float u = x / (resolution - 1f);
                float v = z / (resolution - 1f);

                float hills = Fbm(u * 4f + ox, v * 4f + oz, 5);
                float ridges = RidgedFbm(u * 3f + ox + 50f, v * 3f + oz + 50f, 5);
                float mountainMask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.95f, v));

                float height = 0.12f + 0.14f * hills + mountainMask * 0.6f * ridges;

                // A valley that winds south to north, fading out where the mountains begin.
                float valleyShape = Mathf.SmoothStep(0f, 1f, Mathf.Abs(u - ValleyCentre(v)) / 0.09f);
                height = Mathf.Lerp(height * Mathf.Lerp(0.55f, 1f, mountainMask), height, valleyShape);

                heights[z, x] = Mathf.Clamp01(height);
            }
            return heights;
        }

        /// <summary>Normalised east-west position of the valley floor at normalised north-south position <paramref name="v"/>.</summary>
        static float ValleyCentre(float v) => 0.5f + 0.12f * Mathf.Sin(v * 7f) + 0.05f * Mathf.Sin(v * 17f + 1f);

        static float[,,] GenerateSplatmap(TerrainData data)
        {
            int res = data.alphamapResolution;
            int layerCount = data.terrainLayers.Length;
            var splat = new float[res, res, layerCount];

            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float u = x / (res - 1f);
                float v = z / (res - 1f);
                float steepness = data.GetSteepness(u, v);
                float height01 = data.GetInterpolatedHeight(u, v) / data.size.y;
                float patchNoise = Mathf.PerlinNoise(u * 40f + 7f, v * 40f + 3f);

                float rock = Mathf.InverseLerp(28f, 40f, steepness);
                float snow = Mathf.InverseLerp(0.55f, 0.65f, height01) * (1f - rock);
                float dirt = Mathf.InverseLerp(0.55f, 0.75f, patchNoise) * (1f - rock) * (1f - snow);
                float grass = Mathf.Max(0f, 1f - rock - snow - dirt);

                float total = grass + dirt + rock + snow;
                splat[z, x, 0] = grass / total;
                splat[z, x, 1] = dirt / total;
                splat[z, x, 2] = rock / total;
                splat[z, x, 3] = snow / total;
            }
            return splat;
        }

        static float Fbm(float x, float z, int octaves)
        {
            float sum = 0f, amplitude = 0.5f, frequency = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amplitude * Mathf.PerlinNoise(x * frequency, z * frequency);
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }
            return sum / norm;
        }

        static float RidgedFbm(float x, float z, int octaves)
        {
            float sum = 0f, amplitude = 0.5f, frequency = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(x * frequency, z * frequency) * 2f - 1f);
                sum += amplitude * ridge * ridge;
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }
            return sum / norm;
        }

        // ---------- Placeholder textures ----------

        static TerrainLayer CreateTerrainLayer(string layerName, Color baseColor, float variation, float tileSize)
        {
            string texturePath = $"{GeneratedFolder}/{layerName}.png";
            File.WriteAllBytes(texturePath, CreateTileableNoiseTexture(baseColor, variation, 256).EncodeToPNG());
            AssetDatabase.ImportAsset(texturePath);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            string layerPath = $"{GeneratedFolder}/{layerName}.terrainlayer";
            AssetDatabase.DeleteAsset(layerPath);
            var layer = new TerrainLayer { diffuseTexture = texture, tileSize = new Vector2(tileSize, tileSize) };
            AssetDatabase.CreateAsset(layer, layerPath);
            return layer;
        }

        static Texture2D CreateTileableNoiseTexture(Color baseColor, float variation, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float n = 0.5f * TileableValueNoise(u, v, 8) + 0.3f * TileableValueNoise(u, v, 32)
                          + 0.2f * TileableValueNoise(u, v, 64);
                float shade = 1f + (n - 0.5f) * 2f * variation;
                pixels[y * size + x] = baseColor * shade;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Value noise on a lattice that wraps every <paramref name="cells"/> cells, so it tiles seamlessly.</summary>
        static float TileableValueNoise(float u, float v, int cells)
        {
            float x = u * cells, y = v * cells;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = Mathf.SmoothStep(0f, 1f, x - x0), ty = Mathf.SmoothStep(0f, 1f, y - y0);

            float Lattice(int ix, int iy)
            {
                ix = ((ix % cells) + cells) % cells;
                iy = ((iy % cells) + cells) % cells;
                uint h = (uint)(ix * 374761393 + iy * 668265263 + cells * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h ^ (h >> 16)) / (float)uint.MaxValue;
            }

            float a = Mathf.Lerp(Lattice(x0, y0), Lattice(x0 + 1, y0), tx);
            float b = Mathf.Lerp(Lattice(x0, y0 + 1), Lattice(x0 + 1, y0 + 1), tx);
            return Mathf.Lerp(a, b, ty);
        }

        // ---------- Lighting & atmosphere ----------

        static Light CreateDirectionalLight(string lightName, Color color, float intensity, LightShadows shadows)
        {
            var light = new GameObject(lightName).AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows;
            return light;
        }

        static void SetUpSkyAndFog(Light sun)
        {
            string skyboxPath = GeneratedFolder + "/ProceduralSky.mat";
            var skybox = AssetDatabase.LoadAssetAtPath<Material>(skyboxPath);
            if (skybox == null)
            {
                skybox = new Material(Shader.Find("Skybox/Procedural"));
                skybox.SetFloat("_SunSize", 0.03f);
                skybox.SetFloat("_AtmosphereThickness", 1.1f);
                AssetDatabase.CreateAsset(skybox, skyboxPath);
            }

            RenderSettings.skybox = skybox;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0018f;
        }

        static void CreatePostProcessingVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
                return;
            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        // ---------- Player ----------

        static FirstPersonController CreatePlayer(Terrain terrain)
        {
            var player = new GameObject("Player");
            player.tag = "Player";

            // Start in the southern hills, looking north toward the mountains.
            var spawn = new Vector3(0f, 0f, -TerrainSize * 0.35f);
            spawn.y = terrain.SampleHeight(spawn) + terrain.transform.position.y + 0.1f;
            player.transform.position = spawn;

            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.4f;

            var pivot = new GameObject("Camera Pivot").transform;
            pivot.SetParent(player.transform, false);
            pivot.localPosition = new Vector3(0f, 1.68f, 0f);

            var camera = pivot.gameObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 3000f;
            camera.fieldOfView = 70f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            pivot.gameObject.AddComponent<AudioListener>();

            var fpc = player.AddComponent<FirstPersonController>();
            SetField(fpc, "cameraPivot", pivot);
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (inputActions == null)
                Debug.LogWarning($"Couldn't find {InputActionsPath}; assign Input Actions on the Player manually.");
            SetField(fpc, "inputActions", inputActions);
            return fpc;
        }

        // ---------- Navigation points ----------

        static void CreateNavigationPoints(Terrain terrain, Vector3 spawn)
        {
            float half = TerrainSize / 2f;
            Material stone = GetOrCreateMaterial("CairnStone", new Color(0.45f, 0.44f, 0.42f));
            Material flag = GetOrCreateMaterial("MarkerFlag", new Color(1f, 0.45f, 0.05f));
            var parent = new GameObject("Navigation Points").transform;

            // Start point, already visited.
            CreateNavigationPoint("Trailhead", spawn + new Vector3(4f, 0f, 6f), terrain, parent, stone, flag, visited: true);

            // On the valley floor, about halfway north.
            const float valleyV = 0.45f;
            var valley = new Vector3(ValleyCentre(valleyV) * TerrainSize - half, 0f, valleyV * TerrainSize - half);
            CreateNavigationPoint("Valley Crossing", valley, terrain, parent, stone, flag);

            // Out in the western hills.
            CreateNavigationPoint("Aspen Meadow", new Vector3(-TerrainSize * 0.3f, 0f, -TerrainSize * 0.08f), terrain, parent, stone, flag);

            // The lowest point along a line through the mountains: a natural pass.
            float passZ = TerrainSize * 0.28f;
            var pass = new Vector3(0f, 0f, passZ);
            float lowest = float.MaxValue;
            for (float x = -half * 0.8f; x <= half * 0.8f; x += 10f)
            {
                float h = terrain.SampleHeight(new Vector3(x, 0f, passZ));
                if (h < lowest)
                {
                    lowest = h;
                    pass.x = x;
                }
            }
            CreateNavigationPoint("North Pass", pass, terrain, parent, stone, flag);
        }

        /// <summary>A stone cairn with a tall orange flag, visible from a distance.</summary>
        static void CreateNavigationPoint(string pointName, Vector3 position, Terrain terrain, Transform parent,
            Material stone, Material flag, bool visited = false)
        {
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
            var root = new GameObject(pointName);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            var point = root.AddComponent<NavigationPoint>();
            SetString(point, "displayName", pointName);
            SetBool(point, "visited", visited);

            float[] stoneSizes = { 1.1f, 0.8f, 0.55f };
            float y = 0f;
            foreach (float size in stoneSizes)
            {
                y += size * 0.4f;
                CreatePart(PrimitiveType.Sphere, root.transform, new Vector3(0f, y, 0f), new Vector3(size, size * 0.8f, size), stone, keepCollider: true);
                y += size * 0.4f;
            }

            CreatePart(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 3.5f, 0f), new Vector3(0.06f, 3.5f, 0.06f), stone, keepCollider: false);
            CreatePart(PrimitiveType.Cube, root.transform, new Vector3(0.6f, 6.4f, 0f), new Vector3(1.2f, 0.75f, 0.03f), flag, keepCollider: false);
        }

        static void CreatePart(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Material material, bool keepCollider)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
                Object.DestroyImmediate(part.GetComponent<Collider>());
        }

        static Material GetOrCreateMaterial(string materialName, Color colour, float smoothness = 0.15f)
        {
            string path = $"{GeneratedFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ---------- Helpers ----------

        static void SetField(Object target, string fieldName, Object value) =>
            Modify(target, fieldName, property => property.objectReferenceValue = value);

        static void SetString(Object target, string fieldName, string value) =>
            Modify(target, fieldName, property => property.stringValue = value);

        static void SetBool(Object target, string fieldName, bool value) =>
            Modify(target, fieldName, property => property.boolValue = value);

        static void Modify(Object target, string fieldName, System.Action<SerializedProperty> apply)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"No serialized field '{fieldName}' on {target.GetType().Name}.");
                return;
            }
            apply(property);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void AddSceneToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
