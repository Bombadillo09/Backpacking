using Backpacking.Camp;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>The camp lantern: a green enamel base, a glowing glass globe in a wire guard, a vented cap and a bail handle.</summary>
    public static partial class PrototypeSceneBuilder
    {
        static GameObject BuildLantern()
        {
            var root = new GameObject();
            var lantern = root.AddComponent<CampLantern>();
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.17f, 0f);
            collider.size = new Vector3(0.16f, 0.34f, 0.16f);

            Material enamel = GetOrCreateMaterial("LanternEnamel", new Color(0.12f, 0.32f, 0.2f), 0.55f);
            Material metal = GetOrCreateMaterial("DarkMetal", new Color(0.18f, 0.18f, 0.19f), 0.5f);
            Material globe = GetOrCreateEmissiveMaterial("LanternGlobe", new Color(1f, 0.78f, 0.45f));

            // The base and fuel font.
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0f, 0.02f, 0f), Quaternion.identity, new Vector3(0.14f, 0.02f, 0.14f), enamel);
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0f, 0.06f, 0f), Quaternion.identity, new Vector3(0.115f, 0.03f, 0.115f), enamel);
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0f, 0.093f, 0f), Quaternion.identity, new Vector3(0.09f, 0.005f, 0.09f), metal);
            // The globe, and its wire guard.
            AddVisual(PrimitiveType.Sphere, root, new Vector3(0f, 0.16f, 0f), Quaternion.identity, new Vector3(0.095f, 0.13f, 0.095f), globe);
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI / 2f + Mathf.PI / 4f;
                AddVisual(PrimitiveType.Cylinder, root, new Vector3(Mathf.Cos(angle) * 0.052f, 0.165f, Mathf.Sin(angle) * 0.052f), Quaternion.identity,
                    new Vector3(0.006f, 0.075f, 0.006f), metal);
            }
            // The cap, with its vent, and the bail handle arching over it.
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0f, 0.245f, 0f), Quaternion.identity, new Vector3(0.12f, 0.012f, 0.12f), enamel);
            AddVisual(PrimitiveType.Sphere, root, new Vector3(0f, 0.262f, 0f), Quaternion.identity, new Vector3(0.09f, 0.04f, 0.09f), enamel);
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0f, 0.29f, 0f), Quaternion.identity, new Vector3(0.025f, 0.012f, 0.025f), metal);
            const int segments = 7;
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.PI * i / segments, a1 = Mathf.PI * (i + 1) / segments;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.065f, 0.25f + Mathf.Sin(a0) * 0.09f, 0f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.065f, 0.25f + Mathf.Sin(a1) * 0.09f, 0f);
                AddVisual(PrimitiveType.Cylinder, root, (p0 + p1) / 2f, Quaternion.FromToRotation(Vector3.up, p1 - p0),
                    new Vector3(0.005f, Vector3.Distance(p0, p1) / 2f, 0.005f), metal);
            }

            var lightObject = new GameObject("Lantern Light");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.17f, 0f);
            var lamp = lightObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.8f, 0.55f);
            lamp.range = 14f;
            lamp.intensity = 7f;
            lamp.shadows = LightShadows.None;

            SetField(lantern, "lamp", lamp);
            // The globe is the fourth part built (base, font, collar, globe).
            SetField(lantern, "globe", root.transform.GetChild(3).GetComponent<Renderer>());
            return root;
        }
    }
}
