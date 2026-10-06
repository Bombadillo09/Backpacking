using System.IO;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Turns the downloaded ground textures in Assets/_Project/Art/Ground into terrain layers and puts them
    /// into the biome art settings. Each texture lives in its own folder as &lt;name&gt;_diffuse.jpg and
    /// &lt;name&gt;_normal.jpg (OpenGL-style normals, which is what Unity expects).
    /// </summary>
    public static class GroundTextureSetup
    {
        const string GroundFolder = "Assets/_Project/Art/Ground";

        /// <summary>Which download fills which ground slot, and how many metres one tile covers.</summary>
        static readonly (string slot, string folder, float tileMetres)[] Assignments =
        {
            ("grass", "leafy_grass", 3f),
            ("dirt", "dirt_floor", 3f),
            // An aerial capture of a 50 m area; shrunk so the rock reads at walking distance.
            ("rock", "aerial_rocks_02", 12f),
            ("snow", "snow_02", 4f),
            ("forestFloor", "forrest_ground_01", 3f),
            ("alpineMeadow", "withered_grass", 3f),
            ("leafLitter", "forest_leaves_02", 3f),
            ("needleLitter", "forest_ground_04", 3f),
        };

        [MenuItem("Backpacking/Use Downloaded Ground Textures")]
        public static void Apply()
        {
            BiomeArtSettings art = AssetDatabase.LoadAssetAtPath<BiomeArtSettings>("Assets/_Project/Settings/BiomeArt.asset");
            if (art == null)
            {
                EditorUtility.DisplayDialog("Biome art not found",
                    "Run Backpacking > Build Prototype Scene once first, so the biome art settings exist.", "OK");
                return;
            }

            var serialized = new SerializedObject(art);
            int applied = 0;
            foreach ((string slot, string folder, float tileMetres) in Assignments)
            {
                TerrainLayer layer = CreateLayer(folder, tileMetres);
                if (layer == null)
                {
                    Debug.LogWarning($"Ground texture '{folder}' is missing from {GroundFolder}; keeping the current {slot} layer.");
                    continue;
                }
                serialized.FindProperty(slot).objectReferenceValue = layer;
                applied++;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"Applied {applied} downloaded ground textures to the biome art settings.");

            if (EditorUtility.DisplayDialog("Ground textures ready",
                    $"{applied} ground layers now use the downloaded textures. Rebuild the prototype scene to see them?",
                    "Rebuild now", "Later"))
                PrototypeSceneBuilder.Build();
        }

        static TerrainLayer CreateLayer(string folder, float tileMetres)
        {
            string directory = $"{GroundFolder}/{folder}";
            string diffusePath = $"{directory}/{folder}_diffuse.jpg";
            string normalPath = $"{directory}/{folder}_normal.jpg";
            if (!File.Exists(diffusePath))
                return null;

            ConfigureImporter(diffusePath, normalMap: false);
            bool hasNormal = File.Exists(normalPath);
            if (hasNormal)
                ConfigureImporter(normalPath, normalMap: true);

            string layerPath = $"{directory}/{folder}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, layerPath);
            }
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(diffusePath);
            layer.normalMapTexture = hasNormal ? AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) : null;
            layer.normalScale = 1f;
            layer.tileSize = new Vector2(tileMetres, tileMetres);
            layer.smoothness = 0.05f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        static void ConfigureImporter(string path, bool normalMap)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
                return;
            bool changed = false;
            TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type)
            {
                importer.textureType = type;
                changed = true;
            }
            if (importer.anisoLevel != 4)
            {
                importer.anisoLevel = 4;
                changed = true;
            }
            if (changed)
                importer.SaveAndReimport();
        }
    }

    /// <summary>Imports ground normal maps as normal maps as soon as they land in the project.</summary>
    class GroundTexturePostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Project/Art/Ground/"))
                return;
            var importer = (TextureImporter)assetImporter;
            if (Path.GetFileNameWithoutExtension(assetPath).EndsWith("_normal"))
                importer.textureType = TextureImporterType.NormalMap;
            importer.anisoLevel = 4;
        }
    }
}
