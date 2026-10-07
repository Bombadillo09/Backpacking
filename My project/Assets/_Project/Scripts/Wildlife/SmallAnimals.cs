using System.Collections.Generic;
using Backpacking.Camp;
using UnityEngine;

namespace Backpacking.Wildlife
{
    /// <summary>
    /// Rabbits built in code: rounded body parts (torso, haunches, head, ears, legs, tail) on pivots that
    /// <see cref="SmallAnimalRig"/> moves, with a generated fur texture, looking like a wild cottontail.
    /// </summary>
    public static class SmallAnimals
    {
        static readonly Dictionary<string, Mesh> meshes = new();
        static readonly Dictionary<Color, Material> furs = new();
        static Texture2D furTexture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            meshes.Clear();
            furs.Clear();
            furTexture = null;
        }

        // ---------- Materials ----------

        /// <summary>A fine, streaky grey fur pattern, tinted per animal.</summary>
        static Texture2D FurTexture()
        {
            if (furTexture != null)
                return furTexture;
            const int size = 128;
            furTexture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Fur", wrapMode = TextureWrapMode.Repeat };
            var random = new System.Random(5);
            var pixels = new Color[size * size];
            var strands = new float[size * size];
            // Short strands: streaks along v at random spots, lighter at the tips.
            for (int s = 0; s < 2600; s++)
            {
                int x = random.Next(size), y = random.Next(size), length = 4 + random.Next(7);
                float shade = 0.65f + (float)random.NextDouble() * 0.5f;
                for (int k = 0; k < length; k++)
                    strands[((y + k) % size) * size + (x + k / 4) % size] = shade * (0.7f + 0.3f * k / length);
            }
            for (int i = 0; i < pixels.Length; i++)
            {
                float v = strands[i] > 0f ? strands[i] : 0.72f + (float)random.NextDouble() * 0.12f;
                pixels[i] = new Color(v, v, v, 1f);
            }
            furTexture.SetPixels(pixels);
            furTexture.Apply(true);
            return furTexture;
        }

        static Material Fur(Material plain, Color colour, float smoothness = 0.1f)
        {
            if (furs.TryGetValue(colour, out Material material) && material != null)
                return material;
            material = new Material(plain) { name = $"Fur {colour}" };
            material.SetTexture("_BaseMap", FurTexture());
            material.SetTextureScale("_BaseMap", new Vector2(6f, 6f));
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            furs[colour] = material;
            return material;
        }

        // ---------- Building ----------

        static Mesh Cached(string key, System.Action<MeshBuilder> make)
        {
            if (meshes.TryGetValue(key, out Mesh mesh) && mesh != null)
                return mesh;
            var b = new MeshBuilder { Tiling = 6f };
            make(b);
            mesh = b.Build(key);
            meshes[key] = mesh;
            return mesh;
        }

        static Transform Pivot(string name, Transform parent, Vector3 at)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = at;
            return pivot;
        }

        static void Part(Transform parent, string key, Material material, System.Action<MeshBuilder> make)
        {
            var part = new GameObject(key);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = Cached(key, make);
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            // Small animals: their shadows are too small to matter and many are about.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Quaternion Pitch(float degrees) => Quaternion.Euler(degrees, 0f, 0f);

        /// <summary>A wild cottontail: grey-brown with a cream belly and white tail, long ears, big haunches.</summary>
        public static GameObject Rabbit(Material plain)
        {
            Material coat = Fur(plain, new Color(0.52f, 0.43f, 0.33f)), belly = Fur(plain, new Color(0.85f, 0.8f, 0.72f)),
                white = Fur(plain, new Color(0.95f, 0.94f, 0.9f)), dark = Fur(plain, new Color(0.04f, 0.035f, 0.03f), 0.85f),
                pink = Fur(plain, new Color(0.62f, 0.42f, 0.4f), 0.3f);
            var root = new GameObject("Rabbit");
            Transform body = Pivot("Body", root.transform, Vector3.zero);
            Part(body, "Rabbit torso", coat, b => b.Ellipsoid(new Vector3(0f, 0.115f, -0.02f), new Vector3(0.075f, 0.085f, 0.14f), Pitch(-12f)));
            Part(body, "Rabbit chest", belly, b => b.Ellipsoid(new Vector3(0f, 0.09f, 0.06f), new Vector3(0.055f, 0.06f, 0.06f), Quaternion.identity));
            foreach (float side in new[] { -1f, 1f })
            {
                string s = side < 0f ? "L" : "R";
                Transform hind = Pivot($"Hind {s}", body, new Vector3(side * 0.05f, 0.11f, -0.08f));
                Part(hind, "Rabbit haunch", coat, b => b.Ellipsoid(new Vector3(0f, -0.02f, -0.01f), new Vector3(0.035f, 0.06f, 0.075f), Pitch(20f)));
                Part(hind, "Rabbit hind foot", coat, b => b.Ellipsoid(new Vector3(0f, -0.095f, 0.035f), new Vector3(0.018f, 0.014f, 0.065f), Quaternion.identity));
                Transform front = Pivot($"Front {s}", body, new Vector3(side * 0.035f, 0.08f, 0.08f));
                Part(front, "Rabbit foreleg", coat, b => b.Ellipsoid(new Vector3(0f, -0.04f, 0.005f), new Vector3(0.013f, 0.045f, 0.016f), Quaternion.identity));
            }
            Transform tail = Pivot("Tail", body, new Vector3(0f, 0.14f, -0.155f));
            Part(tail, "Rabbit tail", white, b => b.Ellipsoid(Vector3.zero, new Vector3(0.026f, 0.028f, 0.022f), Quaternion.identity, 10));

            Transform head = Pivot("Head", body, new Vector3(0f, 0.16f, 0.1f));
            Part(head, "Rabbit head", coat, b => b.Ellipsoid(new Vector3(0f, 0.025f, 0.045f), new Vector3(0.045f, 0.05f, 0.065f), Pitch(15f)));
            Part(head, "Rabbit muzzle", belly, b => b.Ellipsoid(new Vector3(0f, 0.008f, 0.095f), new Vector3(0.026f, 0.022f, 0.025f), Quaternion.identity, 10));
            Part(head, "Rabbit nose", pink, b => b.Ellipsoid(new Vector3(0f, 0.016f, 0.117f), new Vector3(0.007f, 0.005f, 0.005f), Quaternion.identity, 8));
            foreach (float side in new[] { -1f, 1f })
            {
                Part(head, side < 0f ? "Rabbit eye L" : "Rabbit eye R", dark,
                    b => b.Ellipsoid(new Vector3(side * 0.036f, 0.04f, 0.065f), Vector3.one * 0.011f, Quaternion.identity, 8));
                Transform ear = Pivot(side < 0f ? "Ear L" : "Ear R", head, new Vector3(side * 0.018f, 0.065f, 0.025f));
                ear.localRotation = Quaternion.Euler(-12f, 0f, -side * 10f);
                Part(ear, "Rabbit ear", coat, b => b.Ellipsoid(new Vector3(0f, 0.055f, 0f), new Vector3(0.017f, 0.06f, 0.006f), Quaternion.identity, 12));
            }
            root.AddComponent<SmallAnimalRig>().Setup();
            return root;
        }
    }

    /// <summary>
    /// Moves a rabbit's parts: hind legs kick and forelegs reach as it hops, the head dips to feed, ears turn
    /// and twitch, and it sits up tall when alert.
    /// </summary>
    public class SmallAnimalRig : MonoBehaviour
    {
        Transform body, head, tail, earL, earR, hindL, hindR, frontL, frontR;
        Quaternion earLRest, earRRest;
        float sit, feed, twitchTime, flickTime, seed;

        public void Setup()
        {
            body = transform.Find("Body");
            head = body.Find("Head");
            tail = body.Find("Tail");
            earL = head.Find("Ear L");
            earR = head.Find("Ear R");
            hindL = body.Find("Hind L");
            hindR = body.Find("Hind R");
            frontL = body.Find("Front L");
            frontR = body.Find("Front R");
            earLRest = earL.localRotation;
            earRRest = earR.localRotation;
            seed = Random.Range(0f, 100f);
        }

        /// <summary>
        /// Poses the animal for this frame: its ground speed, the phase of its gait (one hop per half turn), how
        /// high off the ground the hop has it (metres), whether it's feeding, and whether it's alert (sitting up,
        /// ears pricked). <paramref name="instant"/> snaps straight to the pose instead of easing into it.
        /// </summary>
        public void Pose(float speed, float phase, float hop, bool feeding, bool alert, bool instant = false)
        {
            float dt = instant ? 1f : Time.deltaTime;
            bool moving = speed > 0.05f;
            sit = Mathf.MoveTowards(sit, alert && !moving ? 1f : 0f, dt * 5f);
            feed = Mathf.MoveTowards(feed, feeding && !moving && !alert ? 1f : 0f, dt * 3f);
            float stride = moving ? Mathf.Sin(phase) : 0f;
            float lift = moving ? Mathf.Abs(Mathf.Sin(phase)) : 0f;
            float bounce = Mathf.Clamp01(speed / 2f);

            // Body: sits up on its haunches when alert, pitches through each bound when moving.
            body.localRotation = Quaternion.Euler(-35f * sit - 10f * stride * bounce, 0f, 0f);
            // Sitting up rocks back onto the haunches.
            body.localPosition = new Vector3(0f, hop + sit * 0.05f, -sit * 0.05f);

            // Legs: hind legs drive back, forelegs reach forward, out of step.
            float kick = stride * 45f * bounce;
            hindL.localRotation = hindR.localRotation = Quaternion.Euler(kick + sit * 30f, 0f, 0f);
            frontL.localRotation = frontR.localRotation = Quaternion.Euler(-kick * 0.8f - sit * 25f, 0f, 0f);

            // Head: down to nibble when feeding (with little chews), up and level when alert.
            float chew = feed * Mathf.Sin(Time.time * 11f + seed) * 3f;
            // (Counter to the body sitting up, so an alert rabbit holds its head level.)
            head.localRotation = Quaternion.Euler(feed * 40f + chew + sit * 28f + lift * 8f, 0f, 0f);

            // Ears: pricked when alert, flattened back when running, with a twitch now and then.
            if (Time.time > twitchTime)
                twitchTime = Time.time + Random.Range(1.5f, 5f);
            float twitch = Time.time > twitchTime - 0.15f ? 18f : 0f;
            // Positive tips the ears forward: pricked up when alert, laid flat back when running.
            float back = sit * 14f - Mathf.Clamp01(speed / 4f) * 55f;
            earL.localRotation = earLRest * Quaternion.Euler(back, 0f, twitch);
            earR.localRotation = earRRest * Quaternion.Euler(back, 0f, -twitch * 0.5f);

            // Tail: flicks up now and then, and as it hops (the white flash of a fleeing cottontail).
            if (Time.time > flickTime)
                flickTime = Time.time + Random.Range(0.8f, 3f);
            float flick = Time.time > flickTime - 0.2f ? 1f : 0f;
            tail.localRotation = Quaternion.Euler(-flick * 25f - lift * 15f, 0f, 0f);
        }
    }
}
