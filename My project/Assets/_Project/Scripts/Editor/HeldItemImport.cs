using UnityEditor;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Import settings for the held-item models in Art/Held (Poly Haven, CC0): static meshes, no animation or
    /// imported materials (HeldItemSetup makes URP ones), normal maps marked as such.
    /// </summary>
    public class HeldItemImport : AssetPostprocessor
    {
        const string Folder = "Assets/_Project/Art/Held/";

        public override uint GetVersion() => 1;

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder))
                return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.bakeAxisConversion = true;
            importer.isReadable = false;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder))
                return;
            var importer = (TextureImporter)assetImporter;
            if (assetPath.Contains("_nor_gl"))
                importer.textureType = TextureImporterType.NormalMap;
            else if (assetPath.Contains("_arm"))
            {
                importer.sRGBTexture = false;
                importer.isReadable = true;
            }
            importer.maxTextureSize = 1024;
        }
    }
}
