using System;
using Backpacking.Audio;
using Backpacking.Camp;
using Backpacking.Character;
using Backpacking.Gathering;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Saving;
using Backpacking.Survival;
using Backpacking.Trip;
using Backpacking.UI;
using Backpacking.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Backpacking.EditorTools
{
    /// <summary>Camp gear prefabs, the lake, scattered firewood, and the player's survival systems.</summary>
    public static partial class PrototypeSceneBuilder
    {
        const string PrefabFolder = Root + "/Prefabs/Camp";
        const float LakeDepth = 1.3f;
        const int FirewoodCount = 450;
        const int BerryBushCount = 140;

        struct Lake
        {
            /// <summary>World position of the lake's centre at the water surface.</summary>
            public Vector3 Centre;
            public float Radius;
        }

        // ---------- Lakes ----------

        /// <summary>
        /// Digs a round bowl at a normalised map position. The water level sits just below the lowest point
        /// of the rim, so water never spills over the land.
        /// </summary>
        static Lake CarveLake(float[,] heights, float u, float v, float radiusMetres)
        {
            int resolution = heights.GetLength(0);
            float cx = u * (resolution - 1), cz = v * (resolution - 1);
            float radius = radiusMetres / TerrainSize * (resolution - 1);

            float rimLowest = float.MaxValue;
            for (int i = 0; i < 64; i++)
            {
                float angle = i / 64f * Mathf.PI * 2f;
                int x = Mathf.Clamp(Mathf.RoundToInt(cx + Mathf.Cos(angle) * radius), 0, resolution - 1);
                int z = Mathf.Clamp(Mathf.RoundToInt(cz + Mathf.Sin(angle) * radius), 0, resolution - 1);
                rimLowest = Mathf.Min(rimLowest, heights[z, x]);
            }

            float waterLevel = rimLowest - 0.3f / TerrainHeight;
            float floor = waterLevel - LakeDepth / TerrainHeight;
            ForEachSampleWithin(resolution, cx, cz, Mathf.CeilToInt(radius), (x, z, distance) =>
            {
                float t = distance / radius;
                if (t >= 1f)
                    return;
                float bowl = Mathf.Lerp(floor, heights[z, x], Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, t)));
                heights[z, x] = Mathf.Min(heights[z, x], bowl);
            });

            float half = TerrainSize / 2f;
            return new Lake
            {
                Centre = new Vector3(u * TerrainSize - half, waterLevel * TerrainHeight, v * TerrainSize - half),
                Radius = radiusMetres,
            };
        }

        static void CreateLake(Lake lake, Transform parent)
        {
            // A Unity plane is 10 x 10 units.
            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Lake";
            water.transform.SetParent(parent, false);
            water.transform.position = lake.Centre;
            float scale = lake.Radius * 2.2f / 10f;
            water.transform.localScale = new Vector3(scale, 1f, scale);
            water.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Water", new Color(0.12f, 0.24f, 0.3f), 0.95f);

            // Swap the solid collider for a thin trigger: the player can wade in, but can still target the water.
            Object.DestroyImmediate(water.GetComponent<Collider>());
            var trigger = water.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(10f, 0.1f, 10f);

            water.AddComponent<WaterSource>();
        }

        // ---------- Scattered props ----------

        /// <summary>
        /// Scatters prefab instances over gentle, lower ground along the route, away from lakes and the
        /// trading posts. The first <paramref name="nearSpawn"/> land close to the start so they're easy to find early on.
        /// </summary>
        static Transform Scatter(string groupName, GameObject prefab, int count, int nearSpawn, float nearRadius,
            float maxSteepness, float maxHeight01, int seed, Terrain terrain, RouteLayout route, Vector3 spawn,
            System.Func<Biome, float> likelihood)
        {
            var random = new System.Random(seed);
            var parent = new GameObject(groupName).transform;
            TerrainData data = terrain.terrainData;
            float half = TerrainSize / 2f;

            int placed = 0, attempts = 0;
            while (placed < count && attempts++ < count * 20)
            {
                Vector3 position = placed < nearSpawn
                    ? spawn + new Vector3((float)random.NextDouble() * 2f - 1f, 0f, (float)random.NextDouble() * 2f - 1f) * nearRadius
                    : RandomPointNearRoute(route, random);

                float u = (position.x + half) / TerrainSize, v = (position.z + half) / TerrainSize;
                if (u is < 0.01f or > 0.99f || v is < 0.01f or > 0.99f)
                    continue;
                if (data.GetSteepness(u, v) > maxSteepness || data.GetInterpolatedHeight(u, v) > TerrainHeight * maxHeight01)
                    continue;
                if (TooCloseToFeature(position, route, terrain))
                    continue;
                // Away from the start, some biomes are richer than others.
                if (placed >= nearSpawn && random.NextDouble() > likelihood(SampleBiome(data, u, v)))
                    continue;

                position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                AddSaveId(instance, $"{groupName}-{placed}");
                placed++;
            }
            return parent;
        }

        static bool TooCloseToFeature(Vector3 position, RouteLayout route, Terrain terrain)
        {
            foreach (Lake lake in route.Lakes)
            {
                Vector3 fromLake = position - lake.Centre;
                fromLake.y = 0f;
                if (fromLake.magnitude < lake.Radius + 3f)
                    return true;
            }
            foreach (RouteStop stop in route.Stops)
            {
                Vector3 fromStop = position - StopWorldPosition(stop, terrain);
                fromStop.y = 0f;
                if (fromStop.magnitude < (stop.Vendor != null ? PostFlattenRadius : 4f))
                    return true;
            }
            // Nothing lying on the path, the road or the lots.
            if (TrailDistance(position) < 2f || RoadDistance(position) < RoadHalfWidth + 2f)
                return true;
            if (InsideLot(new Vector2(position.x, position.z)) > -3f)
                return true;
            return false;
        }

        static void ScatterGatherables(Terrain terrain, RouteLayout route, Vector3 spawn, CampPrefabs prefabs, BiomeArtSettings art)
        {
            // Fallen wood is mostly in the forest. Berries like sunny meadows and forest edges.
            Transform firewood = Scatter("Firewood", prefabs.Firewood, FirewoodCount, nearSpawn: 15, nearRadius: 40f,
                maxSteepness: 25f, maxHeight01: 0.55f, Seed + 1, terrain, route, spawn,
                biome => 0.15f + 0.85f * biome.Forest);
            DressFirewood(firewood, art);
            Scatter("Berry Bushes", prefabs.BerryBush, BerryBushCount, nearSpawn: 4, nearRadius: 50f,
                maxSteepness: 20f, maxHeight01: 0.45f, Seed + 2, terrain, route, spawn,
                biome => 0.2f + 0.8f * Mathf.Clamp01(1f - Mathf.Abs(biome.Forest - 0.35f) * 2.5f));
        }

        // ---------- Player survival systems ----------

        static void AddSurvivalSystems(FirstPersonController player, TimeOfDay timeOfDay, AmbientTemperature temperature,
            WeatherSystem weather, GameObject hud, CampPrefabs prefabs)
        {
            GameObject go = player.gameObject;
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);

            var backpack = go.AddComponent<Backpack>();
            var vitals = go.AddComponent<Vitals>();
            var activity = go.AddComponent<PlayerActivity>();
            var placer = go.AddComponent<CampPlacer>();
            var interactor = go.AddComponent<Interactor>();
            var fishing = go.AddComponent<FishingSession>();

            SetField(backpack, "vitals", vitals);
            SetField(backpack, "timeOfDay", timeOfDay);
            SetField(backpack, "temperature", temperature);

            SetField(fishing, "inputActions", inputActions);
            SetField(fishing, "timeOfDay", timeOfDay);
            SetField(fishing, "backpack", backpack);

            SetField(vitals, "timeOfDay", timeOfDay);
            SetField(vitals, "temperature", temperature);
            SetField(vitals, "player", player);
            SetField(vitals, "backpack", backpack);
            SetField(vitals, "weather", weather);

            SetField(activity, "timeOfDay", timeOfDay);
            SetField(activity, "vitals", vitals);

            SetField(placer, "inputActions", inputActions);
            SetField(placer, "viewPoint", player.CameraPivot);
            SetField(placer, "backpack", backpack);
            SetField(placer, "activity", activity);
            SetField(placer, "previewMaterial", GetOrCreatePreviewMaterial());
            SetField(placer, "tentPrefab", prefabs.Tent);
            SetField(placer, "fireRingPrefab", prefabs.FireRing);
            SetField(placer, "stovePrefab", prefabs.Stove);
            SetField(placer, "snarePrefab", prefabs.Snare);
            SetField(placer, "chairPrefab", prefabs.Chair);
            SetField(placer, "clearingPrefab", prefabs.ClearingMarker);
            var packHandling = go.AddComponent<PackHandling>();
            SetField(packHandling, "backpack", backpack);
            SetField(packHandling, "groundPackPrefab", prefabs.GroundPack);
            SetField(packHandling, "tentBagPrefab", prefabs.TentBag);
            var clearing = go.AddComponent<GroundClearing>();
            SetIntArray(clearing, "brushLayers", brushDetailLayers);
            SetFloatArray(clearing, "brushWeights", brushDetailWeights);
            SetIntArray(clearing, "deadfallLayers", deadfallDetailLayers);
            var undergrowth = go.AddComponent<Undergrowth>();
            SetField(undergrowth, "inputActions", inputActions);
            SetField(undergrowth, "player", player);
            SetField(undergrowth, "clearing", clearing);
            SetField(undergrowth, "backpack", backpack);
            SetField(undergrowth, "vitals", vitals);
            SetField(undergrowth, "placer", placer);
            var hotbar = go.AddComponent<Hotbar>();
            SetField(hotbar, "inputActions", inputActions);
            SetField(hotbar, "backpack", backpack);
            SetField(hotbar, "vitals", vitals);
            SetField(hotbar, "activity", activity);
            SetField(hotbar, "placer", placer);
            SetField(placer, "clearing", clearing);
            SetField(placer, "vitals", vitals);

            SetField(interactor, "inputActions", inputActions);
            SetField(interactor, "viewPoint", player.CameraPivot);
            SetField(interactor, "backpack", backpack);
            SetField(interactor, "vitals", vitals);
            SetField(interactor, "activity", activity);
            SetField(interactor, "placer", placer);
            SetField(interactor, "fishing", fishing);

            var shop = hud.AddComponent<ShopView>();
            SetField(shop, "backpack", backpack);
            SetField(interactor, "shop", shop);

            var saves = new GameObject("Save System").AddComponent<SaveSystem>();
            SetField(saves, "timeOfDay", timeOfDay);
            SetField(saves, "player", player);
            SetField(saves, "backpack", backpack);
            SetField(saves, "vitals", vitals);
            SetField(saves, "placer", placer);
            SetField(saves, "activity", activity);
            SetField(saves, "weather", weather);
            SetField(saves, "packHandling", packHandling);
            SetField(interactor, "saves", saves);

            var rescue = go.AddComponent<Rescue>();
            SetField(rescue, "vitals", vitals);
            SetField(rescue, "activity", activity);
            SetField(rescue, "player", player);
            SetField(rescue, "backpack", backpack);
            SetField(rescue, "timeOfDay", timeOfDay);
            SetField(rescue, "saves", saves);

            var menus = hud.AddComponent<GameMenus>();
            SetField(menus, "saves", saves);

            // The hiker: a body you can see, built from the character chosen at the start of a trip.
            CharacterLibrary library = CharacterSetup.GetOrCreateLibrary();
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.transform.SetParent(go.transform, false);
            var appearance = avatarRoot.AddComponent<CharacterAppearance>();
            SetField(appearance, "library", library);
            var avatar = go.AddComponent<PlayerAvatar>();
            SetField(avatar, "appearance", appearance);
            SetField(avatar, "player", player);
            SetField(avatar, "activity", activity);
            // What's on the hotbar, shown in the hiker's hand.
            var held = go.AddComponent<HeldItemView>();
            SetField(held, "hotbar", go.GetComponent<Hotbar>());
            SetField(held, "library", HeldItemSetup.GetOrCreateLibrary());
            SetField(held, "player", player);
            SetField(held, "appearance", appearance);
            SetField(held, "activity", activity);
            // The bow, when it's in hand: drawing, shooting and holding it out.
            var bow = go.AddComponent<Hunting.Bow>();
            SetField(bow, "inputActions", inputActions);
            SetField(bow, "player", player);
            SetField(bow, "appearance", appearance);
            SetField(bow, "backpack", backpack);
            SetField(bow, "vitals", vitals);
            SetField(bow, "activity", activity);
            SetField(bow, "placer", placer);
            var creator = hud.AddComponent<CharacterCreator>();
            SetField(creator, "library", library);
            SetField(menus, "creator", creator);
            SetField(menus, "avatar", avatar);
            SetField(menus, "backpack", backpack);
            SetField(saves, "avatar", avatar);

            var trip = go.AddComponent<TripLog>();
            SetField(trip, "timeOfDay", timeOfDay);
            SetField(trip, "player", player);
            SetField(trip, "vitals", vitals);
            SetField(trip, "activity", activity);
            SetField(trip, "temperature", temperature);
            SetField(trip, "weather", weather);
            SetField(saves, "trip", trip);
            SetField(saves, "clearing", go.GetComponent<GroundClearing>());
            var journal = hud.AddComponent<JournalView>();
            SetField(journal, "timeOfDay", timeOfDay);
            SetField(journal, "saves", saves);
            SetField(journal, "vitals", vitals);
            SetField(journal, "backpack", backpack);

            var rest = go.AddComponent<RestMode>();
            SetField(rest, "inputActions", inputActions);
            SetField(rest, "player", player);
            SetField(rest, "vitals", vitals);
            SetField(rest, "activity", activity);

            var tutorial = hud.AddComponent<Tutorial>();
            SetField(tutorial, "player", player);
            SetField(tutorial, "backpack", backpack);
            SetField(tutorial, "map", Object.FindAnyObjectByType<Navigation.MapView>());
            SetField(tutorial, "compass", Object.FindAnyObjectByType<Navigation.Compass>());
            SetField(tutorial, "placer", placer);
            SetField(tutorial, "clearing", go.GetComponent<GroundClearing>());
            SetField(menus, "tutorial", tutorial);
            SetField(saves, "tutorial", tutorial);

            go.AddComponent<HeadBob>();
            go.AddComponent<Footsteps>();
            var ambience = go.AddComponent<AmbienceAudio>();
            SetField(ambience, "timeOfDay", timeOfDay);
            SetField(ambience, "weather", weather);
            SetField(ambience, "temperature", temperature);
            SetField(ambience, "vitals", vitals);

            var effects = hud.AddComponent<ConditionEffects>();
            SetField(effects, "vitals", vitals);
            SetField(effects, "player", player);
            SetField(effects, "activity", activity);

            var vitalsHud = hud.AddComponent<VitalsHud>();
            SetField(vitalsHud, "vitals", vitals);
            SetField(vitalsHud, "backpack", backpack);
            var backpackView = hud.AddComponent<BackpackView>();
            SetField(backpackView, "backpack", backpack);
            SetField(backpackView, "vitals", vitals);
            SetField(backpackView, "placer", placer);
            SetField(backpackView, "activity", activity);
            SetField(backpackView, "packHandling", go.GetComponent<PackHandling>());
            // Pictures of every item, rendered from their models (needs the gear prefabs, made above).
            ItemIconLibrary icons = ItemIcons.GetOrCreateLibrary();
            SetField(backpackView, "icons", icons);
            SetField(go.GetComponent<Hotbar>(), "icons", icons);
        }

        // ---------- Prefabs ----------

        struct CampPrefabs
        {
            public GameObject Tent, FireRing, Stove, Firewood, Snare, BerryBush, TradingPost, ClearingMarker, GroundPack, TentBag, Chair;
        }

        /// <summary>
        /// Placeholder gear built from primitives. Existing prefabs are left alone, so they can be edited
        /// or replaced with real art; delete one to have it regenerated.
        /// </summary>
        static CampPrefabs GetOrCreateCampPrefabs()
        {
            EnsureFolder(PrefabFolder);
            return new CampPrefabs
            {
                // Renamed when the tent became staged, so the old single-piece prefab isn't reused.
                Tent = GetOrCreatePrefab("Tent (staged)", BuildTent),
                // Renamed when the pack became a detailed model, so the old block-built prefab isn't reused.
                GroundPack = GetOrCreatePrefab("Ground Pack (detailed)", BuildGroundPack),
                TentBag = GetOrCreatePrefab("Tent Bag (detailed)", BuildTentBag),
                Chair = GetOrCreatePrefab("Camp Chair", BuildChair),
                FireRing = GetOrCreatePrefab("Fire Ring", BuildFireRing),
                // Renamed when the gear got detailed models, so the old primitive-built prefabs aren't reused.
                Stove = GetOrCreatePrefab("Camp Stove (detailed)", BuildStove),
                Firewood = GetOrCreatePrefab("Firewood", BuildFirewood),
                Snare = GetOrCreatePrefab("Snare (detailed)", BuildSnare),
                BerryBush = GetOrCreatePrefab("Berry Bush", BuildBerryBush),
                TradingPost = GetOrCreatePrefab("Trading Post", BuildTradingPost),
                ClearingMarker = GetOrCreatePrefab("Clearing Marker", BuildClearingMarker),
            };
        }

        /// <summary>A flat disc the size of the area the machete clears, shown while choosing where.</summary>
        static GameObject BuildClearingMarker()
        {
            var root = new GameObject();
            // A cylinder primitive is 2 units tall; this one is a thin 8 m wide disc.
            CreatePart(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(8f, 0.03f, 8f),
                GetOrCreateMaterial("ClearingMarker", new Color(0.4f, 0.8f, 0.4f)), keepCollider: false);
            return root;
        }

        /// <summary>A stake with a wire loop. Its "Caught" child (a rabbit) is shown when something is caught.</summary>
        static GameObject BuildSnare()
        {
            var root = new GameObject();
            var snare = root.AddComponent<Snare>();
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.15f, 0f);
            collider.size = new Vector3(0.45f, 0.3f, 0.45f);

            Material wood = GetOrCreateMaterial("Wood", new Color(0.33f, 0.22f, 0.13f));
            Material brass = GetOrCreateMaterial("BrassWire", new Color(0.72f, 0.56f, 0.28f), 0.7f);
            Material fur = GetOrCreateMaterial("RabbitFur", new Color(0.45f, 0.38f, 0.3f));
            GearLibrary gear = GearSetup.GetOrCreateLibrary();
            MeshVisual("Stake", root, gear.stake, wood, Vector3.zero);
            MeshVisual("Noose", root, gear.noose, brass, Vector3.zero);

            var caught = new GameObject("Caught");
            caught.transform.SetParent(root.transform, false);
            AddVisual(PrimitiveType.Capsule, caught, new Vector3(0f, 0.08f, 0.18f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.16f, 0.14f, 0.16f), fur);
            AddVisual(PrimitiveType.Sphere, caught, new Vector3(0f, 0.09f, 0.33f), Quaternion.identity, new Vector3(0.1f, 0.1f, 0.11f), fur);
            AddVisual(PrimitiveType.Capsule, caught, new Vector3(-0.025f, 0.12f, 0.39f), Quaternion.Euler(75f, 0f, 0f), new Vector3(0.025f, 0.05f, 0.02f), fur);
            AddVisual(PrimitiveType.Capsule, caught, new Vector3(0.025f, 0.12f, 0.39f), Quaternion.Euler(75f, 0f, 0f), new Vector3(0.025f, 0.05f, 0.02f), fur);
            caught.SetActive(false);

            SetField(snare, "caughtVisual", caught);
            return root;
        }

        static GameObject BuildBerryBush()
        {
            var root = new GameObject();
            var bush = root.AddComponent<BerryBush>();
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.45f, 0f);
            collider.size = new Vector3(1.1f, 0.9f, 1.1f);

            Material leaves = GetOrCreateMaterial("BushLeaves", new Color(0.16f, 0.3f, 0.12f));
            Material berry = GetOrCreateMaterial("Berries", new Color(0.55f, 0.05f, 0.15f), 0.6f);

            AddVisual(PrimitiveType.Sphere, root, new Vector3(0f, 0.4f, 0f), Quaternion.identity, new Vector3(1f, 0.75f, 0.95f), leaves);
            AddVisual(PrimitiveType.Sphere, root, new Vector3(0.25f, 0.55f, 0.15f), Quaternion.identity, new Vector3(0.6f, 0.55f, 0.6f), leaves);
            AddVisual(PrimitiveType.Sphere, root, new Vector3(-0.2f, 0.5f, -0.2f), Quaternion.identity, new Vector3(0.65f, 0.5f, 0.6f), leaves);

            // Berries dotted over the outside of the bush.
            var berries = new GameObject("Berries");
            berries.transform.SetParent(root.transform, false);
            var random = new System.Random(7);
            for (int i = 0; i < 18; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float height = 0.2f + (float)random.NextDouble() * 0.5f;
                float radius = 0.5f * Mathf.Sqrt(1f - Mathf.Pow((height - 0.4f) / 0.45f, 2f)) + 0.02f;
                var position = new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
                AddVisual(PrimitiveType.Sphere, berries, position, Quaternion.identity, Vector3.one * 0.07f, berry);
            }

            SetField(bush, "berriesVisual", berries);
            return root;
        }

        static GameObject GetOrCreatePrefab(string prefabName, Func<GameObject> build)
        {
            string path = $"{PrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            GameObject instance = build();
            instance.name = prefabName;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        /// <summary>A two-person A-frame tent. The open door faces +Z.</summary>
        /// <summary>
        /// The tent: just its materials and a collider. Its shape for each model and stage (laid out, poles up,
        /// pitched) is generated at runtime by TentDesign.
        /// </summary>
        static GameObject BuildTent()
        {
            var root = new GameObject();
            var tent = root.AddComponent<Tent>();
            root.AddComponent<BoxCollider>();
            // Colours come per model at runtime, so the fabrics are white here.
            SetField(tent, "materials.inner", GetOrCreateMaterial("TentInner", Color.white, 0.2f));
            SetField(tent, "materials.fly", GetOrCreateMaterial("TentFly", Color.white, 0.35f));
            SetField(tent, "materials.floor", GetOrCreateMaterial("TentFloor", new Color(0.16f, 0.17f, 0.18f), 0.3f));
            SetField(tent, "materials.pole", GetOrCreateMaterial("TentPole", new Color(0.12f, 0.12f, 0.13f), 0.6f));
            SetField(tent, "materials.stake", GetOrCreateMaterial("Metal", new Color(0.6f, 0.6f, 0.62f), 0.6f));
            return root;
        }

        /// <summary>The camp chair: materials and a collider. Its shape for each stage is generated by CampChair.</summary>
        static GameObject BuildChair()
        {
            var root = new GameObject();
            var chair = root.AddComponent<CampChair>();
            root.AddComponent<BoxCollider>();
            SetField(chair, "frameMaterial", GetOrCreateMaterial("TentPole", new Color(0.12f, 0.12f, 0.13f), 0.6f));
            SetField(chair, "fabricMaterial", GetOrCreateMaterial("ChairFabric", Color.white, 0.25f));
            return root;
        }

        /// <summary>Your backpack standing on the ground: the trekking pack from PackDesign, built when it appears.</summary>
        static GameObject BuildGroundPack()
        {
            var root = new GameObject();
            var pack = root.AddComponent<GroundPack>();
            SetField(pack, "gear", GearSetup.GetOrCreateLibrary());
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.33f, 0.09f);
            collider.size = new Vector3(0.42f, 0.68f, 0.34f);
            return root;
        }

        /// <summary>The tent in its stuff sack: a fat cylinder with a cinched end and the pole bag strapped alongside.</summary>
        static GameObject BuildTentBag()
        {
            var root = new GameObject();
            root.AddComponent<TentBag>();
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.09f, 0f);
            collider.size = new Vector3(0.5f, 0.18f, 0.22f);
            // The sack is tinted to the tent's fly colour at runtime (renderers named "Sack...").
            GearLibrary gear = GearSetup.GetOrCreateLibrary();
            MeshVisual("Sack", root, gear.stuffSack, gear.pack.gearFabric, Vector3.zero);
            MeshVisual("Straps", root, gear.stuffSackStraps, gear.pack.webbing, Vector3.zero);
            return root;
        }

        /// <summary>A generated mesh as a visual part (no collider; the root's collider covers the piece).</summary>
        static void MeshVisual(string name, GameObject parent, Mesh mesh, Material material, Vector3 position)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = position;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static void NamedVisual(string name, PrimitiveType type, GameObject parent, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent.transform, false);
            part.transform.SetLocalPositionAndRotation(position, rotation);
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(part.GetComponent<Collider>());
        }

        static GameObject BuildFireRing()
        {
            var root = new GameObject();
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.15f, 0f);
            collider.size = new Vector3(1.2f, 0.3f, 1.2f);
            var heat = root.AddComponent<HeatSource>();
            var campfire = root.AddComponent<Campfire>();

            Material stone = GetOrCreateMaterial("CairnStone", new Color(0.45f, 0.44f, 0.42f));
            Material wood = GetOrCreateMaterial("Wood", new Color(0.33f, 0.22f, 0.13f));

            for (int i = 0; i < 9; i++)
            {
                float angle = i / 9f * Mathf.PI * 2f;
                var position = new Vector3(Mathf.Cos(angle) * 0.5f, 0.07f, Mathf.Sin(angle) * 0.5f);
                AddVisual(PrimitiveType.Sphere, root, position, Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f), new Vector3(0.26f, 0.18f, 0.22f), stone);
            }

            // Logs stacked in a teepee, leaning in toward the middle.
            var woodGroup = new GameObject("Wood");
            woodGroup.transform.SetParent(root.transform, false);
            for (int i = 0; i < 4; i++)
            {
                float angle = i / 4f * Mathf.PI * 2f + 0.4f;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Quaternion tilt = Quaternion.FromToRotation(Vector3.up, (Vector3.up - outward * 0.6f).normalized);
                AddVisual(PrimitiveType.Cylinder, woodGroup, outward * 0.12f + Vector3.up * 0.22f, tilt, new Vector3(0.07f, 0.28f, 0.07f), wood);
            }

            var lightObject = new GameObject("Fire Light");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            var fireLight = lightObject.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1f, 0.55f, 0.2f);
            fireLight.range = 10f;
            fireLight.intensity = 4f;
            fireLight.shadows = LightShadows.None;

            ParticleSystem flames = BuildFlames(root.transform);

            SetField(campfire, "woodVisual", woodGroup);
            SetField(campfire, "flames", flames);
            SetField(campfire, "fireLight", fireLight);
            SetField(campfire, "heat", heat);
            return root;
        }

        static ParticleSystem BuildFlames(Transform parent)
        {
            var go = new GameObject("Flames");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            // Cone emitters fire along local +Z; point it up.
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.35f), new Color(1f, 0.4f, 0.1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 150;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 45f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.12f;

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f), new GradientColorKey(new Color(0.9f, 0.25f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
            colour.enabled = true;
            colour.color = fade;

            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            if (GraphicsSettings.currentRenderPipeline != null)
                particleRenderer.sharedMaterial = GraphicsSettings.currentRenderPipeline.defaultParticleMaterial;
            return particles;
        }

        static GameObject BuildStove()
        {
            var root = new GameObject();
            root.AddComponent<CampStove>();
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.13f, 0f);
            collider.size = new Vector3(0.3f, 0.26f, 0.3f);

            GearLibrary gear = GearSetup.GetOrCreateLibrary();
            MeshVisual("Canister", root, gear.canister, gear.canisterPaint, Vector3.zero);
            MeshVisual("Burner", root, gear.burner, gear.aluminium, Vector3.zero);
            // The pot sits on the supports, ready to cook in.
            MeshVisual("Pot", root, gear.pot, gear.aluminium, new Vector3(0f, 0.154f, 0f));
            return root;
        }

        static GameObject BuildFirewood()
        {
            var root = new GameObject();
            root.AddComponent<FirewoodPickup>();
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.08f, 0f);
            collider.size = new Vector3(0.75f, 0.16f, 0.35f);

            Material wood = GetOrCreateMaterial("Wood", new Color(0.33f, 0.22f, 0.13f));
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0f, 0.04f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.05f, 0.34f, 0.05f), wood);
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0.05f, 0.09f, 0.05f), Quaternion.Euler(0f, 25f, 90f), new Vector3(0.04f, 0.28f, 0.04f), wood);
            return root;
        }

        /// <summary>Gives a scene object a stable name for save files.</summary>
        static void AddSaveId(GameObject target, string id)
        {
            var saveId = target.AddComponent<SaveId>();
            SetString(saveId, "id", id);
        }

        /// <summary>A primitive with no collider; the root object's collider covers the whole piece.</summary>
        static void AddVisual(PrimitiveType type, GameObject parent, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.transform.SetParent(parent.transform, false);
            part.transform.SetLocalPositionAndRotation(position, rotation);
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(part.GetComponent<Collider>());
        }

        /// <summary>Semi-transparent material for placement previews; tinted green or red at runtime.</summary>
        static Material GetOrCreatePreviewMaterial()
        {
            string path = $"{GeneratedFolder}/PlacementPreview.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(0.2f, 1f, 0.3f, 0.45f));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void SetFloatArray(Object target, string fieldName, float[] values) =>
            Modify(target, fieldName, property =>
            {
                property.arraySize = values.Length;
                for (int i = 0; i < values.Length; i++)
                    property.GetArrayElementAtIndex(i).floatValue = values[i];
            });

        static void SetIntArray(Object target, string fieldName, int[] values) =>
            Modify(target, fieldName, property =>
            {
                property.arraySize = values.Length;
                for (int i = 0; i < values.Length; i++)
                    property.GetArrayElementAtIndex(i).intValue = values[i];
            });
    }
}
