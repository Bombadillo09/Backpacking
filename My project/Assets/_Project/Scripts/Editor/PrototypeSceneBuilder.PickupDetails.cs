using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// A second layer of detail on the pickup: the lived-in cab (mats, pedals, door cards, a coffee cup, a map, an
    /// air freshener) and more trim outside (drip rails, fog lights, tow hooks, emblems, bed ribs, the spare wheel).
    /// Same local space as <see cref="CreatePickup"/>: +Z forward, origin on the ground between the axles.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        static void BuildMoreDetails(GameObject truck, Material paint, Material black, Material chrome, Material interior)
        {
            Material mat = GetOrCreateMaterial("TruckFloorMat", new Color(0.09f, 0.09f, 0.08f), 0.05f);
            Material panel = GetOrCreateMaterial("TruckDoorCard", new Color(0.32f, 0.3f, 0.27f), 0.1f);
            Material glow = GetOrCreateEmissiveMaterial("TruckDomeLight", new Color(1f, 0.92f, 0.75f));
            Material red = GetOrCreateMaterial("TruckTowHook", new Color(0.7f, 0.1f, 0.08f), 0.4f);
            Material fog = GetOrCreateEmissiveMaterial("TruckFogLight", new Color(1f, 0.9f, 0.6f));
            Material paper = GetOrCreateMaterial("Paper", new Color(0.9f, 0.86f, 0.74f));
            Material cup = GetOrCreateMaterial("CoffeeCup", new Color(0.85f, 0.85f, 0.82f), 0.3f);
            Material tree = GetOrCreateMaterial("AirFreshener", new Color(0.15f, 0.55f, 0.2f));
            Material mirror = GetOrCreateMaterial("Mirror", new Color(0.6f, 0.68f, 0.72f), 0.95f);
            const float floor = CabFloor;

            // ---------- The cab ----------

            foreach (float x in new[] { -0.42f, 0.42f })
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, floor + 0.006f, 0.75f), Quaternion.identity, new Vector3(0.55f, 0.012f, 0.62f), mat);
            // Clutch, brake and accelerator.
            foreach ((float x, float width) in new[] { (-0.62f, 0.07f), (-0.47f, 0.08f), (-0.28f, 0.05f) })
            {
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, floor + 0.17f, 0.98f), Quaternion.Euler(-35f, 0f, 0f), new Vector3(width, 0.1f, 0.015f), black);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, floor + 0.35f, 1.04f), Quaternion.Euler(20f, 0f, 0f), new Vector3(0.015f, 0.3f, 0.015f), black);
            }
            // Handbrake handle under the dash.
            AddVisual(PrimitiveType.Cube, truck, new Vector3(-0.78f, floor + 0.42f, 1.0f), Quaternion.identity, new Vector3(0.1f, 0.03f, 0.03f), black);
            // Door cards: armrest, inside handle, window crank and lock knob.
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * 0.92f;
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, floor + 0.38f, 0.5f), Quaternion.identity, new Vector3(0.02f, 0.5f, 1.4f), panel);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x - side * 0.03f, floor + 0.47f, 0.2f), Quaternion.identity, new Vector3(0.06f, 0.04f, 0.42f), interior);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x - side * 0.015f, floor + 0.54f, 0.75f), Quaternion.identity, new Vector3(0.02f, 0.03f, 0.09f), chrome);
                AddVisual(PrimitiveType.Cylinder, truck, new Vector3(x - side * 0.02f, floor + 0.33f, 0.45f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.05f, 0.012f, 0.05f), chrome);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x - side * 0.04f, floor + 0.36f, 0.43f), Quaternion.identity, new Vector3(0.012f, 0.012f, 0.06f), black);
                AddVisual(PrimitiveType.Cylinder, truck, new Vector3(side * 0.9f, 1.28f, -0.15f), Quaternion.identity, new Vector3(0.012f, 0.03f, 0.012f), black);
                // Stitching across the bench back.
                for (int row = 0; row < 3; row++)
                    AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.42f, floor + 0.5f + row * 0.18f, -0.162f - row * 0.03f), Quaternion.Euler(-10f, 0f, 0f),
                        new Vector3(0.7f, 0.005f, 0.005f), black);
            }
            // Dome light, dash vents, glovebox handle, ignition key, indicator stalks.
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.905f, 0.35f), Quaternion.identity, new Vector3(0.2f, 0.02f, 0.1f), glow);
            foreach (float x in new[] { -0.15f, 0.15f })
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, floor + 0.62f, 0.985f), Quaternion.identity, new Vector3(0.14f, 0.04f, 0.01f), mat);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0.45f, floor + 0.5f, 0.978f), Quaternion.identity, new Vector3(0.1f, 0.02f, 0.01f), chrome);
            AddVisual(PrimitiveType.Cylinder, truck, new Vector3(-0.18f, floor + 0.55f, 0.98f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.03f, 0.01f, 0.03f), chrome);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(-0.18f, floor + 0.53f, 0.965f), Quaternion.identity, new Vector3(0.012f, 0.04f, 0.004f), chrome);
            foreach (float x in new[] { -0.6f, -0.24f })
                AddVisual(PrimitiveType.Cylinder, truck, new Vector3(x, floor + 0.64f, 0.9f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.012f, 0.09f, 0.012f), black);
            // A coffee in a travel cup on the dash, a folded road map, and a pine-tree air freshener on the mirror.
            AddVisual(PrimitiveType.Cylinder, truck, new Vector3(0.18f, 1.36f, 1.1f), Quaternion.identity, new Vector3(0.07f, 0.065f, 0.07f), cup);
            AddVisual(PrimitiveType.Cylinder, truck, new Vector3(0.18f, 1.43f, 1.1f), Quaternion.identity, new Vector3(0.075f, 0.008f, 0.075f), black);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0.55f, 1.3f, 1.12f), Quaternion.Euler(-8f, 15f, 0f), new Vector3(0.3f, 0.006f, 0.2f), paper);
            AddVisual(PrimitiveType.Cylinder, truck, new Vector3(0f, 1.72f, 1.05f), Quaternion.identity, new Vector3(0.003f, 0.05f, 0.003f), paper);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.62f, 1.05f), Quaternion.identity, new Vector3(0.06f, 0.1f, 0.004f), tree);

            // ---------- Outside ----------

            foreach (float side in new[] { -1f, 1f })
            {
                // Drip rail along the roof edge, chrome round the top of the door window.
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.975f, 1.955f, 0.42f), Quaternion.identity, new Vector3(0.025f, 0.025f, 1.55f), chrome);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.972f, 1.9f, 0.45f), Quaternion.identity, new Vector3(0.012f, 0.02f, 1.3f), chrome);
                // Mirror glass.
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 1.026f, 1.37f, 1.12f), Quaternion.identity, new Vector3(0.04f, 0.14f, 0.005f), mirror);
                // Fog lights and red tow hooks at the front.
                AddVisual(PrimitiveType.Cylinder, truck, new Vector3(side * 0.6f, 0.66f, 2.795f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.1f, 0.01f, 0.1f), fog);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.42f, 0.5f, 2.72f), Quaternion.Euler(-30f, 0f, 0f), new Vector3(0.04f, 0.12f, 0.04f), red);
                // Stake pockets along the bed rails.
                foreach (float z in new[] { -0.7f, -1.5f, -2.3f })
                    AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.94f, 1.363f, z), Quaternion.identity, new Vector3(0.06f, 0.004f, 0.06f), black);
            }
            // Windscreen trim, a skid plate under the front, emblems on the grille and tailgate.
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.935f, 0.99f), Quaternion.identity, new Vector3(1.9f, 0.025f, 0.03f), chrome);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 0.46f, 2.35f), Quaternion.identity, new Vector3(1f, 0.03f, 0.6f), black);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.1f, 2.685f), Quaternion.identity, new Vector3(0.26f, 0.06f, 0.02f), chrome);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.16f, -2.665f), Quaternion.identity, new Vector3(0.4f, 0.05f, 0.01f), chrome);
            // Step pad on the rear bumper, ribs along the bed floor, the fuel filler door.
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 0.745f, -2.72f), Quaternion.identity, new Vector3(0.5f, 0.01f, 0.1f), black);
            for (int i = 0; i < 6; i++)
                AddVisual(PrimitiveType.Cube, truck, new Vector3(-0.75f + i * 0.3f, 0.937f, -1.5f), Quaternion.identity, new Vector3(0.04f, 0.015f, 2.2f), black);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(-0.985f, 1.08f, -1.0f), Quaternion.identity, new Vector3(0.01f, 0.13f, 0.13f), paint);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(-0.99f, 1.08f, -1.0f), Quaternion.identity, new Vector3(0.005f, 0.14f, 0.14f), black);
            // The spare wheel, slung under the back of the bed.
            AddVisual(PrimitiveType.Cylinder, truck, new Vector3(0f, 0.45f, -2.25f), Quaternion.identity, new Vector3(0.74f, 0.1f, 0.74f), black);
            AddVisual(PrimitiveType.Cylinder, truck, new Vector3(0f, 0.452f, -2.25f), Quaternion.identity, new Vector3(0.44f, 0.101f, 0.44f), chrome);
        }
    }
}
