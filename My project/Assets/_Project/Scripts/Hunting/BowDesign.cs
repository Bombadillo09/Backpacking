using System.Collections.Generic;
using Backpacking.Camp;
using UnityEngine;

namespace Backpacking.Hunting
{
    /// <summary>
    /// The takedown recurve bow and its arrows, built in code. The bow's origin is the grip, with +Y up the top limb
    /// and +Z toward the target; the string runs behind it (−Z). An arrow's origin is its broadhead's tip, the shaft
    /// running back along −Z to the nock.
    /// </summary>
    public static class BowDesign
    {
        /// <summary>Grip to string at rest (brace height), metres.</summary>
        public const float BraceHeight = 0.2f;
        /// <summary>How far the string comes back at full draw, beyond brace height (shown a little short of a real draw).</summary>
        public const float DrawLength = 0.27f;
        public const float ArrowLength = 0.74f;
        /// <summary>Where the arrow sits on the riser, relative to the grip: the arrow shelf, on the left of the riser.</summary>
        public static readonly Vector3 ArrowRest = new(-0.016f, 0.03f, 0f);

        const float TipHeight = 0.72f;

        static readonly Dictionary<string, Material> materials = new();
        static Mesh riser, limbs, grip, shaft, head, fletching;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            materials.Clear();
            riser = limbs = grip = shaft = head = fletching = null;
        }

        /// <summary>The string's two ends, at the limb tips.</summary>
        public static Vector3 TopNock => new(0f, TipHeight, -0.095f);
        public static Vector3 BottomNock => new(0f, -TipHeight, -0.095f);

        /// <summary>A bow, its string a line through three points (tip, nocking point, tip) for <see cref="SetDraw"/> to move.</summary>
        public static GameObject Bow()
        {
            BuildMeshes();
            var root = new GameObject("Bow");
            Part(root.transform, "Riser", riser, Material("Riser", new Color(0.2f, 0.11f, 0.06f), 0.55f));
            Part(root.transform, "Limbs", limbs, Material("Limbs", new Color(0.33f, 0.22f, 0.12f), 0.65f));
            Part(root.transform, "Grip", grip, Material("Grip", new Color(0.12f, 0.1f, 0.08f), 0.15f));

            var line = new GameObject("String").AddComponent<LineRenderer>();
            line.transform.SetParent(root.transform, false);
            line.useWorldSpace = false;
            line.positionCount = 3;
            line.widthMultiplier = 0.003f;
            line.numCapVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.sharedMaterial = Unlit("String", new Color(0.82f, 0.8f, 0.74f));
            SetDraw(root, 0f);
            return root;
        }

        /// <summary>Pulls the string back: 0 at rest, 1 at full draw. Returns the nocking point (local to the bow).</summary>
        public static Vector3 SetDraw(GameObject bow, float draw)
        {
            Vector3 nock = NockPoint(draw);
            if (bow.transform.Find("String") is { } child && child.TryGetComponent(out LineRenderer line))
            {
                line.SetPosition(0, TopNock);
                line.SetPosition(1, nock);
                line.SetPosition(2, BottomNock);
            }
            return nock;
        }

        /// <summary>The string's nocking point, local to the bow, at a draw from 0 (rest) to 1 (full).</summary>
        public static Vector3 NockPoint(float draw) => new(ArrowRest.x * 0.5f, ArrowRest.y, -BraceHeight - DrawLength * Mathf.Clamp01(draw));

        /// <summary>An arrow, tip at the origin, pointing along +Z.</summary>
        public static GameObject Arrow()
        {
            BuildMeshes();
            var root = new GameObject("Arrow");
            Part(root.transform, "Shaft", shaft, Material("Shaft", new Color(0.08f, 0.08f, 0.09f), 0.6f));
            Part(root.transform, "Head", head, Material("Head", new Color(0.62f, 0.63f, 0.65f), 0.85f, metal: true));
            // Bright fletching, so a lost arrow can be found in the grass.
            Part(root.transform, "Fletching", fletching, Material("Fletching", new Color(0.95f, 0.45f, 0.08f), 0.3f));
            return root;
        }

        static void Part(Transform parent, string name, Mesh mesh, Material material)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void BuildMeshes()
        {
            if (riser != null)
                return;

            // The riser: a shaped block, thick through the grip and slimming toward the limb pockets, with the
            // sight window cut away on the left where the arrow passes.
            var b = new MeshBuilder();
            var rings = new List<Vector3[]>();
            for (int k = 0; k <= 12; k++)
            {
                float y = Mathf.Lerp(-0.2f, 0.2f, k / 12f);
                bool window = y > 0.0f && y < 0.14f;
                float depth = 0.028f + 0.018f * Mathf.Exp(-y * y / 0.004f);
                float width = window ? 0.012f : 0.022f;
                float shift = window ? 0.008f : 0f;
                float back = -0.01f + 0.015f * Mathf.Abs(y) / 0.2f;
                rings.Add(new[]
                {
                    new Vector3(shift - width, y, back + depth * 0.5f), new Vector3(shift + width, y, back + depth * 0.5f),
                    new Vector3(shift + width, y, back - depth * 0.5f), new Vector3(shift - width, y, back - depth * 0.5f),
                });
            }
            b.Loft(rings, closed: true, flip: true);
            b.Cap(rings[0], Vector3.zero);
            b.Cap(rings[^1], Vector3.zero, flip: true);
            riser = b.Build("Bow riser");

            // Limbs: flat and wide, sweeping back toward the archer, then curling forward again at the tips (the recurve).
            b = new MeshBuilder();
            foreach (float side in new[] { 1f, -1f })
            {
                var path = new List<Vector3>();
                Vector2[] profile =
                {
                    new(0.19f, 0f), new(0.3f, -0.035f), new(0.42f, -0.075f), new(0.54f, -0.115f), new(0.63f, -0.14f),
                    new(0.69f, -0.135f), new(TipHeight, -0.11f), new(TipHeight + 0.01f, -0.08f),
                };
                foreach (Vector2 p in profile)
                    path.Add(new Vector3(0f, p.x * side, p.y));
                float[] widths = { 0.04f, 0.038f, 0.034f, 0.028f, 0.022f, 0.017f, 0.013f, 0.01f };
                for (int k = 0; k < path.Count - 1; k++)
                    b.Strap(new[] { path[k], path[k + 1] }, _ => Vector3.forward, Mathf.Lerp(widths[k], widths[k + 1], 0.5f), 0.009f);
            }
            limbs = b.Build("Bow limbs");

            b = new MeshBuilder();
            b.Tube(new[] { new Vector3(0f, -0.07f, 0f), new Vector3(0f, 0.0f, -0.003f) }, 0.022f, 12);
            grip = b.Build("Bow grip");

            b = new MeshBuilder();
            b.Tube(new[] { new Vector3(0f, 0f, -0.05f), new Vector3(0f, 0f, -ArrowLength) }, 0.0045f, 6);
            // The nock: a slightly wider end that clips onto the string.
            b.Tube(new[] { new Vector3(0f, 0f, -ArrowLength + 0.012f), new Vector3(0f, 0f, -ArrowLength - 0.006f) }, 0.0055f, 6);
            shaft = b.Build("Arrow shaft");

            b = new MeshBuilder();
            GearDesign.Lathe(b, new[] { new Vector2(0.0001f, 0f), new Vector2(0.012f, 0.035f), new Vector2(0.005f, 0.045f), new Vector2(0.005f, 0.055f) }, 4);
            head = Turned(b.Build("Broadhead"));

            // Three vanes standing out from the shaft, 120° apart.
            b = new MeshBuilder();
            for (int vane = 0; vane < 3; vane++)
            {
                Vector3 outward = Quaternion.Euler(0f, 0f, vane * 120f) * Vector3.up;
                b.Box(outward * 0.0155f + new Vector3(0f, 0f, -ArrowLength + 0.075f), Vector3.Cross(outward, Vector3.forward), outward, Vector3.forward,
                    new Vector3(0.0005f, 0.011f, 0.045f));
            }
            fletching = b.Build("Fletching");
        }

        /// <summary>Lathe output runs up +Y; the broadhead points along +Z with its tip at the origin.</summary>
        static Mesh Turned(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vector3(vertices[i].x, vertices[i].z, -vertices[i].y);
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material Material(string name, Color colour, float smoothness, bool metal = false)
        {
            if (materials.TryGetValue(name, out Material material) && material != null)
                return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", colour);
            material.SetColor("_Color", colour);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metal ? 0.9f : 0f);
            materials[name] = material;
            return material;
        }

        static Material Unlit(string name, Color colour)
        {
            if (materials.TryGetValue(name, out Material material) && material != null)
                return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", colour);
            material.SetColor("_Color", colour);
            materials[name] = material;
            return material;
        }
    }
}
