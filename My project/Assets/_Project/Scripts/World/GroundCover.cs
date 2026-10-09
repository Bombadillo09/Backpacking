using UnityEngine;

namespace Backpacking.World
{
    /// <summary>What the ground is made of at a spot. Footsteps sound different on each; animals prefer some.</summary>
    public enum Surface
    {
        Soft,
        Leaves,
        Hard,
        Snow,
        Water,
        /// <summary>Floorboards: a building's floor or porch. Not a terrain layer.</summary>
        Wood,
    }

    /// <summary>Reads the terrain's strongest ground layer at a position and classifies it by the layer's name.</summary>
    public static class GroundCover
    {
        static Terrain cachedTerrain;
        static Surface[] layerSurfaces;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cachedTerrain = null;
            layerSurfaces = null;
        }

        /// <summary>The ground at <paramref name="position"/>, or Soft off the terrain. Never returns Water.</summary>
        public static Surface At(Vector3 position)
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null)
                return Surface.Soft;
            if (terrain != cachedTerrain)
                Classify(terrain);

            TerrainData data = terrain.terrainData;
            Vector3 local = position - terrain.transform.position;
            int x = Mathf.Clamp((int)(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp((int)(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            float[,,] weights = data.GetAlphamaps(x, z, 1, 1);

            int strongest = 0;
            for (int layer = 1; layer < weights.GetLength(2); layer++)
                if (weights[0, 0, layer] > weights[0, 0, strongest])
                    strongest = layer;
            return strongest < layerSurfaces.Length ? layerSurfaces[strongest] : Surface.Soft;
        }

        static void Classify(Terrain terrain)
        {
            cachedTerrain = terrain;
            TerrainLayer[] layers = terrain.terrainData.terrainLayers;
            layerSurfaces = new Surface[layers.Length];
            for (int i = 0; i < layers.Length; i++)
                layerSurfaces[i] = Classify(layers[i] != null ? layers[i].name : "");
        }

        /// <summary>Guesses what a ground layer is from its name, e.g. "aerial_rocks_02" or "ForestFloor".</summary>
        static Surface Classify(string layerName)
        {
            string lower = layerName.ToLowerInvariant();
            if (lower.Contains("rock") || lower.Contains("stone") || lower.Contains("gravel"))
                return Surface.Hard;
            if (lower.Contains("snow"))
                return Surface.Snow;
            if (lower.Contains("grass") || lower.Contains("meadow") || lower.Contains("dirt"))
                return Surface.Soft;
            if (lower.Contains("leaf") || lower.Contains("leaves") || lower.Contains("litter") || lower.Contains("forest") || lower.Contains("forrest"))
                return Surface.Leaves;
            return Surface.Soft;
        }

        /// <summary>Ground height at a position on the active terrain, or the position's own height off it.</summary>
        public static float HeightAt(Vector3 position)
        {
            Terrain terrain = Terrain.activeTerrain;
            return terrain != null ? terrain.SampleHeight(position) + terrain.transform.position.y : position.y;
        }
    }
}
