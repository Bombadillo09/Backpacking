using System.Collections;
using UnityEngine;

namespace Backpacking.World
{
    /// <summary>
    /// Fresh snow on the high ground: paints the terrain's snow layer down to the weather's snow line, fading in
    /// over a band of height, thinner on steep rock that sheds it. Snow underfoot then sounds and counts as snow
    /// (footsteps, animals' habitat), since those read the terrain's ground layers.
    /// <para>
    /// Repaints in strips over several frames whenever the line moves, so there's no hitch. The terrain data is an
    /// asset, so the original ground is kept and put back when play stops.
    /// </para>
    /// </summary>
    public class SnowCover : MonoBehaviour
    {
        [SerializeField] WeatherSystem weather;
        [Tooltip("Height (metres) over which fresh snow fades from none to full cover.")]
        [SerializeField] float edgeBand = 40f;
        [Tooltip("Repaint once the snow line has moved this many metres.")]
        [SerializeField] float repaintStep = 6f;
        [Tooltip("Alphamap rows repainted per frame.")]
        [SerializeField] int rowsPerFrame = 24;

        TerrainData data;
        int resolution, layers, snowLayer = -1;
        float[,,] original;
        float[] texelHeight, texelSteepness;
        float[] rowLowest, rowHighest;
        float highestGround = float.MinValue;
        float paintedLine = float.MaxValue;
        bool painting, changedAnything;

        void Start()
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null || weather == null)
            {
                enabled = false;
                return;
            }
            data = terrain.terrainData;
            TerrainLayer[] terrainLayers = data.terrainLayers;
            for (int i = 0; i < terrainLayers.Length; i++)
                if (terrainLayers[i] != null && terrainLayers[i].name.ToLowerInvariant().Contains("snow"))
                    snowLayer = i;
            if (snowLayer < 0)
            {
                enabled = false;
                return;
            }
            resolution = data.alphamapResolution;
            layers = data.alphamapLayers;
            original = data.GetAlphamaps(0, 0, resolution, resolution);
            MeasureGround(terrain);
        }

        /// <summary>The ground height and steepness at every alphamap texel, and each row's height range.</summary>
        void MeasureGround(Terrain terrain)
        {
            int heightRes = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, heightRes, heightRes);
            float baseY = terrain.transform.position.y;
            float cell = data.size.x / (heightRes - 1);
            texelHeight = new float[resolution * resolution];
            texelSteepness = new float[resolution * resolution];
            rowLowest = new float[resolution];
            rowHighest = new float[resolution];
            for (int z = 0; z < resolution; z++)
            {
                rowLowest[z] = float.MaxValue;
                rowHighest[z] = float.MinValue;
                int hz = Mathf.Clamp(Mathf.RoundToInt((z + 0.5f) / resolution * (heightRes - 1)), 1, heightRes - 2);
                for (int x = 0; x < resolution; x++)
                {
                    int hx = Mathf.Clamp(Mathf.RoundToInt((x + 0.5f) / resolution * (heightRes - 1)), 1, heightRes - 2);
                    float y = baseY + heights[hz, hx] * data.size.y;
                    float dx = (heights[hz, hx + 1] - heights[hz, hx - 1]) * data.size.y / (2f * cell);
                    float dz = (heights[hz + 1, hx] - heights[hz - 1, hx]) * data.size.y / (2f * cell);
                    int i = z * resolution + x;
                    texelHeight[i] = y;
                    texelSteepness[i] = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
                    rowLowest[z] = Mathf.Min(rowLowest[z], y);
                    rowHighest[z] = Mathf.Max(rowHighest[z], y);
                }
                highestGround = Mathf.Max(highestGround, rowHighest[z]);
            }
        }

        void Update()
        {
            if (painting)
                return;
            float line = weather.SnowLine;
            // Above the highest ground: no fresh snow anywhere.
            if (line > highestGround + edgeBand)
                line = float.MaxValue;
            bool same = line == paintedLine
                        || line != float.MaxValue && paintedLine != float.MaxValue && Mathf.Abs(line - paintedLine) < repaintStep;
            if (!same)
                StartCoroutine(Repaint(line));
        }

        IEnumerator Repaint(float line)
        {
            painting = true;
            // Only ground between the old and new lines (and their soft edges) changes.
            float low = Mathf.Min(line, paintedLine) - edgeBand;
            float high = line == float.MaxValue || paintedLine == float.MaxValue ? float.MaxValue : Mathf.Max(line, paintedLine) + edgeBand;
            for (int z0 = 0; z0 < resolution; z0 += rowsPerFrame)
            {
                int rows = Mathf.Min(rowsPerFrame, resolution - z0);
                bool touched = false;
                for (int z = z0; z < z0 + rows && !touched; z++)
                    touched = rowHighest[z] >= low && rowLowest[z] <= high;
                if (!touched)
                    continue;
                data.SetAlphamaps(0, z0, Strip(z0, rows, line));
                changedAnything = true;
                yield return null;
            }
            paintedLine = line;
            painting = false;
        }

        /// <summary>The original ground for these rows, with fresh snow laid over it down to <paramref name="line"/>.</summary>
        float[,,] Strip(int z0, int rows, float line)
        {
            var strip = new float[rows, resolution, layers];
            for (int z = 0; z < rows; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = (z0 + z) * resolution + x;
                float cover = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(line - edgeBand * 0.5f, line + edgeBand * 0.5f, texelHeight[i]));
                // Cliffs shed snow.
                cover *= Mathf.InverseLerp(55f, 35f, texelSteepness[i]) * 0.97f;
                float oldSnow = original[z0 + z, x, snowLayer];
                float snow = oldSnow + (1f - oldSnow) * cover;
                float rest = oldSnow < 1f ? (1f - snow) / (1f - oldSnow) : 0f;
                for (int layer = 0; layer < layers; layer++)
                    strip[z, x, layer] = layer == snowLayer ? snow : original[z0 + z, x, layer] * rest;
            }
            return strip;
        }

        void OnDisable()
        {
            StopAllCoroutines();
            painting = false;
            if (changedAnything && data != null && original != null)
            {
                data.SetAlphamaps(0, 0, original);
                changedAnything = false;
                paintedLine = float.MaxValue;
            }
        }
    }
}
