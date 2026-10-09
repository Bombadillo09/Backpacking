using System.Collections.Generic;
using Backpacking.Interaction;
using Backpacking.UI;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>How far a camp chair is set up.</summary>
    public enum ChairStage
    {
        /// <summary>Out of the pack: its stuff sack, folded poles and rolled seat on the ground.</summary>
        Packed,
        /// <summary>The shock-corded poles snapped into the hubs: a frame of legs and uprights.</summary>
        Frame,
        /// <summary>The seat fabric hooked over the pole tips: ready to sit in.</summary>
        Ready,
    }

    /// <summary>
    /// A lightweight camp chair, after the low hub-framed backpacking kind: set up like the tent, poles first and
    /// then the fabric. Sitting in it rests your feet faster than sitting on the ground, and you can still take
    /// your boots off. Bought at trading posts; taken out of the pack like the rest of the camp gear.
    /// </summary>
    public class CampChair : MonoBehaviour, IInteractable
    {
        [SerializeField] Material frameMaterial;
        [SerializeField] Material fabricMaterial;
        [SerializeField] float frameMinutes = 1.5f;
        [SerializeField] float fabricMinutes = 1f;

        static readonly Color Fabric = new(0.2f, 0.24f, 0.28f);
        static readonly Dictionary<string, Mesh> meshes = new();

        Transform visuals;

        public ChairStage Stage { get; private set; }
        public bool Occupied { get; private set; }

        /// <summary>Where you sit (the seat's middle) and which way you face, in world space.</summary>
        public Vector3 SeatPoint => transform.position;
        public Quaternion Facing => transform.rotation;

        public string DisplayName => Stage == ChairStage.Ready ? "Camp chair" : Stage == ChairStage.Frame ? "Camp chair (frame up)" : "Camp chair (packed)";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => meshes.Clear();

        public void Setup(ChairStage stage)
        {
            Stage = stage;
            if (visuals != null)
                Destroy(visuals.gameObject);
            visuals = new GameObject("Visuals").transform;
            visuals.SetParent(transform, false);
            Bounds bounds = Build(visuals, stage, frameMaterial, fabricMaterial);

            var box = GetComponent<BoxCollider>();
            if (box == null)
                box = gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, new Vector3(0.2f, 0.1f, 0.2f));
            box.enabled = !Occupied;
        }

        /// <summary>Someone's sitting in it: its collider stands aside so it doesn't shove them out.</summary>
        public void SetOccupied(bool occupied)
        {
            Occupied = occupied;
            var box = GetComponent<BoxCollider>();
            if (box != null)
                box.enabled = !occupied;
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            PlayerActivity activity = interactor.Activity;
            switch (Stage)
            {
                case ChairStage.Packed:
                    options.Add(new InteractionOption($"Snap the poles together into the frame ({frameMinutes:0.#} min)", () =>
                        activity.Begin("Snapping the chair's poles into their hubs", frameMinutes, () =>
                        {
                            Setup(ChairStage.Frame);
                            Notifications.Post("Frame up. Next, stretch the seat fabric over it.", 3f);
                        })));
                    options.Add(new InteractionOption("Put it back in your pack", () => PackAway(interactor), CampOwner.PackProblem(gameObject) ?? PackProblem()));
                    break;
                case ChairStage.Frame:
                    options.Add(new InteractionOption($"Hook the seat fabric over the pole tips ({fabricMinutes:0.#} min)", () =>
                        activity.Begin("Stretching the seat over the frame", fabricMinutes, () => Setup(ChairStage.Ready))));
                    options.Add(new InteractionOption($"Fold the frame up ({frameMinutes:0.#} min)", () =>
                        activity.Begin("Folding the chair's frame", frameMinutes, () => Setup(ChairStage.Packed))));
                    break;
                default:
                    options.Add(new InteractionOption("Sit in the chair", () =>
                    {
                        if (RestMode.Current != null)
                            RestMode.Current.SitInChair(this);
                    }));
                    options.Add(new InteractionOption($"Take the seat off ({fabricMinutes:0.#} min)", () =>
                        activity.Begin("Unhooking the seat", fabricMinutes, () => Setup(ChairStage.Frame))));
                    break;
            }
        }

        static string PackProblem() =>
            PackHandling.Current == null || PackHandling.Current.CanReachPack ? null : "Bring it to your pack first (or put the pack on)";

        void PackAway(Interactor interactor)
        {
            interactor.Backpack.ChairInPack = true;
            Notifications.Post("The chair's back in your pack.", 2.5f);
            Destroy(gameObject);
        }

        // ---------- Shape ----------

        // The chair faces +Z. Seat about 0.3 m up, sagging between the front and back pole tips; the back leans
        // up to 0.75 m. Two hubs under the seat send out the legs and the uprights.
        static readonly Vector3 FrontHub = new(0f, 0.15f, 0.07f), BackHub = new(0f, 0.15f, -0.1f);

        static Vector3 Mirror(Vector3 v, float side) => new(v.x * side, v.y, v.z);

        /// <summary>Builds the chair's visuals at a stage under <paramref name="parent"/>; returns their bounds.</summary>
        public static Bounds Build(Transform parent, ChairStage stage, Material frame, Material fabric)
        {
            var bounds = new Bounds(new Vector3(0f, 0.1f, 0f), Vector3.zero);
            var tint = new MaterialPropertyBlock();

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

            if (stage == ChairStage.Packed)
            {
                Add("Sack", Cached("sack", () => Cylinder(new Vector3(-0.12f, 0.06f, 0f), new Vector3(0.18f, 0.06f, 0f), 0.06f)), fabric, Fabric);
                Add("Folded poles", Cached("bundle", PoleBundle), frame);
                Add("Rolled seat", Cached("roll", () => Cylinder(new Vector3(-0.15f, 0.045f, 0.16f), new Vector3(0.2f, 0.045f, 0.16f), 0.045f)), fabric,
                    Color.Lerp(Fabric, Color.black, 0.2f));
                return bounds;
            }
            Add("Frame", Cached("frame", FrameMesh), frame);
            if (stage == ChairStage.Ready)
                Add("Seat", Cached("seat", SeatMesh), fabric, Fabric);
            return bounds;
        }

        static Mesh Cached(string key, System.Func<Mesh> make)
        {
            if (!meshes.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                mesh = make();
                mesh.name = "Camp chair " + key;
                meshes[key] = mesh;
            }
            return mesh;
        }

        static Mesh Cylinder(Vector3 from, Vector3 to, float radius) => TentDesign.Tube(new[] { from, to }, radius, 12);

        static Mesh Combine(List<Mesh> parts)
        {
            var combine = new CombineInstance[parts.Count];
            for (int i = 0; i < parts.Count; i++)
                combine[i] = new CombineInstance { mesh = parts[i], transform = Matrix4x4.identity };
            var mesh = new Mesh();
            mesh.CombineMeshes(combine, true, true);
            foreach (Mesh part in parts)
                TentDesign.DiscardMesh(part);
            return mesh;
        }

        /// <summary>A gently bowed pole from a to b (bowing outwards and up).</summary>
        static Mesh Pole(Vector3 a, Vector3 b, float bow = 0.03f)
        {
            var path = new Vector3[9];
            Vector3 outward = new Vector3((a.x + b.x) * 0.5f, 0f, 0f).normalized;
            for (int k = 0; k < path.Length; k++)
            {
                float t = k / (float)(path.Length - 1);
                path[k] = Vector3.Lerp(a, b, t) + (outward + Vector3.up * 0.5f) * (bow * Mathf.Sin(t * Mathf.PI));
            }
            return TentDesign.Tube(path, 0.0075f, 6);
        }

        static Mesh FrameMesh()
        {
            var parts = new List<Mesh>();
            foreach (float side in new[] { -1f, 1f })
            {
                // Legs: from each hub out to a foot, with a little rubber cap.
                Vector3 frontFoot = Mirror(new Vector3(0.27f, 0.01f, 0.26f), side), backFoot = Mirror(new Vector3(0.26f, 0.01f, -0.26f), side);
                parts.Add(Pole(FrontHub, frontFoot));
                parts.Add(Pole(BackHub, backFoot));
                parts.Add(Cylinder(frontFoot + Vector3.down * 0.008f, frontFoot + Vector3.up * 0.02f, 0.013f));
                parts.Add(Cylinder(backFoot + Vector3.down * 0.008f, backFoot + Vector3.up * 0.02f, 0.013f));
                // Uprights: the front hub to the seat's front corners, the back hub up to the top of the backrest.
                parts.Add(Pole(FrontHub, Mirror(new Vector3(0.23f, 0.33f, 0.23f), side), 0.02f));
                parts.Add(Pole(BackHub, Mirror(new Vector3(0.21f, 0.76f, -0.3f), side), 0.04f));
            }
            // The two hubs and the strut between them.
            parts.Add(Cylinder(FrontHub + Vector3.left * 0.025f, FrontHub + Vector3.right * 0.025f, 0.02f));
            parts.Add(Cylinder(BackHub + Vector3.left * 0.025f, BackHub + Vector3.right * 0.025f, 0.02f));
            parts.Add(TentDesign.Tube(new[] { FrontHub, BackHub }, 0.0075f, 6));
            return Combine(parts);
        }

        static Mesh PoleBundle()
        {
            var parts = new List<Mesh>();
            for (int k = 0; k < 8; k++)
            {
                var start = new Vector3(-0.2f + k % 4 * 0.012f, 0.012f + k / 4 * 0.012f, -0.14f);
                parts.Add(TentDesign.Tube(new[] { start, start + new Vector3(0.38f, 0f, 0.01f) }, 0.0075f, 6));
            }
            return Combine(parts);
        }

        /// <summary>The sling: from the front edge, sagging into the seat, curving up the backrest to its top.</summary>
        static Mesh SeatMesh()
        {
            // Profile down the middle (front to top of the back), and half-width at each point.
            Vector3[] profile =
            {
                new(0f, 0.34f, 0.25f), new(0f, 0.3f, 0.14f), new(0f, 0.27f, 0.02f), new(0f, 0.28f, -0.1f),
                new(0f, 0.36f, -0.19f), new(0f, 0.5f, -0.24f), new(0f, 0.64f, -0.28f), new(0f, 0.78f, -0.31f),
            };
            float[] halfWidth = { 0.23f, 0.235f, 0.23f, 0.22f, 0.215f, 0.215f, 0.215f, 0.215f };
            const int across = 10, along = 28;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int j = 0; j <= along; j++)
            {
                float t = j / (float)along * (profile.Length - 1);
                int i0 = Mathf.Min((int)t, profile.Length - 2);
                float f = t - i0;
                // Smooth between profile points (Catmull-Rom).
                Vector3 p0 = profile[Mathf.Max(i0 - 1, 0)], p1 = profile[i0], p2 = profile[i0 + 1], p3 = profile[Mathf.Min(i0 + 2, profile.Length - 1)];
                Vector3 centre = 0.5f * (2f * p1 + (-p0 + p2) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f * f + (-p0 + 3f * p1 - 3f * p2 + p3) * f * f * f);
                float width = Mathf.Lerp(halfWidth[i0], halfWidth[i0 + 1], f);
                for (int i = 0; i <= across; i++)
                {
                    float u = i / (float)across * 2f - 1f;
                    // The fabric dips a little in the middle, between the poles at its edges.
                    vertices.Add(centre + new Vector3(u * width, -0.025f * (1f - u * u), 0f));
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
            return TentDesign.DoubleSidedMesh(vertices, uvs, triangles);
        }
    }
}
