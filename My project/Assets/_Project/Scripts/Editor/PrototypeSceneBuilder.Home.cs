using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Your home at the end of the road: a small timber house with a front porch, an open living room and
    /// kitchen across the front, and a bedroom and bathroom at the back. You wake up in the bedroom. Built from
    /// simple shapes like the other buildings; walls, counters and big furniture are solid.
    /// Local +Z is the front, facing down the road; the house is 8 m wide and 7 m deep.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        /// <summary>The house stands this far back from the middle of the home lot, behind the end of the road.</summary>
        const float HomeCabinBack = 10f;
        const float CabinFloorTop = 0.3f;
        const float HouseWidth = 8f, HouseDepth = 7f, HouseWallHeight = 2.6f;

        /// <summary>Where the trip starts: in the bedroom beside the bed, facing its door. Null if there's no home.</summary>
        static Vector3? HomeSpawn(Terrain terrain, out Quaternion facing)
        {
            facing = Quaternion.identity;
            if (home == null)
                return null;
            Vector2 along = home.RoadDirection;
            Vector3 house = OnGround(terrain, home.Centre - along * HomeCabinBack);
            facing = Quaternion.LookRotation(new Vector3(along.x, 0f, along.y));
            return house + Vector3.up * (CabinFloorTop + 0.05f) + facing * new Vector3(-0.7f, 0f, -1.0f);
        }

        static void CreateHome(Place place, Terrain terrain, Transform parent)
        {
            Vector2 along = place.RoadDirection;
            GameObject house = PlaceBuilding("Home", place, new Vector2(-HomeCabinBack, 0f), along, terrain, parent);
            AddSaveId(house, "home");
            MarkBuilding(house, new Vector3(0f, (CabinFloorTop + HouseWallHeight) / 2f, 0f), new Vector3(HouseWidth, CabinFloorTop + HouseWallHeight, HouseDepth));

            Material walls = GetOrCreateMaterial("CabinWalls", new Color(0.5f, 0.36f, 0.24f));
            Material inside = GetOrCreateMaterial("InteriorWalls", new Color(0.86f, 0.8f, 0.68f));
            Material floor = GetOrCreateMaterial("CabinFloor", new Color(0.62f, 0.47f, 0.32f));
            Material roof = GetOrCreateMaterial("RoofShingles", new Color(0.25f, 0.22f, 0.2f));
            Material glass = GetOrCreateMaterial("WindowGlass", new Color(0.12f, 0.16f, 0.2f), 0.9f);
            Material trim = GetOrCreateMaterial("CabinTrim", new Color(0.88f, 0.85f, 0.78f));
            Material wood = GetOrCreateMaterial("Furniture", new Color(0.45f, 0.3f, 0.18f));
            Material metal = GetOrCreateMaterial("DarkMetal", new Color(0.18f, 0.18f, 0.19f), 0.5f);

            const float floorTop = CabinFloorTop, height = HouseWallHeight, outer = 0.18f, inner = 0.12f;
            const float halfWidth = HouseWidth / 2f, halfDepth = HouseDepth / 2f;
            float top = floorTop + height;

            // Floor slab and ceiling.
            AddSolid(PrimitiveType.Cube, house, new Vector3(0f, floorTop / 2f, 0f), Quaternion.identity, new Vector3(HouseWidth + 0.2f, floorTop, HouseDepth + 0.2f), floor);
            AddVisual(PrimitiveType.Cube, house, new Vector3(0f, top + 0.03f, 0f), Quaternion.identity, new Vector3(HouseWidth, 0.06f, HouseDepth), inside);

            // Outside walls, the front door at the left of the front wall.
            float front = halfDepth - outer / 2f, back = -halfDepth + outer / 2f, left = -halfWidth + outer / 2f, right = halfWidth - outer / 2f;
            const float doorX = -1.2f;
            AddTrimmedWall(house, new Vector2(-halfWidth, front), new Vector2(halfWidth, front), floorTop, height, outer, walls, trim, (doorX + halfWidth, 1f, 2.1f));
            AddTrimmedWall(house, new Vector2(-halfWidth, back), new Vector2(halfWidth, back), floorTop, height, outer, walls, trim);
            AddTrimmedWall(house, new Vector2(left, -halfDepth), new Vector2(left, halfDepth), floorTop, height, outer, walls, trim);
            AddTrimmedWall(house, new Vector2(right, -halfDepth), new Vector2(right, halfDepth), floorTop, height, outer, walls, trim);

            // Inside: a wall across the middle with doors to the bedroom and the bathroom, and one between them.
            const float bedroomDoor = -1.6f, bathroomDoor = 2.2f, partition = 0.9f;
            AddTrimmedWall(house, new Vector2(-halfWidth + outer, 0f), new Vector2(halfWidth - outer, 0f), floorTop, height, inner, inside, trim,
                (bedroomDoor + halfWidth - outer, 0.9f, 2.05f), (bathroomDoor + halfWidth - outer, 0.8f, 2.05f));
            AddTrimmedWall(house, new Vector2(partition, -halfDepth + outer), new Vector2(partition, -inner / 2f), floorTop, height, inner, inside, trim);

            // Pitched roof along the width, gables at the ends, a chimney for the wood stove.
            const float rise = 1.5f;
            float halfSpan = halfDepth + 0.15f;
            float slope = Mathf.Atan2(rise, halfSpan) * Mathf.Rad2Deg;
            float slab = Mathf.Sqrt(halfSpan * halfSpan + rise * rise) + 0.45f;
            AddVisual(PrimitiveType.Cube, house, new Vector3(0f, top + rise / 2f + 0.08f, halfSpan / 2f), Quaternion.Euler(slope, 0f, 0f), new Vector3(HouseWidth + 0.7f, 0.14f, slab), roof);
            AddVisual(PrimitiveType.Cube, house, new Vector3(0f, top + rise / 2f + 0.08f, -halfSpan / 2f), Quaternion.Euler(-slope, 0f, 0f), new Vector3(HouseWidth + 0.7f, 0.14f, slab), roof);
            AddGable(house, left, top, halfSpan, rise, outer, walls);
            AddGable(house, right, top, halfSpan, rise, outer, walls);
            AddVisual(PrimitiveType.Cube, house, new Vector3(-3.3f, top + rise * 0.7f, 2.4f), Quaternion.identity, new Vector3(0.45f, 1.6f, 0.45f), metal);

            // Windows: two at the front, one in each room's outside walls.
            AddWindow(house, new Vector3(-3f, floorTop + 1.45f, front), 0f, new Vector2(1.2f, 1f), glass, trim);
            AddWindow(house, new Vector3(2.7f, floorTop + 1.55f, front), 0f, new Vector2(1.1f, 0.8f), glass, trim);
            AddWindow(house, new Vector3(left, floorTop + 1.45f, 1.7f), 90f, new Vector2(1.1f, 1f), glass, trim);
            AddWindow(house, new Vector3(left, floorTop + 1.45f, -1.8f), 90f, new Vector2(1f, 1f), glass, trim);
            AddWindow(house, new Vector3(-2f, floorTop + 1.5f, back), 0f, new Vector2(1.1f, 0.9f), glass, trim);
            AddWindow(house, new Vector3(right, floorTop + 1.7f, -2f), 90f, new Vector2(0.6f, 0.5f), glass, trim);

            BuildPorch(house, doorX, halfDepth, floorTop, wood, roof, floor);
            // Doors that open: the front door swings in, the bedroom and bathroom doors swing into their rooms.
            Material brass = GetOrCreateMaterial("Brass", new Color(0.78f, 0.62f, 0.3f), 0.7f);
            AddDoor(house, "Front door", floorTop, front, 2.1f, GetOrCreateMaterial("FrontDoor", new Color(0.35f, 0.15f, 0.1f)), brass, null, false,
                (doorX - 0.5f, 1f, 1f, 90f));
            AddDoor(house, "Bedroom door", floorTop, 0f, 2.05f, wood, brass, null, true, (bedroomDoor - 0.45f, 1f, 0.9f, 90f));
            AddDoor(house, "Bathroom door", floorTop, 0f, 2.05f, wood, brass, null, false, (bathroomDoor - 0.4f, 1f, 0.8f, 90f));
            FurnishLivingRoom(house, floorTop, wood, metal);
            FurnishKitchen(house, floorTop, wood, metal, trim);
            FurnishBedroom(house, floorTop, wood, trim);
            FurnishBathroom(house, floorTop, partition, trim);
            DetailHouseOutside(house, floorTop, top, rise, walls, trim, wood, metal);
            DetailHouseInside(house, floorTop, top, wood, metal, trim);

            AddLamp(house, new Vector3(-1.8f, top - 0.3f, 1.8f), 6f, 1.5f);
            AddLamp(house, new Vector3(2.4f, top - 0.3f, 1.8f), 5f, 1.3f);
            AddLamp(house, new Vector3(-1.6f, top - 0.3f, -1.8f), 5f, 1.2f);
            AddLamp(house, new Vector3(2.4f, top - 0.3f, -1.8f), 4f, 1.1f);

            // A mailbox by the road, across from where the truck is parked.
            Vector2 side = new(-along.y, along.x);
            Vector2 mailboxXZ = place.Centre + along * 4f - side * (RoadHalfWidth + 1.2f);
            var mailbox = new GameObject("Mailbox");
            mailbox.transform.SetParent(parent, false);
            mailbox.transform.SetPositionAndRotation(OnGround(terrain, mailboxXZ), Quaternion.LookRotation(new Vector3(side.x, 0f, side.y)));
            AddSolid(PrimitiveType.Cube, mailbox, new Vector3(0f, 0.55f, 0f), Quaternion.identity, new Vector3(0.1f, 1.1f, 0.1f), wood);
            AddVisual(PrimitiveType.Cube, mailbox, new Vector3(0f, 1.2f, 0.05f), Quaternion.identity, new Vector3(0.25f, 0.25f, 0.5f), metal);
        }

        /// <summary>A covered deck and steps up to the front door.</summary>
        static void BuildPorch(GameObject house, float doorX, float halfDepth, float floorTop, Material wood, Material roof, Material boards)
        {
            const float depth = 1.4f, width = 3f;
            float z = halfDepth + depth / 2f;
            AddSolid(PrimitiveType.Cube, house, new Vector3(doorX, floorTop - 0.05f, z), Quaternion.identity, new Vector3(width, 0.1f, depth), boards);
            AddSolid(PrimitiveType.Cube, house, new Vector3(doorX, 0.08f, halfDepth + depth + 0.25f), Quaternion.identity, new Vector3(1.2f, 0.16f, 0.5f), boards);
            foreach (float x in new[] { doorX - width / 2f + 0.1f, doorX + width / 2f - 0.1f })
                AddSolid(PrimitiveType.Cube, house, new Vector3(x, floorTop + 1.1f, halfDepth + depth - 0.1f), Quaternion.identity, new Vector3(0.12f, 2.2f, 0.12f), wood);
            AddVisual(PrimitiveType.Cube, house, new Vector3(doorX, floorTop + 2.3f, z), Quaternion.Euler(-10f, 0f, 0f), new Vector3(width + 0.3f, 0.08f, depth + 0.3f), roof);
            // A railing either side of the steps.
            foreach (float x in new[] { doorX - width / 2f + 0.1f, doorX + width / 2f - 0.1f })
                AddVisual(PrimitiveType.Cube, house, new Vector3(x, floorTop + 0.85f, z), Quaternion.identity, new Vector3(0.06f, 0.06f, depth), wood);
        }

        /// <summary>
        /// A working door in a doorway along a wall that runs along local X. Each leaf hangs on a hinge at
        /// <c>hinge.x</c> and reaches <c>width</c> metres toward +X (<c>reach</c> 1) or −X (−1); it swings
        /// <c>openAngle</c> degrees to open (positive swings toward −Z from a +X leaf). Panels, a knob each side,
        /// and an optional glass pane in the top half.
        /// </summary>
        static void AddDoor(GameObject root, string name, float floorTop, float wallZ, float height, Material material, Material knob,
            Material glass, bool startOpen, params (float hinge, float reach, float width, float openAngle)[] leafs)
        {
            var doorObject = new GameObject(name);
            doorObject.transform.SetParent(root.transform, false);
            var door = doorObject.AddComponent<World.Door>();
            Material panel = GetOrCreateMaterial("DoorPanel", Color.Lerp(material.color, Color.black, 0.25f));
            var hinges = new Transform[leafs.Length];
            for (int i = 0; i < leafs.Length; i++)
            {
                (float hingeX, float reach, float width, _) = leafs[i];
                var hinge = new GameObject("Hinge").transform;
                hinge.SetParent(doorObject.transform, false);
                hinge.localPosition = new Vector3(hingeX, floorTop, wallZ);
                hinges[i] = hinge;
                GameObject leaf = hinge.gameObject;
                float middle = reach * width / 2f;
                AddSolid(PrimitiveType.Cube, leaf, new Vector3(middle, height / 2f, 0f), Quaternion.identity, new Vector3(width - 0.01f, height - 0.01f, 0.045f), material);
                foreach (float face in new[] { -1f, 1f })
                {
                    if (glass != null)
                        AddVisual(PrimitiveType.Cube, leaf, new Vector3(middle, height * 0.68f, face * 0.024f), Quaternion.identity, new Vector3(width * 0.7f, height * 0.42f, 0.006f), glass);
                    else
                        AddVisual(PrimitiveType.Cube, leaf, new Vector3(middle, height * 0.7f, face * 0.024f), Quaternion.identity, new Vector3(width * 0.62f, height * 0.36f, 0.006f), panel);
                    AddVisual(PrimitiveType.Cube, leaf, new Vector3(middle, height * 0.27f, face * 0.024f), Quaternion.identity, new Vector3(width * 0.62f, height * 0.32f, 0.006f), panel);
                    AddVisual(PrimitiveType.Sphere, leaf, new Vector3(reach * (width - 0.09f), 0.98f, face * 0.05f), Quaternion.identity, Vector3.one * 0.06f, knob);
                }
            }
            SetString(door, "doorName", name);
            SetBool(door, "startOpen", startOpen);
            Modify(door, "leaves", property =>
            {
                property.arraySize = leafs.Length;
                for (int i = 0; i < leafs.Length; i++)
                {
                    UnityEditor.SerializedProperty entry = property.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("hinge").objectReferenceValue = hinges[i];
                    entry.FindPropertyRelative("openAngle").floatValue = leafs[i].openAngle;
                }
            });
        }

        /// <summary>
        /// Puts one of the Poly Haven models (see <see cref="HomeModelSetup"/>) in a building: its base at
        /// <paramref name="position"/> (local), turned <paramref name="yaw"/> degrees, with a box collider if
        /// <paramref name="solid"/>. Null if the model isn't there.
        /// </summary>
        static GameObject Furnish(GameObject root, string id, Vector3 position, float yaw, bool solid = false, float scale = 1f)
        {
            GameObject prefab = HomeModelSetup.Prefab(id);
            if (prefab == null)
                return null;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.localScale = Vector3.one * scale;
            if (solid)
            {
                // Measured at the origin, unturned, so the box fits the model's own axes.
                Bounds bounds = HomeModelSetup.WorldBounds(instance);
                var box = instance.AddComponent<BoxCollider>();
                box.center = bounds.center / scale;
                box.size = bounds.size / scale;
            }
            instance.transform.SetParent(root.transform, false);
            instance.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return instance;
        }

        /// <summary>Front left: a leather sofa and a rocking chair round a coffee table on a rug, the wood stove, a bookshelf.</summary>
        static void FurnishLivingRoom(GameObject house, float floorTop, Material wood, Material metal)
        {
            Material rug = GetOrCreateMaterial("Rug", new Color(0.3f, 0.4f, 0.5f));
            Material fire = GetOrCreateEmissiveMaterial("StoveGlow", new Color(1f, 0.45f, 0.15f));
            float y = floorTop;

            AddVisual(PrimitiveType.Cube, house, new Vector3(-2.2f, y + 0.005f, 1.8f), Quaternion.identity, new Vector3(2.4f, 0.01f, 1.8f), rug);
            // Sofa against the left wall facing into the room, cushions on it; a rocking chair turned to the stove.
            Furnish(house, "sofa_02", new Vector3(-3.38f, y, 1.8f), 90f, solid: true);
            Furnish(house, "throw_pillows_01", new Vector3(-3.42f, y + 0.42f, 1.8f), 90f, scale: 0.85f);
            Furnish(house, "Rockingchair_01", new Vector3(-1.15f, y, 0.95f), -40f, solid: true);
            // A low wooden table with a lantern and a little plant on it.
            Furnish(house, "small_wooden_table_01", new Vector3(-2.3f, y, 1.8f), 90f, solid: true);
            Furnish(house, "Lantern_01", new Vector3(-2.25f, y + 0.535f, 1.5f), 20f);
            Furnish(house, "potted_plant_04", new Vector3(-2.32f, y + 0.535f, 2.1f), 0f);
            // Wood stove in the front corner, with its pipe up to the chimney.
            Vector3 stove = new(-3.3f, y, 3f);
            AddSolid(PrimitiveType.Cube, house, stove + new Vector3(0f, 0.38f, 0f), Quaternion.identity, new Vector3(0.6f, 0.76f, 0.55f), metal);
            AddVisual(PrimitiveType.Cube, house, stove + new Vector3(0.301f, 0.35f, 0f), Quaternion.identity, new Vector3(0.01f, 0.25f, 0.3f), fire);
            AddVisual(PrimitiveType.Cylinder, house, stove + new Vector3(0f, 1.7f, 0f), Quaternion.identity, new Vector3(0.14f, 0.95f, 0.14f), metal);
            AddVisual(PrimitiveType.Cube, house, stove + new Vector3(0f, 0.01f, 0f), Quaternion.identity, new Vector3(1f, 0.02f, 0.9f), GetOrCreateMaterial("Hearth", new Color(0.35f, 0.34f, 0.33f)));
            // A worn bookshelf against the middle wall, with a set of encyclopedias and an oil lamp on top.
            Furnish(house, "wooden_bookshelf_worn", new Vector3(-3.0f, y, 0.36f), 0f, solid: true);
            Furnish(house, "book_encyclopedia_set_01", new Vector3(-3.15f, y + 1.06f, 0.36f), 0f);
            Furnish(house, "book_encyclopedia_set_01", new Vector3(-2.85f, y + 0.4f, 0.36f), 180f);
            Furnish(house, "vintage_oil_lamp", new Vector3(-3.4f, y + 2.07f, 0.36f), 0f, scale: 0.6f);
            // A framed picture on the middle wall.
            Furnish(house, "hanging_picture_frame_01", new Vector3(-0.95f, y + 1.2f, 0.07f), 0f);
        }

        /// <summary>Front right: an L of counters with a sink under the window, an electric cooker and a fridge, a table for two.</summary>
        static void FurnishKitchen(GameObject house, float floorTop, Material wood, Material metal, Material white)
        {
            Material counter = GetOrCreateMaterial("Countertop", new Color(0.32f, 0.33f, 0.34f), 0.4f);
            Material cupboard = GetOrCreateMaterial("Cupboards", new Color(0.55f, 0.62f, 0.55f));
            Material steel = GetOrCreateMaterial("Steel", new Color(0.7f, 0.72f, 0.74f), 0.7f);
            float y = floorTop;

            // Along the front wall, under the window: cupboards, worktop, sink.
            AddSolid(PrimitiveType.Cube, house, new Vector3(2.65f, y + 0.43f, 3.0f), Quaternion.identity, new Vector3(2.3f, 0.86f, 0.6f), cupboard);
            AddVisual(PrimitiveType.Cube, house, new Vector3(2.65f, y + 0.88f, 3.0f), Quaternion.identity, new Vector3(2.34f, 0.04f, 0.64f), counter);
            AddVisual(PrimitiveType.Cube, house, new Vector3(2.7f, y + 0.89f, 3.0f), Quaternion.identity, new Vector3(0.55f, 0.03f, 0.4f), steel);
            AddVisual(PrimitiveType.Cylinder, house, new Vector3(2.7f, y + 1.02f, 3.22f), Quaternion.identity, new Vector3(0.03f, 0.12f, 0.03f), steel);
            // Along the right wall: worktop either side of the electric cooker, then the fridge.
            foreach ((float from, float to) in new[] { (0.8f, 1.13f), (1.67f, 2.7f) })
            {
                float middle = (from + to) / 2f, length = to - from;
                AddSolid(PrimitiveType.Cube, house, new Vector3(3.5f, y + 0.43f, middle), Quaternion.identity, new Vector3(0.6f, 0.86f, length), cupboard);
                AddVisual(PrimitiveType.Cube, house, new Vector3(3.5f, y + 0.88f, middle), Quaternion.identity, new Vector3(0.64f, 0.04f, length), counter);
            }
            Furnish(house, "electric_stove", new Vector3(3.5f, y, 1.4f), -90f, solid: true);
            Furnish(house, "pot_enamel_01", new Vector3(3.5f, y + 0.86f, 1.28f), 0f);
            Furnish(house, "brass_pan_01", new Vector3(3.5f, y + 0.86f, 1.55f), 90f);
            // The fridge opens (a bottle of water inside), and so does the wall cupboard above the worktop (trail mix).
            AddFridge(house, new Vector3(3.48f, y, 0.45f), -90f, white, steel);
            AddWallCupboard(house, new Vector3(3.65f, y + 1.525f, 1.9f), -90f, 1.4f, cupboard, steel);
            // A kettle and a chopping board on the worktops.
            Furnish(house, "vintage_electric_kettle", new Vector3(3.5f, y + 0.9f, 2.35f), -90f);
            Furnish(house, "wooden_cutting_board", new Vector3(3.25f, y + 0.9f, 3.0f), 10f);
            // A wooden table for two with painted chairs, a jug and a bowl of fruit.
            Furnish(house, "wooden_table_02", new Vector3(1.9f, y, 1.5f), 0f, solid: true);
            Furnish(house, "painted_wooden_chair_01", new Vector3(1.9f, y, 0.92f), 0f, solid: true);
            Furnish(house, "painted_wooden_chair_01", new Vector3(1.9f, y, 2.08f), 180f, solid: true);
            Furnish(house, "wooden_bowl_01", new Vector3(1.9f, y + 0.8f, 1.5f), 0f);
            Furnish(house, "jug_01", new Vector3(2.25f, y + 0.8f, 1.62f), 30f);
        }

        /// <summary>Back left: an iron single bed with a mattress and blanket, nightstands, a chest of drawers, a wardrobe.</summary>
        static void FurnishBedroom(GameObject house, float floorTop, Material wood, Material white)
        {
            Material bedding = GetOrCreateMaterial("Bedding", new Color(0.85f, 0.84f, 0.8f));
            Material blanket = GetOrCreateMaterial("Blanket", new Color(0.55f, 0.15f, 0.12f));
            Material rug = GetOrCreateMaterial("BedroomRug", new Color(0.62f, 0.55f, 0.42f));
            Material lampShade = GetOrCreateEmissiveMaterial("LampShade", new Color(1f, 0.85f, 0.6f));
            float y = floorTop;

            // The bed: an old iron frame against the back wall, made up with a mattress, sheets, a blanket and a pillow.
            Vector3 bed = new(-2.0f, y, -2.31f);
            Furnish(house, "old_bed_frame", bed, 0f, solid: true);
            AddVisual(PrimitiveType.Cube, house, bed + new Vector3(0f, 0.5f, 0f), Quaternion.identity, new Vector3(0.84f, 0.16f, 1.88f), bedding);
            AddVisual(PrimitiveType.Cube, house, bed + new Vector3(0f, 0.59f, 0.25f), Quaternion.Euler(1.5f, 0f, 0f), new Vector3(0.9f, 0.05f, 1.35f), blanket);
            AddVisual(PrimitiveType.Cube, house, bed + new Vector3(0f, 0.63f, -0.72f), Quaternion.identity, new Vector3(0.55f, 0.11f, 0.32f), bedding);
            // A nightstand each side, a lamp on the left one and the alarm clock on the right.
            foreach (float x in new[] { -0.95f, 0.95f })
                Furnish(house, "painted_wooden_nightstand", bed + new Vector3(x, 0f, -0.75f), 0f, solid: true);
            AddVisual(PrimitiveType.Cylinder, house, bed + new Vector3(-0.95f, 0.72f, -0.8f), Quaternion.identity, new Vector3(0.18f, 0.1f, 0.18f), lampShade);
            Furnish(house, "alarm_clock_01", bed + new Vector3(0.95f, 0.62f, -0.75f), -20f);
            AddVisual(PrimitiveType.Cube, house, bed + new Vector3(0f, 0.005f, 1.4f), Quaternion.identity, new Vector3(1.6f, 0.01f, 0.9f), rug);
            // A chest of drawers against the bathroom wall, a photo on it and a mirror above.
            Furnish(house, "vintage_wooden_drawer_01", new Vector3(0.6f, y, -1.0f), -90f, solid: true);
            Furnish(house, "standing_picture_frame_01", new Vector3(0.62f, y + 0.55f, -0.8f), -110f);
            Furnish(house, "ornate_mirror_01", new Vector3(0.83f, y + 0.95f, -1.0f), -90f);
            // The old daypack against the wall by the bedroom door, the first thing you see on waking.
            AddDaypack(house, new Vector3(-0.6f, y, -0.25f), 180f);
            // Wardrobe against the left wall, by the door.
            AddSolid(PrimitiveType.Cube, house, new Vector3(-3.55f, y + 1f, -0.7f), Quaternion.identity, new Vector3(0.6f, 2f, 1.1f), wood);
            AddVisual(PrimitiveType.Cube, house, new Vector3(-3.24f, y + 1f, -0.7f), Quaternion.identity, new Vector3(0.01f, 1.9f, 0.02f), white);
        }

        /// <summary>Back right: a shower in the corner, a toilet, a basin with a mirror, a tiled floor.</summary>
        static void FurnishBathroom(GameObject house, float floorTop, float partition, Material white)
        {
            Material tiles = GetOrCreateMaterial("BathroomTiles", new Color(0.78f, 0.82f, 0.84f), 0.45f);
            Material steel = GetOrCreateMaterial("Steel", new Color(0.7f, 0.72f, 0.74f), 0.7f);
            Material mirror = GetOrCreateMaterial("Mirror", new Color(0.6f, 0.68f, 0.72f), 0.95f);
            Material towel = GetOrCreateMaterial("Towel", new Color(0.25f, 0.45f, 0.6f));
            float y = floorTop;
            // Measured to the walls' inside faces.
            float left = partition + 0.06f, right = HouseWidth / 2f - 0.18f;
            float middle = (left + right) / 2f, width = right - left;

            AddVisual(PrimitiveType.Cube, house, new Vector3(middle, y + 0.005f, -1.75f), Quaternion.identity, new Vector3(width, 0.01f, 3.3f), tiles);
            // Shower: a tiled tray and walls in the back right corner, with a glass screen.
            Vector3 shower = new(right - 0.5f, y, -HouseDepth / 2f + 0.18f + 0.5f);
            AddSolid(PrimitiveType.Cube, house, shower + new Vector3(0f, 0.05f, 0f), Quaternion.identity, new Vector3(1f, 0.1f, 1f), white);
            AddVisual(PrimitiveType.Cube, house, shower + new Vector3(0.49f, 1.1f, 0f), Quaternion.identity, new Vector3(0.02f, 2f, 1f), tiles);
            AddVisual(PrimitiveType.Cube, house, shower + new Vector3(0f, 1.1f, -0.49f), Quaternion.identity, new Vector3(1f, 2f, 0.02f), tiles);
            AddSolid(PrimitiveType.Cube, house, shower + new Vector3(-0.5f, 1.05f, 0.05f), Quaternion.identity, new Vector3(0.02f, 1.9f, 0.9f), GetOrCreateGlassMaterial());
            AddVisual(PrimitiveType.Cylinder, house, shower + new Vector3(0.3f, 2f, -0.3f), Quaternion.identity, new Vector3(0.18f, 0.02f, 0.18f), steel);
            // Toilet against the right wall.
            Vector3 toilet = new(right - 0.35f, y, -1.6f);
            AddSolid(PrimitiveType.Cylinder, house, toilet + new Vector3(0f, 0.2f, 0f), Quaternion.identity, new Vector3(0.38f, 0.2f, 0.45f), white);
            AddVisual(PrimitiveType.Cube, house, toilet + new Vector3(0.18f, 0.6f, 0f), Quaternion.identity, new Vector3(0.18f, 0.4f, 0.42f), white);
            AddVisual(PrimitiveType.Cylinder, house, toilet + new Vector3(-0.02f, 0.41f, 0f), Quaternion.identity, new Vector3(0.4f, 0.015f, 0.46f), white);
            // Basin on a vanity against the partition, a mirror above.
            Vector3 basin = new(left + 0.3f, y, -2.3f);
            AddSolid(PrimitiveType.Cube, house, basin + new Vector3(0f, 0.4f, 0f), Quaternion.identity, new Vector3(0.5f, 0.8f, 0.8f), GetOrCreateMaterial("Cupboards", new Color(0.55f, 0.62f, 0.55f)));
            AddVisual(PrimitiveType.Cylinder, house, basin + new Vector3(0f, 0.84f, 0f), Quaternion.identity, new Vector3(0.38f, 0.05f, 0.5f), white);
            AddVisual(PrimitiveType.Cube, house, basin + new Vector3(-0.24f, 1.5f, 0f), Quaternion.identity, new Vector3(0.02f, 0.7f, 0.55f), mirror);
            // A towel on a rail by the door.
            AddVisual(PrimitiveType.Cube, house, new Vector3(left + 0.04f, y + 1.1f, -0.7f), Quaternion.identity, new Vector3(0.05f, 0.6f, 0.45f), towel);
        }

        /// <summary>
        /// A straight solid wall from <paramref name="a"/> to <paramref name="b"/> (local x, z) standing on the floor,
        /// with doorways cut in it: each gap is its distance along the wall from <paramref name="a"/>, its width and height.
        /// </summary>
        static void AddWall(GameObject root, Vector2 a, Vector2 b, float floorTop, float height, float thickness, Material material,
            params (float at, float width, float height)[] gaps) =>
            AddTrimmedWall(root, a, b, floorTop, height, thickness, material, null, gaps);

        /// <summary>
        /// <see cref="AddWall"/> with <paramref name="trim"/> boards: a baseboard along the foot of both faces, and casing
        /// round each doorway on both sides.
        /// </summary>
        static void AddTrimmedWall(GameObject root, Vector2 a, Vector2 b, float floorTop, float height, float thickness, Material material,
            Material trim, params (float at, float width, float height)[] gaps)
        {
            Vector2 direction = b - a;
            float length = direction.magnitude;
            direction /= length;
            Quaternion rotation = Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.y));

            void Piece(float from, float to, float bottom, float topOf)
            {
                if (to - from < 0.01f || topOf - bottom < 0.01f)
                    return;
                Vector2 middle = a + direction * ((from + to) / 2f);
                AddSolid(PrimitiveType.Cube, root, new Vector3(middle.x, floorTop + (bottom + topOf) / 2f, middle.y), rotation,
                    new Vector3(thickness, topOf - bottom, to - from), material);
                if (trim == null || bottom > 0f)
                    return;
                foreach (float face in new[] { -1f, 1f })
                {
                    Vector3 at = new Vector3(middle.x, floorTop + 0.05f, middle.y) + rotation * Vector3.right * face * (thickness / 2f + 0.008f);
                    AddVisual(PrimitiveType.Cube, root, at, rotation, new Vector3(0.016f, 0.1f, to - from), trim);
                }
            }

            void Casing(float at, float width, float gapHeight)
            {
                if (trim == null)
                    return;
                foreach (float face in new[] { -1f, 1f })
                {
                    Vector3 outward = rotation * Vector3.right * face * (thickness / 2f + 0.01f);
                    foreach (float edge in new[] { at - width / 2f - 0.035f, at + width / 2f + 0.035f })
                    {
                        Vector2 side = a + direction * edge;
                        AddVisual(PrimitiveType.Cube, root, new Vector3(side.x, floorTop + gapHeight / 2f, side.y) + outward, rotation,
                            new Vector3(0.02f, gapHeight, 0.07f), trim);
                    }
                    Vector2 head = a + direction * at;
                    AddVisual(PrimitiveType.Cube, root, new Vector3(head.x, floorTop + gapHeight + 0.035f, head.y) + outward, rotation,
                        new Vector3(0.02f, 0.07f, width + 0.14f), trim);
                }
            }

            float cursor = 0f;
            var sorted = new System.Collections.Generic.List<(float at, float width, float height)>(gaps);
            sorted.Sort((x, y) => x.at.CompareTo(y.at));
            foreach ((float at, float width, float gapHeight) in sorted)
            {
                Piece(cursor, at - width / 2f, 0f, height);
                Piece(at - width / 2f, at + width / 2f, gapHeight, height);
                Casing(at, width, gapHeight);
                cursor = at + width / 2f;
            }
            Piece(cursor, length, 0f, height);
        }
    }
}
