using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// The finishing touches on your home, so it reads as somewhere lived in. Outside: lap siding, corner boards, a
    /// stone foundation, shutters, a window box, gutters, a porch light, a bench, a woodpile and a rain barrel.
    /// Inside: ceiling beams and lamp shades, curtains, pictures and a clock, a coat rack, cushions and a throw, a
    /// floor lamp, a log basket, cupboard doors and handles, an oven, tiles, a dish rack and fruit bowl, things on
    /// the nightstands, a laundry basket, and the bathroom's fittings. Same local space as <see cref="CreateHome"/>.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        static void DetailHouseOutside(GameObject house, float floorTop, float top, float rise, Material walls, Material trim, Material wood, Material metal)
        {
            Material siding = GetOrCreateMaterial("SidingShadow", new Color(0.3f, 0.21f, 0.14f));
            Material stone = GetOrCreateMaterial("FoundationStone", new Color(0.42f, 0.41f, 0.39f));
            Material shutter = GetOrCreateMaterial("Shutters", new Color(0.18f, 0.3f, 0.22f));
            Material soil = GetOrCreateMaterial("Soil", new Color(0.22f, 0.15f, 0.1f));
            Material gutter = GetOrCreateMaterial("Gutter", new Color(0.55f, 0.56f, 0.57f), 0.5f);
            Material bark = GetOrCreateMaterial("LogBark", new Color(0.36f, 0.26f, 0.17f));
            Material logEnd = GetOrCreateMaterial("LogEnd", new Color(0.72f, 0.58f, 0.4f));
            Material barrel = GetOrCreateMaterial("RainBarrel", new Color(0.2f, 0.32f, 0.4f), 0.3f);
            Material porchGlow = GetOrCreateEmissiveMaterial("PorchLight", new Color(1f, 0.82f, 0.55f));
            Material[] flowers =
            {
                GetOrCreateMaterial("FlowerRed", new Color(0.8f, 0.15f, 0.15f)), GetOrCreateMaterial("FlowerYellow", new Color(0.95f, 0.8f, 0.2f)),
                GetOrCreateMaterial("FlowerWhite", new Color(0.92f, 0.92f, 0.9f)),
            };
            Material leaves = GetOrCreateMaterial("PlantLeaves", new Color(0.2f, 0.42f, 0.18f));
            float halfWidth = HouseWidth / 2f, halfDepth = HouseDepth / 2f;
            const float doorLeft = -1.7f, doorRight = -0.7f, doorTop = 2.1f;

            // Lap siding: a shadow line every 22 cm on each outside face, broken at the front door.
            for (float y = floorTop + 0.22f; y < top - 0.05f; y += 0.22f)
            {
                bool besideDoor = y - floorTop < doorTop + 0.05f;
                foreach (float z in new[] { halfDepth + 0.006f, -halfDepth - 0.006f })
                {
                    if (z > 0f && besideDoor)
                    {
                        float leftLength = doorLeft + halfWidth, rightLength = halfWidth - doorRight;
                        AddVisual(PrimitiveType.Cube, house, new Vector3(-halfWidth + leftLength / 2f, y, z), Quaternion.identity, new Vector3(leftLength, 0.012f, 0.01f), siding);
                        AddVisual(PrimitiveType.Cube, house, new Vector3(halfWidth - rightLength / 2f, y, z), Quaternion.identity, new Vector3(rightLength, 0.012f, 0.01f), siding);
                    }
                    else
                        AddVisual(PrimitiveType.Cube, house, new Vector3(0f, y, z), Quaternion.identity, new Vector3(HouseWidth, 0.012f, 0.01f), siding);
                }
                foreach (float x in new[] { halfWidth + 0.006f, -halfWidth - 0.006f })
                    AddVisual(PrimitiveType.Cube, house, new Vector3(x, y, 0f), Quaternion.identity, new Vector3(0.01f, 0.012f, HouseDepth), siding);
            }
            // Corner boards, a stone foundation, the ridge cap and a cap on the chimney.
            foreach (float x in new[] { -halfWidth, halfWidth })
            foreach (float z in new[] { -halfDepth, halfDepth })
                AddVisual(PrimitiveType.Cube, house, new Vector3(x, (floorTop + top) / 2f, z), Quaternion.identity, new Vector3(0.2f, top - floorTop + 0.02f, 0.2f), trim);
            AddVisual(PrimitiveType.Cube, house, new Vector3(0f, 0.15f, -halfDepth - 0.03f), Quaternion.identity, new Vector3(HouseWidth + 0.26f, 0.32f, 0.06f), stone);
            AddVisual(PrimitiveType.Cube, house, new Vector3(0f, 0.15f, halfDepth + 0.03f), Quaternion.identity, new Vector3(HouseWidth + 0.26f, 0.32f, 0.06f), stone);
            foreach (float x in new[] { -halfWidth - 0.03f, halfWidth + 0.03f })
                AddVisual(PrimitiveType.Cube, house, new Vector3(x, 0.15f, 0f), Quaternion.identity, new Vector3(0.06f, 0.32f, HouseDepth + 0.26f), stone);
            AddVisual(PrimitiveType.Cube, house, new Vector3(0f, top + rise + 0.13f, 0f), Quaternion.identity, new Vector3(HouseWidth + 0.75f, 0.1f, 0.28f), metal);
            AddVisual(PrimitiveType.Cube, house, new Vector3(-3.3f, top + rise * 0.7f + 0.83f, 2.4f), Quaternion.identity, new Vector3(0.6f, 0.06f, 0.6f), metal);

            // Gutters along both eaves, a downspout at the front corner.
            foreach (float z in new[] { halfDepth + 0.38f, -halfDepth - 0.38f })
                AddVisual(PrimitiveType.Cube, house, new Vector3(0f, top - 0.04f, z), Quaternion.identity, new Vector3(HouseWidth + 0.7f, 0.08f, 0.1f), gutter);
            AddVisual(PrimitiveType.Cylinder, house, new Vector3(halfWidth - 0.15f, (top + 0.1f) / 2f, halfDepth + 0.07f), Quaternion.identity, new Vector3(0.07f, top / 2f, 0.07f), gutter);
            AddVisual(PrimitiveType.Cube, house, new Vector3(halfWidth - 0.15f, top - 0.06f, halfDepth + 0.22f), Quaternion.identity, new Vector3(0.06f, 0.06f, 0.3f), gutter);

            // Shutters either side of the front windows, and a window box of flowers under the left one.
            foreach ((float x, float width, float y, float height) in new[] { (-3f, 1.2f, floorTop + 1.45f, 1f), (2.7f, 1.1f, floorTop + 1.55f, 0.8f) })
                foreach (float side in new[] { -1f, 1f })
                {
                    float at = x + side * (width / 2f + 0.2f);
                    AddVisual(PrimitiveType.Cube, house, new Vector3(at, y, halfDepth + 0.04f), Quaternion.identity, new Vector3(0.26f, height + 0.12f, 0.04f), shutter);
                    for (int slat = 0; slat < 5; slat++)
                        AddVisual(PrimitiveType.Cube, house, new Vector3(at, y - height / 2f + 0.1f + slat * (height / 5f), halfDepth + 0.065f), Quaternion.Euler(20f, 0f, 0f),
                            new Vector3(0.2f, 0.012f, 0.012f), siding);
                }
            Vector3 box = new(-3f, floorTop + 0.82f, halfDepth + 0.13f);
            AddVisual(PrimitiveType.Cube, house, box, Quaternion.identity, new Vector3(1.2f, 0.18f, 0.22f), wood);
            AddVisual(PrimitiveType.Cube, house, box + new Vector3(0f, 0.085f, 0f), Quaternion.identity, new Vector3(1.12f, 0.02f, 0.18f), soil);
            var random = new System.Random(Seed + 31);
            for (int i = 0; i < 9; i++)
            {
                var at = box + new Vector3(-0.5f + i * 0.125f, 0.16f + (float)random.NextDouble() * 0.06f, ((float)random.NextDouble() - 0.5f) * 0.1f);
                AddVisual(PrimitiveType.Sphere, house, at - Vector3.up * 0.04f, Quaternion.identity, new Vector3(0.12f, 0.08f, 0.12f), leaves);
                AddVisual(PrimitiveType.Sphere, house, at, Quaternion.identity, Vector3.one * 0.06f, flowers[random.Next(flowers.Length)]);
            }

            // By the door: a porch light, the house number, a mat; a bench along the porch.
            AddVisual(PrimitiveType.Cube, house, new Vector3(-0.45f, floorTop + 2.05f, halfDepth + 0.06f), Quaternion.identity, new Vector3(0.12f, 0.2f, 0.1f), metal);
            AddVisual(PrimitiveType.Sphere, house, new Vector3(-0.45f, floorTop + 2.0f, halfDepth + 0.12f), Quaternion.identity, Vector3.one * 0.1f, porchGlow);
            AddVisual(PrimitiveType.Cube, house, new Vector3(-0.45f, floorTop + 1.6f, halfDepth + 0.02f), Quaternion.identity, new Vector3(0.18f, 0.12f, 0.02f), metal);
            AddVisual(PrimitiveType.Cube, house, new Vector3(-1.2f, floorTop + 0.006f, halfDepth + 0.45f), Quaternion.identity, new Vector3(0.8f, 0.012f, 0.5f),
                GetOrCreateMaterial("DoorMat", new Color(0.35f, 0.28f, 0.18f)));
            Vector3 bench = new(-2.15f, floorTop, halfDepth + 0.35f);
            AddSolid(PrimitiveType.Cube, house, bench + new Vector3(0f, 0.43f, 0f), Quaternion.identity, new Vector3(0.9f, 0.05f, 0.35f), wood);
            foreach (float x in new[] { -0.38f, 0.38f })
                AddVisual(PrimitiveType.Cube, house, bench + new Vector3(x, 0.21f, 0f), Quaternion.identity, new Vector3(0.05f, 0.42f, 0.32f), wood);
            AddVisual(PrimitiveType.Cube, house, bench + new Vector3(0f, 0.75f, -0.15f), Quaternion.identity, new Vector3(0.9f, 0.3f, 0.04f), wood);
            Furnish(house, "planter_box_01", new Vector3(-0.2f, floorTop, halfDepth + 0.32f), 0f, solid: true);

            // A woodpile against the side wall under the chimney: logs stacked end-out.
            for (int row = 0; row < 4; row++)
                for (int i = 0; i < 7 - row; i++)
                {
                    var at = new Vector3(-halfWidth - 0.33f, 0.09f + row * 0.15f, 1.65f + i * 0.17f + row * 0.085f);
                    AddVisual(PrimitiveType.Cylinder, house, at, Quaternion.Euler(0f, 0f, 90f), new Vector3(0.15f, 0.25f, 0.15f), bark);
                    AddVisual(PrimitiveType.Cylinder, house, at + new Vector3(-0.251f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.13f, 0.002f, 0.13f), logEnd);
                }
            Furnish(house, "wooden_axe", new Vector3(-halfWidth - 0.3f, 0f, 1.38f), 90f);
            // A rain barrel at the back corner, under the gutter, and a bucket beside it.
            Furnish(house, "wooden_bucket_01", new Vector3(halfWidth - 0.85f, 0f, -halfDepth - 0.4f), 30f, solid: true);
            AddSolid(PrimitiveType.Cylinder, house, new Vector3(halfWidth - 0.2f, 0.4f, -halfDepth - 0.45f), Quaternion.identity, new Vector3(0.55f, 0.4f, 0.55f), barrel);
        }

        static void DetailHouseInside(GameObject house, float floorTop, float top, Material wood, Material metal, Material trim)
        {
            Material shade = GetOrCreateMaterial("LampShadeFabric", new Color(0.9f, 0.85f, 0.72f));
            Material canvasSky = GetOrCreateMaterial("PaintingSky", new Color(0.55f, 0.7f, 0.82f));
            Material canvasHills = GetOrCreateMaterial("PaintingHills", new Color(0.3f, 0.45f, 0.3f));
            Material canvasPeak = GetOrCreateMaterial("PaintingPeak", new Color(0.85f, 0.85f, 0.88f));
            Material jacket = GetOrCreateMaterial("Jacket", new Color(0.65f, 0.2f, 0.15f));
            Material cushion = GetOrCreateMaterial("Cushion", new Color(0.75f, 0.6f, 0.3f));
            Material throwBlanket = GetOrCreateMaterial("ThrowBlanket", new Color(0.55f, 0.18f, 0.15f));
            Material wicker = GetOrCreateMaterial("Wicker", new Color(0.62f, 0.5f, 0.32f));
            Material bark = GetOrCreateMaterial("LogBark", new Color(0.36f, 0.26f, 0.17f));
            Material white = GetOrCreateMaterial("Porcelain", new Color(0.92f, 0.92f, 0.9f), 0.6f);
            Material steel = GetOrCreateMaterial("Steel", new Color(0.7f, 0.72f, 0.74f), 0.7f);
            Material clockFace = GetOrCreateMaterial("ClockFace", new Color(0.95f, 0.94f, 0.9f));
            float y = floorTop;

            // Beams across the ceiling, and a shade and cord for each lamp.
            foreach (float z in new[] { -2.5f, -1.2f, 1.2f, 2.5f })
                AddVisual(PrimitiveType.Cube, house, new Vector3(0f, top - 0.07f, z), Quaternion.identity, new Vector3(HouseWidth - 0.36f, 0.14f, 0.12f), wood);
            foreach (Vector3 lamp in new[] { new Vector3(-1.8f, top, 1.8f), new Vector3(2.4f, top, 1.8f), new Vector3(-1.6f, top, -1.8f), new Vector3(2.4f, top, -1.8f) })
                if (Furnish(house, "modern_ceiling_lamp_01", lamp - Vector3.up * 0.95f, 0f, scale: 0.75f) == null)
                    AddVisual(PrimitiveType.Cylinder, house, lamp - Vector3.up * 0.18f, Quaternion.identity, new Vector3(0.38f, 0.07f, 0.38f), shade);

            // Curtains on a rod at each main window: (centre on the wall's line, the way into the room, width, height, colour).
            Material living = GetOrCreateMaterial("CurtainLiving", new Color(0.62f, 0.3f, 0.18f));
            Material kitchen = GetOrCreateMaterial("CurtainKitchen", new Color(0.85f, 0.75f, 0.4f));
            Material bedroom = GetOrCreateMaterial("CurtainBedroom", new Color(0.3f, 0.38f, 0.55f));
            float frontLine = HouseDepth / 2f - 0.09f, sideLine = HouseWidth / 2f - 0.09f;
            foreach ((Vector3 centre, Vector3 inward, float width, float height, Material cloth) in new[]
                     {
                         (new Vector3(-3f, y + 1.45f, frontLine), Vector3.back, 1.2f, 1f, living),
                         (new Vector3(2.7f, y + 1.55f, frontLine), Vector3.back, 1.1f, 0.8f, kitchen),
                         (new Vector3(-sideLine, y + 1.45f, 1.7f), Vector3.right, 1.1f, 1f, living),
                         (new Vector3(-sideLine, y + 1.45f, -1.8f), Vector3.right, 1f, 1f, bedroom),
                         (new Vector3(-2f, y + 1.5f, -frontLine), Vector3.forward, 1.1f, 0.9f, bedroom),
                     })
            {
                Vector3 along = Vector3.Cross(Vector3.up, inward);
                Quaternion facing = Quaternion.LookRotation(inward);
                Vector3 rod = centre + inward * 0.2f + Vector3.up * (height / 2f + 0.15f);
                AddVisual(PrimitiveType.Cube, house, rod, facing, new Vector3(width + 0.6f, 0.02f, 0.02f), metal);
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 panel = centre + inward * 0.2f + along * side * (width / 2f + 0.12f);
                    AddVisual(PrimitiveType.Cube, house, panel - Vector3.up * 0.05f, facing, new Vector3(0.3f, height + 0.35f, 0.03f), cloth);
                    // Folds.
                    for (int fold = -1; fold <= 1; fold++)
                        AddVisual(PrimitiveType.Cube, house, panel - Vector3.up * 0.05f + along * fold * 0.09f + inward * 0.018f, facing,
                            new Vector3(0.02f, height + 0.33f, 0.012f), cloth);
                }
            }

            // A mountain painting in the living room's picture frame (its own print is a blank card), a framed
            // trail map in the bedroom, a clock in the kitchen.
            Vector3 painting = new(-0.95f, y + 1.62f, 0.079f);
            AddVisual(PrimitiveType.Cube, house, painting + new Vector3(0f, 0.12f, 0f), Quaternion.identity, new Vector3(0.5f, 0.5f, 0.002f), canvasSky);
            AddVisual(PrimitiveType.Cube, house, painting + new Vector3(0f, -0.25f, 0.0012f), Quaternion.identity, new Vector3(0.5f, 0.24f, 0.002f), canvasHills);
            AddVisual(PrimitiveType.Cube, house, painting + new Vector3(0.04f, -0.06f, 0.0006f), Quaternion.Euler(0f, 0f, 45f), new Vector3(0.24f, 0.24f, 0.002f), canvasPeak);
            Vector3 map = new(0.825f, y + 1.6f, -2.1f);
            AddVisual(PrimitiveType.Cube, house, map, Quaternion.identity, new Vector3(0.03f, 0.6f, 0.8f), wood);
            AddVisual(PrimitiveType.Cube, house, map + new Vector3(-0.012f, 0f, 0f), Quaternion.identity, new Vector3(0.01f, 0.52f, 0.72f),
                GetOrCreateMaterial("MapPaper", new Color(0.88f, 0.84f, 0.7f)));
            for (int line = 0; line < 5; line++)
                AddVisual(PrimitiveType.Cube, house, map + new Vector3(-0.018f, -0.2f + line * 0.09f, -0.25f + line * 0.11f), Quaternion.Euler(30f + line * 15f, 0f, 0f),
                    new Vector3(0.004f, 0.012f, 0.16f), GetOrCreateMaterial("MapTrail", new Color(0.72f, 0.12f, 0.08f)));
            Furnish(house, "wall_clock", new Vector3(1.25f, y + 1.89f, 0.08f), 0f);

            // ---------- Living room ----------

            // A coat rack by the front door with your jacket and hat, and a boot tray.
            Vector3 rack = new(-0.25f, y, 3.0f);
            AddSolid(PrimitiveType.Cylinder, house, rack + new Vector3(0f, 0.85f, 0f), Quaternion.identity, new Vector3(0.05f, 0.85f, 0.05f), wood);
            AddVisual(PrimitiveType.Cylinder, house, rack + new Vector3(0f, 0.02f, 0f), Quaternion.identity, new Vector3(0.4f, 0.02f, 0.4f), wood);
            AddVisual(PrimitiveType.Capsule, house, rack + new Vector3(0.12f, 1.2f, 0f), Quaternion.Euler(0f, 0f, 8f), new Vector3(0.32f, 0.38f, 0.18f), jacket);
            AddVisual(PrimitiveType.Sphere, house, rack + new Vector3(-0.08f, 1.68f, 0f), Quaternion.identity, new Vector3(0.2f, 0.1f, 0.2f), throwBlanket);
            AddVisual(PrimitiveType.Cube, house, new Vector3(-0.35f, y + 0.008f, 2.55f), Quaternion.identity, new Vector3(0.6f, 0.016f, 0.35f), metal);
            // A magazine on the table.
            AddVisual(PrimitiveType.Cube, house, new Vector3(-2.3f, y + 0.538f, 1.82f), Quaternion.Euler(0f, 20f, 0f), new Vector3(0.22f, 0.008f, 0.3f), canvasSky);
            // A floor lamp by the rocking chair.
            Vector3 floorLamp = new(-0.45f, y, 0.4f);
            AddVisual(PrimitiveType.Cylinder, house, floorLamp + new Vector3(0f, 0.015f, 0f), Quaternion.identity, new Vector3(0.3f, 0.015f, 0.3f), metal);
            AddSolid(PrimitiveType.Cylinder, house, floorLamp + new Vector3(0f, 0.8f, 0f), Quaternion.identity, new Vector3(0.025f, 0.8f, 0.025f), metal);
            AddVisual(PrimitiveType.Cylinder, house, floorLamp + new Vector3(0f, 1.62f, 0f), Quaternion.identity, new Vector3(0.38f, 0.12f, 0.38f),
                GetOrCreateEmissiveMaterial("LampShade", new Color(1f, 0.85f, 0.6f)));
            // A log basket by the stove, and the stove's legs and glass door.
            Vector3 basket = new(-2.6f, y, 3.05f);
            AddVisual(PrimitiveType.Cube, house, basket + new Vector3(0f, 0.15f, 0f), Quaternion.identity, new Vector3(0.45f, 0.3f, 0.34f), wicker);
            for (int i = 0; i < 4; i++)
                AddVisual(PrimitiveType.Cylinder, house, basket + new Vector3(-0.12f + i * 0.08f, 0.33f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.08f, 0.2f, 0.08f), bark);
            AddVisual(PrimitiveType.Cube, house, new Vector3(-3.0f, y + 0.36f, 3.0f), Quaternion.identity, new Vector3(0.01f, 0.32f, 0.38f),
                GetOrCreateMaterial("StoveGlass", new Color(0.1f, 0.08f, 0.07f), 0.9f));

            // ---------- Kitchen ----------

            Material cupboardEdge = GetOrCreateMaterial("CupboardEdge", new Color(0.4f, 0.46f, 0.4f));
            // Doors and handles along the front counter (facing into the room) and the side counter.
            for (float x = 1.55f; x < 3.75f; x += 0.55f)
            {
                AddVisual(PrimitiveType.Cube, house, new Vector3(x, y + 0.43f, 2.695f), Quaternion.identity, new Vector3(0.01f, 0.8f, 0.01f), cupboardEdge);
                AddVisual(PrimitiveType.Cube, house, new Vector3(x + 0.43f, y + 0.68f, 2.69f), Quaternion.identity, new Vector3(0.02f, 0.12f, 0.02f), steel);
            }
            AddVisual(PrimitiveType.Cube, house, new Vector3(2.65f, y + 0.74f, 2.695f), Quaternion.identity, new Vector3(2.3f, 0.01f, 0.01f), cupboardEdge);
            AddVisual(PrimitiveType.Cube, house, new Vector3(3.19f, y + 0.68f, 0.97f), Quaternion.identity, new Vector3(0.02f, 0.12f, 0.02f), steel);
            foreach (float z in new[] { 1.69f, 2.18f })
            {
                AddVisual(PrimitiveType.Cube, house, new Vector3(3.195f, y + 0.43f, z), Quaternion.identity, new Vector3(0.01f, 0.8f, 0.01f), cupboardEdge);
                AddVisual(PrimitiveType.Cube, house, new Vector3(3.19f, y + 0.68f, z + 0.3f), Quaternion.identity, new Vector3(0.02f, 0.12f, 0.02f), steel);
            }
            // Tiles behind the worktops.
            Material tile = GetOrCreateMaterial("KitchenTiles", new Color(0.82f, 0.85f, 0.8f), 0.5f);
            AddVisual(PrimitiveType.Cube, house, new Vector3(2.65f, y + 1.04f, 3.315f), Quaternion.identity, new Vector3(2.3f, 0.26f, 0.01f), tile);
            AddVisual(PrimitiveType.Cube, house, new Vector3(3.815f, y + 1.2f, 1.75f), Quaternion.identity, new Vector3(0.01f, 0.6f, 1.9f), tile);
            // Magnets on the freezer door (the fridge and the wall cupboard are AddFridge's and AddWallCupboard's).
            Material[] magnet = { GetOrCreateMaterial("FlowerRed", new Color(0.8f, 0.15f, 0.15f)), GetOrCreateMaterial("FlowerYellow", new Color(0.95f, 0.8f, 0.2f)), canvasSky };
            for (int i = 0; i < 3; i++)
                AddVisual(PrimitiveType.Cube, house, new Vector3(3.097f, y + 1.45f + i * 0.1f, 0.6f - i * 0.12f), Quaternion.identity, new Vector3(0.006f, 0.06f, 0.06f), magnet[i % magnet.Length]);
            // A dish rack with plates by the sink, a knife block, a chopping board, a bin, a bowl of fruit on the table.
            Vector3 rackAt = new(2.0f, y + 0.92f, 3.0f);
            AddVisual(PrimitiveType.Cube, house, rackAt, Quaternion.identity, new Vector3(0.4f, 0.04f, 0.3f), steel);
            for (int i = 0; i < 4; i++)
                AddVisual(PrimitiveType.Cylinder, house, rackAt + new Vector3(-0.12f + i * 0.08f, 0.11f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.22f, 0.006f, 0.22f), white);
            AddVisual(PrimitiveType.Cube, house, new Vector3(3.6f, y + 1.0f, 2.95f), Quaternion.Euler(-12f, 0f, 0f), new Vector3(0.12f, 0.2f, 0.1f), wood);
            for (int i = 0; i < 3; i++)
                AddVisual(PrimitiveType.Cube, house, new Vector3(3.57f + i * 0.03f, y + 1.13f, 2.93f), Quaternion.identity, new Vector3(0.015f, 0.06f, 0.025f), metal);
            AddSolid(PrimitiveType.Cylinder, house, new Vector3(1.22f, y + 0.3f, 3.05f), Quaternion.identity, new Vector3(0.3f, 0.3f, 0.3f), steel);
            Vector3 bowl = new(1.9f, y + 0.8f, 1.5f);
            Material[] fruit = { GetOrCreateMaterial("Apple", new Color(0.75f, 0.12f, 0.1f)), GetOrCreateMaterial("Orange", new Color(0.95f, 0.55f, 0.1f)),
                GetOrCreateMaterial("GreenApple", new Color(0.5f, 0.75f, 0.2f)) };
            for (int i = 0; i < 4; i++)
                AddVisual(PrimitiveType.Sphere, house, bowl + new Vector3(Mathf.Cos(i * 1.6f) * 0.06f, 0.07f + (i == 3 ? 0.05f : 0f), Mathf.Sin(i * 1.6f) * 0.06f), Quaternion.identity,
                    Vector3.one * 0.08f, fruit[i % fruit.Length]);
            // Two mugs by the kettle.
            foreach (float z in new[] { 1.85f, 2.0f })
                AddVisual(PrimitiveType.Cylinder, house, new Vector3(3.55f, y + 0.95f, z), Quaternion.identity, new Vector3(0.08f, 0.05f, 0.08f), white);

            // ---------- Bedroom ----------

            // Slippers by the bed, a laundry basket, wardrobe handles.
            foreach (float x in new[] { -1.02f, -0.88f })
                AddVisual(PrimitiveType.Capsule, house, new Vector3(x, y + 0.03f, -1.05f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.1f, 0.13f, 0.06f), cushion);
            AddVisual(PrimitiveType.Cylinder, house, new Vector3(0.5f, y + 0.25f, -3.0f), Quaternion.identity, new Vector3(0.45f, 0.25f, 0.45f), wicker);
            AddVisual(PrimitiveType.Sphere, house, new Vector3(0.5f, y + 0.5f, -3.0f), Quaternion.identity, new Vector3(0.38f, 0.12f, 0.38f), canvasSky);
            foreach (float z in new[] { -0.75f, -0.65f })
                AddVisual(PrimitiveType.Cube, house, new Vector3(-3.24f, y + 1.05f, z), Quaternion.identity, new Vector3(0.03f, 0.18f, 0.02f), steel);

            // ---------- Bathroom ----------

            float left = 0.96f, right = HouseWidth / 2f - 0.18f;
            // Toilet seat and lid, a roll of paper on the wall.
            AddVisual(PrimitiveType.Cylinder, house, new Vector3(right - 0.37f, y + 0.425f, -1.6f), Quaternion.identity, new Vector3(0.38f, 0.01f, 0.44f), white);
            AddVisual(PrimitiveType.Cube, house, new Vector3(right - 0.15f, y + 0.55f, -1.6f), Quaternion.Euler(0f, 0f, 75f), new Vector3(0.32f, 0.02f, 0.38f), white);
            AddVisual(PrimitiveType.Cylinder, house, new Vector3(right - 0.06f, y + 0.75f, -1.15f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.11f, 0.05f, 0.11f), white);
            AddVisual(PrimitiveType.Cube, house, new Vector3(right - 0.02f, y + 0.8f, -1.15f), Quaternion.identity, new Vector3(0.04f, 0.02f, 0.14f), steel);
            // A tap on the basin, soap and a toothbrush mug, a frame round the mirror.
            Vector3 basin = new(left + 0.3f, y, -2.3f);
            AddVisual(PrimitiveType.Cylinder, house, basin + new Vector3(-0.17f, 0.98f, 0f), Quaternion.identity, new Vector3(0.03f, 0.08f, 0.03f), steel);
            AddVisual(PrimitiveType.Cube, house, basin + new Vector3(-0.1f, 1.05f, 0f), Quaternion.identity, new Vector3(0.14f, 0.02f, 0.025f), steel);
            AddVisual(PrimitiveType.Cylinder, house, basin + new Vector3(-0.15f, 0.9f, 0.28f), Quaternion.identity, new Vector3(0.06f, 0.05f, 0.06f), canvasSky);
            AddVisual(PrimitiveType.Cylinder, house, basin + new Vector3(-0.13f, 0.98f, 0.28f), Quaternion.identity, new Vector3(0.012f, 0.06f, 0.012f), white);
            AddVisual(PrimitiveType.Cube, house, basin + new Vector3(-0.15f, 0.87f, -0.28f), Quaternion.identity, new Vector3(0.08f, 0.03f, 0.05f), cushion);
            AddVisual(PrimitiveType.Cube, house, basin + new Vector3(-0.245f, 1.5f, 0f), Quaternion.identity, new Vector3(0.015f, 0.78f, 0.63f), wood);
            // A bath mat by the shower, bottles on a shower shelf, a bin.
            AddVisual(PrimitiveType.Cube, house, new Vector3(right - 0.5f, y + 0.012f, -2.0f), Quaternion.identity, new Vector3(0.7f, 0.012f, 0.45f),
                GetOrCreateMaterial("Towel", new Color(0.25f, 0.45f, 0.6f)));
            Vector3 shelf = new(right - 0.06f, y + 1.3f, -2.95f);
            AddVisual(PrimitiveType.Cube, house, shelf, Quaternion.identity, new Vector3(0.12f, 0.02f, 0.4f), steel);
            for (int i = 0; i < 3; i++)
                AddVisual(PrimitiveType.Cylinder, house, shelf + new Vector3(0f, 0.09f, -0.12f + i * 0.12f), Quaternion.identity, new Vector3(0.06f, 0.08f, 0.06f), magnet[i]);
            AddVisual(PrimitiveType.Cylinder, house, new Vector3(left + 0.2f, y + 0.15f, -1.3f), Quaternion.identity, new Vector3(0.22f, 0.15f, 0.22f), steel);
        }
    }
}
