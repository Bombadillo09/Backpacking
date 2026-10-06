using System.Collections.Generic;
using Backpacking.Character;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Builds the <see cref="CharacterLibrary"/> from the Quaternius models: materials for skin, hair, eyes and
    /// clothing, and an animator that blends idle, walk, jog and sprint, crouching, jumping, kneeling for
    /// camp tasks and a swing for the machete. Run by the scene builder; safe to run again.
    /// </summary>
    public static class CharacterSetup
    {
        const string ArtFolder = "Assets/_Project/Art/Characters";
        const string OutputFolder = "Assets/_Project/Generated/Characters";
        const string LibraryPath = "Assets/_Project/Settings/CharacterLibrary.asset";

        /// <summary>
        /// Re-imports any character file imported before <see cref="CharacterImport"/> existed, so it picks up
        /// the right rig and settings.
        /// </summary>
        static void EnsureImported()
        {
            foreach (string guid in AssetDatabase.FindAssets("", new[] { ArtFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AssetImporter importer = AssetImporter.GetAtPath(path);
                bool stale = importer switch
                {
                    ModelImporter model => !model.bakeAxisConversion || (path.Contains("/Bodies/") && !model.isReadable),
                    TextureImporter texture => path.EndsWith("_Normal.png") && texture.textureType != TextureImporterType.NormalMap,
                    _ => false,
                };
                if (stale)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        public static CharacterLibrary GetOrCreateLibrary()
        {
            EnsureImported();
            var maleBody = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArtFolder}/Bodies/Superhero_Male_FullBody.fbx");
            if (maleBody == null)
            {
                Debug.LogWarning($"Character models not found in {ArtFolder}; the player will have no body.");
                return null;
            }
            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Generated", "Characters");

            var library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CharacterLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            library.maleBody = maleBody;
            library.femaleBody = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArtFolder}/Bodies/Superhero_Female_FullBody.fbx");
            library.hairStyles = new[]
            {
                new CharacterLibrary.HairStyle { name = "Shaved" },
                Hair("Short", "Hair_SimpleParted", true, true),
                Hair("Buzz cut", "Hair_Buzzed", true, false),
                Hair("Buzz cut", "Hair_BuzzedFemale", false, true),
                Hair("Long", "Hair_Long", true, true),
                Hair("Buns", "Hair_Buns", true, true),
            };
            library.beard = Model("Hair_Beard");

            Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtFolder}/Textures/{name}.png");
            library.maleSkin = GetOrCreateMaterial("Skin_Male", Tex("T_Superhero_Male_Light"), Tex("T_Superhero_Male_Normal"), 0.35f);
            library.femaleSkin = GetOrCreateMaterial("Skin_Female", Tex("T_Superhero_Female_Light"), Tex("T_Superhero_Female_Normal"), 0.35f);
            library.hairShort = GetOrCreateMaterial("Hair_Short", Tex("T_Hair_1_BaseColor"), Tex("T_Hair_1_Normal"), 0.3f);
            library.hairLong = GetOrCreateMaterial("Hair_Long", Tex("T_Hair_2_BaseColor"), Tex("T_Hair_2_Normal"), 0.3f);
            library.eyes = GetOrCreateMaterial("Eyes", Tex("T_Eye_Brown"), Tex("T_Eye_Normal"), 0.8f);
            library.clothing = GetOrCreateMaterial("Clothing", null, null, 0.12f);
            library.boots = GetOrCreateMaterial("Boots", null, null, 0.3f, new Color(0.22f, 0.15f, 0.1f));
            library.pack = GetOrCreateMaterial("Pack", null, null, 0.2f);
            library.animator = BuildAnimator();

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        static CharacterLibrary.HairStyle Hair(string name, string model, bool male, bool female) =>
            new() { name = name, model = Model(model), male = male, female = female };

        static GameObject Model(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{ArtFolder}/Hair/{name}.fbx");

        static Material GetOrCreateMaterial(string name, Texture2D baseMap, Texture2D normal, float smoothness, Color? colour = null)
        {
            string path = $"{OutputFolder}/{name}.mat";
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
                    clips[clip.name] = clip;
            AnimationClip Clip(string name) => clips.TryGetValue(name, out AnimationClip clip) ? clip : null;

            string path = $"{OutputFolder}/Hiker.controller";
            AssetDatabase.DeleteAsset(path);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            controller.AddParameter(new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true });
            controller.AddParameter("Busy", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Swing", AnimatorControllerParameterType.Trigger);

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

            AnyTransition(machine, swing, 0.05f).AddCondition(AnimatorConditionMode.If, 0f, "Swing");
            AnimatorStateTransition back = swing.AddTransition(locomotion);
            back.hasExitTime = true;
            back.exitTime = 0.8f;
            back.duration = 0.15f;

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
