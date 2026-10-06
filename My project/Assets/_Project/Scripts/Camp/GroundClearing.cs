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
        [Tooltip("Terrain detail layers of fallen sticks and branches, which can be gathered for firewood. Set by the scene builder.")]
        [SerializeField] int[] deadfallLayers;
        [Tooltip("How much each brush layer thickens the going, in the same order (shrubs count more than leaf litter).")]
        [SerializeField] float[] brushWeights;
        [Tooltip("Weighted plants in a cell that make it as thick as it gets.")]
        [SerializeField] float fullBrush = 2.5f;
        [SerializeField] float radius = 4f;
        [Tooltip("Reach of one swing of the machete, in metres.")]
        [SerializeField] float chopRadius = 1.6f;
        [SerializeField] float chopEnergy = 0.5f;
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
        // xyz is the centre, w the radius.
        readonly List<Vector4> cleared = new();

        public float Radius => radius;
        /// <summary>Every spot cleared or chopped this trip: centre in xyz, radius in w. Saved.</summary>
        public IReadOnlyList<Vector4> Cleared => cleared;

        void Awake()
        {
            terrain = Terrain.activeTerrain;
            if (terrain == null)
                return;
            originalTrees = terrain.terrainData.treeInstances;
            // detailPrototypes copies the array on every read, so count once.
            detailLayerCount = terrain.terrainData.detailPrototypes.Length;
            // The sticks are part of the terrain, so the terrain is what you look at to gather them.
            terrain.gameObject.AddComponent<TerrainDeadfall>().Clearing = this;
        }

        /// <summary>How many fallen sticks lie within reach of a point on the ground.</summary>
        public int DeadfallNear(Vector3 position, float within = GatherReach)
        {
            if (terrain == null || deadfallLayers == null)
                return 0;
            TerrainData data = terrain.terrainData;
            int sticks = 0;
            foreach ((int x, int z) in CellsWithin(position, within))
                foreach (int layer in deadfallLayers)
                    if (layer < detailLayerCount)
                        sticks += data.GetDetailLayer(x, z, 1, 1, layer)[0, 0];
            return sticks;
        }

        /// <summary>Picks up the fallen sticks around a point. Returns how many there were. Saved like a machete cut.</summary>
        public int GatherDeadfall(Vector3 position, float within = GatherReach)
        {
            if (terrain == null || deadfallLayers == null)
                return 0;
            int gathered = RemovePlants(position, within, deadfallLayers);
            if (gathered > 0)
                // A negative radius marks a spot where only the sticks were taken.
                cleared.Add(new Vector4(position.x, position.y, position.z, -within));
            return gathered;
        }

        /// <summary>How far round the spot you look at your hands gather sticks, in metres.</summary>
        public const float GatherReach = 0.9f;

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

        /// <summary>How thick the undergrowth is right here, 0 (open) to 1 (as thick as it gets).</summary>
        public float BrushDensity(Vector3 position)
        {
            if (terrain == null || brushLayers == null)
                return 0f;
            TerrainData data = terrain.terrainData;
            (int x, int z) = CellAt(position);
            if (x < 0 || z < 0 || x >= data.detailWidth || z >= data.detailHeight)
                return 0f;
            float weighted = 0f;
            for (int i = 0; i < brushLayers.Length; i++)
            {
                int layer = brushLayers[i];
                if (layer >= detailLayerCount)
                    continue;
                float weight = brushWeights != null && i < brushWeights.Length ? brushWeights[i] : 1f;
                weighted += data.GetDetailLayer(x, z, 1, 1, layer)[0, 0] * weight;
            }
            return Mathf.Clamp01(weighted / fullBrush);
        }

        /// <summary>
        /// One swing of the machete: cuts the brush around a point. Returns how many plants were cut.
        /// Costs a little energy whether or not it hit anything.
        /// </summary>
        public int Chop(Vector3 position, Vitals vitals)
        {
            if (terrain == null)
                return 0;
            vitals?.Exert(chopEnergy);
            int cut = RemovePlants(position, chopRadius);
            if (cut > 0)
                cleared.Add(new Vector4(position.x, position.y, position.z, chopRadius));
            return cut;
        }

        /// <summary>Re-applies a spot from a save: a full campsite (felling trees) or a machete cut.</summary>
        public void Restore(Vector4 spot)
        {
            var position = new Vector3(spot.x, spot.y, spot.z);
            if (spot.w < 0f)
            {
                RemovePlants(position, -spot.w, deadfallLayers);
                cleared.Add(spot);
                return;
            }
            // Older saves stored campsites without a radius.
            float spotRadius = spot.w > 0f ? spot.w : radius;
            if (spotRadius >= radius)
                Clear(position, byHand: false);
            else
            {
                RemovePlants(position, spotRadius);
                cleared.Add(spot);
            }
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
            RemovePlants(position, radius);
            cleared.Add(new Vector4(position.x, position.y, position.z, radius));
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

        /// <summary>Removes the plants of the given layers (all of them if null) around a point. Returns how many.</summary>
        int RemovePlants(Vector3 position, float within, int[] layers = null)
        {
            if (layers == null)
            {
                layers = new int[detailLayerCount];
                for (int i = 0; i < layers.Length; i++)
                    layers[i] = i;
            }
            TerrainData data = terrain.terrainData;
            int removed = 0;
            foreach ((int x, int z) in CellsWithin(position, within))
            {
                foreach (int layer in layers)
                {
                    if (layer >= detailLayerCount)
                        continue;
                    int[,] cell = data.GetDetailLayer(x, z, 1, 1, layer);
                    if (cell[0, 0] == 0)
                        continue;
                    originalDetails.Add((layer, x, z, cell));
                    removed += cell[0, 0];
                    data.SetDetailLayer(x, z, layer, new int[1, 1]);
                }
            }
            return removed;
        }

        (int x, int z) CellAt(Vector3 position)
        {
            TerrainData data = terrain.terrainData;
            Vector3 local = position - terrain.transform.position;
            return (Mathf.FloorToInt(local.x / (data.size.x / data.detailWidth)), Mathf.FloorToInt(local.z / (data.size.z / data.detailHeight)));
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
