using System.Collections.Generic;
using Backpacking.Navigation;
using Backpacking.Trade;
using Backpacking.Trip;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// The route through the map: trading posts a few days' hike apart, with small checkpoints between,
    /// some beside ponds for water. Posts get flattened ground and a trading post building.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        const float PondRadius = 25f;
        const float ValleyLakeRadius = 45f;
        const float PostFlattenRadius = 14f;
        const float SummitFlattenRadius = 5f;
        const float RouteCorridorHalfWidth = 250f;

        class RouteStop
        {
            public string Name;
            /// <summary>Normalised map position: u west→east, v south→north.</summary>
            public float U, V;
            public NavigationPointKind Kind;
            /// <summary>Radius of a lake beside the stop, or 0 for none.</summary>
            public float LakeRadius;
            public VendorSetup Vendor;
        }

        class VendorSetup
        {
            public float PriceMultiplier;
            public float SellRate;
            public (ShopItemId item, int quantity)[] Stock;
        }

        class RouteLayout
        {
            public readonly List<RouteStop> Stops = new();
            public readonly List<Lake> Lakes = new();
        }

        const int Unlimited = -1;

        static readonly VendorSetup OutfitterStock = new()
        {
            PriceMultiplier = 1f,
            SellRate = 0.6f,
            Stock = new[]
            {
                (ShopItemId.GasCanister, Unlimited), (ShopItemId.Matches, Unlimited), (ShopItemId.TrailMix, Unlimited),
                (ShopItemId.DehydratedMeal, Unlimited), (ShopItemId.Snare, Unlimited), (ShopItemId.Antibiotics, Unlimited),
                (ShopItemId.WaterBladder, 1), (ShopItemId.WaterFilter, 1), (ShopItemId.FishingRod, 1), (ShopItemId.WoolHatAndGloves, 1),
                (ShopItemId.LeatherBoots, 1), (ShopItemId.FoamMat, 3), (ShopItemId.InflatableMat, 1),
                (ShopItemId.Bandages, Unlimited), (ShopItemId.CampChair, 1),
                (ShopItemId.HuntingBow, 1), (ShopItemId.Arrows, Unlimited),
            },
        };

        static readonly VendorSetup ValleyStoreStock = new()
        {
            PriceMultiplier = 1.15f,
            SellRate = 0.8f,
            Stock = new[]
            {
                (ShopItemId.GasCanister, 6), (ShopItemId.Matches, 10), (ShopItemId.TrailMix, 12), (ShopItemId.DehydratedMeal, 8),
                (ShopItemId.Snare, 4), (ShopItemId.Antibiotics, 3), (ShopItemId.WaterFilter, 1), (ShopItemId.FishingRod, 1),
                (ShopItemId.InsulatedPants, 1), (ShopItemId.WinterSleepingBag, 1), (ShopItemId.LeatherBoots, 1),
                (ShopItemId.TwoPersonTent, 1), (ShopItemId.FoamMat, 2), (ShopItemId.InflatableMat, 1),
                (ShopItemId.Bandages, 6), (ShopItemId.CampChair, 1),
                (ShopItemId.HuntingBow, 1), (ShopItemId.Arrows, 18),
            },
        };

        static readonly VendorSetup MountainHutStock = new()
        {
            PriceMultiplier = 1.5f,
            SellRate = 1f,
            Stock = new[]
            {
                (ShopItemId.GasCanister, 2), (ShopItemId.Matches, 4), (ShopItemId.DehydratedMeal, 4), (ShopItemId.Antibiotics, 2),
                (ShopItemId.WoolHatAndGloves, 1), (ShopItemId.WinterSleepingBag, 1), (ShopItemId.FourSeasonTent, 1),
                (ShopItemId.MountaineeringBoots, 1), (ShopItemId.InflatableMat, 1), (ShopItemId.Bandages, 3),
                (ShopItemId.Arrows, 6),
            },
        };

        /// <summary>
        /// Lays out the route on the generated heights, then carves its lakes and levels the ground at
        /// trading posts. Must run before the heights are applied to the terrain.
        /// </summary>
        static RouteLayout PlanRoute(float[,] heights)
        {
            var layout = new RouteLayout();
            const float valleyV = 0.40f;
            // The road from home ends in a car park just south of the first cairn. The outdoor store on that road
            // is the first place to buy gear (see PrototypeSceneBuilder.Road).
            layout.Stops.Add(new RouteStop { Name = "Trailhead", U = 0.50f, V = 0.06f });
            layout.Stops.Add(new RouteStop { Name = "Beaver Pond", U = 0.44f, V = 0.17f, LakeRadius = PondRadius });
            layout.Stops.Add(new RouteStop { Name = "Aspen Meadow", U = 0.36f, V = 0.27f });
            layout.Stops.Add(new RouteStop { Name = "Valley Crossing", U = ValleyCentre(valleyV), V = valleyV, Kind = NavigationPointKind.TradingPost, LakeRadius = ValleyLakeRadius, Vendor = ValleyStoreStock });
            layout.Stops.Add(new RouteStop { Name = "Birch Ridge", U = 0.70f, V = 0.50f });
            layout.Stops.Add(new RouteStop { Name = "Alpine Tarn", U = 0.58f, V = 0.63f, LakeRadius = PondRadius });
            layout.Stops.Add(new RouteStop { Name = "North Pass Hut", U = FindPass(heights, 0.76f, 0.35f, 0.75f), V = 0.76f, Kind = NavigationPointKind.TradingPost, Vendor = MountainHutStock });
            // The end of the thru-hike: the high point north of the pass.
            const float summitV = 0.9f;
            layout.Stops.Add(new RouteStop { Name = TripLog.Destination, U = FindPeak(heights, summitV, 0.35f, 0.75f), V = summitV, Kind = NavigationPointKind.Summit });

            foreach (RouteStop stop in layout.Stops)
            {
                if (stop.Kind == NavigationPointKind.TradingPost)
                    FlattenAround(heights, stop.U, stop.V, PostFlattenRadius);
                // A small level spot on top for the cairn and the register.
                if (stop.Kind == NavigationPointKind.Summit)
                    FlattenAround(heights, stop.U, stop.V, SummitFlattenRadius);
                if (stop.LakeRadius > 0f)
                {
                    // Lake just east of the cairn, so the cairn sits on its shore.
                    float offset = (stop.LakeRadius + 14f) / TerrainSize;
                    layout.Lakes.Add(CarveLake(heights, stop.U + offset, stop.V, stop.LakeRadius));
                }
            }
            return layout;
        }

        /// <summary>
        /// The highest point near a row of the heightmap that isn't a cliff, so the summit can be walked up.
        /// Searches a band a little either side of the row.
        /// </summary>
        static float FindPeak(float[,] heights, float v, float uMin, float uMax)
        {
            int resolution = heights.GetLength(0);
            int z = Mathf.RoundToInt(v * (resolution - 1));
            float metresPerSample = TerrainSize / (resolution - 1);
            float bestU = (uMin + uMax) / 2f, highest = float.MinValue;
            for (int x = Mathf.RoundToInt(uMin * (resolution - 1)); x <= Mathf.RoundToInt(uMax * (resolution - 1)); x++)
            {
                // Average slope to the neighbours a few samples away, in rise over run.
                const int reach = 4;
                float h = heights[z, x];
                float slope = 0f;
                slope = Mathf.Max(slope, Mathf.Abs(h - heights[z, Mathf.Max(0, x - reach)]));
                slope = Mathf.Max(slope, Mathf.Abs(h - heights[z, Mathf.Min(resolution - 1, x + reach)]));
                slope = Mathf.Max(slope, Mathf.Abs(h - heights[Mathf.Max(0, z - reach), x]));
                slope = Mathf.Max(slope, Mathf.Abs(h - heights[Mathf.Min(resolution - 1, z + reach), x]));
                float grade = slope * TerrainHeight / (reach * metresPerSample);
                // Steeper than about 35° all round would be a spire; skip it.
                if (grade > 0.7f)
                    continue;
                if (h > highest)
                {
                    highest = h;
                    bestU = x / (resolution - 1f);
                }
            }
            return bestU;
        }

        /// <summary>The lowest point across a row of the heightmap: a natural pass through the mountains.</summary>
        static float FindPass(float[,] heights, float v, float uMin, float uMax)
        {
            int resolution = heights.GetLength(0);
            int z = Mathf.RoundToInt(v * (resolution - 1));
            float bestU = (uMin + uMax) / 2f, lowest = float.MaxValue;
            for (int x = Mathf.RoundToInt(uMin * (resolution - 1)); x <= Mathf.RoundToInt(uMax * (resolution - 1)); x++)
            {
                if (heights[z, x] < lowest)
                {
                    lowest = heights[z, x];
                    bestU = x / (resolution - 1f);
                }
            }
            return bestU;
        }

        /// <summary>Levels a round patch to its average height, blending smoothly into the surroundings.</summary>
        static void FlattenAround(float[,] heights, float u, float v, float radiusMetres)
        {
            int resolution = heights.GetLength(0);
            float cx = u * (resolution - 1), cz = v * (resolution - 1);
            float radius = radiusMetres / TerrainSize * (resolution - 1);
            float blendRadius = radius * 2f;
            int reach = Mathf.CeilToInt(blendRadius);

            float sum = 0f;
            int count = 0;
            ForEachSampleWithin(resolution, cx, cz, reach, (x, z, distance) =>
            {
                if (distance > radius)
                    return;
                sum += heights[z, x];
                count++;
            });
            if (count == 0)
                return;
            float level = sum / count;

            ForEachSampleWithin(resolution, cx, cz, reach, (x, z, distance) =>
            {
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius, blendRadius, distance));
                heights[z, x] = Mathf.Lerp(level, heights[z, x], blend);
            });
        }

        static void ForEachSampleWithin(int resolution, float cx, float cz, int reach, System.Action<int, int, float> visit)
        {
            for (int z = Mathf.Max(0, Mathf.FloorToInt(cz) - reach); z <= Mathf.Min(resolution - 1, Mathf.CeilToInt(cz) + reach); z++)
            for (int x = Mathf.Max(0, Mathf.FloorToInt(cx) - reach); x <= Mathf.Min(resolution - 1, Mathf.CeilToInt(cx) + reach); x++)
            {
                float distance = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
                if (distance <= reach)
                    visit(x, z, distance);
            }
        }

        static Vector3 StopWorldPosition(RouteStop stop, Terrain terrain)
        {
            float half = TerrainSize / 2f;
            var position = new Vector3(stop.U * TerrainSize - half, 0f, stop.V * TerrainSize - half);
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
            return position;
        }

        /// <summary>Where the player starts: just south of the first stop, facing north up the route.</summary>
        static Vector3 SpawnPosition(RouteLayout layout, Terrain terrain)
        {
            Vector3 spawn = StopWorldPosition(layout.Stops[0], terrain) + new Vector3(0f, 0f, -18f);
            spawn.y = terrain.SampleHeight(spawn) + terrain.transform.position.y + 0.1f;
            return spawn;
        }

        static void CreateRoute(RouteLayout layout, Terrain terrain, GameObject tradingPostPrefab)
        {
            Material stone = GetOrCreateMaterial("CairnStone", new Color(0.45f, 0.44f, 0.42f));
            Material flag = GetOrCreateMaterial("MarkerFlag", new Color(1f, 0.45f, 0.05f));
            var parent = new GameObject("Route").transform;

            for (int i = 0; i < layout.Stops.Count; i++)
            {
                RouteStop stop = layout.Stops[i];
                Vector3 position = StopWorldPosition(stop, terrain);
                GameObject point = CreateNavigationPoint(stop.Name, stop.Kind, position, parent, stone, flag, visited: i == 0);
                AddSaveId(point, $"point-{stop.Name}");
                if (stop.Vendor != null)
                    CreateTradingPost(stop, position, terrain, parent, tradingPostPrefab);
                if (stop.Kind == NavigationPointKind.Summit)
                    CreateSummitRegister(position, terrain, point.transform, stone);
            }

            foreach (Lake lake in layout.Lakes)
                CreateLake(lake, parent);
        }

        /// <summary>A metal box on a short post beside the summit cairn, holding the register to sign.</summary>
        static void CreateSummitRegister(Vector3 cairn, Terrain terrain, Transform parent, Material post)
        {
            Vector3 position = cairn + new Vector3(0f, 0f, -2f);
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;

            var root = new GameObject("Summit Register");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            CreatePart(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.1f, 0.45f, 0.1f), post, keepCollider: false);
            CreatePart(PrimitiveType.Cube, root.transform, new Vector3(0f, 1.02f, 0f), new Vector3(0.4f, 0.28f, 0.3f),
                GetOrCreateMaterial("RegisterBox", new Color(0.18f, 0.32f, 0.22f), 0.4f), keepCollider: false);

            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.6f, 0f);
            collider.size = new Vector3(0.45f, 1.2f, 0.35f);
            root.AddComponent<SummitRegister>();
        }

        /// <summary>A trading post building just west of the cairn, its counter facing the cairn.</summary>
        static void CreateTradingPost(RouteStop stop, Vector3 cairn, Terrain terrain, Transform parent, GameObject prefab)
        {
            Vector3 position = cairn + new Vector3(-9f, 0f, 0f);
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;

            var post = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            post.name = $"{stop.Name} (trading post)";
            Vector3 toCairn = cairn - position;
            toCairn.y = 0f;
            post.transform.SetPositionAndRotation(position, Quaternion.LookRotation(toCairn));

            AddSaveId(post, $"vendor-{stop.Name}");
            var vendor = post.GetComponent<Vendor>();
            SetString(vendor, "vendorName", stop.Name);
            SetFloat(vendor, "priceMultiplier", stop.Vendor.PriceMultiplier);
            SetFloat(vendor, "sellRate", stop.Vendor.SellRate);
            SetStock(vendor, stop.Vendor.Stock);

            var sign = post.GetComponentInChildren<TextMesh>();
            if (sign != null)
            {
                Undo.RecordObject(sign, "Set sign");
                sign.text = stop.Name.ToUpperInvariant();
                PrefabUtility.RecordPrefabInstancePropertyModifications(sign);
            }
        }

        static void SetStock(Vendor vendor, (ShopItemId item, int quantity)[] stock)
        {
            var serialized = new SerializedObject(vendor);
            SerializedProperty list = serialized.FindProperty("stock");
            list.arraySize = stock.Length;
            for (int i = 0; i < stock.Length; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("item").enumValueIndex = (int)stock[i].item;
                entry.FindPropertyRelative("quantity").intValue = stock[i].quantity;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A random spot within the corridor either side of the route.</summary>
        static Vector3 RandomPointNearRoute(RouteLayout layout, System.Random random)
        {
            float half = TerrainSize / 2f;
            int segment = random.Next(0, layout.Stops.Count - 1);
            RouteStop a = layout.Stops[segment], b = layout.Stops[segment + 1];
            float t = (float)random.NextDouble();
            var along = new Vector2(Mathf.Lerp(a.U, b.U, t), Mathf.Lerp(a.V, b.V, t)) * TerrainSize - new Vector2(half, half);
            var offset = new Vector2((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f) * RouteCorridorHalfWidth;
            return new Vector3(along.x + offset.x, 0f, along.y + offset.y);
        }

        // ---------- Trading post prefab ----------

        /// <summary>An open-fronted hut with a counter, a shopkeeper and a sign. The counter faces +Z.</summary>
        static GameObject BuildTradingPost()
        {
            var root = new GameObject();
            root.AddComponent<Vendor>();

            Material timber = GetOrCreateMaterial("Timber", new Color(0.42f, 0.3f, 0.19f));
            Material roof = GetOrCreateMaterial("RoofShingles", new Color(0.25f, 0.22f, 0.2f));
            Material shopkeeper = GetOrCreateMaterial("Shopkeeper", new Color(0.55f, 0.3f, 0.2f));
            Material skin = GetOrCreateMaterial("Skin", new Color(0.85f, 0.66f, 0.5f));

            // Floor, posts and counter are solid; everything else is decoration.
            AddSolid(PrimitiveType.Cube, root, new Vector3(0f, 0.1f, 0f), Quaternion.identity, new Vector3(5f, 0.4f, 4f), timber);
            foreach (Vector2 corner in new[] { new Vector2(-2.3f, -1.8f), new Vector2(2.3f, -1.8f), new Vector2(-2.3f, 1.8f), new Vector2(2.3f, 1.8f) })
                AddSolid(PrimitiveType.Cube, root, new Vector3(corner.x, 1.6f, corner.y), Quaternion.identity, new Vector3(0.2f, 2.8f, 0.2f), timber);
            AddSolid(PrimitiveType.Cube, root, new Vector3(0f, 1.6f, -1.9f), Quaternion.identity, new Vector3(4.6f, 2.8f, 0.12f), timber);
            AddSolid(PrimitiveType.Cube, root, new Vector3(0f, 0.8f, 1.2f), Quaternion.identity, new Vector3(3.6f, 1.0f, 0.6f), timber);

            AddVisual(PrimitiveType.Cube, root, new Vector3(0f, 3.1f, 0f), Quaternion.Euler(-8f, 0f, 0f), new Vector3(5.6f, 0.15f, 4.8f), roof);

            // Shopkeeper behind the counter.
            AddVisual(PrimitiveType.Capsule, root, new Vector3(0f, 1.2f, 0.1f), Quaternion.identity, new Vector3(0.5f, 0.75f, 0.5f), shopkeeper);
            AddVisual(PrimitiveType.Sphere, root, new Vector3(0f, 2.15f, 0.1f), Quaternion.identity, Vector3.one * 0.32f, skin);

            // Sign above the counter. Its text is set per post.
            var signObject = new GameObject("Sign");
            signObject.transform.SetParent(root.transform, false);
            signObject.transform.SetLocalPositionAndRotation(new Vector3(0f, 2.75f, 2.05f), Quaternion.Euler(0f, 180f, 0f));
            var sign = signObject.AddComponent<TextMesh>();
            sign.text = "TRADING POST";
            sign.anchor = TextAnchor.MiddleCenter;
            sign.alignment = TextAlignment.Center;
            sign.characterSize = 0.06f;
            sign.fontSize = 48;
            sign.color = new Color(0.95f, 0.9f, 0.75f);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            sign.font = font;
            signObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return root;
        }

        /// <summary>A primitive that keeps its collider, for parts the player shouldn't walk through.</summary>
        static void AddSolid(PrimitiveType type, GameObject parent, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.transform.SetParent(parent.transform, false);
            part.transform.SetLocalPositionAndRotation(position, rotation);
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
