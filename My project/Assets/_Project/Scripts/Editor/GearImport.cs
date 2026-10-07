using UnityEditor;

namespace Backpacking.EditorTools
{
    /// <summary>Import settings for the fabric textures in Art/Gear: normal maps marked, packed maps linear.</summary>
    public class GearImport : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Project/Art/Gear/"))
                return;
            var importer = (TextureImporter)assetImporter;
            if (assetPath.Contains("_nor_gl"))
                importer.textureType = TextureImporterType.NormalMap;
            else if (assetPath.Contains("_arm"))
                importer.sRGBTexture = false;
            importer.maxTextureSize = 1024;
        }
    }
}
