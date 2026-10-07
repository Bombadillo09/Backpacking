using System.Collections.Generic;
using System.IO;
using Backpacking.Character;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Builds the <see cref="CharacterLibrary"/> from the Microsoft Rocketbox people in Art/Characters/Rocketbox
    /// (URP materials for each person's body, head and hair), and an animator from the Quaternius animation
    /// library that blends idle, walk, jog and sprint, crouching, jumping, sitting, kneeling for camp tasks and a
    /// swing for the machete. Run by the scene builder; safe to run again.
    /// </summary>
    public static class CharacterSetup
    {
        const string ArtFolder = "Assets/_Project/Art/Characters";
        const string RocketboxFolder = ArtFolder + "/Rocketbox";
        const string OutputFolder = "Assets/_Project/Generated/Characters";
        const string LibraryPath = "Assets/_Project/Settings/CharacterLibrary.asset";

        static readonly Color Light = Color.white, Brown = new(0.62f, 0.45f, 0.36f), Dark = new(0.42f, 0.3f, 0.24f);

        /// <summary>The roster: outdoorsy outfits, with where their footwear ends and their skin for bare feet.</summary>
        static readonly (string id, string label, bool female, float footwearTop, Color skin)[] Roster =
        {
            ("Male_Adult_05", "Field vest", false, 0.12f, Light),
            ("Male_Adult_04", "Black jacket", false, 0.11f, Brown),
            ("Male_Adult_07", "Wool jacket", false, 0.1f, Light),
            ("Male_Adult_12", "Denim jacket", false, 0.1f, Dark),
            ("Male_Adult_18", "Grey hoodie", false, 0.1f, Dark),
            ("Wood_Male_01", "Work shirt and cap", false, 0.12f, Brown),
            ("Female_Adult_04", "Leather jacket", true, 0.42f, Light),
            ("Female_Adult_07", "Brown jacket", true, 0.42f, Light),
            ("Female_Adult_12", "Hoodie and shorts", true, 0.11f, Light),
            ("Female_Adult_13", "Waistcoat", true, 0.12f, Light),
            ("Female_Adult_14", "Cardigan", true, 0.4f, Light),
            ("Female_Adult_17", "Green tee", true, 0.08f, Light),
        };

        /// <summary>
        /// Saves the import settings <see cref="CharacterImport"/> applies into each Rocketbox model's .meta and
        /// re-imports any model imported without them (e.g. before the importer knew about Rocketbox). Without
        /// Read/Write the game can't cut the head or feet from the meshes; Unity only lets the editor read them.
        /// </summary>
        static void EnsureImported()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { RocketboxFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                    continue;
                if (importer.isReadable && importer.materialImportMode == ModelImporterMaterialImportMode.None
                    && importer.animationType == ModelImporterAnimationType.Human && importer.bakeAxisConversion)
                    continue;
                importer.isReadable = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.bakeAxisConversion = true;
                importer.SaveAndReimport();
            }
        }

        public static CharacterLibrary GetOrCreateLibrary()
        {
            if (!AssetDatabase.IsValidFolder(RocketboxFolder))
            {
                Debug.LogWarning($"Character models not found in {RocketboxFolder}; the player will have no body.");
                return null;
            }
            EnsureImported();
            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Generated", "Characters");

            var library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CharacterLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var hikers = new List<CharacterLibrary.Hiker>();
            foreach ((string id, string label, bool female, float footwearTop, Color skin) in Roster)
            {
                GameObject model = Model(id);
                if (model == null)
                {
                    Debug.LogWarning($"Rocketbox model {id} is missing from {RocketboxFolder}; it's left out of the roster.");
                    continue;
                }
                hikers.Add(new CharacterLibrary.Hiker
                {
                    id = id,
                    label = label,
                    female = female,
                    model = model,
                    avatar = TPoseAvatar(id, model),
                    body = PersonMaterial(id, "body", 0.25f),
                    head = PersonMaterial(id, "head", 0.35f),
                    hair = HairMaterial(id),
                    footwearTop = footwearTop,
                    skinTint = skin,
                });
            }
            library.hikers = hikers.ToArray();
            library.maleFeet = Model("Sports_Male_01");
            library.femaleFeet = Model("Sports_Female_01");
            library.maleFeetSkin = PersonMaterial("Sports_Male_01", "body", 0.3f);
            library.femaleFeetSkin = PersonMaterial("Sports_Female_01", "body", 0.3f);
            library.pack = GetOrCreateMaterial("Pack", null, null, 0.2f);
            library.socks = GetOrCreateMaterial("Socks", null, null, 0.05f);
            library.animator = BuildAnimator();
            library.gear = GearSetup.GetOrCreateLibrary();

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        /// <summary>
        /// A humanoid avatar for one person, defined from a proper T-pose, saved next to their materials.
        /// Rocketbox people stand in an A-pose with the hands turned; Unity measures each bone's zero from the
        /// pose it's given, so the imported avatar retargets every animation with the hands twisted round the
        /// forearm (thumbs out, fingers curling the wrong way). This does what the avatar editor's "Enforce
        /// T-Pose" does for the arms: straight out to the sides, palms down with the thumbs forward. (The legs are
        /// left as modelled; straightening them turned the hips round.)
        /// </summary>
        static Avatar TPoseAvatar(string id, GameObject model)
        {
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(model)) is not ModelImporter importer)
                return null;
            GameObject instance = Object.Instantiate(model);
            instance.name = model.name;
            try
            {
                var animator = instance.GetComponent<Animator>();
                if (animator == null || !animator.isHuman)
                    return null;
                Transform Bone(HumanBodyBones bone) => animator.GetBoneTransform(bone);

                // Start from the rest pose the importer built its avatar from (the scene copy isn't quite the same).
                HumanDescription description = importer.humanDescription;
                SkeletonBone[] skeleton = description.skeleton;
                var transforms = new Dictionary<string, Transform>();
                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                    transforms.TryAdd(t.name, t);
                for (int b = 1; b < skeleton.Length; b++)
                    if (transforms.TryGetValue(skeleton[b].name, out Transform t))
                    {
                        t.localPosition = skeleton[b].position;
                        t.localRotation = skeleton[b].rotation;
                        t.localScale = skeleton[b].scale;
                    }

                foreach ((float side, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand, HumanBodyBones index, HumanBodyBones middle, HumanBodyBones little) in new[]
                         {
                             (-1f, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftIndexProximal,
                                 HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftLittleProximal),
                             (1f, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightIndexProximal,
                                 HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightLittleProximal),
                         })
                {
                    Vector3 outward = new(side, 0f, 0f);
                    Aim(Bone(upper), Bone(lower), outward);
                    Aim(Bone(lower), Bone(hand), outward);
                    Aim(Bone(hand), Bone(middle), outward);
                    // Palm down: turn the hand about the arm until the index side (the thumb side) faces forward.
                    Transform h = Bone(hand), i = Bone(index), l = Bone(little);
                    if (h != null && i != null && l != null)
                    {
                        Vector3 across = Vector3.ProjectOnPlane(i.position - l.position, outward);
                        h.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(across, Vector3.forward, outward), outward) * h.rotation;
                    }
                }
                // Write back only the turned arm bones.
                for (int b = 1; b < skeleton.Length; b++)
                    if (transforms.TryGetValue(skeleton[b].name, out Transform t))
                        skeleton[b].rotation = t.localRotation;
                description.skeleton = skeleton;
                Avatar avatar = AvatarBuilder.BuildHumanAvatar(instance, description);
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                {
                    Debug.LogWarning($"Couldn't build a T-pose avatar for {id}; using the imported one.");
                    return null;
                }
                avatar.name = $"{id} T-Pose";

                string path = $"{OutputFolder}/Rocketbox/{id}_Avatar.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Avatar>(path);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(avatar, path);
                    return avatar;
                }
                EditorUtility.CopySerialized(avatar, existing);
                Object.DestroyImmediate(avatar);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Turns a bone so its child lies in the given direction from it.</summary>
        static void Aim(Transform bone, Transform child, Vector3 direction)
        {
            if (bone == null || child == null)
                return;
            bone.rotation = Quaternion.FromToRotation(child.position - bone.position, direction) * bone.rotation;
        }

        static GameObject Model(string id) => AssetDatabase.LoadAssetAtPath<GameObject>($"{RocketboxFolder}/{id}/{id}.fbx");

        /// <summary>A texture of one person by its part and kind, e.g. ("body", "color") finds m009_body_color.png.</summary>
        static Texture2D PersonTexture(string id, string part, string kind)
        {
            string folder = $"{RocketboxFolder}/{id}/Textures";
            if (!AssetDatabase.IsValidFolder(folder))
                return null;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path).EndsWith($"_{part}_{kind}"))
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }

        static Material PersonMaterial(string id, string part, float smoothness) =>
            GetOrCreateMaterial($"Rocketbox/{id}_{part}", PersonTexture(id, part, "color"), PersonTexture(id, part, "normal"), smoothness);

        /// <summary>Hair and eyelash cards: cut out by the opacity texture's alpha and drawn from both sides.</summary>
        static Material HairMaterial(string id)
        {
            Texture2D cards = PersonTexture(id, "opacity", "color");
            if (cards == null)
                return null;
            Material material = GetOrCreateMaterial($"Rocketbox/{id}_hair", cards, null, 0.3f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.45f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetFloat("_Cull", 0f);
            material.doubleSidedGI = true;
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material GetOrCreateMaterial(string name, Texture2D baseMap, Texture2D normal, float smoothness, Color? colour = null)
        {
            string path = $"{OutputFolder}/{name}.mat";
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", colour ?? Color.white);
            material.SetFloat("_Smoothness", smoothness);
            material.SetTexture("_BumpMap", normal);
            if (normal != null)
                material.EnableKeyword("_NORMALMAP");
            else
                material.DisableKeyword("_NORMALMAP");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------- Animator ----------

        static RuntimeAnimatorController BuildAnimator()
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath($"{ArtFolder}/Animations/UAL1_Standard.fbx"))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    clips[clip.name.Substring(clip.name.LastIndexOf('|') + 1)] = clip; // takes are named "Armature|Idle_Loop"
            AnimationClip Clip(string name)
            {
                if (clips.TryGetValue(name, out AnimationClip clip))
                    return clip;
                Debug.LogWarning($"Animation {name} not found in UAL1_Standard.fbx; the hiker will hold still in that state.");
                return null;
            }

            string path = $"{OutputFolder}/Hiker.controller";
            AssetDatabase.DeleteAsset(path);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            controller.AddParameter(new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true });
            controller.AddParameter("Busy", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Swing", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Seated", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            // Thresholds are ground speeds in m/s, matching the controller's walk, jog and sprint.
            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree walking, 0);
            walking.blendParameter = "Speed";
            walking.useAutomaticThresholds = false;
            walking.AddChild(Clip("Idle_Loop"), 0f);
            walking.AddChild(Clip("Walk_Loop"), 2.2f);
            walking.AddChild(Clip("Jog_Fwd_Loop"), 3.4f);
            walking.AddChild(Clip("Sprint_Loop"), 4.6f);

            AnimatorState crouching = controller.CreateBlendTreeInController("Crouch", out BlendTree crouchTree, 0);
            crouchTree.blendParameter = "Speed";
            crouchTree.useAutomaticThresholds = false;
            crouchTree.AddChild(Clip("Crouch_Idle_Loop"), 0f);
            crouchTree.AddChild(Clip("Crouch_Fwd_Loop"), 1.1f);

            AnimatorState air = machine.AddState("Air");
            air.motion = Clip("Jump_Loop");
            AnimatorState kneel = machine.AddState("Kneel");
            kneel.motion = Clip("Fixing_Kneeling");
            AnimatorState seated = machine.AddState("Seated");
            seated.motion = Clip("Sitting_Idle_Loop");
            AnimatorState swing = machine.AddState("Swing");
            swing.motion = Clip("Sword_Attack");
            swing.speed = 1.4f;
            machine.defaultState = locomotion;

            Transition(locomotion, crouching, 0.2f).AddCondition(AnimatorConditionMode.If, 0f, "Crouch");
            Transition(crouching, locomotion, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouch");

            AnyTransition(machine, air, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            Transition(air, locomotion, 0.15f).AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            AnyTransition(machine, kneel, 0.3f).AddCondition(AnimatorConditionMode.If, 0f, "Busy");
            Transition(kneel, locomotion, 0.3f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Busy");

            AnyTransition(machine, seated, 0.4f).AddCondition(AnimatorConditionMode.If, 0f, "Seated");
            Transition(seated, locomotion, 0.4f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Seated");

            AnyTransition(machine, swing, 0.05f).AddCondition(AnimatorConditionMode.If, 0f, "Swing");
            AnimatorStateTransition back = swing.AddTransition(locomotion);
            back.hasExitTime = true;
            back.exitTime = 0.8f;
            back.duration = 0.15f;

            // Feet stay planted where the clips put them, and HikerPose adjusts the body after the clips
            // (sitting on the ground, looking around, feet on slopes).
            foreach (ChildAnimatorState child in machine.states)
                child.state.iKOnFeet = true;
            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = duration;
            return transition;
        }

        static AnimatorStateTransition AnyTransition(AnimatorStateMachine machine, AnimatorState to, float duration)
        {
            AnimatorStateTransition transition = machine.AddAnyStateTransition(to);
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
            return transition;
        }
    }
}
