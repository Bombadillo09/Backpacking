using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>The tents you can own. You start with the 1-person tent; trading posts sell the others.</summary>
    public enum TentModel
    {
        OnePerson,
        TwoPerson,
        FourSeason,
    }

    /// <summary>How far a tent on the ground has got: from laid out flat to fully pitched.</summary>
    public enum TentStage
    {
        /// <summary>Floor and body spread flat, poles still bundled.</summary>
        LaidOut,
        /// <summary>Fibreglass poles assembled and arched into the corner grommets over the flat body.</summary>
        Poled,
        /// <summary>Body clipped up to the poles, rainfly over the top, staked and guyed out.</summary>
        Pitched,
    }

    /// <summary>The materials a tent is made of; colours per model are applied on top.</summary>
    [System.Serializable]
    public struct TentMaterials
    {
        public Material inner, fly, floor, pole, stake;
    }

    /// <summary>
    /// Shape and geometry of each tent model, built from how real tents stand: the fibreglass poles arch from
    /// grommet to grommet, the inner body hangs from them, and the rainfly sits over the poles with a vestibule
    /// in front. Meshes are generated (and cached) here for every stage, for the tent in the world and for the
    /// see-through preview when choosing a spot.
    /// </summary>
    public static class TentDesign
    {
        public readonly struct Spec
        {
            public readonly string Name;
            /// <summary>Half the floor's width (x) and length (z), and the peak height, in metres.</summary>
            public readonly float HalfWidth, HalfLength, Height;
            /// <summary>Height and width at the foot end compared with the head, for tapered tents (1 = none).</summary>
            public readonly float FootHeight, FootWidth;
            /// <summary>How much further the rainfly reaches out over the door, as a fraction of the half length.</summary>
            public readonly float Vestibule;
            public readonly Color Fly, Inner;
            public readonly bool GuyLines;

            public Spec(string name, float halfWidth, float halfLength, float height, float footHeight, float footWidth, float vestibule,
                Color fly, Color inner, bool guyLines)
            {
                Name = name;
                HalfWidth = halfWidth;
                HalfLength = halfLength;
                Height = height;
                FootHeight = footHeight;
                FootWidth = footWidth;
                Vestibule = vestibule;
                Fly = fly;
                Inner = inner;
                GuyLines = guyLines;
            }
        }

        public static Spec Of(TentModel model) => model switch
        {
            // A narrow trekking tent: one hoop pole near the head and a short strut at the foot.
            TentModel.OnePerson => new Spec("1-person trekking tent", 0.46f, 1.1f, 0.92f, 0.42f, 0.7f, 0.5f,
                new Color(0.36f, 0.45f, 0.26f), new Color(0.85f, 0.83f, 0.68f), false),
            // A freestanding dome: two poles crossing over the top from corner to corner.
            TentModel.TwoPerson => new Spec("2-person dome tent", 0.66f, 1.06f, 1.08f, 1f, 1f, 0.5f,
                new Color(0.86f, 0.46f, 0.16f), new Color(0.9f, 0.86f, 0.55f), false),
            // A low, stiff mountain tent: two crossing poles plus a third across the front, guyed out.
            _ => new Spec("4-season mountain tent", 0.72f, 1.08f, 0.98f, 1f, 1f, 0.6f,
                new Color(0.72f, 0.14f, 0.12f), new Color(0.92f, 0.82f, 0.3f), true),
        };

        // ---------- Shape ----------

        /// <summary>
        /// Height of the canopy over a point of the floor, with u across (−1 left to 1 right) and v along (−1 foot
        /// to 1 head/door). Zero at the edges, where the fabric meets the ground.
        /// </summary>
        static float CanopyHeight(TentModel model, in Spec spec, float u, float v)
        {
            if (model == TentModel.OnePerson)
            {
                // Across, the half-circle of the hoop; along, the fabric's taut run between the poles and stakes.
                float hoopSection = Mathf.Sqrt(Mathf.Clamp01(1f - u * u));
                return spec.Height * Mathf.Pow(hoopSection, 0.85f) * HoopTentRidge(spec, v);
            }
            // A dome's fabric slopes down between the poles to the corners.
            return spec.Height * Slope(u, 0.8f) * Slope(v, 0.8f);
        }

        /// <summary>Where the 1-person tent's hoop pole and foot strut cross the floor (v, from −1 foot to 1 head).</summary>
        const float HoopAt = 0.22f, StrutAt = -0.74f;

        /// <summary>
        /// Height along the 1-person tent's ridge, as a fraction of its peak: up from the stakes at the foot to the
        /// low strut, a long taut rise to the hoop near the head, then a steep run down over the vestibule to the
        /// stakes at the head end. Fabric between supports sags a little.
        /// </summary>
        static float HoopTentRidge(in Spec spec, float v)
        {
            float foot = spec.FootHeight;
            if (v <= StrutAt)
            {
                float t = Mathf.InverseLerp(-1f, StrutAt, v);
                return foot * Mathf.Sin(t * Mathf.PI * 0.5f);
            }
            // The rise to the hoop and the fall to the head stakes, each a taut line that sags a little, meet in a
            // rounded crown where the fly drapes over the pole rather than a sharp peak.
            float up = (v - StrutAt) / (HoopAt - StrutAt);
            float rise = Mathf.Lerp(foot, 1f, up) - 0.06f * Mathf.Sin(Mathf.Clamp01(up) * Mathf.PI);
            float down = (v - HoopAt) / (1f - HoopAt);
            float fall = 1f - down - 0.08f * Mathf.Sin(Mathf.Clamp01(down) * Mathf.PI);
            const float crown = 0.14f;
            float soft = -crown * Mathf.Log(Mathf.Exp(-rise / crown) + Mathf.Exp(-fall / crown));
            // Keep the hoop at full height despite the rounding.
            return Mathf.Max(0f, soft + crown * Mathf.Log(2f) * Mathf.Clamp01(1f - Mathf.Abs(v - HoopAt) * 1.6f));
        }

        /// <summary>1 in the middle, curving down to 0 at ±1; lower <paramref name="sharpness"/> keeps it fuller.</summary>
        static float Slope(float t, float sharpness) => Mathf.Pow(Mathf.Max(0f, Mathf.Cos(Mathf.Clamp(t, -1f, 1f) * Mathf.PI * 0.5f)), sharpness);

        /// <summary>1 in the middle falling to 0 at ±1, with steeper walls for higher <paramref name="power"/>.</summary>
        static float Profile(float t, float power) => Mathf.Pow(Mathf.Clamp01(1f - Mathf.Pow(Mathf.Abs(t), power)), 0.55f);

        /// <summary>Where a floor point (u, v) is in the tent's space, before any height is added.</summary>
        static Vector3 FloorPoint(in Spec spec, float u, float v, float grow = 1f)
        {
            float width = spec.HalfWidth * Mathf.Lerp(spec.FootWidth, 1f, Mathf.InverseLerp(-1f, 0.2f, v));
            return new Vector3(u * width * grow, 0f, v * spec.HalfLength * grow);
        }

        // ---------- Building ----------

        static readonly Dictionary<(TentModel, string), Mesh> meshes = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => meshes.Clear();

        /// <summary>
        /// Builds the visuals of a tent at a stage under <paramref name="parent"/>, colliders not included.
        /// Returns the bounds of the parts in the parent's space.
        /// </summary>
        public static Bounds Build(Transform parent, TentModel model, TentStage stage, TentMaterials materials)
        {
            Spec spec = Of(model);
            var tint = new MaterialPropertyBlock();
            var bounds = new Bounds(Vector3.zero, Vector3.zero);

            void Add(string name, Mesh mesh, Material material, Color? colour = null)
            {
                var part = new GameObject(name);
                part.transform.SetParent(parent, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                if (colour.HasValue)
                {
                    tint.SetColor("_BaseColor", colour.Value);
                    renderer.SetPropertyBlock(tint);
                }
                bounds.Encapsulate(mesh.bounds);
            }

            // The floor, a touch bigger than the body, with a stake at each corner.
            Add("Floor", Cached(model, "floor", () => Floor(spec)), materials.floor);
            Add("Stakes", Cached(model, "stakes", () => Stakes(model, spec, stage == TentStage.Pitched)), materials.stake);

            switch (stage)
            {
                case TentStage.LaidOut:
                    Add("Body (flat)", Cached(model, "flat", () => Canopy(model, spec, 0.03f, 1f, 0f, true)), materials.inner, spec.Inner);
                    Add("Pole bundle", Cached(model, "bundle", () => PoleBundle(spec)), materials.pole);
                    break;
                case TentStage.Poled:
                    Add("Body (flat)", Cached(model, "flat", () => Canopy(model, spec, 0.03f, 1f, 0f, true)), materials.inner, spec.Inner);
                    Add("Poles", Cached(model, "poles", () => Poles(model, spec)), materials.pole);
                    break;
                default:
                    Add("Inner body", Cached(model, "inner", () => Canopy(model, spec, 1f, 0.97f, 0f, false)), materials.inner, spec.Inner);
                    Add("Poles", Cached(model, "poles", () => Poles(model, spec)), materials.pole);
                    Add("Rainfly", Cached(model, "fly", () => Canopy(model, spec, 1.05f, 1.07f, spec.Vestibule, false)), materials.fly, spec.Fly);
                    Add("Door", Cached(model, "door", () => Canopy(model, spec, 1.05f, 1.07f, spec.Vestibule, false, 0.32f, 0.7f, 0.012f)), materials.fly,
                        Color.Lerp(spec.Fly, Color.black, 0.45f));
                    if (spec.GuyLines)
                        Add("Guy lines", Cached(model, "guys", () => GuyLines(model, spec)), materials.stake);
                    break;
            }
            return bounds;
        }

        static void Discard(Object thing)
        {
            if (Application.isPlaying)
                Object.Destroy(thing);
            else
                Object.DestroyImmediate(thing);
        }

        static Mesh Cached(TentModel model, string part, System.Func<Mesh> make)
        {
            if (!meshes.TryGetValue((model, part), out Mesh mesh) || mesh == null)
            {
                mesh = make();
                mesh.name = $"{model} {part}";
                meshes[(model, part)] = mesh;
            }
            return mesh;
        }

        /// <summary>
        /// The fabric as a grid over the floor, drawn from both sides. <paramref name="heightScale"/> flattens it
        /// (a body laid out on the ground), <paramref name="grow"/> makes it bigger (the fly over the inner), and
        /// <paramref name="vestibule"/> stretches the door end out into a porch.
        /// </summary>
        static Mesh Canopy(TentModel model, in Spec spec, float heightScale, float grow, float vestibule, bool rumpled,
            float patchHalfWidth = 1f, float patchFrom = -1f, float lift = 0f)
        {
            // Normally the whole canopy; a patch (the door) covers u within ±patchHalfWidth and v from patchFrom to 1.
            const int steps = 28;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int j = 0; j <= steps; j++)
            for (int i = 0; i <= steps; i++)
            {
                float u = (i / (float)steps * 2f - 1f) * patchHalfWidth;
                float v = Mathf.Lerp(patchFrom, 1f, j / (float)steps);
                Vector3 point = FloorPoint(spec, u, v, grow);
                // The vestibule: the fly's front panel runs further out and lower than the body.
                if (vestibule > 0f && v > 0f)
                    point.z += v * v * vestibule * spec.HalfLength;
                float height = CanopyHeight(model, spec, u, v) * heightScale;
                if (rumpled)
                    height = 0.02f + 0.015f * Mathf.Sin(u * 7f + v * 3f) * Mathf.Sin(v * 9f);
                else if (grow > 1f)
                    height += 0.04f * Profile(u, 2f) * Profile(v, 2f);
                point.y = height + 0.012f;
                // Stand the patch just proud of the fabric it sits on.
                if (lift > 0f)
                    point += new Vector3(0f, lift, lift * Mathf.Sign(v));
                vertices.Add(point);
                uvs.Add(new Vector2(i / (float)steps, j / (float)steps));
            }

            var triangles = new List<int>();
            for (int j = 0; j < steps; j++)
            for (int i = 0; i < steps; i++)
            {
                int a = j * (steps + 1) + i, b = a + 1, c = a + steps + 1, d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            return DoubleSided(vertices, uvs, triangles);
        }

        /// <summary>Adds a reversed copy of every triangle, so fabric shows from inside and out.</summary>
        static Mesh DoubleSided(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            var mesh = new Mesh();
            int count = vertices.Count;
            var both = new List<Vector3>(vertices);
            both.AddRange(vertices);
            var bothUvs = new List<Vector2>(uvs);
            bothUvs.AddRange(uvs);
            var bothTriangles = new List<int>(triangles);
            for (int t = 0; t < triangles.Count; t += 3)
                bothTriangles.AddRange(new[] { triangles[t] + count, triangles[t + 2] + count, triangles[t + 1] + count });
            mesh.SetVertices(both);
            mesh.SetUVs(0, bothUvs);
            mesh.SetTriangles(bothTriangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The pole paths: each runs over the canopy from grommet to grommet.</summary>
        static List<Vector3[]> PolePaths(TentModel model, in Spec spec)
        {
            var paths = new List<Vector3[]>();
            Spec s = spec;
            Vector3[] Along(System.Func<float, Vector2> uv)
            {
                const int points = 24;
                var path = new Vector3[points + 1];
                for (int k = 0; k <= points; k++)
                {
                    Vector2 at = uv(k / (float)points * 2f - 1f);
                    Vector3 point = FloorPoint(s, at.x, at.y);
                    point.y = CanopyHeight(model, s, at.x, at.y) + 0.03f;
                    path[k] = point;
                }
                return path;
            }

            if (model == TentModel.OnePerson)
            {
                // One hoop across near the head, one short strut across the foot.
                paths.Add(Along(t => new Vector2(t * 0.999f, HoopAt)));
                paths.Add(Along(t => new Vector2(t * 0.999f, StrutAt)));
            }
            else
            {
                // Two poles crossing over the top, corner to corner.
                paths.Add(Along(t => new Vector2(t, t) * 0.999f));
                paths.Add(Along(t => new Vector2(t, -t) * 0.999f));
                if (model == TentModel.FourSeason)
                    paths.Add(Along(t => new Vector2(t * 0.999f, 0.45f)));
            }
            return paths;
        }

        static Mesh Poles(TentModel model, in Spec spec)
        {
            var combine = new List<CombineInstance>();
            foreach (Vector3[] path in PolePaths(model, spec))
                combine.Add(new CombineInstance { mesh = Tube(path, 0.0055f), transform = Matrix4x4.identity });
            var mesh = new Mesh();
            mesh.CombineMeshes(combine.ToArray(), true, true);
            foreach (CombineInstance part in combine)
                Discard(part.mesh);
            return mesh;
        }

        /// <summary>The shock-corded poles folded into short sections, lying in a bundle beside the body.</summary>
        static Mesh PoleBundle(in Spec spec)
        {
            var combine = new List<CombineInstance>();
            var start = new Vector3(spec.HalfWidth + 0.25f, 0.02f, -0.2f);
            for (int k = 0; k < 6; k++)
            {
                Vector3 offset = new(k % 3 * 0.014f, k / 3 * 0.013f, 0f);
                combine.Add(new CombineInstance
                {
                    mesh = Tube(new[] { start + offset, start + offset + new Vector3(0.02f, 0f, 0.42f) }, 0.006f),
                    transform = Matrix4x4.identity,
                });
            }
            var mesh = new Mesh();
            mesh.CombineMeshes(combine.ToArray(), true, true);
            foreach (CombineInstance part in combine)
                Discard(part.mesh);
            return mesh;
        }

        /// <summary>A round tube along a path, for poles and guy lines.</summary>
        static Mesh Tube(IReadOnlyList<Vector3> path, float radius, int sides = 6)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int k = 0; k < path.Count; k++)
            {
                Vector3 forward = (path[Mathf.Min(k + 1, path.Count - 1)] - path[Mathf.Max(k - 1, 0)]).normalized;
                Vector3 side = Vector3.Cross(forward, Mathf.Abs(forward.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 up = Vector3.Cross(side, forward);
                for (int s = 0; s < sides; s++)
                {
                    float angle = s / (float)sides * Mathf.PI * 2f;
                    vertices.Add(path[k] + (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius);
                }
                if (k == 0)
                    continue;
                int ring = k * sides, previous = ring - sides;
                for (int s = 0; s < sides; s++)
                {
                    int next = (s + 1) % sides;
                    triangles.AddRange(new[] { previous + s, ring + s, previous + next, previous + next, ring + s, ring + next });
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh Floor(in Spec spec)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            const int steps = 6;
            for (int j = 0; j <= steps; j++)
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps * 2f - 1f, v = j / (float)steps * 2f - 1f;
                Vector3 point = FloorPoint(spec, u, v, 1.02f);
                point.y = 0.006f;
                vertices.Add(point);
                uvs.Add(new Vector2(i / (float)steps, j / (float)steps));
            }
            for (int j = 0; j < steps; j++)
            for (int i = 0; i < steps; i++)
            {
                int a = j * (steps + 1) + i, b = a + 1, c = a + steps + 1, d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            return DoubleSided(vertices, uvs, triangles);
        }

        /// <summary>Stakes in the corners; once pitched, more out front holding the vestibule.</summary>
        static Mesh Stakes(TentModel model, in Spec spec, bool pitched)
        {
            var points = new List<Vector3>();
            foreach (float u in new[] { -1f, 1f })
            foreach (float v in new[] { -1f, 1f })
                points.Add(FloorPoint(spec, u, v, 1.04f));
            if (pitched)
            {
                Vector3 porch = FloorPoint(spec, 0f, 1f, 1.07f);
                porch.z += spec.Vestibule * spec.HalfLength;
                points.Add(porch);
                if (spec.GuyLines)
                    points.AddRange(GuyStakes(model, spec));
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

        static IEnumerable<Vector3> GuyStakes(TentModel model, Spec spec)
        {
            foreach (float u in new[] { -1f, 1f })
            foreach (float v in new[] { -0.45f, 0.45f })
            {
                Vector3 point = FloorPoint(spec, u, v, 1.07f);
                point.x += Mathf.Sign(u) * 0.55f;
                yield return point;
            }
        }

        /// <summary>Taut lines from the fly's sides out to stakes, for a tent that has to stand up to storms.</summary>
        static Mesh GuyLines(TentModel model, in Spec spec)
        {
            var combine = new List<CombineInstance>();
            foreach (Vector3 stake in GuyStakes(model, spec))
            {
                float u = Mathf.Sign(stake.x) * 0.55f, v = Mathf.Sign(stake.z) * 0.45f;
                Vector3 tie = FloorPoint(spec, u, v, 1.07f);
                tie.y = CanopyHeight(model, spec, u, v) + 0.05f;
                combine.Add(new CombineInstance { mesh = Tube(new[] { tie, stake + Vector3.up * 0.04f }, 0.0018f, 3), transform = Matrix4x4.identity });
            }
            var mesh = new Mesh();
            mesh.CombineMeshes(combine.ToArray(), true, true);
            foreach (CombineInstance part in combine)
                Discard(part.mesh);
            return mesh;
        }
    }
}
