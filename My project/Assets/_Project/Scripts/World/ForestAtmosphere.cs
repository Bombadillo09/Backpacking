using UnityEngine;

namespace Backpacking.World
{
    /// <summary>
    /// How the air feels where the player is. Measures how closed the forest canopy is overhead from the
    /// terrain's trees, and tells <see cref="TimeOfDay"/> so the woods are shaded and hazy. Adds ground mist
    /// at dawn and in damp, still weather, specks drifting in the air under the trees by day, and a light fog
    /// hanging among the trees (only in the woods), the colour of the air: pale by day, dark at night.
    /// </summary>
    public class ForestAtmosphere : MonoBehaviour
    {
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] WeatherSystem weather;
        [SerializeField] Transform player;
        [Tooltip("Specks drifting in the air (pollen, dust). Optional.")]
        [SerializeField] ParticleSystem motes;
        [Tooltip("Faint wisps of fog among the trees. Optional.")]
        [SerializeField] ParticleSystem haze;
        [Tooltip("Haze wisps per second under a full canopy (more in mist, fewer in wind).")]
        [SerializeField] float hazePerSecond = 6f;

        [Header("Canopy")]
        [Tooltip("Trees per 100 m² that count as a fully closed canopy.")]
        [SerializeField] float closedCanopyDensity = 2f;
        [Tooltip("How far around the player the canopy is measured, in metres.")]
        [SerializeField] float sampleRadius = 14f;
        [Tooltip("Seconds for the light to adjust when walking in or out of the trees.")]
        [SerializeField] float adjustSeconds = 1.5f;

        [Header("Mist")]
        [Tooltip("Mist at its thickest around dawn, from 0 to 1.")]
        [SerializeField, Range(0f, 1f)] float dawnMist = 0.6f;
        [SerializeField, Range(0f, 1f)] float rainMist = 0.4f;
        [SerializeField] float motesPerSecond = 30f;

        const float Cell = 8f;
        float[,] treeWeight;
        Vector3 gridOrigin;
        float canopy, mist;

        public float Canopy => canopy;

        void Start() => BuildCanopyGrid();

        /// <summary>Counts trees into a coarse grid. Small saplings count for less than mature trees.</summary>
        public void BuildCanopyGrid()
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null)
                return;
            TerrainData data = terrain.terrainData;
            gridOrigin = terrain.transform.position;
            int width = Mathf.CeilToInt(data.size.x / Cell), depth = Mathf.CeilToInt(data.size.z / Cell);
            treeWeight = new float[width, depth];
            foreach (TreeInstance tree in data.treeInstances)
            {
                int x = Mathf.Clamp((int)(tree.position.x * data.size.x / Cell), 0, width - 1);
                int z = Mathf.Clamp((int)(tree.position.z * data.size.z / Cell), 0, depth - 1);
                treeWeight[x, z] += Mathf.Clamp01(tree.heightScale);
            }
        }

        void Update()
        {
            float target = MeasureCanopy(player.position);
            float step = Time.deltaTime / Mathf.Max(0.01f, adjustSeconds);
            canopy = Mathf.MoveTowards(canopy, target, step);
            mist = Mathf.MoveTowards(mist, TargetMist(), step * 0.2f);
            timeOfDay.CanopyShade = canopy;
            timeOfDay.Mist = mist;

            if (haze != null)
            {
                // Only in the woods; thicker in mist and damp, blown thin by wind.
                float calm = weather != null ? 1f - 0.7f * Mathf.InverseLerp(8f, 35f, weather.WindKmh) : 1f;
                ParticleSystem.EmissionModule emission = haze.emission;
                emission.rateOverTime = hazePerSecond * Mathf.InverseLerp(0.35f, 0.9f, canopy) * calm * (1f + mist);
                // Lit like the air around it, so it doesn't glow at night.
                Color air = RenderSettings.fogColor;
                ParticleSystem.MainModule main = haze.main;
                main.startColor = new Color(Mathf.Min(1f, air.r * 1.5f + 0.05f), Mathf.Min(1f, air.g * 1.5f + 0.05f), Mathf.Min(1f, air.b * 1.5f + 0.05f),
                    0.12f + 0.06f * mist);
            }

            if (motes != null)
            {
                ParticleSystem.EmissionModule emission = motes.emission;
                emission.rateOverTime = motesPerSecond * canopy * timeOfDay.Daylight;
            }
        }

        float MeasureCanopy(Vector3 position)
        {
            if (treeWeight == null)
                return 0f;
            int reach = Mathf.CeilToInt(sampleRadius / Cell);
            int cx = Mathf.FloorToInt((position.x - gridOrigin.x) / Cell), cz = Mathf.FloorToInt((position.z - gridOrigin.z) / Cell);
            float trees = 0f;
            int cells = 0;
            for (int z = cz - reach; z <= cz + reach; z++)
            for (int x = cx - reach; x <= cx + reach; x++)
            {
                if (x < 0 || z < 0 || x >= treeWeight.GetLength(0) || z >= treeWeight.GetLength(1))
                    continue;
                trees += treeWeight[x, z];
                cells++;
            }
            if (cells == 0)
                return 0f;
            float perHundred = trees / (cells * Cell * Cell) * 100f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(perHundred / closedCanopyDensity));
        }

        float TargetMist()
        {
            // Mist gathers before dawn and burns off in the morning sun.
            float sinceDawn = (timeOfDay.Hour - 6f) / 1.8f;
            float dawn = Mathf.Exp(-sinceDawn * sinceDawn) * dawnMist;
            if (weather == null)
                return dawn;
            // Wind blows it away; rain brings its own.
            float calm = 1f - Mathf.InverseLerp(10f, 35f, weather.WindKmh);
            return Mathf.Clamp01(dawn * calm + weather.RainIntensity * rainMist);
        }
    }
}
