using System.Collections.Generic;
using Backpacking.Survival;
using UnityEngine;

namespace Backpacking.Player
{
    /// <summary>
    /// Foods with no model of their own, shaped from simple forms to hold in the hand: a pouch of trail mix, a
    /// handful of berries, a whole fish, a piece of meat, strips of jerky. Origin at the grip, +Y along the item.
    /// </summary>
    public static class HeldFood
    {
        public static GameObject Build(FoodKind food, Material plain, List<Material> owned)
        {
            if (plain == null)
                return null;
            var root = new GameObject("Held food");
            var random = new System.Random((int)food * 7919 + 11);

            Material Tint(Color colour, float smoothness = 0.35f)
            {
                var material = new Material(plain);
                material.SetColor("_BaseColor", colour);
                material.SetFloat("_Smoothness", smoothness);
                owned.Add(material);
                return material;
            }

            void Part(PrimitiveType type, Vector3 position, Vector3 euler, Vector3 scale, Material material)
            {
                GameObject part = GameObject.CreatePrimitive(type);
                if (Application.isPlaying)
                    Object.Destroy(part.GetComponent<Collider>());
                else
                    Object.DestroyImmediate(part.GetComponent<Collider>());
                part.transform.SetParent(root.transform, false);
                part.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(euler));
                part.transform.localScale = scale;
                part.GetComponent<Renderer>().sharedMaterial = material;
            }

            switch (food)
            {
                case FoodKind.TrailMix:
                {
                    // A resealable pouch, gripped at the bottom, with the zip strip across the top.
                    Material pouch = Tint(new Color(0.78f, 0.5f, 0.18f), 0.55f);
                    Part(PrimitiveType.Cube, new Vector3(0f, 0.06f, 0f), Vector3.zero, new Vector3(0.1f, 0.15f, 0.028f), pouch);
                    Part(PrimitiveType.Capsule, new Vector3(0f, 0.035f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.05f, 0.05f, 0.04f), pouch);
                    Part(PrimitiveType.Cube, new Vector3(0f, 0.13f, 0f), Vector3.zero, new Vector3(0.1f, 0.012f, 0.03f), Tint(new Color(0.2f, 0.35f, 0.18f)));
                    Part(PrimitiveType.Cube, new Vector3(0f, 0.07f, 0.0145f), Vector3.zero, new Vector3(0.06f, 0.05f, 0.002f), Tint(new Color(0.93f, 0.9f, 0.8f)));
                    break;
                }
                case FoodKind.Berries:
                {
                    // A loose handful in the palm.
                    Material berry = Tint(new Color(0.18f, 0.1f, 0.3f), 0.7f), red = Tint(new Color(0.5f, 0.08f, 0.15f), 0.7f);
                    for (int i = 0; i < 11; i++)
                    {
                        var at = new Vector3((float)random.NextDouble() * 0.06f - 0.03f, (float)random.NextDouble() * 0.03f, (float)random.NextDouble() * 0.05f - 0.01f);
                        float size = 0.014f + (float)random.NextDouble() * 0.006f;
                        Part(PrimitiveType.Sphere, at, Vector3.zero, Vector3.one * size, i % 4 == 0 ? red : berry);
                    }
                    break;
                }
                case FoodKind.RawFish:
                case FoodKind.CookedFish:
                case FoodKind.SmokedFish:
                {
                    // Held by the tail, head up: body, tail fin and a back fin.
                    Color skin = food == FoodKind.RawFish ? new Color(0.55f, 0.6f, 0.55f) : food == FoodKind.CookedFish ? new Color(0.72f, 0.5f, 0.25f) : new Color(0.38f, 0.22f, 0.1f);
                    Material body = Tint(skin, food == FoodKind.RawFish ? 0.8f : 0.4f);
                    Part(PrimitiveType.Sphere, new Vector3(0f, 0.14f, 0f), Vector3.zero, new Vector3(0.03f, 0.24f, 0.065f), body);
                    Part(PrimitiveType.Cube, new Vector3(0f, 0.015f, 0f), new Vector3(0f, 0f, 0f), new Vector3(0.006f, 0.045f, 0.06f), body);
                    Part(PrimitiveType.Cube, new Vector3(0f, 0.16f, -0.034f), new Vector3(20f, 0f, 0f), new Vector3(0.004f, 0.06f, 0.02f), body);
                    // An eye near the head.
                    Part(PrimitiveType.Sphere, new Vector3(0.012f, 0.235f, 0.012f), Vector3.zero, Vector3.one * 0.008f, Tint(new Color(0.05f, 0.05f, 0.05f), 0.9f));
                    break;
                }
                case FoodKind.Jerky:
                {
                    Material jerky = Tint(new Color(0.32f, 0.14f, 0.08f), 0.3f);
                    for (int i = 0; i < 3; i++)
                        Part(PrimitiveType.Cube, new Vector3(i * 0.012f - 0.012f, 0.05f, i * 0.004f), new Vector3(0f, 0f, i * 8f - 8f), new Vector3(0.022f, 0.12f, 0.004f), jerky);
                    break;
                }
                default:
                {
                    // Meat: a rough chunk, raw red or cooked brown. A rabbit carcass is a bigger, paler one.
                    Color colour = food == FoodKind.CookedMeat ? new Color(0.45f, 0.25f, 0.12f) : food == FoodKind.RabbitCarcass ? new Color(0.6f, 0.45f, 0.38f) : new Color(0.6f, 0.15f, 0.12f);
                    float size = food == FoodKind.RabbitCarcass ? 1.8f : 1f;
                    Material meat = Tint(colour, food == FoodKind.CookedMeat ? 0.35f : 0.6f);
                    Part(PrimitiveType.Sphere, new Vector3(0f, 0.045f * size, 0f), new Vector3(0f, 20f, 10f), new Vector3(0.07f, 0.09f, 0.04f) * size, meat);
                    Part(PrimitiveType.Sphere, new Vector3(0.015f, 0.08f * size, 0.005f), new Vector3(0f, -10f, 0f), new Vector3(0.05f, 0.05f, 0.035f) * size, meat);
                    break;
                }
            }
            return root;
        }
    }
}
