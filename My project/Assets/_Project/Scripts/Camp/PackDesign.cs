using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>The materials a backpack is made of.</summary>
    [System.Serializable]
    public struct PackMaterials
    {
        [Tooltip("Main pack fabric (ripstop weave), tinted to the hiker's pack colour.")]
        public Material fabric;
        [Tooltip("Stretch-mesh pockets.")]
        public Material mesh;
        [Tooltip("Straps and webbing.")]
        public Material webbing;
        [Tooltip("Tough patches: the base and lash points.")]
        public Material patch;
        [Tooltip("Buckles, cord locks and the like.")]
        public Material plastic;
        [Tooltip("Gear strapped on: tent bag (tinted to the fly), sleeping pad, chair sack.")]
        public Material gearFabric;
    }

    /// <summary>
    /// A 50-litre trekking pack, built in code: a main body lofted from rounded rings (flat against the back,
    /// bulging at the front), a padded top lid overhanging the front, a stretch-mesh front pocket and two side
    /// pockets, compression straps with buckles, a padded back panel, shoulder straps, hip-belt wings and a haul
    /// loop, with a tougher base. Origin at the bottom of the back panel; +Y up, +Z out from the wearer's back.
    /// Gear strapped on outside (see <see cref="PackVisual"/>) is built here too.
    /// </summary>
    public static class PackDesign
    {
        public const float Height = 0.56f;

        static readonly Dictionary<string, Mesh> meshes = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => meshes.Clear();

        // ---------- The body's shape ----------

        /// <summary>Half the width (x) at height y.</summary>
        static float HalfWidth(float y) => Mathf.Lerp(0.15f, 0.165f, Mathf.Sin(Mathf.Clamp01(y / Height) * Mathf.PI * 0.8f)) - 0.02f * Mathf.Clamp01((y - 0.45f) / 0.11f);

        /// <summary>Depth out from the back at height y: fuller in the middle.</summary>
        static float Depth(float y) => 0.16f + 0.065f * Mathf.Sin(Mathf.Clamp01(y / Height) * Mathf.PI);

        /// <summary>
        /// A point round the body at height y; t goes round from the back's middle (0) through the left side, the
        /// front (0.5) and the right side. The back is nearly flat, the front rounder.
        /// </summary>
        public static Vector3 BodyPoint(float y, float t)
        {
            float w = HalfWidth(y), d = Depth(y);
            float angle = t * Mathf.PI * 2f;
            float c = -Mathf.Sin(angle), s = -Mathf.Cos(angle);
            // Superellipse: boxier toward the back (higher exponent), rounder at the front.
            float exponent = s < 0f ? 5f : 2.6f;
            float x = w * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / exponent);
            float z = d * 0.5f + d * 0.5f * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / exponent);
            return new Vector3(x, y, z);
        }

        /// <summary>The outward normal of the body at (y, t), worked out from neighbouring points.</summary>
        public static Vector3 BodyNormal(float y, float t)
        {
            Vector3 along = BodyPoint(y, t + 0.002f) - BodyPoint(y, t - 0.002f);
            Vector3 up = BodyPoint(Mathf.Min(y + 0.01f, Height), t) - BodyPoint(Mathf.Max(y - 0.01f, 0f), t);
            return Vector3.Cross(along, up).normalized;
        }

        static Mesh Cached(string key, System.Func<Mesh> make)
        {
            if (!meshes.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                mesh = make();
                meshes[key] = mesh;
            }
            return mesh;
        }

        // ---------- Parts ----------

        static Mesh Body()
        {
            var b = new MeshBuilder();
            const int around = 40, up = 18;
            var rings = new List<Vector3[]>();
            // The base rounds in under the bottom; the top closes into a drawstring collar under the lid.
            for (int j = 0; j <= up; j++)
            {
                float f = j / (float)up;
                float y = f * Height;
                float shrink = f < 0.06f ? Mathf.Lerp(0.82f, 1f, Mathf.Sin(f / 0.06f * Mathf.PI * 0.5f)) : 1f;
                var ring = new Vector3[around];
                for (int i = 0; i < around; i++)
                {
                    Vector3 p = BodyPoint(y, i / (float)around);
                    ring[i] = new Vector3(p.x * shrink, Mathf.Max(0f, y - (1f - shrink) * 0.04f), p.z * shrink + (1f - shrink) * Depth(y) * 0.5f);
                }
                rings.Add(ring);
            }
            b.Loft(rings, closed: true, flip: true);
            b.Cap(rings[0], new Vector3(0f, -0.012f, 0f));
            b.Cap(rings[^1], new Vector3(0f, 0.03f, 0f), flip: true);
            return b.Build("Pack body");
        }

        /// <summary>The base: the body's bottom few centimetres again, just outside it, in tougher fabric.</summary>
        static Mesh Base()
        {
            var b = new MeshBuilder();
            const int around = 40;
            var rings = new List<Vector3[]>();
            foreach (float y in new[] { 0f, 0.035f, 0.07f })
            {
                var ring = new Vector3[around];
                for (int i = 0; i < around; i++)
                {
                    float t = i / (float)around;
                    float shrink = y < 0.01f ? 0.85f : 1f;
                    Vector3 p = BodyPoint(y, t) + BodyNormal(Mathf.Max(y, 0.04f), t) * 0.003f;
                    ring[i] = new Vector3(p.x * shrink, y, p.z * shrink + (1f - shrink) * Depth(y) * 0.5f);
                }
                rings.Add(ring);
            }
            b.Loft(rings, closed: true, flip: true);
            b.Cap(rings[0], new Vector3(0f, -0.014f, 0f));
            return b.Build("Pack base");
        }

        /// <summary>The top lid: a padded cushion over the top, reaching out over the front.</summary>
        static Mesh Lid()
        {
            var b = new MeshBuilder();
            const int around = 36, up = 8;
            var rings = new List<Vector3[]>();
            for (int j = 0; j <= up; j++)
            {
                float f = j / (float)up;
                // A pillow in section: full in the middle, pinched at the top and bottom seams.
                float swell = Mathf.Sin(f * Mathf.PI);
                float y = Height - 0.015f + f * 0.095f;
                var ring = new Vector3[around];
                for (int i = 0; i < around; i++)
                {
                    float t = i / (float)around;
                    Vector3 p = BodyPoint(Height - 0.04f, t);
                    // Wider than the collar, and reaching further forward than back.
                    float scale = 0.92f + 0.12f * swell;
                    p.x *= scale;
                    p.z = (p.z - 0.02f) * (0.95f + 0.2f * swell) + 0.015f;
                    ring[i] = new Vector3(p.x, y, p.z);
                }
                rings.Add(ring);
            }
            b.Loft(rings, closed: true, flip: true);
            b.Cap(rings[0], Vector3.zero);
            b.Cap(rings[^1], new Vector3(0f, 0.012f, 0.01f), flip: true);
            return b.Build("Pack lid");
        }

        /// <summary>
        /// A stretch pocket over part of the body: t from t0 to t1 round it, y from y0 to y1, standing out by up to
        /// <paramref name="bulge"/> in the middle. Closed along its sides and bottom by the bulge falling to zero.
        /// </summary>
        static Mesh Pocket(float t0, float t1, float y0, float y1, float bulge, string name)
        {
            var b = new MeshBuilder { Tiling = 6f };
            const int columns = 14, rows = 10;
            var rings = new List<Vector3[]>();
            for (int j = 0; j <= rows; j++)
            {
                float fy = j / (float)rows;
                float y = Mathf.Lerp(y0, y1, fy);
                var ring = new Vector3[columns + 1];
                for (int i = 0; i <= columns; i++)
                {
                    float ft = i / (float)columns;
                    float t = Mathf.Lerp(t0, t1, ft);
                    // Full at the top edge (the elastic hem), rounding to nothing at the sides and bottom.
                    float out_ = bulge * Mathf.Sin(ft * Mathf.PI) * Mathf.Sqrt(Mathf.Sin(Mathf.Min(fy * 1.3f, 1f) * Mathf.PI * 0.5f));
                    ring[i] = BodyPoint(y, t) + BodyNormal(y, t) * (0.004f + out_);
                }
                rings.Add(ring);
            }
            b.Loft(rings, closed: false, flip: true);
            return b.Build(name);
        }

        /// <summary>The elastic hem across the top of a pocket.</summary>
        static void Hem(MeshBuilder b, float t0, float t1, float y, float bulge)
        {
            var path = new List<Vector3>();
            const int steps = 14;
            for (int i = 0; i <= steps; i++)
            {
                float ft = i / (float)steps, t = Mathf.Lerp(t0, t1, ft);
                path.Add(BodyPoint(y, t) + BodyNormal(y, t) * (0.004f + bulge * Mathf.Sin(ft * Mathf.PI)));
            }
            b.Tube(path, 0.004f, 6);
        }

        /// <summary>Webbing round the body at height y between t0 and t1, with a buckle at <paramref name="buckleAt"/>.</summary>
        static void BodyStrap(MeshBuilder webbing, MeshBuilder plastic, float y, float t0, float t1, float buckleAt, float lift = 0.006f)
        {
            var path = new List<Vector3>();
            var normals = new List<Vector3>();
            const int steps = 16;
            for (int i = 0; i <= steps; i++)
            {
                float t = Mathf.Lerp(t0, t1, i / (float)steps);
                Vector3 n = BodyNormal(y, t);
                path.Add(BodyPoint(y, t) + n * lift);
                normals.Add(n);
            }
            webbing.Strap(path, k => normals[k], 0.02f, 0.0025f);
            float tb = Mathf.Lerp(t0, t1, buckleAt);
            Vector3 nb = BodyNormal(y, tb), at = BodyPoint(y, tb) + nb * (lift + 0.006f);
            Vector3 along = (BodyPoint(y, tb + 0.01f) - BodyPoint(y, tb - 0.01f)).normalized;
            plastic.Box(at, along, Vector3.up, nb, new Vector3(0.018f, 0.014f, 0.005f));
        }

        static Mesh Webbing(out Mesh buckles)
        {
            var webbing = new MeshBuilder { Tiling = 8f };
            var plastic = new MeshBuilder();
            // Two compression straps on each side, from the back edge round to the front corner.
            foreach (float y in new[] { 0.2f, 0.4f })
            {
                BodyStrap(webbing, plastic, y, 0.08f, 0.36f, 0.6f, 0.03f);
                BodyStrap(webbing, plastic, y, 0.64f, 0.92f, 0.4f, 0.03f);
            }
            // Lid straps: from the lid's front edge down to buckles on the front.
            foreach (float x in new[] { -0.07f, 0.07f })
            {
                float t = 0.5f + x * 0.9f;
                var path = new List<Vector3>();
                var normals = new List<Vector3>();
                for (int i = 0; i <= 8; i++)
                {
                    float y = Mathf.Lerp(Height + 0.04f, Height - 0.13f, i / 8f);
                    Vector3 n = BodyNormal(Mathf.Min(y, Height), t);
                    Vector3 p = BodyPoint(Mathf.Min(y, Height - 0.02f), t);
                    // Over the lid's bulge at the top.
                    float over = y > Height - 0.02f ? 0.03f + 0.03f * Mathf.Sin((y - (Height - 0.02f)) / 0.06f * Mathf.PI) : 0.008f;
                    path.Add(p + n * over + Vector3.up * Mathf.Max(0f, y - (Height - 0.02f)));
                    normals.Add(n);
                }
                webbing.Strap(path, k => normals[k], 0.02f, 0.0025f);
                Vector3 nb = BodyNormal(Height - 0.13f, t);
                plastic.Box(BodyPoint(Height - 0.13f, t) + nb * 0.012f, Vector3.right, Vector3.up, nb, new Vector3(0.014f, 0.018f, 0.005f));
            }
            // Haul loop at the top of the back.
            var loop = new List<Vector3>();
            for (int i = 0; i <= 10; i++)
            {
                float a = i / 10f * Mathf.PI;
                loop.Add(new Vector3(Mathf.Cos(a) * 0.025f, Height + 0.02f + Mathf.Sin(a) * 0.03f, -0.01f));
            }
            webbing.Strap(loop, _ => Vector3.back, 0.015f, 0.003f);
            buckles = plastic.Build("Pack buckles");
            return webbing.Build("Pack webbing");
        }

        /// <summary>The padded back panel, shoulder straps and hip-belt wings, for a pack that isn't being worn.</summary>
        static Mesh Harness()
        {
            var b = new MeshBuilder { Tiling = 4f };
            // Padded back panel with a channel down the middle.
            foreach (float x in new[] { -0.075f, 0.075f })
            {
                var path = new List<Vector3>();
                for (int i = 0; i <= 10; i++)
                    path.Add(new Vector3(x, Mathf.Lerp(0.09f, Height - 0.08f, i / 10f), 0.004f));
                b.Strap(path, _ => Vector3.back, 0.11f, 0.018f);
            }
            // Shoulder straps: from the top of the back panel, curving out and down to the lower corners, lying slack.
            foreach (float side in new[] { -1f, 1f })
            {
                var path = new List<Vector3>();
                for (int i = 0; i <= 14; i++)
                {
                    float f = i / 14f;
                    float x = side * Mathf.Lerp(0.05f, 0.13f, Mathf.Sin(f * Mathf.PI * 0.6f));
                    float y = Mathf.Lerp(Height - 0.06f, 0.12f, f);
                    float z = -0.02f - 0.05f * Mathf.Sin(f * Mathf.PI);
                    path.Add(new Vector3(x, y, z));
                }
                b.Strap(path, _ => Vector3.back, 0.06f, 0.014f);
                // Hip-belt wings, folded forward round the sides.
                var wing = new List<Vector3>();
                for (int i = 0; i <= 8; i++)
                {
                    float f = i / 8f;
                    float angle = f * 1.1f;
                    wing.Add(new Vector3(side * (0.09f + 0.1f * Mathf.Sin(angle)), 0.07f, -0.03f + 0.1f * (1f - Mathf.Cos(angle))));
                }
                b.Strap(wing, k => new Vector3(side * Mathf.Sin(k / 8f * 1.1f), 0f, -Mathf.Cos(k / 8f * 1.1f)), 0.09f, 0.02f);
            }
            return b.Build("Pack harness");
        }

        // ---------- Strapped-on gear ----------

        static Mesh Roll(float radius, float length, string name)
        {
            var b = new MeshBuilder { Tiling = 5f };
            var path = new List<Vector3>();
            for (int i = 0; i <= 6; i++)
                path.Add(new Vector3(Mathf.Lerp(-length / 2f, length / 2f, i / 6f), 0f, 0f));
            // A roll squashes a little where it's strapped, and is rounded at the ends.
            b.Tube(path, radius, 16);
            return b.Build(name);
        }

        static Mesh RollStraps(float radius, float length, float apart)
        {
            var b = new MeshBuilder { Tiling = 8f };
            foreach (float x in new[] { -apart / 2f, apart / 2f })
            {
                var path = new List<Vector3>();
                for (int i = 0; i <= 16; i++)
                {
                    float a = i / 16f * Mathf.PI * 2f;
                    path.Add(new Vector3(x, Mathf.Sin(a) * (radius + 0.003f), Mathf.Cos(a) * (radius + 0.003f)));
                }
                b.Strap(path, k => new Vector3(0f, Mathf.Sin(k / 16f * Mathf.PI * 2f), Mathf.Cos(k / 16f * Mathf.PI * 2f)), 0.02f, 0.0025f);
            }
            return b.Build("Roll straps");
        }

        /// <summary>
        /// Builds a pack under <paramref name="parent"/>. With <paramref name="harness"/> it has its own back panel
        /// and straps (one set down on the ground); without, the wearer's fitted straps are used. Returns the
        /// strapped-on gear, to show and hide as it goes in and out.
        /// </summary>
        public static PackVisual Build(Transform parent, PackMaterials materials, bool harness, GameObject bottle = null, GameObject machete = null)
        {
            void Add(string name, Mesh mesh, Material material, Transform under = null)
            {
                var part = new GameObject(name);
                part.transform.SetParent(under != null ? under : parent, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.AddComponent<MeshRenderer>().sharedMaterial = material;
            }

            Add("Bag Body", Cached("body", Body), materials.fabric);
            Add("Bag Lid", Cached("lid", Lid), materials.fabric);
            Add("Base", Cached("base", Base), materials.patch);
            Add("Front pocket", Cached("front", () => Pocket(0.37f, 0.63f, 0.09f, 0.34f, 0.018f, "Front pocket")), materials.mesh);
            Add("Left pocket", Cached("left", () => Pocket(0.18f, 0.31f, 0.03f, 0.17f, 0.022f, "Left pocket")), materials.mesh);
            Add("Right pocket", Cached("right", () => Pocket(0.69f, 0.82f, 0.03f, 0.17f, 0.022f, "Right pocket")), materials.mesh);
            Add("Hems", Cached("hems", () =>
            {
                var b = new MeshBuilder();
                Hem(b, 0.37f, 0.63f, 0.34f, 0.018f);
                Hem(b, 0.18f, 0.31f, 0.17f, 0.022f);
                Hem(b, 0.69f, 0.82f, 0.17f, 0.022f);
                return b.Build("Pocket hems");
            }), materials.webbing);
            Mesh buckles = null;
            Add("Webbing", Cached("webbing", () => Webbing(out buckles)), materials.webbing);
            Add("Buckles", Cached("buckles", () => { if (buckles == null) Webbing(out buckles); return buckles; }), materials.plastic);
            if (harness)
                Add("Harness", Cached("harness", Harness), materials.webbing);

            // Strapped-on gear, shown while it's in the pack.
            var visual = parent.gameObject.AddComponent<PackVisual>();
            Transform Slot(string name, Vector3 at, Quaternion turn)
            {
                var slot = new GameObject(name).transform;
                slot.SetParent(parent, false);
                slot.SetLocalPositionAndRotation(at, turn);
                return slot;
            }

            // A foam pad rolled and strapped under the base.
            visual.pad = Slot("Sleeping pad", new Vector3(0f, -0.07f, 0.1f), Quaternion.identity).gameObject;
            Add("Pad", Cached("pad", () => Roll(0.07f, 0.52f, "Foam pad")), materials.gearFabric, visual.pad.transform);
            Add("Pad straps", Cached("padstraps", () => RollStraps(0.07f, 0.52f, 0.3f)), materials.webbing, visual.pad.transform);
            // The tent bag stood upright in the left side pocket, held by the compression straps.
            visual.tent = Slot("Tent bag", BodyPoint(0.24f, 0.245f) + BodyNormal(0.24f, 0.245f) * 0.072f, Quaternion.Euler(0f, 0f, 90f)).gameObject;
            Add("Tent sack", Cached("tentbag", () => Roll(0.07f, 0.42f, "Tent bag")), materials.gearFabric, visual.tent.transform);
            // The chair, in its sack, across the top of the lid.
            visual.chair = Slot("Chair sack", new Vector3(0f, Height + 0.13f, 0.11f), Quaternion.identity).gameObject;
            Add("Chair", Cached("chair", () => Roll(0.05f, 0.36f, "Chair sack")), materials.gearFabric, visual.chair.transform);
            Add("Chair straps", Cached("chairstraps", () => RollStraps(0.05f, 0.36f, 0.2f)), materials.webbing, visual.chair.transform);
            // The water bottle in the right side pocket, and the machete behind the right compression straps.
            if (bottle != null)
            {
                // Down in the pocket, its bottom on the pocket's floor and the top half showing above the hem.
                visual.bottle = Slot("Bottle", BodyPoint(0.03f, 0.755f) + BodyNormal(0.1f, 0.755f) * 0.03f, Quaternion.identity).gameObject;
                GameObject copy = Object.Instantiate(bottle, visual.bottle.transform);
                copy.transform.SetLocalPositionAndRotation(new Vector3(0f, 0.105f, 0f), Quaternion.Euler(0f, 90f, 0f));
                copy.transform.localScale = Vector3.one * 0.85f;
            }
            if (machete != null)
            {
                visual.machete = Slot("Machete", BodyPoint(0.22f, 0.72f) + BodyNormal(0.22f, 0.72f) * 0.036f, Quaternion.Euler(0f, -60f, 14f)).gameObject;
                GameObject copy = Object.Instantiate(machete, visual.machete.transform);
                copy.transform.localPosition = Vector3.zero;
            }
            foreach (Collider collider in parent.GetComponentsInChildren<Collider>())
                if (collider.gameObject != parent.gameObject)
                    Discard(collider);
            // Coloured from the start; the tent bag takes the tent's colour once it's known.
            visual.Tint(TentDesign.Of(TentModel.OnePerson).Fly);
            return visual;
        }

        static void Discard(Object thing)
        {
            if (Application.isPlaying)
                Object.Destroy(thing);
            else
                Object.DestroyImmediate(thing);
        }
    }

    /// <summary>
    /// The gear strapped outside a pack, shown only while it's in the pack: sleeping pad, tent bag, chair, the water
    /// bottle in its pocket and the machete behind the straps (those two vanish while in your hand).
    /// </summary>
    public class PackVisual : MonoBehaviour
    {
        public GameObject pad, tent, chair, bottle, machete;

        public void Show(bool hasPad, bool hasTent, bool hasChair, bool hasBottle, bool hasMachete)
        {
            Set(pad, hasPad);
            Set(tent, hasTent);
            Set(chair, hasChair);
            Set(bottle, hasBottle);
            Set(machete, hasMachete);
        }

        /// <summary>Colours the tent bag to the tent's fly, the pad to foam, the chair sack dark.</summary>
        public void Tint(Color tentFly)
        {
            var block = new MaterialPropertyBlock();
            void Colour(GameObject thing, Color colour)
            {
                if (thing == null)
                    return;
                block.SetColor("_BaseColor", colour);
                foreach (Renderer renderer in thing.GetComponentsInChildren<Renderer>())
                    if (!renderer.name.Contains("straps"))
                        renderer.SetPropertyBlock(block);
            }
            Colour(tent, tentFly);
            Colour(pad, new Color(0.85f, 0.75f, 0.2f));
            Colour(chair, new Color(0.2f, 0.24f, 0.28f));
        }

        static void Set(GameObject thing, bool on)
        {
            if (thing != null && thing.activeSelf != on)
                thing.SetActive(on);
        }
    }
}
