using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Wildlife
{
    /// <summary>
    /// Where the terrain's trees are, for animals: squirrels run up the nearest trunk, birds perch in the nearest
    /// crowns. Built once from the terrain's tree instances into a coarse grid.
    /// </summary>
    public static class TreeIndex
    {
        public struct Tree
        {
            public Vector3 position;
            public float height;
            public float trunkRadius;
        }

        const float Cell = 16f;
        static Dictionary<Vector2Int, List<Tree>> grid;
        static Terrain indexed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            grid = null;
            indexed = null;
        }

        static void Build()
        {
            Terrain terrain = Terrain.activeTerrain;
            grid = new Dictionary<Vector2Int, List<Tree>>();
            indexed = terrain;
            if (terrain == null)
                return;
            TerrainData data = terrain.terrainData;
            TreePrototype[] prototypes = data.treePrototypes;
            var heights = new float[prototypes.Length];
            for (int i = 0; i < prototypes.Length; i++)
                heights[i] = PrototypeHeight(prototypes[i].prefab);
            foreach (TreeInstance instance in data.treeInstances)
            {
                Vector3 position = Vector3.Scale(instance.position, data.size) + terrain.transform.position;
                float height = heights[instance.prototypeIndex] * instance.heightScale;
                // Small saplings and bushes aren't worth climbing or perching in.
                if (height < 3f)
                    continue;
                var tree = new Tree
                {
                    position = position,
                    height = height,
                    trunkRadius = Mathf.Clamp(height * 0.012f * instance.widthScale, 0.08f, 0.45f),
                };
                Vector2Int key = Key(position);
                if (!grid.TryGetValue(key, out List<Tree> cell))
                    grid[key] = cell = new List<Tree>();
                cell.Add(tree);
            }
        }

        /// <summary>A tree prototype's height at scale 1, from its meshes (all LODs share a height).</summary>
        static float PrototypeHeight(GameObject prefab)
        {
            if (prefab == null)
                return 10f;
            float top = 0f;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                    top = Mathf.Max(top, filter.sharedMesh.bounds.max.y * filter.transform.lossyScale.y);
            return top > 0.5f ? top : 10f;
        }

        static Vector2Int Key(Vector3 position) => new(Mathf.FloorToInt(position.x / Cell), Mathf.FloorToInt(position.z / Cell));

        /// <summary>The nearest tree to <paramref name="position"/> within <paramref name="radius"/> metres.</summary>
        public static bool Nearest(Vector3 position, float radius, out Tree nearest)
        {
            nearest = default;
            float best = radius * radius;
            bool found = false;
            foreach (Tree tree in Near(position, radius))
            {
                Vector3 offset = tree.position - position;
                offset.y = 0f;
                if (offset.sqrMagnitude < best)
                {
                    best = offset.sqrMagnitude;
                    nearest = tree;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>A random tree within <paramref name="radius"/> metres, preferring ones further than <paramref name="minimum"/>.</summary>
        public static bool Random(Vector3 position, float minimum, float radius, out Tree chosen)
        {
            chosen = default;
            int seen = 0;
            foreach (Tree tree in Near(position, radius))
            {
                Vector3 offset = tree.position - position;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance > radius || distance < minimum)
                    continue;
                // Reservoir sampling: each tree in range equally likely.
                seen++;
                if (UnityEngine.Random.Range(0, seen) == 0)
                    chosen = tree;
            }
            return seen > 0;
        }

        static IEnumerable<Tree> Near(Vector3 position, float radius)
        {
            if (grid == null || indexed != Terrain.activeTerrain)
                Build();
            Vector2Int min = Key(position - new Vector3(radius, 0f, radius));
            Vector2Int max = Key(position + new Vector3(radius, 0f, radius));
            for (int x = min.x; x <= max.x; x++)
            for (int z = min.y; z <= max.y; z++)
                if (grid.TryGetValue(new Vector2Int(x, z), out List<Tree> cell))
                    foreach (Tree tree in cell)
                        yield return tree;
        }
    }
}
