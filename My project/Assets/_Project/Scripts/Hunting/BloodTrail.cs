using System.Collections.Generic;
using Backpacking.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backpacking.Hunting
{
    /// <summary>
    /// Drops and splashes of blood on the ground, left by a wounded animal for the hunter to follow. Drawn instanced
    /// (no objects per drop), they last half a game day, and only the newest few hundred are kept.
    /// </summary>
    public class BloodTrail : MonoBehaviour
    {
        const int MaxDrops = 600;
        const float LifetimeHours = 12f;

        struct DropInfo
        {
            public Matrix4x4 matrix;
            public float bornAtHour;
        }

        static BloodTrail instance;

        readonly List<DropInfo> drops = new();
        readonly List<Matrix4x4> matrices = new();
        Mesh quad;
        Material material;
        TimeOfDay timeOfDay;
        float nextCleanup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        static BloodTrail Instance
        {
            get
            {
                if (instance == null)
                    instance = new GameObject("Blood Trail").AddComponent<BloodTrail>();
                return instance;
            }
        }

        /// <summary>A drop or two where a bleeding animal passed. <paramref name="size"/> scales them (1 is a steady drip).</summary>
        public static void Drop(Vector3 position, float size = 1f)
        {
            int count = Random.value < 0.3f ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                Vector2 scatter = Random.insideUnitCircle * 0.12f;
                Instance.Add(position + new Vector3(scatter.x, 0f, scatter.y), Random.Range(0.08f, 0.15f) * size);
            }
        }

        /// <summary>A heavier patch: where an animal was hit, lay down or fell. Spread over <paramref name="radius"/> metres.</summary>
        public static void Splash(Vector3 position, float radius)
        {
            Instance.Add(position, radius * 0.8f);
            int count = Mathf.RoundToInt(10 + radius * 30f);
            for (int i = 0; i < count; i++)
            {
                Vector2 scatter = Random.insideUnitCircle * radius;
                Instance.Add(position + new Vector3(scatter.x, 0f, scatter.y), Random.Range(0.06f, 0.14f));
            }
        }

        void Awake()
        {
            timeOfDay = FindAnyObjectByType<TimeOfDay>();
            quad = new Mesh { name = "Blood drop" };
            // A flat, roughly round blot: a fan of eight points with a ragged edge.
            var vertices = new List<Vector3> { Vector3.zero };
            var triangles = new List<int>();
            for (int i = 0; i < 9; i++)
            {
                float angle = i / 9f * Mathf.PI * 2f;
                float r = 0.5f * (0.75f + 0.25f * Mathf.Sin(i * 2.7f));
                vertices.Add(new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r));
                triangles.AddRange(new[] { 0, 1 + (i + 1) % 9, 1 + i });
            }
            quad.SetVertices(vertices);
            quad.SetTriangles(triangles, 0);
            quad.RecalculateNormals();
            quad.RecalculateBounds();

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = "Blood", enableInstancing = true };
            var colour = new Color(0.55f, 0.03f, 0.04f);
            material.SetColor("_BaseColor", colour);
            material.SetColor("_Color", colour);
            // Wet: it catches the light, which is how you spot it in the grass.
            material.SetFloat("_Smoothness", 0.85f);
            // A faint glow of its own, so a trail can still be followed in the shade and at dusk.
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(0.09f, 0f, 0.005f));
        }

        void OnDestroy()
        {
            if (quad != null)
                Destroy(quad);
            if (material != null)
                Destroy(material);
        }

        void Add(Vector3 position, float size)
        {
            position.y = GroundCover.HeightAt(position);
            Vector3 normal = Vector3.up;
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                Vector3 local = position - terrain.transform.position;
                normal = terrain.terrainData.GetInterpolatedNormal(local.x / terrain.terrainData.size.x, local.z / terrain.terrainData.size.z);
            }
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var scale = new Vector3(size, 1f, size * Random.Range(0.7f, 1.3f));
            if (drops.Count >= MaxDrops)
                drops.RemoveAt(0);
            drops.Add(new DropInfo
            {
                matrix = Matrix4x4.TRS(position + normal * 0.02f, rotation, scale),
                bornAtHour = timeOfDay != null ? timeOfDay.TotalHours : 0f,
            });
        }

        void Update()
        {
            if (timeOfDay != null && Time.time >= nextCleanup)
            {
                nextCleanup = Time.time + 5f;
                float now = timeOfDay.TotalHours;
                drops.RemoveAll(drop => now - drop.bornAtHour > LifetimeHours);
            }
            if (drops.Count == 0)
                return;

            matrices.Clear();
            foreach (DropInfo drop in drops)
                matrices.Add(drop.matrix);
            Camera view = Camera.main;
            var parameters = new RenderParams(material)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                worldBounds = new Bounds(view != null ? view.transform.position : Vector3.zero, Vector3.one * 2000f),
            };
            for (int start = 0; start < matrices.Count; start += 1023)
                Graphics.RenderMeshInstanced(parameters, quad, 0, matrices, Mathf.Min(1023, matrices.Count - start), start);
        }
    }
}
