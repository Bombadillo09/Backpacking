using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Wildlife
{
    /// <summary>
    /// Simple stand-in animals built from primitives, used until real models are assigned to the spawner.
    /// Each has a "Body" child that bobs or hops and a "Head" pivot that dips to graze. Birds also have
    /// "Wing L" and "Wing R" pivots that flap.
    /// </summary>
    public static class PlaceholderAnimals
    {
        static readonly Dictionary<Color, Material> materials = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => materials.Clear();

        public static GameObject Rabbit()
        {
            var fur = new Color(0.46f, 0.39f, 0.31f);
            var root = new GameObject("Rabbit");
            Transform body = Pivot("Body", root.transform, Vector3.zero);
            Part(PrimitiveType.Sphere, body, new Vector3(0f, 0.13f, 0f), new Vector3(0.22f, 0.2f, 0.32f), fur);
            Part(PrimitiveType.Sphere, body, new Vector3(0f, 0.15f, -0.17f), Vector3.one * 0.08f, new Color(0.92f, 0.9f, 0.86f));
            Transform head = Pivot("Head", body, new Vector3(0f, 0.2f, 0.1f));
            Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.04f, 0.06f), new Vector3(0.14f, 0.13f, 0.16f), fur);
            Part(PrimitiveType.Cube, head, new Vector3(-0.035f, 0.14f, 0.03f), new Vector3(0.03f, 0.13f, 0.05f), fur, -12f);
            Part(PrimitiveType.Cube, head, new Vector3(0.035f, 0.14f, 0.03f), new Vector3(0.03f, 0.13f, 0.05f), fur, -12f);
            return root;
        }

        public static GameObject Deer()
        {
            var coat = new Color(0.47f, 0.33f, 0.2f);
            var dark = new Color(0.2f, 0.15f, 0.11f);
            var root = new GameObject("Deer");
            Transform body = Pivot("Body", root.transform, Vector3.zero);
            Part(PrimitiveType.Sphere, body, new Vector3(0f, 1f, 0f), new Vector3(0.42f, 0.48f, 1.1f), coat);
            Part(PrimitiveType.Sphere, body, new Vector3(0f, 1.05f, -0.55f), Vector3.one * 0.12f, new Color(0.9f, 0.88f, 0.84f));
            foreach (float x in new[] { -0.13f, 0.13f })
            foreach (float z in new[] { -0.36f, 0.38f })
                Part(PrimitiveType.Cylinder, body, new Vector3(x, 0.42f, z), new Vector3(0.07f, 0.42f, 0.07f), dark);

            // The neck and head swing down together from the shoulders.
            Transform head = Pivot("Head", body, new Vector3(0f, 1.12f, 0.42f));
            Part(PrimitiveType.Cylinder, head, new Vector3(0f, 0.2f, 0.08f), new Vector3(0.13f, 0.24f, 0.13f), coat, 25f);
            Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.44f, 0.24f), new Vector3(0.16f, 0.18f, 0.34f), coat);
            Part(PrimitiveType.Cube, head, new Vector3(-0.09f, 0.55f, 0.14f), new Vector3(0.05f, 0.12f, 0.03f), coat, 0f, 30f);
            Part(PrimitiveType.Cube, head, new Vector3(0.09f, 0.55f, 0.14f), new Vector3(0.05f, 0.12f, 0.03f), coat, 0f, -30f);
            return root;
        }

        public static GameObject Bird()
        {
            var feathers = new Color(0.24f, 0.2f, 0.17f);
            var root = new GameObject("Bird");
            Transform body = Pivot("Body", root.transform, Vector3.zero);
            Part(PrimitiveType.Sphere, body, new Vector3(0f, 0.06f, 0f), new Vector3(0.09f, 0.08f, 0.15f), feathers);
            Transform head = Pivot("Head", body, new Vector3(0f, 0.09f, 0.05f));
            Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.02f, 0.02f), Vector3.one * 0.06f, feathers);
            Part(PrimitiveType.Cube, head, new Vector3(0f, 0.015f, 0.06f), new Vector3(0.012f, 0.012f, 0.03f), new Color(0.85f, 0.65f, 0.2f));
            foreach ((string name, float side) in new[] { ("Wing L", -1f), ("Wing R", 1f) })
            {
                Transform wing = Pivot(name, body, new Vector3(side * 0.035f, 0.08f, 0f));
                Part(PrimitiveType.Cube, wing, new Vector3(side * 0.08f, 0f, 0f), new Vector3(0.16f, 0.01f, 0.08f), feathers);
            }
            return root;
        }

        static Transform Pivot(string pivotName, Transform parent, Vector3 localPosition)
        {
            var pivot = new GameObject(pivotName).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = localPosition;
            return pivot;
        }

        static void Part(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Color colour,
            float tiltX = 0f, float tiltZ = 0f)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            // Animals shouldn't block the player or the interaction ray.
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.Euler(tiltX, 0f, tiltZ);
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = MaterialFor(colour);
        }

        static Material MaterialFor(Color colour)
        {
            if (materials.TryGetValue(colour, out Material material) && material != null)
                return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = "Placeholder Animal" };
            material.SetColor("_BaseColor", colour);
            material.SetColor("_Color", colour);
            material.SetFloat("_Smoothness", 0.1f);
            materials[colour] = material;
            return material;
        }
    }
}
