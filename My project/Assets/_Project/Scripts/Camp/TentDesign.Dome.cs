using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// The pitched domes (2-person and 4-season), shaped by their poles: two poles arch corner to corner and cross
    /// at the top, pushing the fabric out in ridges, and the panels between them sag in a little. The inner tent
    /// comes down to a dark bathtub floor; the rainfly sits over the poles a hand's width off the ground, staked at
    /// its corners and hanging in scallops between them, and runs out at the front to a staked porch with the door.
    /// Points are found from (u, v) over the floor as a direction (the floor's edge it points at) and how far out
    /// towards that edge they are.
    /// </summary>
    public static partial class TentDesign
    {
        /// <summary>How a layer of fabric sits: the inner tent or the fly.</summary>
        readonly struct Layer
        {
            /// <summary>Bigger than the floor by this much across the ground, and this tall at the top.</summary>
            public readonly float Grow, Height;
            /// <summary>How far the panels between the poles sag in, how far the porch runs out, and how rippled it is.</summary>
            public readonly float Sag, Porch, Ripple;
            /// <summary>How high the hem hangs off the ground between the stakes (0 = right down to the ground).</summary>
            public readonly float Hem;

            public Layer(float grow, float height, float sag, float porch, float ripple, float hem)
            {
                Grow = grow;
                Height = height;
                Sag = sag;
                Porch = porch;
                Ripple = ripple;
                Hem = hem;
            }
        }

        static Layer InnerLayer(in Spec spec) => new(1f, spec.Height, 0.05f, 0f, 0.0025f, 0f);

        static Layer FlyLayer(in Spec spec) => new(1.07f, spec.Height + 0.07f, 0.035f, spec.Vestibule, 0.004f, 0.14f);

        /// <summary>A dome's height going out from the top (0) to the edge (1): rounded on top, steep walls.</summary>
        static float DomeProfile(float s) => Mathf.Pow(Mathf.Clamp01(1f - Mathf.Pow(Mathf.Clamp01(s), 2.4f)), 0.6f);

        /// <summary>How far out a height is on the dome profile (the inverse of <see cref="DomeProfile"/>).</summary>
        static float DomeReach(float fraction) => Mathf.Pow(Mathf.Clamp01(1f - Mathf.Pow(Mathf.Clamp01(fraction), 1f / 0.6f)), 1f / 2.4f);

        /// <summary>
        /// The floor's edge as a loop: <paramref name="t"/> from 0 to 4 runs round its sides (0 right, 1 front,
        /// 2 left, 3 back) with a corner at each whole number. <paramref name="c"/> is how far along that side
        /// (−1 to 1), and the point is on the square edge of (u, v).
        /// </summary>
        static Vector2 Edge(float t, out int side, out float c)
        {
            t = Mathf.Repeat(t, 4f);
            side = Mathf.Min(3, (int)t);
            c = (t - side) * 2f - 1f;
            return side switch
            {
                0 => new Vector2(1f, c),
                1 => new Vector2(-c, 1f),
                2 => new Vector2(-1f, -c),
                _ => new Vector2(c, -1f),
            };
        }

        /// <summary>The side and position along it that (u, v) points at, and how far out it is.</summary>
        static float EdgeOf(float u, float v, out int side, out float c)
        {
            float s = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
            if (s < 1e-5f)
            {
                side = 0;
                c = 0f;
                return 0f;
            }
            if (Mathf.Abs(u) >= Mathf.Abs(v))
            {
                side = u > 0f ? 0 : 2;
                c = u > 0f ? v / s : -v / s;
            }
            else
            {
                side = v > 0f ? 1 : 3;
                c = v > 0f ? -u / s : u / s;
            }
            return s;
        }

        /// <summary>How high a layer's hem hangs at a point along the floor's edge.</summary>
        static float HemHeight(in Layer layer, int side, float c)
        {
            if (layer.Hem <= 0f)
                return 0f;
            // At the front, the hem comes down to the porch stake in the middle as well as at the corners.
            if (side == 1 && layer.Porch > 0f)
                return layer.Hem * Mathf.Abs(Mathf.Sin(c * Mathf.PI));
            return layer.Hem * Mathf.Cos(c * Mathf.PI * 0.5f);
        }

        /// <summary>How far out a layer reaches towards a point on the edge before its hem.</summary>
        static float HemReach(in Layer layer, int side, float c)
        {
            float hem = HemHeight(layer, side, c);
            return hem <= 0f ? 1f : DomeReach(hem / layer.Height);
        }

        /// <summary>The layer's surface over the floor's edge point (side, c), <paramref name="s"/> of the way out from the top.</summary>
        static Vector3 DomePoint(in Spec spec, in Layer layer, int side, float c, float s)
        {
            Vector2 edge = side switch
            {
                0 => new Vector2(1f, c),
                1 => new Vector2(-c, 1f),
                2 => new Vector2(-1f, -c),
                _ => new Vector2(c, -1f),
            };
            // 1 midway between the poles, 0 along them (the corners).
            float between = 1f - c * c;
            Vector3 point = FloorPoint(spec, edge.x * s, edge.y * s, layer.Grow);
            float pull = 1f - layer.Sag * between * Mathf.Sin(Mathf.PI * s);
            point.x *= pull;
            point.z *= pull;
            if (side == 1 && layer.Porch > 0f)
                point.z += layer.Porch * spec.HalfLength * s * s * between;
            point.y = layer.Height * DomeProfile(s) + 0.012f;
            // Shallow creases running down the panels towards the stakes.
            if (layer.Ripple > 0f)
            {
                float crease = layer.Ripple * between * Mathf.Sin(Mathf.PI * s) * Mathf.Sin(c * 13f + s * 9f + side * 1.7f);
                var outward = new Vector3(point.x, 0f, point.z);
                if (outward.sqrMagnitude > 1e-6f)
                    point += outward.normalized * crease * 0.6f;
                point.y += crease;
            }
            return point;
        }

        /// <summary>The layer's surface over a floor point (u, v), as for the poles and guy lines.</summary>
        static Vector3 DomePointAt(in Spec spec, in Layer layer, float u, float v)
        {
            float s = EdgeOf(u, v, out int side, out float c);
            return DomePoint(spec, layer, side, c, s);
        }

        /// <summary>Lifts a point off the layer, away from the dome's middle and up.</summary>
        static Vector3 Proud(Vector3 point, float lift)
        {
            var outward = new Vector3(point.x, Mathf.Max(0.3f, point.y), point.z);
            return point + outward.normalized * lift;
        }

        /// <summary>
        /// A whole layer from the top down to its hem, or a band of it (from <paramref name="from"/> of the way
        /// out), drawn from both sides, with UVs in half-metres for the fabric's weave.
        /// </summary>
        static Mesh DomeSurface(Spec spec, Layer layer, float from = 0f, float grow = 1f)
        {
            const int perSide = 32, rings = 34;
            const int around = perSide * 4;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            float distance = 0f;
            Vector3 last = Vector3.zero;
            for (int i = 0; i <= around; i++)
            {
                // Closing the loop back at the first corner, as the end of the back side.
                Edge(Mathf.Min(i, around - 0.001f) / perSide, out int side, out float c);
                if (i == around)
                    c = 1f;
                float reach = HemReach(layer, side, c);
                Vector3 rim = DomePoint(spec, layer, side, c, reach);
                if (i > 0)
                    distance += Vector3.Distance(rim, last);
                last = rim;
                for (int j = 0; j <= rings; j++)
                {
                    float s = Mathf.Lerp(from, reach, j / (float)rings);
                    Vector3 point = DomePoint(spec, layer, side, c, s);
                    if (grow != 1f)
                        point = Proud(point, grow - 1f);
                    vertices.Add(point);
                    uvs.Add(new Vector2(distance * 2f, s * (spec.Height + spec.HalfLength) * 2f));
                }
            }
            var triangles = new List<int>();
            for (int i = 0; i < around; i++)
            for (int j = 0; j < rings; j++)
            {
                int a = i * (rings + 1) + j, b = a + 1, d = a + rings + 1, e = d + 1;
                triangles.AddRange(new[] { a, b, d, d, b, e });
            }
            return DoubleSided(vertices, uvs, triangles);
        }

        /// <summary>
        /// The fly's door, unzipped and rolled aside: the opening, wide at the bottom and closing to the zip's top
        /// under the peak. <paramref name="shrink"/> &lt; 1 gives the edges just inside it (for the zips).
        /// </summary>
        static float DoorHalfWidth(float s) => 0.3f * Mathf.Sqrt(Mathf.InverseLerp(DoorTop, 0.98f, s));

        const float DoorTop = 0.42f;

        static Mesh DomeDoor(Spec spec, Layer layer, float lift)
        {
            const int across = 16, along = 24;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int j = 0; j <= along; j++)
            {
                float s = Mathf.Lerp(DoorTop, 1f, j / (float)along);
                float half = DoorHalfWidth(s);
                for (int i = 0; i <= across; i++)
                {
                    float c = Mathf.Lerp(-half, half, i / (float)across);
                    vertices.Add(Proud(DomePoint(spec, layer, 1, c, Mathf.Min(s, HemReach(layer, 1, c))), lift));
                    uvs.Add(new Vector2(i / (float)across, j / (float)along));
                }
            }
            var triangles = new List<int>();
            for (int j = 0; j < along; j++)
            for (int i = 0; i < across; i++)
            {
                int a = j * (across + 1) + i, b = a + 1, c = a + across + 1, d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            return DoubleSided(vertices, uvs, triangles);
        }

        /// <summary>The zips up both sides of the door and the door itself rolled up on its left and tied back.</summary>
        static Mesh DomeDoorEdges(Spec spec, Layer layer, bool rolled)
        {
            var parts = new List<Mesh>();
            foreach (float sign in rolled ? new[] { -1f } : new[] { -1f, 1f })
            {
                var path = new List<Vector3>();
                // The rolled door hangs down the opening's side from its tie, clear of the ground.
                float top = rolled ? 0.58f : DoorTop, bottom = rolled ? 0.93f : 1f;
                for (int k = 0; k <= 20; k++)
                {
                    float s = Mathf.Lerp(top, bottom, k / 20f);
                    float c = sign * (DoorHalfWidth(s) + (rolled ? 0.05f : 0f));
                    path.Add(Proud(DomePoint(spec, layer, 1, c, Mathf.Min(s, HemReach(layer, 1, c))), rolled ? 0.02f : 0.006f));
                }
                parts.Add(Tube(path, rolled ? 0.021f : 0.0045f, rolled ? 8 : 5));
            }
            return Combined(parts);
        }

        /// <summary>The binding round the fly's hem.</summary>
        static Mesh DomeHem(Spec spec, Layer layer)
        {
            var path = new List<Vector3>();
            const int around = 4 * 32;
            for (int i = 0; i <= around; i++)
            {
                // Closing the loop back at the first corner, as the end of the back side.
                Edge(Mathf.Min(i, around - 0.001f) / 32f, out int side, out float c);
                if (i == around)
                    c = 1f;
                path.Add(DomePoint(spec, layer, side, c, HemReach(layer, side, c)));
            }
            return Tube(path, 0.0065f, 5);
        }

        /// <summary>Seam tape along the fly over each pole, from the top down to the corners.</summary>
        static Mesh DomeSeams(Spec spec, Layer layer)
        {
            var parts = new List<Mesh>();
            for (int corner = 0; corner < 4; corner++)
            {
                var path = new List<Vector3>();
                for (int k = 0; k <= 26; k++)
                    path.Add(Proud(DomePoint(spec, layer, corner, -1f, k / 26f), 0.002f));
                parts.Add(Tube(path, 0.006f, 5));
            }
            // A small hooded vent high on the back.
            var vent = new List<Vector3>();
            for (int k = 0; k <= 10; k++)
            {
                float c = Mathf.Lerp(-0.22f, 0.22f, k / 10f);
                vent.Add(Proud(DomePoint(spec, layer, 3, c, 0.42f), 0.018f));
            }
            parts.Add(Tube(vent, 0.02f, 6));
            return Combined(parts);
        }

        /// <summary>The third pole of the 4-season tent, run across the front on the outside of the fly.</summary>
        static Mesh DomeBrowPole(Spec spec, Layer fly)
        {
            // Over the fabric's creases, not following them.
            var layer = new Layer(fly.Grow, fly.Height, fly.Sag, fly.Porch, 0f, fly.Hem);
            var path = new List<Vector3>();
            // Down to the ground at both ends, below the hem.
            for (int k = 0; k <= 24; k++)
            {
                float u = Mathf.Lerp(-1f, 1f, k / 24f);
                path.Add(Proud(DomePointAt(spec, layer, u, 0.45f), 0.012f));
            }
            return Tube(path, 0.0065f, 6);
        }

        /// <summary>Where the guy lines tie on to the fly, as (side, c).</summary>
        static IEnumerable<(int side, float c)> GuyPoints(TentModel model)
        {
            if (model == TentModel.FourSeason)
            {
                yield return (0, -0.5f);
                yield return (0, 0.5f);
                yield return (2, -0.5f);
                yield return (2, 0.5f);
            }
            else
            {
                yield return (0, 0f);
                yield return (2, 0f);
            }
            yield return (3, 0f);
        }

        static void GuyLine(in Spec spec, in Layer layer, int side, float c, out Vector3 tie, out Vector3 stake)
        {
            tie = Proud(DomePoint(spec, layer, side, c, 0.62f), 0.01f);
            Vector3 hem = DomePoint(spec, layer, side, c, HemReach(layer, side, c));
            var outward = new Vector3(hem.x, 0f, hem.z).normalized;
            stake = new Vector3(hem.x, 0f, hem.z) + outward * 0.6f;
        }

        static Mesh DomeGuyLines(TentModel model, Spec spec, Layer layer)
        {
            var parts = new List<Mesh>();
            foreach ((int side, float c) in GuyPoints(model))
            {
                GuyLine(spec, layer, side, c, out Vector3 tie, out Vector3 stake);
                parts.Add(Tube(new[] { tie, stake + Vector3.up * 0.04f }, 0.0022f, 4));
            }
            // Short webbing from each fly corner down to its stake.
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 at = DomePoint(spec, layer, corner, -1f, 0.995f);
                Vector3 outward = new Vector3(at.x, 0f, at.z).normalized;
                parts.Add(Tube(new[] { at, at + outward * 0.06f + Vector3.down * 0.01f }, 0.006f, 4));
            }
            return Combined(parts);
        }

        /// <summary>The stakes once a dome is pitched: the fly's corners, the porch, and every guy line.</summary>
        static Mesh DomeStakes(TentModel model, Spec spec)
        {
            Layer fly = FlyLayer(spec);
            var points = new List<Vector3>();
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 at = DomePoint(spec, fly, corner, -1f, 1f);
                points.Add(new Vector3(at.x, 0f, at.z) + new Vector3(at.x, 0f, at.z).normalized * 0.06f);
            }
            Vector3 porch = DomePoint(spec, fly, 1, 0f, 1f);
            points.Add(new Vector3(porch.x, 0f, porch.z + 0.03f));
            foreach ((int side, float c) in GuyPoints(model))
            {
                GuyLine(spec, fly, side, c, out _, out Vector3 stake);
                points.Add(stake);
            }
            var parts = new List<Mesh>();
            foreach (Vector3 point in points)
                parts.Add(Tube(new[] { point + new Vector3(0f, -0.05f, 0f), point + new Vector3(0.02f, 0.05f, 0f) }, 0.004f, 4));
            return Combined(parts);
        }

        /// <summary>The poles of a pitched dome, just under the inner tent's ridges, grommet to grommet.</summary>
        static Mesh DomePoles(Spec spec)
        {
            Layer inner = InnerLayer(spec);
            var parts = new List<Mesh>();
            foreach ((int from, int to) in new[] { (0, 2), (1, 3) })
            {
                var path = new List<Vector3>();
                for (int k = 26; k >= 0; k--)
                    path.Add(Proud(DomePoint(spec, inner, from, -1f, k / 26f), 0.008f));
                for (int k = 1; k <= 26; k++)
                    path.Add(Proud(DomePoint(spec, inner, to, -1f, k / 26f), 0.008f));
                parts.Add(Tube(path, 0.0055f));
            }
            return Combined(parts);
        }

        static Mesh Combined(List<Mesh> parts)
        {
            var combine = new CombineInstance[parts.Count];
            for (int k = 0; k < parts.Count; k++)
                combine[k] = new CombineInstance { mesh = parts[k], transform = Matrix4x4.identity };
            var mesh = new Mesh();
            mesh.CombineMeshes(combine, true, true);
            foreach (Mesh part in parts)
                Discard(part);
            return mesh;
        }

        /// <summary>Adds a pitched dome's parts.</summary>
        static void BuildDome(System.Action<string, Mesh, Material, Color?> add, TentModel model, Spec spec, TentMaterials materials)
        {
            Layer inner = InnerLayer(spec), fly = FlyLayer(spec);
            Color binding = Color.Lerp(spec.Fly, Color.black, 0.55f);
            add("Stakes", Cached(model, "dome stakes", () => DomeStakes(model, spec)), materials.stake, null);
            add("Inner body", Cached(model, "dome inner", () => DomeSurface(spec, inner)), materials.inner, spec.Inner);
            // The bathtub floor's sides, coming a hand's width up the inner tent.
            float tub = DomeReach(0.1f / inner.Height);
            add("Bathtub", Cached(model, "dome tub", () => DomeSurface(spec, inner, tub, 1.003f)), materials.floor, null);
            add("Poles", Cached(model, "dome poles", () => DomePoles(spec)), materials.pole, null);
            add("Rainfly", Cached(model, "dome fly", () => DomeSurface(spec, fly)), materials.fly, spec.Fly);
            add("Seams", Cached(model, "dome seams", () => DomeSeams(spec, fly)), materials.fly, Color.Lerp(spec.Fly, Color.black, 0.2f));
            add("Hem", Cached(model, "dome hem", () => DomeHem(spec, fly)), materials.fly, binding);
            Color shade = Color.Lerp(spec.Inner, Color.black, 0.6f);
            add("Door", Cached(model, "dome door", () => DomeDoor(spec, fly, 0.004f)), materials.inner, shade);
            // The inner tent's door is open too: from inside, the dim porch beyond it.
            add("Inner door", Cached(model, "dome inner door", () => DomeDoor(spec, inner, -0.004f)), materials.inner, Color.Lerp(shade, Color.black, 0.3f));
            add("Door zips", Cached(model, "dome zips", () => DomeDoorEdges(spec, fly, false)), materials.fly, binding);
            add("Door (rolled)", Cached(model, "dome roll", () => DomeDoorEdges(spec, fly, true)), materials.fly, Color.Lerp(spec.Fly, Color.black, 0.12f));
            add("Guy lines", Cached(model, "dome guys", () => DomeGuyLines(model, spec, fly)), materials.stake, new Color(0.86f, 0.85f, 0.78f));
            if (model == TentModel.FourSeason)
                add("Brow pole", Cached(model, "dome brow", () => DomeBrowPole(spec, fly)), materials.pole, null);
        }

        // ---------- Fabric ----------

        static Texture2D fabric;

        /// <summary>
        /// A tileable ripstop weave, near white so the tint shows through: a faint grid of heavier threads over
        /// soft mottling. UVs are in half-metres, so it repeats every half metre.
        /// </summary>
        internal static Texture2D Fabric
        {
            get
            {
                if (fabric != null)
                    return fabric;
                const int size = 256;
                fabric = new Texture2D(size, size, TextureFormat.RGBA32, true)
                {
                    name = "Tent ripstop",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 8,
                    hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color32[size * size];
                // Mottling from a few tileable sine waves, so it wraps without a seam.
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = x / (float)size * Mathf.PI * 2f, fy = y / (float)size * Mathf.PI * 2f;
                    float mottle = 0.5f * Mathf.Sin(fx * 2f + Mathf.Sin(fy * 3f) * 1.3f) + 0.3f * Mathf.Sin(fy * 5f + fx * 3f) + 0.2f * Mathf.Sin(fx * 7f - fy * 4f);
                    float weave = ((x + y) & 1) == 0 ? 0.012f : -0.012f;
                    bool rip = x % 16 == 0 || y % 16 == 0;
                    float value = 0.94f + mottle * 0.025f + weave - (rip ? 0.06f : 0f);
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
                    pixels[y * size + x] = new Color32(b, b, b, 255);
                }
                fabric.SetPixels32(pixels);
                fabric.Apply(true, true);
                return fabric;
            }
        }
    }
}
