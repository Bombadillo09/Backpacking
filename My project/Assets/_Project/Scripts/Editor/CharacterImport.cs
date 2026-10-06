using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Import settings for the characters in Assets/_Project/Art/Characters:
    /// <list type="bullet">
    /// <item>Rocketbox people (Rocketbox/): Humanoid rigs built from their 3ds Max Biped skeletons, readable meshes
    /// (bare feet and footwear are cut from them at runtime), no imported materials (the character setup makes
    /// URP ones), and the legs and collarbones re-parented the way Unity's humanoid rig expects.</item>
    /// <item>The Quaternius animation library (Animations/): a Humanoid rig so the clips retarget onto anyone,
    /// every clip ending in _Loop looped, root motion locked.</item>
    /// </list>
    /// </summary>
    public class CharacterImport : AssetPostprocessor
    {
        const string Folder = "Assets/_Project/Art/Characters/";
        const string RocketboxFolder = Folder + "Rocketbox/";

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
        /// Before the avatar is made: Biped puts the thighs under the first spine bone and the collarbones under
        /// the neck. Humanoid retargeting expects thighs under the pelvis and collarbones under the chest, or the
        /// legs swing with the spine. Moving them keeps their world placement, so the skinning is unchanged.
        /// </summary>
        void OnPostprocessMeshHierarchy(GameObject root)
        {
            if (!assetPath.StartsWith(RocketboxFolder))
                return;
            if (root.name.ToLowerInvariant().Contains("poly") && !root.name.ToLowerInvariant().Contains("hipoly"))
                root.SetActive(false);
            Transform pelvis = Find(root.transform, "Bip01 Pelvis");
            Transform spine2 = Find(root.transform, "Bip01 Spine2");
            if (pelvis == null || spine2 == null)
                return;
            foreach (string thigh in new[] { "Bip01 L Thigh", "Bip01 R Thigh" })
                Find(root.transform, thigh)?.SetParent(pelvis, true);
            foreach (string clavicle in new[] { "Bip01 L Clavicle", "Bip01 R Clavicle" })
                Find(root.transform, clavicle)?.SetParent(spine2, true);
        }

        static Transform Find(Transform parent, string name)
        {
            if (parent.name == name)
                return parent;
            foreach (Transform child in parent)
            {
                Transform found = Find(child, name);
                if (found != null)
                    return found;
            }
            return null;
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
