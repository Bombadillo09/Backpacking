using UnityEngine;

namespace Backpacking.Navigation
{
    /// <summary>
    /// Renders a terrain into a topographic map texture: elevation tint, hill shading,
    /// contour lines and a grid. North (+Z) is at the top.
    /// </summary>
    public static class TopographicMap
    {
        static readonly Color ContourColour = new(0.55f, 0.33f, 0.18f);
        static readonly Color GridColour = new(0.3f, 0.45f, 0.7f);

        public static Texture2D Generate(Terrain terrain, int resolution, float contourInterval, int indexContourEvery, float gridSpacing)
        {
            float[] elevation = SampleElevations(terrain, resolution, out float minElevation, out float maxElevation);
            float metresPerPixel = terrain.terrainData.size.x / resolution;
            Gradient tint = ElevationTint();

            // Light from the north-west, the cartographic convention.
            Vector3 light = new Vector3(-1f, 1f, 1.4f).normalized;

            var pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float e = elevation[y * resolution + x];
                float east = elevation[y * resolution + Mathf.Min(x + 1, resolution - 1)];
                float west = elevation[y * resolution + Mathf.Max(x - 1, 0)];
                float north = elevation[Mathf.Min(y + 1, resolution - 1) * resolution + x];
                float south = elevation[Mathf.Max(y - 1, 0) * resolution + x];

                var normal = new Vector3(-(east - west) / (2f * metresPerPixel), -(north - south) / (2f * metresPerPixel), 1f).normalized;
                float shade = Mathf.Lerp(0.6f, 1.1f, Mathf.Clamp01(Vector3.Dot(normal, light)));
                Color colour = tint.Evaluate(Mathf.InverseLerp(minElevation, maxElevation, e)) * shade;

                // Contour where this pixel and its east or north neighbour fall in different bands.
                int band = Mathf.FloorToInt(e / contourInterval);
                int eastBand = Mathf.FloorToInt(east / contourInterval);
                int northBand = Mathf.FloorToInt(north / contourInterval);
                if (eastBand != band || northBand != band)
                {
                    // The higher band's lower edge is the contour level being crossed.
                    int crossed = Mathf.Max(band, Mathf.Max(eastBand, northBand));
                    bool isIndex = crossed % indexContourEvery == 0;
                    colour = Color.Lerp(colour, ContourColour, isIndex ? 0.9f : 0.5f);
                }

                // Grid lines every gridSpacing metres from the map's south-west corner.
                bool onGrid = Mathf.FloorToInt(x * metresPerPixel / gridSpacing) != Mathf.FloorToInt((x + 1) * metresPerPixel / gridSpacing)
                              || Mathf.FloorToInt(y * metresPerPixel / gridSpacing) != Mathf.FloorToInt((y + 1) * metresPerPixel / gridSpacing);
                if (onGrid)
                    colour = Color.Lerp(colour, GridColour, 0.35f);

                colour.a = 1f;
                pixels[y * resolution + x] = colour;
            }

            var texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>World-space elevation at the centre of each map pixel, read straight from the heightmap.</summary>
        static float[] SampleElevations(Terrain terrain, int resolution, out float min, out float max)
        {
            TerrainData data = terrain.terrainData;
            int heightmapResolution = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, heightmapResolution, heightmapResolution);
            float baseHeight = terrain.transform.position.y;

            var elevation = new float[resolution * resolution];
            min = float.MaxValue;
            max = float.MinValue;
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float hx = (x + 0.5f) / resolution * (heightmapResolution - 1);
                float hz = (y + 0.5f) / resolution * (heightmapResolution - 1);
                int x0 = (int)hx, z0 = (int)hz;
                int x1 = Mathf.Min(x0 + 1, heightmapResolution - 1), z1 = Mathf.Min(z0 + 1, heightmapResolution - 1);
                float tx = hx - x0, tz = hz - z0;

                float h = Mathf.Lerp(
                    Mathf.Lerp(heights[z0, x0], heights[z0, x1], tx),
                    Mathf.Lerp(heights[z1, x0], heights[z1, x1], tx), tz);
                float e = baseHeight + h * data.size.y;

                elevation[y * resolution + x] = e;
                min = Mathf.Min(min, e);
                max = Mathf.Max(max, e);
            }
            return elevation;
        }

        static Gradient ElevationTint()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.70f, 0.80f, 0.58f), 0f),
                    new GradientColorKey(new Color(0.86f, 0.83f, 0.63f), 0.4f),
                    new GradientColorKey(new Color(0.78f, 0.67f, 0.52f), 0.7f),
                    new GradientColorKey(new Color(0.95f, 0.95f, 0.95f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }
    }
}
