using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// The art the scene builder dresses each biome with. Any slot left empty is filled with a generated
    /// placeholder on the next build, so real assets (e.g. from an Asset Store nature pack) can be swapped
    /// in one at a time: drag them in here, then run Backpacking > Build Prototype Scene.
    /// </summary>
    [CreateAssetMenu(menuName = "Backpacking/Biome Art Settings", fileName = "BiomeArt")]
    public class BiomeArtSettings : ScriptableObject
    {
        [Header("Trees (prefabs with a MeshRenderer or LODGroup on the root)")]
        [Tooltip("Broadleaf trees in the lowland forest.")]
        public GameObject[] lowlandTrees;
        [Tooltip("Conifers, taking over as you climb toward the treeline.")]
        public GameObject[] conifers;
        [Tooltip("Birch and willow along the valley floor.")]
        public GameObject[] valleyTrees;

        [Header("Ground Textures")]
        public TerrainLayer grass;
        public TerrainLayer dirt;
        public TerrainLayer rock;
        public TerrainLayer snow;
        public TerrainLayer forestFloor;
        public TerrainLayer alpineMeadow;
        [Tooltip("Patches of fallen leaves under broadleaf trees.")]
        public TerrainLayer leafLitter;
        [Tooltip("Patches of pine needles under conifers.")]
        public TerrainLayer needleLitter;

        [Header("Grass")]
        [Tooltip("A grass tuft texture with transparency.")]
        public Texture2D grassTexture;
        public Color grassHealthy = new(0.45f, 0.6f, 0.3f);
        public Color grassDry = new(0.65f, 0.6f, 0.35f);

        [Header("Ground Plants (optional; prefabs with a MeshFilter and MeshRenderer on the root)")]
        [Tooltip("Painted under the trees, e.g. fallen leaves.")]
        public GameObject[] forestFloorPlants;
        [Tooltip("Painted in meadows and along forest edges, e.g. wildflowers.")]
        public GameObject[] meadowPlants;
        [Tooltip("Shrubs and bushes painted between the trees.")]
        public GameObject[] understoryShrubs;
        [Tooltip("Broken branches and sticks littering the forest floor. Shrunk to a quarter to half size.")]
        public GameObject[] forestDebris;
        [Tooltip("Small stones half-buried in the forest floor. Shrunk to pebble size.")]
        public GameObject[] forestStones;

        [Header("Props (optional)")]
        [Tooltip("Boulders scattered on rocky ground along the route.")]
        public GameObject[] boulders;
        [Tooltip("Models for the firewood you pick up. Resized to branch size.")]
        public GameObject[] firewoodModels;
        [Tooltip("Fallen trunks lying in the forest. Uses the firewood models at full size if empty.")]
        public GameObject[] fallenLogs;

        [Header("Forest")]
        [Tooltip("Trees per 100 m² in full forest near the route, where you'll walk. Around 2 gives a closed canopy.")]
        [Range(0f, 6f)] public float forestDensity = 2.2f;
        [Tooltip("Trees per 100 m² in full forest far from the route. Lower keeps the total tree count manageable.")]
        [Range(0f, 6f)] public float remoteForestDensity = 0.5f;
        [Tooltip("Width in metres of the dense band either side of the route.")]
        public float denseForestWidth = 450f;

        [Header("Size & Density")]
        [Tooltip("Multiplies the size of every tree.")]
        [Range(0.3f, 3f)] public float treeScale = 1f;
        [Tooltip("Multiplies all tree densities.")]
        [Range(0f, 3f)] public float treeDensity = 1f;
        [Range(0f, 3f)] public float grassDensity = 1f;
        [Range(0f, 3f)] public float plantDensity = 1f;
        [Range(0f, 3f)] public float boulderDensity = 1f;
        [Tooltip("Trees and grass further than this aren't drawn, in metres. Lower it if the frame rate struggles.")]
        public float treeDrawDistance = 700f;
        public float grassDrawDistance = 90f;
    }
}
