using System.IO;
using System.Linq;
using Backpacking.Navigation;
using Backpacking.Player;
using Backpacking.UI;
using Backpacking.Wildlife;
using Backpacking.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
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
        // Large enough that trading posts are a few days' hike apart.
        const float TerrainSize = 5000f;
        const float TerrainHeight = 700f;
        const int HeightmapResolution = 2049;
        const int SplatResolution = 1024;
        const int Seed = 1234;

        [MenuItem("Backpacking/Build Prototype Scene")]
        public static void Build() => Build(interactive: true);

        /// <summary>
        /// Without <paramref name="interactive"/>, rebuilds straight away with no prompts, discarding unsaved
        /// changes to open scenes (the prototype scene is generated, so there's nothing to keep in it).
        /// </summary>
        public static void Build(bool interactive)
        {
            if (interactive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (interactive && File.Exists(ScenePath) && !EditorUtility.DisplayDialog(
                    "Rebuild prototype scene?",
                    $"{ScenePath} already exists and will be replaced, along with its generated terrain.",
                    "Rebuild", "Cancel"))
                return;

            EnsureFolder(Root + "/Scenes");
            EnsureFolder(GeneratedFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CampPrefabs prefabs = GetOrCreateCampPrefabs();
            CoopSetup.EnsurePrefabs();
            BiomeArtSettings art = LoadBiomeArt();
            EditorUtility.DisplayProgressBar("Building prototype scene", "Generating terrain...", 0.2f);
            Terrain terrain;
            RouteLayout route;
            try
            {
                terrain = CreateTerrain(art, out route);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            CreateRoute(route, terrain, prefabs.TradingPost);
            CreateTrail(terrain, art);
            CreateRivers(terrain);
            CreateDrive(terrain);
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

            // The trip starts at home; the tutorial's firewood still lies around the trailhead.
            Vector3 trailheadSpawn = SpawnPosition(route, terrain);
            FirstPersonController player = CreatePlayer(HomeSpawn(terrain, out Quaternion facing) ?? trailheadSpawn);
            player.transform.rotation = facing;

            var weather = world.AddComponent<WeatherSystem>();
            SetField(weather, "timeOfDay", timeOfDay);
            SetField(weather, "rainEffect", CreateRain(player.transform));
            SetField(weather, "snowEffect", CreateSnow(player.transform));
            SetField(weather, "fogWisps", CreateFogWisps(player.transform));
            SetField(weather, "temperature", temperature);
            SetField(weather, "player", player.transform);
            var cloudDome = world.AddComponent<CloudDome>();
            SetField(cloudDome, "timeOfDay", timeOfDay);
            SetField(cloudDome, "material", GetOrCreateCloudDomeMaterial());
            var treeWind = world.AddComponent<TreeWind>();
            SetField(treeWind, "weather", weather);
            var snowCover = world.AddComponent<SnowCover>();
            SetField(snowCover, "weather", weather);
            SetField(temperature, "weather", weather);

            var atmosphere = world.AddComponent<ForestAtmosphere>();
            SetField(atmosphere, "timeOfDay", timeOfDay);
            SetField(atmosphere, "weather", weather);
            SetField(atmosphere, "player", player.transform);
            SetField(atmosphere, "motes", CreateMotes(player.transform));
            SetField(atmosphere, "haze", CreateForestHaze(player.transform));

            var navigation = new GameObject("Navigation");
            var map = navigation.AddComponent<MapView>();
            SetField(map, "terrain", terrain);
            SetField(map, "player", player.transform);
            var compass = navigation.AddComponent<Compass>();
            SetField(compass, "holder", player.transform);

            CreateGameUI();
            var hud = new GameObject("Prototype HUD").AddComponent<PrototypeHud>();
            SetField(hud, "timeOfDay", timeOfDay);
            SetField(hud, "temperature", temperature);
            SetField(hud, "player", player);
            SetField(hud, "weather", weather);

            AddSurvivalSystems(player, timeOfDay, temperature, weather, hud.gameObject, prefabs);
            var lightning = world.AddComponent<LightningStorm>();
            SetField(lightning, "weather", weather);
            SetField(lightning, "timeOfDay", timeOfDay);
            SetField(lightning, "atmosphere", atmosphere);
            SetField(lightning, "ambience", Object.FindAnyObjectByType<Backpacking.Audio.AmbienceAudio>());
            SetField(lightning, "vitals", Object.FindAnyObjectByType<Backpacking.Survival.Vitals>());
            SetField(lightning, "player", player.transform);
            SetField(lightning, "boltMaterial", GetOrCreateLightningMaterial());
            var wildlife = world.AddComponent<WildlifeSpawner>();
            SetField(wildlife, "player", player);
            SetField(wildlife, "timeOfDay", timeOfDay);
            SetField(wildlife, "rabbitPrefab", art.rabbitModel);
            SetField(wildlife, "deerPrefab", art.deerModel);
            SetField(wildlife, "birdPrefab", art.birdModel);
            SetField(wildlife, "weather", weather);
            SetField(wildlife, "temperature", temperature);
            // The built-in rabbit's fur is made from a plain lit material.
            SetField(wildlife, "rabbitMaterial", GetOrCreateMaterial("Animal Fur", Color.white, 0.1f));
            SetObjectArray(wildlife, "songbirdPrefabs", art.songbirdModels ?? new GameObject[0]);
            SetField(wildlife, "squirrelPrefab", art.squirrelModel);
            SetField(wildlife, "butterflyPrefab", art.butterflyModel);
            ScatterGatherables(terrain, route, trailheadSpawn, prefabs, art);
            CreatePickup(terrain, player);
            ScatterBoulders(terrain, route, art);
            DressCliffs(terrain, route, art);
            ScatterFallenLogs(terrain, route, art);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            Debug.Log($"Built prototype scene at {ScenePath}. Press Play to walk around.");
        }

        // ---------- Terrain ----------

        static Terrain CreateTerrain(BiomeArtSettings art, out RouteLayout route)
        {
            var data = new TerrainData
            {
                // Resolution must be set before size, or Unity rescales the size.
                heightmapResolution = HeightmapResolution,
                size = new Vector3(TerrainSize, TerrainHeight, TerrainSize),
                alphamapResolution = SplatResolution,
            };
            float[,] heights = GenerateHeights(data.heightmapResolution);
            route = PlanRoute(heights);
            PlanDrive(heights, route);
            currentTrail = PlanTrail(route);
            // Where the rivers will run, then the hills, mesas and cliffs (clear of them), then the channels cut.
            PlanRivers(heights, route);
            ShapeLandforms(heights, route);
            CarveRivers(heights, route);
            data.SetHeights(0, 0, heights);
            DressTerrain(data, route, art);

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
            // Draws the terrain's patches with GPU instancing: far fewer draw calls for the ground.
            terrain.drawInstanced = true;
            terrain.basemapDistance = 400f;
            ApplyNatureDrawSettings(terrain, art);
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
            // A little haze in the air even on clear days; the woods and mist thicken it at runtime.
            RenderSettings.fogDensity = 0.0026f;
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

        static FirstPersonController CreatePlayer(Vector3 spawn)
        {
            var player = new GameObject("Player");
            player.tag = "Player";
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

        // ---------- UI ----------

        const string UIFolder = Root + "/UI";

        /// <summary>The UI Toolkit document all screens draw into, plus an event system so it gets Input System input.</summary>
        static void CreateGameUI()
        {
            var go = new GameObject("Game UI");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = GetOrCreatePanelSettings();
            var ui = go.AddComponent<GameUI>();
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UIFolder + "/Game.uss");
            if (styleSheet == null)
                Debug.LogWarning($"Couldn't find {UIFolder}/Game.uss; the UI will be unstyled.");
            SetField(ui, "styleSheet", styleSheet);

            var events = new GameObject("Event System");
            events.AddComponent<EventSystem>();
            events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        static PanelSettings GetOrCreatePanelSettings()
        {
            string path = UIFolder + "/GamePanelSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, path);
            }
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(UIFolder + "/GameTheme.tss");
            // Sizes in the stylesheet are for 1920 x 1080 and scale with the window.
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        // ---------- Navigation points ----------

        /// <summary>A stone cairn with a tall orange flag, visible from a distance.</summary>
        static GameObject CreateNavigationPoint(string pointName, NavigationPointKind kind, Vector3 position, Transform parent,
            Material stone, Material flag, bool visited)
        {
            var root = new GameObject(pointName);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            var point = root.AddComponent<NavigationPoint>();
            SetString(point, "displayName", pointName);
            SetEnum(point, "kind", (int)kind);
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
            return root;
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

        static void SetObjectArray(Object target, string fieldName, Object[] values) =>
            Modify(target, fieldName, property =>
            {
                property.arraySize = values.Length;
                for (int i = 0; i < values.Length; i++)
                    property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            });

        static void SetString(Object target, string fieldName, string value) =>
            Modify(target, fieldName, property => property.stringValue = value);

        static void SetBool(Object target, string fieldName, bool value) =>
            Modify(target, fieldName, property => property.boolValue = value);

        static void SetFloat(Object target, string fieldName, float value) =>
            Modify(target, fieldName, property => property.floatValue = value);

        static void SetEnum(Object target, string fieldName, int index) =>
            Modify(target, fieldName, property => property.enumValueIndex = index);

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
