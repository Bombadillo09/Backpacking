using System.Collections.Generic;
using Backpacking.Survival;
using Backpacking.Trip;
using Backpacking.UI;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// Clearing a campsite with the machete. In the woods, brush, deadfall and saplings cover the ground,
    /// and the tent and fire ring need cleared ground. Clearing removes the terrain's ground plants in a
    /// circle and fells any trees inside it for firewood.
    /// <para>The terrain data is a shared asset, so every change is undone when the scene ends; cleared
    /// spots are saved with the trip and cleared again on load.</para>
    /// </summary>
    public class GroundClearing : MonoBehaviour
    {
        [Tooltip("Terrain detail layers that count as brush (shrubs, deadfall, forest-floor plants). Set by the scene builder.")]
        [SerializeField] int[] brushLayers;
        [SerializeField] float radius = 4f;
        [Tooltip("Game minutes to clear the brush, plus extra for each tree felled.")]
        [SerializeField] float baseMinutes = 20f;
        [SerializeField] float minutesPerTree = 15f;
        [SerializeField] float baseEnergy = 5f;
        [SerializeField] float energyPerTree = 3f;
        [SerializeField] int firewoodPerTree = 2;

        Terrain terrain;
        int detailLayerCount;
        TreeInstance[] originalTrees;
        readonly List<(int layer, int x, int z, int[,] values)> originalDetails = new();
        readonly List<Vector3> cleared = new();

        public float Radius => radius;
        /// <summary>Centres of every spot cleared this trip. Saved.</summary>
        public IReadOnlyList<Vector3> Cleared => cleared;

        void Awake()
        {
            terrain = Terrain.activeTerrain;
            if (terrain == null)
                return;
            originalTrees = terrain.terrainData.treeInstances;
            // detailPrototypes copies the array on every read, so count once.
            detailLayerCount = terrain.terrainData.detailPrototypes.Length;
        }

        void OnDestroy() => RestoreTerrain();
        void OnApplicationQuit() => RestoreTerrain();

        /// <summary>True if brush or trees cover the ground around <paramref name="position"/>.</summary>
        public bool HasBrush(Vector3 position, float within = 2.5f)
        {
            if (terrain == null || brushLayers == null)
                return false;
            TerrainData data = terrain.terrainData;
            foreach ((int x, int z) in CellsWithin(position, within))
                foreach (int layer in brushLayers)
                    if (layer < detailLayerCount && data.GetDetailLayer(x, z, 1, 1, layer)[0, 0] > 0)
                        return true;
            return TreesWithin(position, within).Count > 0;
        }

        /// <summary>How long clearing here would take, and how many trees it fells.</summary>
        public (float minutes, int trees) Estimate(Vector3 position)
        {
            int trees = TreesWithin(position, radius).Count;
            return (baseMinutes + trees * minutesPerTree, trees);
        }

        /// <summary>Clears the spot. With <paramref name="byHand"/>, it costs energy and gives firewood; without, it's being restored from a save.</summary>
        public void Clear(Vector3 position, Backpack backpack = null, Vitals vitals = null, bool byHand = true)
        {
            if (terrain == null)
                return;
            int felled = RemoveTrees(position);
            RemovePlants(position);
            cleared.Add(position);
            if (!byHand)
                return;

            vitals?.Exert(baseEnergy + felled * energyPerTree);
            if (felled > 0)
                backpack?.AddFirewood(felled * firewoodPerTree);
            string wood = felled == 0 ? "" : felled == 1
                ? $" You felled a sapling: +{firewoodPerTree} firewood."
                : $" You felled {felled} saplings: +{felled * firewoodPerTree} firewood.";
            Notifications.Post($"Campsite cleared.{wood}");
            TripLog.Note(felled > 0 ? "Hacked out a campsite in the woods." : "Cleared the brush for a campsite.");
        }

        // ---------- Trees ----------

        // Reading terrainData.treeInstances copies every tree on the map, so keep our own copy, bucketed into a grid.
        const float TreeCell = 10f;
        List<TreeInstance> trees;
        readonly Dictionary<long, List<int>> treeGrid = new();

        void IndexTrees()
        {
            trees ??= new List<TreeInstance>(originalTrees);
            treeGrid.Clear();
            for (int i = 0; i < trees.Count; i++)
            {
                Vector3 p = TreeWorldPosition(trees[i]);
                long key = CellKey(Mathf.FloorToInt(p.x / TreeCell), Mathf.FloorToInt(p.z / TreeCell));
                if (!treeGrid.TryGetValue(key, out List<int> bucket))
                    treeGrid[key] = bucket = new List<int>();
                bucket.Add(i);
            }
        }

        static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        Vector3 TreeWorldPosition(TreeInstance tree) => Vector3.Scale(tree.position, terrain.terrainData.size) + terrain.transform.position;

        /// <summary>Indices into <see cref="trees"/> of trees within the distance, in ascending order.</summary>
        List<int> TreesWithin(Vector3 position, float distance)
        {
            var found = new List<int>();
            if (terrain == null)
                return found;
            if (trees == null)
                IndexTrees();
            int x0 = Mathf.FloorToInt((position.x - distance) / TreeCell), x1 = Mathf.FloorToInt((position.x + distance) / TreeCell);
            int z0 = Mathf.FloorToInt((position.z - distance) / TreeCell), z1 = Mathf.FloorToInt((position.z + distance) / TreeCell);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (!treeGrid.TryGetValue(CellKey(x, z), out List<int> bucket))
                    continue;
                foreach (int i in bucket)
                {
                    Vector3 p = TreeWorldPosition(trees[i]);
                    float dx = p.x - position.x, dz = p.z - position.z;
                    if (dx * dx + dz * dz < distance * distance)
                        found.Add(i);
                }
            }
            found.Sort();
            return found;
        }

        int RemoveTrees(Vector3 position)
        {
            List<int> doomed = TreesWithin(position, radius);
            if (doomed.Count == 0)
                return 0;
            for (int i = doomed.Count - 1; i >= 0; i--)
                trees.RemoveAt(doomed[i]);
            terrain.terrainData.SetTreeInstances(trees.ToArray(), snapToHeightmap: false);
            IndexTrees();
            // A little more sky shows through now.
            ForestAtmosphere atmosphere = FindAnyObjectByType<ForestAtmosphere>();
            if (atmosphere != null)
                atmosphere.BuildCanopyGrid();
            return doomed.Count;
        }

        void RemovePlants(Vector3 position)
        {
            TerrainData data = terrain.terrainData;
            foreach ((int x, int z) in CellsWithin(position, radius))
            {
                for (int layer = 0; layer < detailLayerCount; layer++)
                {
                    int[,] cell = data.GetDetailLayer(x, z, 1, 1, layer);
                    if (cell[0, 0] == 0)
                        continue;
                    originalDetails.Add((layer, x, z, cell));
                    data.SetDetailLayer(x, z, layer, new int[1, 1]);
                }
            }
        }

        /// <summary>Detail cells whose centre lies within <paramref name="distance"/> of the position (at least the one underneath).</summary>
        IEnumerable<(int x, int z)> CellsWithin(Vector3 position, float distance)
        {
            TerrainData data = terrain.terrainData;
            Vector3 local = position - terrain.transform.position;
            float cellX = data.size.x / data.detailWidth, cellZ = data.size.z / data.detailHeight;
            int cx = Mathf.FloorToInt(local.x / cellX), cz = Mathf.FloorToInt(local.z / cellZ);
            int reach = Mathf.CeilToInt(distance / Mathf.Min(cellX, cellZ));
            for (int z = cz - reach; z <= cz + reach; z++)
            for (int x = cx - reach; x <= cx + reach; x++)
            {
                if (x < 0 || z < 0 || x >= data.detailWidth || z >= data.detailHeight)
                    continue;
                float dx = (x + 0.5f) * cellX - local.x, dz = (z + 0.5f) * cellZ - local.z;
                bool underneath = x == cx && z == cz;
                if (underneath || dx * dx + dz * dz <= distance * distance)
                    yield return (x, z);
            }
        }

        /// <summary>Puts every felled tree and cut plant back in the terrain data.</summary>
        void RestoreTerrain()
        {
            if (terrain == null || originalTrees == null)
                return;
            TerrainData data = terrain.terrainData;
            if (cleared.Count > 0)
                data.SetTreeInstances(originalTrees, snapToHeightmap: false);
            for (int i = originalDetails.Count - 1; i >= 0; i--)
            {
                (int layer, int x, int z, int[,] values) = originalDetails[i];
                data.SetDetailLayer(x, z, layer, values);
            }
            originalDetails.Clear();
            cleared.Clear();
            trees = null;
        }
    }
}
