using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// Shapes of the smaller camp gear, built in code: the canister stove and its titanium pot, the tent's stuff sack
    /// with its pole bag, and the wire snare on its stake. Round parts are lathed from profiles. The editor's gear
    /// setup saves these as mesh assets for the prefabs.
    /// </summary>
    public static class GearDesign
    {
        /// <summary>A shape spun round the Y axis from a profile of (radius, height) points, bottom to top.</summary>
        public static void Lathe(MeshBuilder b, IReadOnlyList<Vector2> profile, int segments = 28, Vector3 offset = default)
        {
            var rings = new List<Vector3[]>();
            foreach (Vector2 p in profile)
            {
                var ring = new Vector3[segments];
                for (int i = 0; i < segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    ring[i] = offset + new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x);
                }
                rings.Add(ring);
            }
            b.Loft(rings, closed: true);
            // Close the ends where the profile doesn't come to the axis.
            if (profile[0].x > 0.0005f)
                b.Cap(rings[0], Vector3.zero, flip: true);
            if (profile[^1].x > 0.0005f)
                b.Cap(rings[^1], Vector3.zero);
        }

        // ---------- Stove ----------

        /// <summary>A 230 g gas canister: crimped base rim, straight wall, domed shoulder up to the valve collar.</summary>
        public static Mesh Canister()
        {
            var b = new MeshBuilder();
            Lathe(b, new[]
            {
                new Vector2(0.044f, 0f), new Vector2(0.05f, 0.003f), new Vector2(0.052f, 0.008f), new Vector2(0.05f, 0.012f),
                new Vector2(0.054f, 0.016f), new Vector2(0.055f, 0.06f), new Vector2(0.054f, 0.068f), new Vector2(0.046f, 0.08f),
                new Vector2(0.03f, 0.088f), new Vector2(0.018f, 0.09f), new Vector2(0.018f, 0.095f), new Vector2(0.012f, 0.097f), new Vector2(0f, 0.098f),
            });
            return b.Build("Gas canister");
        }

        /// <summary>The stove that screws on: valve body, burner head, three pot-support arms and the control wire.</summary>
        public static Mesh Burner()
        {
            var b = new MeshBuilder();
            const float top = 0.098f;
            // Brass-coloured valve body and the burner head.
            Lathe(b, new[]
            {
                new Vector2(0.012f, top), new Vector2(0.013f, top + 0.012f), new Vector2(0.008f, top + 0.016f), new Vector2(0.008f, top + 0.034f),
                new Vector2(0.024f, top + 0.038f), new Vector2(0.028f, top + 0.046f), new Vector2(0.022f, top + 0.05f), new Vector2(0f, top + 0.051f),
            });
            // Pot supports: three serrated arms folded out level with the burner.
            for (int k = 0; k < 3; k++)
            {
                float a = k / 3f * Mathf.PI * 2f + 0.3f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                b.Strap(new[] { dir * 0.012f + Vector3.up * (top + 0.035f), dir * 0.03f + Vector3.up * (top + 0.05f), dir * 0.058f + Vector3.up * (top + 0.054f) },
                    _ => Vector3.Cross(dir, Vector3.up), 0.012f, 0.0015f);
            }
            // The flame-control wire, a loop out to one side.
            var wire = new List<Vector3>();
            for (int i = 0; i <= 12; i++)
            {
                float t = i / 12f * Mathf.PI;
                wire.Add(new Vector3(0.012f + Mathf.Sin(t) * 0.03f, top + 0.02f, Mathf.Cos(t) * 0.012f));
            }
            b.Tube(wire, 0.0015f, 5);
            return b.Build("Stove burner");
        }

        /// <summary>A 750 ml titanium pot with a rolled rim, two folding handles laid down its sides, and its lid.</summary>
        public static Mesh Pot()
        {
            var b = new MeshBuilder();
            Lathe(b, new[]
            {
                new Vector2(0f, 0f), new Vector2(0.05f, 0f), new Vector2(0.054f, 0.004f), new Vector2(0.055f, 0.1f),
                new Vector2(0.057f, 0.102f), new Vector2(0.055f, 0.104f),
            });
            // Lid with a raised centre and a silicone knob.
            Lathe(b, new[]
            {
                new Vector2(0.058f, 0.103f), new Vector2(0.056f, 0.108f), new Vector2(0.03f, 0.112f), new Vector2(0.012f, 0.112f),
                new Vector2(0.012f, 0.122f), new Vector2(0.008f, 0.124f), new Vector2(0f, 0.124f),
            });
            // Folding handles, wire loops lying against the sides.
            foreach (float side in new[] { -1f, 1f })
            {
                var handle = new List<Vector3>();
                for (int i = 0; i <= 10; i++)
                {
                    float t = i / 10f;
                    float y = Mathf.Lerp(0.088f, 0.03f, Mathf.Sin(t * Mathf.PI) * 0.8f);
                    float z = Mathf.Lerp(-0.025f, 0.025f, t);
                    handle.Add(new Vector3(side * (0.058f + 0.004f * Mathf.Sin(t * Mathf.PI)), y, z));
                }
                b.Tube(handle, 0.002f, 6);
            }
            return b.Build("Titanium pot");
        }

        // ---------- Tent bag ----------

        /// <summary>
        /// The tent's stuff sack lying on its side along X: rounded ends, gathered at one end by its drawcord, with two
        /// compression straps along it. Built to the given length and radius.
        /// </summary>
        public static Mesh StuffSack(float length, float radius, out Mesh straps)
        {
            var profile = new List<Vector2>();
            // Along the sack, as a lathe about X: a flat end, the body bulging a little, gathered at the cinched end.
            for (int i = 0; i <= 18; i++)
            {
                float t = i / 18f;
                float r = radius * (0.9f + 0.1f * Mathf.Sin(t * Mathf.PI));
                if (t < 0.06f)
                    r *= Mathf.Sqrt(t / 0.06f) * 0.6f + 0.4f;
                if (t > 0.9f)
                    r *= Mathf.Lerp(1f, 0.25f, (t - 0.9f) / 0.1f);
                profile.Add(new Vector2(r, Mathf.Lerp(-length / 2f, length / 2f, t)));
            }
            var turned = new MeshBuilder { Tiling = 5f };
            Lathe(turned, profile);
            Mesh body = turned.Build("Stuff sack (upright)");
            // Lay it along X.
            Vector3[] vertices = body.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vector3(vertices[i].y, vertices[i].x + radius, vertices[i].z);
            body.vertices = vertices;
            body.RecalculateNormals();
            body.RecalculateBounds();
            body.name = "Tent stuff sack";

            var s = new MeshBuilder { Tiling = 8f };
            foreach (float z in new[] { -radius * 0.55f, radius * 0.55f })
            {
                var path = new List<Vector3>();
                for (int i = 0; i <= 12; i++)
                {
                    float x = Mathf.Lerp(-length * 0.47f, length * 0.42f, i / 12f);
                    float y = radius + Mathf.Sqrt(Mathf.Max(0f, radius * radius - z * z)) + 0.003f;
                    path.Add(new Vector3(x, y, z));
                }
                s.Strap(path, _ => new Vector3(0f, 1f, z / radius).normalized, 0.018f, 0.002f);
            }
            // The drawcord and its toggle at the cinched end.
            s.Tube(new[] { new Vector3(length / 2f, radius, 0f), new Vector3(length / 2f + 0.04f, radius * 0.8f, 0.01f), new Vector3(length / 2f + 0.07f, radius * 0.5f, 0.02f) }, 0.002f, 5);
            s.Box(new Vector3(length / 2f + 0.04f, radius * 0.8f, 0.01f), Vector3.right, Vector3.up, Vector3.forward, new Vector3(0.01f, 0.007f, 0.007f));
            straps = s.Build("Stuff sack straps");
            return body;
        }

        // ---------- Snare ----------

        /// <summary>A sharpened wooden stake driven into the ground, a little of its point showing under the soil.</summary>
        public static Mesh Stake()
        {
            var b = new MeshBuilder { Tiling = 6f };
            Lathe(b, new[]
            {
                new Vector2(0f, -0.06f), new Vector2(0.012f, -0.02f), new Vector2(0.016f, 0.02f), new Vector2(0.017f, 0.26f),
                new Vector2(0.015f, 0.28f), new Vector2(0f, 0.285f),
            }, 9);
            return b.Build("Snare stake");
        }

        /// <summary>The brass-wire noose, held open over the run, its tail twisted round the stake.</summary>
        public static Mesh Noose()
        {
            var b = new MeshBuilder();
            var loop = new List<Vector3>();
            for (int i = 0; i <= 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                loop.Add(new Vector3(Mathf.Sin(a) * 0.075f, 0.12f + Mathf.Cos(a) * 0.075f, 0.12f));
            }
            b.Tube(loop, 0.0012f, 5);
            // The tail from the noose's eye back to the stake, wrapped round it a few times.
            var tail = new List<Vector3> { new(0f, 0.195f, 0.12f), new(0f, 0.2f, 0.06f), new(0f, 0.2f, 0.018f) };
            for (int i = 0; i <= 18; i++)
            {
                float a = i / 18f * Mathf.PI * 6f;
                tail.Add(new Vector3(Mathf.Sin(a) * 0.0185f, 0.2f + i * 0.0012f, Mathf.Cos(a) * 0.0185f));
            }
            b.Tube(tail, 0.0012f, 5);
            return b.Build("Snare noose");
        }
    }
}
