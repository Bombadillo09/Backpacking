using System;
using Backpacking.Camp;
using Backpacking.Gathering;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Saving;
using Backpacking.Survival;
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
            SetField(interactor, "saves", saves);

            var rescue = go.AddComponent<Rescue>();
            SetField(rescue, "vitals", vitals);
            SetField(rescue, "activity", activity);
            SetField(rescue, "player", player);
            SetField(rescue, "backpack", backpack);
            SetField(rescue, "timeOfDay", timeOfDay);
            SetField(rescue, "saves", saves);

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
        }

        // ---------- Prefabs ----------

        struct CampPrefabs
        {
            public GameObject Tent, FireRing, Stove, Firewood, Snare, BerryBush, TradingPost;
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
                Tent = GetOrCreatePrefab("Tent", BuildTent),
                FireRing = GetOrCreatePrefab("Fire Ring", BuildFireRing),
                Stove = GetOrCreatePrefab("Camp Stove", BuildStove),
                Firewood = GetOrCreatePrefab("Firewood", BuildFirewood),
                Snare = GetOrCreatePrefab("Snare", BuildSnare),
                BerryBush = GetOrCreatePrefab("Berry Bush", BuildBerryBush),
                TradingPost = GetOrCreatePrefab("Trading Post", BuildTradingPost),
            };
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
            Material metal = GetOrCreateMaterial("Metal", new Color(0.6f, 0.6f, 0.62f), 0.6f);
            Material fur = GetOrCreateMaterial("RabbitFur", new Color(0.45f, 0.38f, 0.3f));

            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0f, 0.15f, 0f), Quaternion.identity, new Vector3(0.025f, 0.15f, 0.025f), wood);
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0f, 0.12f, 0.1f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.16f, 0.004f, 0.16f), metal);

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
        static GameObject BuildTent()
        {
            var root = new GameObject();
            root.AddComponent<Tent>();
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.55f, 0f);
            collider.size = new Vector3(1.4f, 1.1f, 2.2f);

            Material fabric = GetOrCreateMaterial("TentFabric", new Color(0.2f, 0.42f, 0.3f));
            Material floor = GetOrCreateMaterial("TentFloor", new Color(0.15f, 0.15f, 0.16f));
            Material pole = GetOrCreateMaterial("Metal", new Color(0.6f, 0.6f, 0.62f), 0.6f);

            // Walls lean in from 0.7 m either side to meet at a 1.1 m ridge.
            float lean = Mathf.Atan2(0.7f, 1.1f) * Mathf.Rad2Deg;
            float wallLength = Mathf.Sqrt(0.7f * 0.7f + 1.1f * 1.1f);
            AddVisual(PrimitiveType.Cube, root, new Vector3(0f, 0.01f, 0f), Quaternion.identity, new Vector3(1.45f, 0.02f, 2.25f), floor);
            AddVisual(PrimitiveType.Cube, root, new Vector3(-0.35f, 0.55f, 0f), Quaternion.Euler(0f, 0f, -lean), new Vector3(0.03f, wallLength, 2.2f), fabric);
            AddVisual(PrimitiveType.Cube, root, new Vector3(0.35f, 0.55f, 0f), Quaternion.Euler(0f, 0f, lean), new Vector3(0.03f, wallLength, 2.2f), fabric);
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0f, 1.1f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.03f, 1.15f, 0.03f), pole);
            return root;
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

            Material canister = GetOrCreateMaterial("GasCanister", new Color(0.15f, 0.3f, 0.55f), 0.5f);
            Material metal = GetOrCreateMaterial("Metal", new Color(0.6f, 0.6f, 0.62f), 0.6f);

            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0f, 0.05f, 0f), Quaternion.identity, new Vector3(0.11f, 0.05f, 0.11f), canister);
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0f, 0.13f, 0f), Quaternion.identity, new Vector3(0.05f, 0.03f, 0.05f), metal);
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0f, 0.22f, 0f), Quaternion.identity, new Vector3(0.15f, 0.06f, 0.15f), metal);
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
    }
}
