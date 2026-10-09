using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// What there is to take from home before setting off: the old daypack in the bedroom, a bottle of water in the
    /// fridge and a bag of trail mix in the kitchen's wall cupboard. The fridge and cupboard open like the doors.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        /// <summary>
        /// A fridge standing on the floor at <paramref name="position"/> (house space), its door facing
        /// <paramref name="yaw"/>: hollow inside with shelves, a freezer above, and a door hinged on its left that opens.
        /// </summary>
        static void AddFridge(GameObject house, Vector3 position, float yaw, Material white, Material steel)
        {
            var fridge = new GameObject("Fridge");
            fridge.transform.SetParent(house.transform, false);
            fridge.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Material liner = GetOrCreateMaterial("FridgeLiner", new Color(0.88f, 0.9f, 0.9f), 0.5f);
            Material shelf = GetOrCreateMaterial("FridgeShelf", new Color(0.75f, 0.85f, 0.88f), 0.85f);
            const float width = 0.66f, depth = 0.66f, height = 1.8f, wall = 0.04f, freezer = 1.3f;
            float half = width / 2f, front = depth / 2f;

            // The cabinet: back, sides, top, base and the floor of the freezer, white outside and pale inside.
            AddSolid(PrimitiveType.Cube, fridge, new Vector3(0f, height / 2f, -front + wall / 2f), Quaternion.identity, new Vector3(width, height, wall), white);
            foreach (float side in new[] { -1f, 1f })
                AddSolid(PrimitiveType.Cube, fridge, new Vector3(side * (half - wall / 2f), height / 2f, 0f), Quaternion.identity, new Vector3(wall, height, depth), white);
            AddSolid(PrimitiveType.Cube, fridge, new Vector3(0f, height - wall / 2f, 0f), Quaternion.identity, new Vector3(width, wall, depth), white);
            AddSolid(PrimitiveType.Cube, fridge, new Vector3(0f, 0.06f, 0f), Quaternion.identity, new Vector3(width, 0.12f, depth), white);
            AddSolid(PrimitiveType.Cube, fridge, new Vector3(0f, freezer, 0f), Quaternion.identity, new Vector3(width, 0.04f, depth), white);
            AddVisual(PrimitiveType.Cube, fridge, new Vector3(0f, (freezer + 0.12f) / 2f, -front + wall + 0.002f), Quaternion.identity, new Vector3(width - 2f * wall, freezer - 0.12f, 0.004f), liner);
            foreach (float shelfAt in new[] { 0.52f, 0.92f })
                AddVisual(PrimitiveType.Cube, fridge, new Vector3(0f, shelfAt, -0.02f), Quaternion.identity, new Vector3(width - 2f * wall, 0.012f, depth - 0.1f), shelf);
            // A little light inside.
            AddVisual(PrimitiveType.Cube, fridge, new Vector3(0f, freezer - 0.035f, -0.1f), Quaternion.identity, new Vector3(0.12f, 0.02f, 0.05f),
                GetOrCreateEmissiveMaterial("FridgeLight", new Color(0.9f, 0.95f, 1f)));

            // The freezer door stays shut; the fridge door opens.
            AddVisual(PrimitiveType.Cube, fridge, new Vector3(0f, (freezer + height) / 2f, front + 0.025f), Quaternion.identity, new Vector3(width, height - freezer - 0.02f, 0.05f), white);
            AddVisual(PrimitiveType.Cube, fridge, new Vector3(half - 0.07f, freezer + 0.12f, front + 0.065f), Quaternion.identity, new Vector3(0.025f, 0.18f, 0.03f), steel);
            AddHingedDoor(fridge, "Fridge", new Vector3(-half, 0.12f, front), width, freezer - 0.12f - 0.01f, 0.05f, -105f, white, steel, handleLow: false);

            // Things in it: milk and a jar to look at, and the bottle of water to take.
            Material milk = GetOrCreateMaterial("MilkCarton", new Color(0.92f, 0.92f, 0.9f));
            AddVisual(PrimitiveType.Cube, fridge, new Vector3(0.17f, 0.52f + 0.006f + 0.11f, -0.08f), Quaternion.Euler(0f, 12f, 0f), new Vector3(0.08f, 0.22f, 0.08f), milk);
            AddVisual(PrimitiveType.Cube, fridge, new Vector3(0.17f, 0.52f + 0.006f + 0.18f, -0.08f), Quaternion.Euler(0f, 12f, 0f), new Vector3(0.081f, 0.06f, 0.081f),
                GetOrCreateMaterial("MilkLabel", new Color(0.2f, 0.4f, 0.75f)));
            AddVisual(PrimitiveType.Cylinder, fridge, new Vector3(-0.16f, 0.92f + 0.006f + 0.06f, -0.12f), Quaternion.identity, new Vector3(0.09f, 0.06f, 0.09f),
                GetOrCreateMaterial("JamJar", new Color(0.55f, 0.1f, 0.15f), 0.8f));
            AddWaterBottle(fridge, new Vector3(-0.08f, 0.52f + 0.006f, 0.02f));
        }

        /// <summary>A wall cupboard over the worktop, its bottom at <paramref name="position"/>: two doors that open, a shelf, and the trail mix.</summary>
        static void AddWallCupboard(GameObject house, Vector3 position, float yaw, float width, Material cupboard, Material steel)
        {
            var root = new GameObject("Wall cupboard");
            root.transform.SetParent(house.transform, false);
            root.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            const float depth = 0.35f, height = 0.65f, wall = 0.025f;
            float half = width / 2f, front = depth / 2f;
            Material inside = GetOrCreateMaterial("CupboardInside", new Color(0.82f, 0.78f, 0.68f));

            AddVisual(PrimitiveType.Cube, root, new Vector3(0f, height / 2f, -front + wall / 2f), Quaternion.identity, new Vector3(width, height, wall), inside);
            foreach (float side in new[] { -1f, 1f })
                AddSolid(PrimitiveType.Cube, root, new Vector3(side * (half - wall / 2f), height / 2f, 0f), Quaternion.identity, new Vector3(wall, height, depth), cupboard);
            AddSolid(PrimitiveType.Cube, root, new Vector3(0f, height - wall / 2f, 0f), Quaternion.identity, new Vector3(width, wall, depth), cupboard);
            AddSolid(PrimitiveType.Cube, root, new Vector3(0f, wall / 2f, 0f), Quaternion.identity, new Vector3(width, wall, depth), cupboard);
            AddVisual(PrimitiveType.Cube, root, new Vector3(0f, height * 0.52f, -0.01f), Quaternion.identity, new Vector3(width - 2f * wall, 0.015f, depth - 0.05f), inside);

            // Two doors meeting in the middle, each hinged at its outer edge.
            var door = new GameObject("Kitchen cupboard");
            door.transform.SetParent(root.transform, false);
            var component = door.AddComponent<World.Door>();
            Transform left = HingedLeaf(door, new Vector3(-half, wall, front), half, height - 2f * wall, 0.02f, 1f, cupboard, steel, knobAtTop: false);
            Transform right = HingedLeaf(door, new Vector3(half, wall, front), half, height - 2f * wall, 0.02f, -1f, cupboard, steel, knobAtTop: false);
            SetString(component, "doorName", "Kitchen cupboard");
            Modify(component, "leaves", property =>
            {
                property.arraySize = 2;
                (Transform hinge, float angle)[] leaves = { (left, -100f), (right, 100f) };
                for (int i = 0; i < 2; i++)
                {
                    UnityEditor.SerializedProperty entry = property.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("hinge").objectReferenceValue = leaves[i].hinge;
                    entry.FindPropertyRelative("openAngle").floatValue = leaves[i].angle;
                }
            });

            // Tins and a box of crackers, and the trail mix on the bottom shelf.
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0.25f, wall + 0.06f, -0.04f), Quaternion.identity, new Vector3(0.08f, 0.06f, 0.08f),
                GetOrCreateMaterial("Tin", new Color(0.7f, 0.72f, 0.7f), 0.75f));
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0.35f, wall + 0.06f, -0.06f), Quaternion.identity, new Vector3(0.08f, 0.06f, 0.08f),
                GetOrCreateMaterial("TinLabel", new Color(0.75f, 0.2f, 0.12f)));
            AddVisual(PrimitiveType.Cube, root, new Vector3(-0.3f, height * 0.52f + 0.11f, -0.05f), Quaternion.Euler(0f, 8f, 0f), new Vector3(0.2f, 0.2f, 0.06f),
                GetOrCreateMaterial("CrackerBox", new Color(0.85f, 0.6f, 0.15f)));
            AddTrailMix(root, new Vector3(-0.05f, wall, 0.02f));
        }

        /// <summary>A door made of one leaf on a hinge, opening <paramref name="openAngle"/> degrees, named for its option ("Open the fridge").</summary>
        static void AddHingedDoor(GameObject root, string name, Vector3 hinge, float width, float height, float thickness, float openAngle,
            Material material, Material handle, bool handleLow)
        {
            var door = new GameObject(name);
            door.transform.SetParent(root.transform, false);
            var component = door.AddComponent<World.Door>();
            Transform leaf = HingedLeaf(door, hinge, width, height, thickness, 1f, material, handle, knobAtTop: !handleLow);
            SetString(component, "doorName", name);
            Modify(component, "leaves", property =>
            {
                property.arraySize = 1;
                UnityEditor.SerializedProperty entry = property.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("hinge").objectReferenceValue = leaf;
                entry.FindPropertyRelative("openAngle").floatValue = openAngle;
            });
        }

        /// <summary>
        /// One door leaf: a slab hanging from a hinge at <paramref name="hinge"/>, reaching <paramref name="width"/> toward
        /// +X (<paramref name="reach"/> 1) or −X (−1), with a bar handle near its free edge.
        /// </summary>
        static Transform HingedLeaf(GameObject door, Vector3 hinge, float width, float height, float thickness, float reach,
            Material material, Material handle, bool knobAtTop)
        {
            var pivot = new GameObject("Hinge").transform;
            pivot.SetParent(door.transform, false);
            pivot.localPosition = hinge;
            AddSolid(PrimitiveType.Cube, pivot.gameObject, new Vector3(reach * width / 2f, height / 2f, thickness / 2f), Quaternion.identity,
                new Vector3(width - 0.004f, height - 0.004f, thickness), material);
            float handleHeight = Mathf.Min(0.35f, height * 0.4f);
            AddVisual(PrimitiveType.Cube, pivot.gameObject, new Vector3(reach * (width - 0.06f), knobAtTop ? height - handleHeight / 2f - 0.08f : handleHeight / 2f + 0.06f,
                thickness + 0.018f), Quaternion.identity, new Vector3(0.02f, handleHeight, 0.025f), handle);
            return pivot;
        }

        /// <summary>A half-litre bottle of water standing at <paramref name="position"/>, to take.</summary>
        static void AddWaterBottle(GameObject parent, Vector3 position)
        {
            GameObject bottle = AddPickup(parent, "Bottle of water", PickupKind.BottledWater, "home-water", position, new Vector3(0.1f, 0.24f, 0.1f));
            AddVisual(PrimitiveType.Cylinder, bottle, new Vector3(0f, 0.09f, 0f), Quaternion.identity, new Vector3(0.065f, 0.09f, 0.065f),
                GetOrCreateMaterial("BottleWater", new Color(0.55f, 0.75f, 0.9f), 0.9f));
            AddVisual(PrimitiveType.Cylinder, bottle, new Vector3(0f, 0.09f, 0f), Quaternion.identity, new Vector3(0.067f, 0.03f, 0.067f),
                GetOrCreateMaterial("BottleLabel", new Color(0.15f, 0.45f, 0.8f)));
            AddVisual(PrimitiveType.Cylinder, bottle, new Vector3(0f, 0.195f, 0f), Quaternion.identity, new Vector3(0.03f, 0.015f, 0.03f),
                GetOrCreateMaterial("BottleCap", new Color(0.15f, 0.3f, 0.75f)));
        }

        /// <summary>A resealable bag of trail mix standing at <paramref name="position"/>, to take.</summary>
        static void AddTrailMix(GameObject parent, Vector3 position)
        {
            GameObject bag = AddPickup(parent, "Bag of trail mix", PickupKind.TrailMix, "home-trailmix", position, new Vector3(0.16f, 0.2f, 0.08f));
            Material pouch = GetOrCreateMaterial("TrailMixPouch", new Color(0.78f, 0.5f, 0.18f), 0.55f);
            AddVisual(PrimitiveType.Cube, bag, new Vector3(0f, 0.08f, 0f), Quaternion.identity, new Vector3(0.12f, 0.16f, 0.04f), pouch);
            AddVisual(PrimitiveType.Cube, bag, new Vector3(0f, 0.155f, 0f), Quaternion.identity, new Vector3(0.12f, 0.012f, 0.042f), GetOrCreateMaterial("PouchZip", new Color(0.2f, 0.35f, 0.18f)));
            AddVisual(PrimitiveType.Cube, bag, new Vector3(0f, 0.085f, 0.0205f), Quaternion.identity, new Vector3(0.07f, 0.06f, 0.002f), GetOrCreateMaterial("PouchLabel", new Color(0.93f, 0.9f, 0.8f)));
        }

        /// <summary>The old daypack, standing on the floor at <paramref name="position"/> (house space), harness to the wall.</summary>
        static void AddDaypack(GameObject house, Vector3 position, float yaw)
        {
            GameObject pack = AddPickup(house, "Old daypack", PickupKind.Daypack, "home-daypack", position, new Vector3(0.34f, 0.52f, 0.28f), yaw);
            var collider = pack.GetComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.25f, 0.07f);
            SetField(pack.GetComponent<ItemPickup>(), "gear", GearSetup.GetOrCreateLibrary());
        }

        /// <summary>Something to take: an <see cref="ItemPickup"/> with a save id and a trigger box round it to look at.</summary>
        static GameObject AddPickup(GameObject parent, string name, PickupKind kind, string saveId, Vector3 position, Vector3 size, float yaw = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var pickup = go.AddComponent<ItemPickup>();
            SetEnum(pickup, "kind", (int)kind);
            SetString(pickup, "itemName", name);
            AddSaveId(go, saveId);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, size.y / 2f, 0f);
            box.size = size;
            return go;
        }
    }
}
