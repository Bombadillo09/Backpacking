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

        [Header("Grass")]
        [Tooltip("A grass tuft texture with transparency.")]
        public Texture2D grassTexture;
        public Color grassHealthy = new(0.45f, 0.6f, 0.3f);
        public Color grassDry = new(0.65f, 0.6f, 0.35f);

        [Header("Density")]
        [Range(0f, 3f)] public float treeDensity = 1f;
        [Range(0f, 3f)] public float grassDensity = 1f;
        [Tooltip("Trees and grass further than this aren't drawn, in metres. Lower it if the frame rate struggles.")]
        public float treeDrawDistance = 700f;
        public float grassDrawDistance = 90f;
    }
}
