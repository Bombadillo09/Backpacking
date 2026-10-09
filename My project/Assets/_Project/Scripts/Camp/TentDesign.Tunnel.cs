using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// The 1-person tent, a two-hoop tunnel (after the Snugpak Ionosphere): a tall hoop near the front and a low
    /// one near the foot, a taut roof between them, a short rounded end at the foot, and in front of the main hoop
    /// the fly narrowing in flat panels to a single stake: a wedge-shaped porch with the door in its side.
    /// Built along z (−: foot, +: front) from cross-sections that run from a rounded tunnel to a triangle.
    /// </summary>
    public static partial class TentDesign
    {
        // Along the tent (metres): foot tip, foot hoop, main hoop, front of the inner tent, porch stake.
        const float FootTip = -1.2f, FootHoop = -0.82f, MainHoop = 0.45f, InnerFront = 1.18f, PorchTip = 1.75f;

        /// <summary>One cross-section of the tunnel: half-width, height, and how rounded (2.4) or pointed (1) it is.</summary>
        readonly struct Section
        {
            public readonly float HalfWidth, Height, Roundness;

            public Section(float halfWidth, float height, float roundness)
            {
                HalfWidth = halfWidth;
                Height = height;
                Roundness = roundness;
            }

            /// <summary>The point at a (−1 one side on the ground, 0 the top, 1 the other side on the ground).</summary>
            public Vector3 At(float a, float z) =>
                new(HalfWidth * a, Height * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Pow(Mathf.Abs(a), Roundness)), 1f / Roundness), z);
        }

        /// <summary>The fly's or inner tent's cross-section at z. The inner is smaller and stops at its own door.</summary>
        static Section TunnelSection(float z, bool fly)
        {
            float mainHeight = fly ? 0.98f : 0.9f, footHeight = fly ? 0.62f : 0.55f;
            float mainWidth = fly ? 0.5f : 0.45f, footWidth = fly ? 0.44f : 0.4f;
            float tip = fly ? PorchTip : InnerFront, end = fly ? FootTip : FootTip + 0.03f;
            const float round = 2.4f;

            if (z <= FootHoop)
            {
                // A short rounded end, down to the stakes at the foot.
                float t = Mathf.InverseLerp(end, FootHoop, z);
                float rise = Mathf.Sin(t * Mathf.PI * 0.5f);
                return new Section(footWidth * (0.72f + 0.28f * rise), footHeight * rise, round);
            }
            if (z <= MainHoop)
            {
                // The taut roof between the hoops, sagging a little in the middle.
                float t = Mathf.InverseLerp(FootHoop, MainHoop, z);
                return new Section(Mathf.Lerp(footWidth, mainWidth, t), Mathf.Lerp(footHeight, mainHeight, t) - 0.03f * Mathf.Sin(t * Mathf.PI), round);
            }
            // Forward of the main hoop: flat panels closing in to the porch stake (the fly), or down to the inner's door.
            float f = Mathf.InverseLerp(MainHoop, tip, z);
            float narrowing = fly ? 0.9f : 0.25f;
            // The porch's ridge runs straight down to its stake; its sides fold in to flat panels.
            return new Section(mainWidth * (1f - narrowing * Mathf.Pow(f, 0.8f)), mainHeight * (1f - f), Mathf.Lerp(round, fly ? 1.15f : 1.7f, Mathf.Sqrt(f)));
        }

        /// <summary>
        /// The fly or inner tent as a double-sided surface. A patch (the door) can be cut out of it by limiting
        /// a and z, lifted a little off the fabric it sits on.
        /// </summary>
        static Mesh TunnelSurface(bool fly, float aFrom = -1f, float aTo = 1f, float zFrom = float.NaN, float zTo = float.NaN, float lift = 0f)
        {
            float start = float.IsNaN(zFrom) ? (fly ? FootTip : FootTip + 0.03f) : zFrom;
            float stop = float.IsNaN(zTo) ? (fly ? PorchTip : InnerFront) : zTo;
            const int along = 48, across = 24;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int j = 0; j <= along; j++)
            {
                float z = Mathf.Lerp(start, stop, j / (float)along);
                Section section = TunnelSection(z, fly);
                for (int i = 0; i <= across; i++)
                {
                    float a = Mathf.Lerp(aFrom, aTo, i / (float)across);
                    Vector3 point = section.At(a, z);
                    point.y += 0.012f;
                    if (lift > 0f)
                        point += new Vector3(Mathf.Sign(a) * lift, lift, 0f);
                    vertices.Add(point);
                    // Half-metres round and along, for the fabric's weave.
                    uvs.Add(new Vector2(a * (section.HalfWidth + section.Height) * 2f, z * 2f));
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

        /// <summary>The two hoops, just under the fly; <paramref name="radius"/> is bigger for the fly's pole sleeves.</summary>
        static Mesh TunnelHoops(float radius, float drop)
        {
            var combine = new List<CombineInstance>();
            foreach (float z in new[] { FootHoop, MainHoop })
            {
                Section section = TunnelSection(z, fly: true);
                var path = new Vector3[25];
                for (int k = 0; k < path.Length; k++)
                {
                    Vector3 point = section.At(Mathf.Lerp(-0.995f, 0.995f, k / (float)(path.Length - 1)), z);
                    point.y = Mathf.Max(0f, point.y + 0.012f - drop);
                    path[k] = point;
                }
                combine.Add(new CombineInstance { mesh = Tube(path, radius), transform = Matrix4x4.identity });
            }
            var mesh = new Mesh();
            mesh.CombineMeshes(combine.ToArray(), true, true);
            foreach (CombineInstance part in combine)
                Discard(part.mesh);
            return mesh;
        }

        /// <summary>Stakes at the foot corners, both ends of each hoop and the porch tip.</summary>
        static Mesh TunnelStakes(bool pitched)
        {
            var points = new List<Vector3> { new(-0.45f, 0f, FootTip - 0.02f), new(0.45f, 0f, FootTip - 0.02f), new(-0.46f, 0f, 1.18f), new(0.46f, 0f, 1.18f) };
            if (pitched)
            {
                foreach (float z in new[] { FootHoop, MainHoop })
                {
                    float width = TunnelSection(z, fly: true).HalfWidth + 0.03f;
                    points.Add(new Vector3(-width, 0f, z));
                    points.Add(new Vector3(width, 0f, z));
                }
                points.Add(new Vector3(0f, 0f, PorchTip + 0.03f));
                foreach ((Vector3 _, Vector3 stake) in TunnelGuys())
                    points.Add(stake);
            }
            var combine = new List<CombineInstance>();
            foreach (Vector3 point in points)
                combine.Add(new CombineInstance
                {
                    mesh = Tube(new[] { point + new Vector3(0f, -0.05f, 0f), point + new Vector3(0.02f, 0.05f, 0f) }, 0.004f, 4),
                    transform = Matrix4x4.identity,
                });
            var mesh = new Mesh();
            mesh.CombineMeshes(combine.ToArray(), true, true);
            foreach (CombineInstance part in combine)
                Discard(part.mesh);
            return mesh;
        }

        /// <summary>The binding down both sides of the fly's hem, along the ground from the foot to the porch stake.</summary>
        static Mesh TunnelHem()
        {
            var parts = new List<Mesh>();
            foreach (float side in new[] { -1f, 1f })
            {
                var path = new List<Vector3>();
                for (int k = 0; k <= 40; k++)
                {
                    float z = Mathf.Lerp(FootTip, PorchTip, k / 40f);
                    Vector3 point = TunnelSection(z, fly: true).At(side * 0.995f, z);
                    point.y += 0.012f;
                    path.Add(point);
                }
                parts.Add(Tube(path, 0.0065f, 5));
            }
            return Combined(parts);
        }

        /// <summary>Where each guy line ties on high on a hoop's side, and its stake out to the side.</summary>
        static IEnumerable<(Vector3 tie, Vector3 stake)> TunnelGuys()
        {
            foreach (float z in new[] { FootHoop, MainHoop })
            foreach (float side in new[] { -1f, 1f })
            {
                Section section = TunnelSection(z, fly: true);
                Vector3 tie = section.At(side * 0.62f, z) + new Vector3(side * 0.015f, 0.025f, 0f);
                yield return (tie, new Vector3(side * (section.HalfWidth + 0.65f), 0f, z + (z > 0f ? 0.25f : -0.25f)));
            }
        }

        static Mesh TunnelGuyLines()
        {
            var parts = new List<Mesh>();
            foreach ((Vector3 tie, Vector3 stake) in TunnelGuys())
                parts.Add(Tube(new[] { tie, stake + Vector3.up * 0.04f }, 0.0022f, 4));
            return Combined(parts);
        }

        /// <summary>The bathtub floor under the inner tent, following its outline.</summary>
        static Mesh TunnelFloor()
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            const int along = 24;
            for (int j = 0; j <= along; j++)
            {
                float z = Mathf.Lerp(FootTip + 0.04f, InnerFront, j / (float)along);
                float half = TunnelSection(Mathf.Max(z, FootHoop), fly: false).HalfWidth * 0.97f;
                vertices.Add(new Vector3(-half, 0.006f, z));
                vertices.Add(new Vector3(half, 0.006f, z));
                uvs.Add(new Vector2(-half * 2f, z * 2f));
                uvs.Add(new Vector2(half * 2f, z * 2f));
                if (j > 0)
                {
                    int a = (j - 1) * 2;
                    triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
            }
            return DoubleSided(vertices, uvs, triangles);
        }

        /// <summary>Adds the tunnel tent's parts for a stage; the floor and the flat body come from the shared code.</summary>
        static void BuildTunnel(System.Action<string, Mesh, Material, Color?> add, TentStage stage, in Spec spec, TentMaterials materials)
        {
            const TentModel model = TentModel.OnePerson;
            add("Stakes", Cached(model, "tunnel stakes " + (stage == TentStage.Pitched), () => TunnelStakes(stage == TentStage.Pitched)), materials.stake, null);
            if (stage == TentStage.LaidOut)
                return;
            if (stage == TentStage.Poled)
            {
                // Freestanding hoops arched over the flat inner, ready for the fly to go over.
                add("Hoops", Cached(model, "tunnel hoops", () => TunnelHoops(0.0055f, 0.02f)), materials.pole, null);
                return;
            }
            Color sleeve = Color.Lerp(spec.Fly, Color.black, 0.75f);
            add("Inner tent", Cached(model, "tunnel inner", () => TunnelSurface(false)), materials.inner, spec.Inner);
            add("Rainfly", Cached(model, "tunnel fly", () => TunnelSurface(true)), materials.fly, spec.Fly);
            // The poles run in dark sleeves on the outside of the fly.
            add("Pole sleeves", Cached(model, "tunnel sleeves", () => TunnelHoops(0.017f, -0.004f)), materials.fly, sleeve);
            add("Hem", Cached(model, "tunnel hem", TunnelHem), materials.fly, sleeve);
            add("Guy lines", Cached(model, "tunnel guys", TunnelGuyLines), materials.stake, new Color(0.86f, 0.85f, 0.78f));
            // The door: the porch's side unzipped and rolled back, a dark opening to the black inner.
            add("Door", Cached(model, "tunnel door", () => TunnelSurface(true, 0.12f, 0.97f, MainHoop + 0.05f, PorchTip - 0.3f, 0.006f)), materials.fly,
                new Color(0.05f, 0.05f, 0.05f));
        }
    }
}
