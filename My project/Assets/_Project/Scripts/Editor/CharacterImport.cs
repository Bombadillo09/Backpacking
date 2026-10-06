using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Import settings for the Quaternius characters (Assets/_Project/Art/Characters), following the pack's
    /// Unity setup notes: bake the axis conversion, Humanoid rigs for the bodies and the animation library
    /// (so the animations retarget onto either body), loop every clip ending in _Loop, and mark normal maps.
    /// </summary>
    public class CharacterImport : AssetPostprocessor
    {
        const string Folder = "Assets/_Project/Art/Characters/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder))
                return;
            var importer = (ModelImporter)assetImporter;
            importer.bakeAxisConversion = true;
            importer.globalScale = 1f;

            bool hair = assetPath.Contains("/Hair/");
            bool animations = assetPath.Contains("/Animations/");
            // Hair is skinned to the same skeleton; it's attached to a body by bone name, so it needs no avatar.
            importer.animationType = hair ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.Human;
            importer.avatarSetup = hair ? ModelImporterAvatarSetup.NoAvatar : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = animations;
            importer.optimizeGameObjects = false;
            // The clothes and the first-person body are cut from the body mesh at runtime.
            importer.isReadable = assetPath.Contains("/Bodies/");
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Folder + "Animations/"))
                return;
            var importer = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                bool loop = clip.name.EndsWith("_Loop") || clip.name == "Fixing_Kneeling";
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
            if (assetPath.EndsWith("_Normal.png"))
                importer.textureType = TextureImporterType.NormalMap;
            else if (assetPath.Contains("_Roughness"))
                importer.sRGBTexture = false;
            importer.maxTextureSize = 2048;
        }
    }
}
