using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Import settings for the characters in Assets/_Project/Art/Characters:
    /// <list type="bullet">
    /// <item>Rocketbox people (Rocketbox/): Humanoid rigs built from their 3ds Max Biped skeletons, readable meshes
    /// (bare feet and footwear are cut from them at runtime), no imported materials (the character setup makes
    /// URP ones).</item>
    /// <item>The Quaternius animation library (Animations/): a Humanoid rig so the clips retarget onto anyone,
    /// every clip ending in _Loop looped, root motion locked.</item>
    /// </list>
    /// </summary>
    public class CharacterImport : AssetPostprocessor
    {
        const string Folder = "Assets/_Project/Art/Characters/";
        const string RocketboxFolder = Folder + "Rocketbox/";

        // Bump when these rules change, so Unity re-imports the characters with them.
        public override uint GetVersion() => 3;

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder))
                return;
            var importer = (ModelImporter)assetImporter;
            importer.bakeAxisConversion = true;
            importer.globalScale = 1f;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = assetPath.StartsWith(Folder + "Animations/");
            importer.optimizeGameObjects = false;
            bool person = assetPath.StartsWith(RocketboxFolder);
            importer.isReadable = person;
            if (person)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importBlendShapes = false;
            }
        }

        /// <summary>
        /// Rocketbox models carry four levels of detail; only the finest is used. The Biped skeleton is left as it
        /// is: Unity's humanoid mapping handles it, and its avatar description records that hierarchy, so moving
        /// bones here makes avatar creation fail.
        /// </summary>
        void OnPostprocessMeshHierarchy(GameObject root)
        {
            if (assetPath.StartsWith(RocketboxFolder) && root.name.ToLowerInvariant().Contains("poly") && !root.name.ToLowerInvariant().Contains("hipoly"))
                root.SetActive(false);
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Folder + "Animations/"))
                return;
            var importer = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                bool loop = clip.name.EndsWith("_Loop") || clip.name.EndsWith("Fixing_Kneeling");
                clip.loopTime = loop;
                clip.loopPose = loop;
                // Movement comes from the character controller, not the animation.
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder))
                return;
            var importer = (TextureImporter)assetImporter;
            string name = assetPath.ToLowerInvariant();
            if (name.Contains("_normal"))
                importer.textureType = TextureImporterType.NormalMap;
            else if (name.Contains("_specular"))
                importer.sRGBTexture = false;
            else if (name.Contains("_opacity"))
                importer.alphaIsTransparency = true;
            importer.maxTextureSize = 1024;
        }
    }
}
