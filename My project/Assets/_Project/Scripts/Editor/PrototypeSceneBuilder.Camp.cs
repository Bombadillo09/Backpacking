using System;
using Backpacking.Camp;
using Backpacking.Interaction;
using Backpacking.Player;
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
        const float LakeRadius = 40f;
        const float LakeDepth = 1.3f;
        const int FirewoodCount = 260;

        struct Lake
        {
            /// <summary>World position of the lake's centre at the water surface.</summary>
            public Vector3 Centre;
            public float Radius;
        }

        // ---------- Lake ----------

        /// <summary>
        /// Digs a bowl in the valley a little north of the Valley Crossing. The water level sits just below
        /// the lowest point of the rim, so water never spills over the land.
        /// </summary>
        static Lake CarveLake(float[,] heights)
        {
            int resolution = heights.GetLength(0);
            const float v = 0.52f;
            float u = ValleyCentre(v);
            float cx = u * (resolution - 1), cz = v * (resolution - 1);
            float radiusSamples = LakeRadius / TerrainSize * (resolution - 1);

            float rimLowest = float.MaxValue;
            for (int i = 0; i < 64; i++)
            {
                float angle = i / 64f * Mathf.PI * 2f;
                int x = Mathf.RoundToInt(cx + Mathf.Cos(angle) * radiusSamples);
                int z = Mathf.RoundToInt(cz + Mathf.Sin(angle) * radiusSamples);
                rimLowest = Mathf.Min(rimLowest, heights[z, x]);
            }

            float waterLevel = rimLowest - 0.3f / TerrainHeight;
            float floor = waterLevel - LakeDepth / TerrainHeight;
            int reach = Mathf.CeilToInt(radiusSamples);
            for (int z = Mathf.FloorToInt(cz) - reach; z <= Mathf.CeilToInt(cz) + reach; z++)
            for (int x = Mathf.FloorToInt(cx) - reach; x <= Mathf.CeilToInt(cx) + reach; x++)
            {
                float distance = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) / radiusSamples;
                if (distance >= 1f)
                    continue;
                float bowl = Mathf.Lerp(floor, heights[z, x], Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, distance)));
                heights[z, x] = Mathf.Min(heights[z, x], bowl);
            }

            float half = TerrainSize / 2f;
            return new Lake
            {
                Centre = new Vector3(u * TerrainSize - half, waterLevel * TerrainHeight, v * TerrainSize - half),
                Radius = LakeRadius,
            };
        }

        static void CreateLake(Lake lake)
        {
            // A Unity plane is 10 x 10 units.
            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Lake";
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

        // ---------- Firewood ----------

        static void ScatterFirewood(Terrain terrain, Lake lake, Vector3 spawn, GameObject prefab)
        {
            var random = new System.Random(Seed + 1);
            var parent = new GameObject("Firewood").transform;
            TerrainData data = terrain.terrainData;
            float half = TerrainSize / 2f;

            int placed = 0, attempts = 0;
            while (placed < FirewoodCount && attempts++ < FirewoodCount * 20)
            {
                // A share of the wood lands near the start, so the first fire is easy to gather for.
                Vector3 position = placed < 15
                    ? spawn + new Vector3((float)random.NextDouble() * 60f - 30f, 0f, (float)random.NextDouble() * 60f - 30f)
                    : new Vector3((float)random.NextDouble() * TerrainSize * 0.94f - half * 0.94f, 0f,
                        (float)random.NextDouble() * TerrainSize * 0.94f - half * 0.94f);

                float u = (position.x + half) / TerrainSize, v = (position.z + half) / TerrainSize;
                if (data.GetSteepness(u, v) > 25f || data.GetInterpolatedHeight(u, v) > TerrainHeight * 0.5f)
                    continue;
                Vector3 fromLake = position - lake.Centre;
                fromLake.y = 0f;
                if (fromLake.magnitude < lake.Radius + 3f)
                    continue;

                position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
                var wood = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                wood.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                placed++;
            }
        }

        // ---------- Player survival systems ----------

        static void AddSurvivalSystems(FirstPersonController player, TimeOfDay timeOfDay, AmbientTemperature temperature,
            GameObject hud, CampPrefabs prefabs)
        {
            GameObject go = player.gameObject;
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);

            var backpack = go.AddComponent<Backpack>();
            var vitals = go.AddComponent<Vitals>();
            var activity = go.AddComponent<PlayerActivity>();
            var placer = go.AddComponent<CampPlacer>();
            var interactor = go.AddComponent<Interactor>();

            SetField(backpack, "vitals", vitals);

            SetField(vitals, "timeOfDay", timeOfDay);
            SetField(vitals, "temperature", temperature);
            SetField(vitals, "player", player);
            SetField(vitals, "backpack", backpack);

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

            SetField(interactor, "inputActions", inputActions);
            SetField(interactor, "viewPoint", player.CameraPivot);
            SetField(interactor, "backpack", backpack);
            SetField(interactor, "vitals", vitals);
            SetField(interactor, "activity", activity);
            SetField(interactor, "placer", placer);

            var vitalsHud = hud.AddComponent<VitalsHud>();
            SetField(vitalsHud, "vitals", vitals);
            var backpackView = hud.AddComponent<BackpackView>();
            SetField(backpackView, "backpack", backpack);
            SetField(backpackView, "vitals", vitals);
            SetField(backpackView, "placer", placer);
            SetField(backpackView, "activity", activity);
        }

        // ---------- Prefabs ----------

        struct CampPrefabs
        {
            public GameObject Tent, FireRing, Stove, Firewood;
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
            };
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
