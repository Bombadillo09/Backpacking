using System.IO;
using System.Text;
using Backpacking.Character;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Renders the hiker in its main poses to Temp/CharacterSnapshots/*.png, to check animation and posing
    /// without playing: standing, walking, seated with boots off (from outside and through the hiker's eyes).
    /// Run with the AutoRebuild request "run:Backpacking.EditorTools.CharacterSnapshots.Render".
    /// </summary>
    public static class CharacterSnapshots
    {
        const string Folder = "Temp/CharacterSnapshots";

        public static string Render()
        {
            var library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>("Assets/_Project/Settings/CharacterLibrary.asset");
            Directory.CreateDirectory(Folder);
            var report = new StringBuilder();
            var preview = new PreviewRenderUtility();
            var root = new GameObject("Snapshot Hiker");
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            GameObject boots = null;
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            try
            {
                groundMaterial.SetColor("_BaseColor", new Color(0.35f, 0.42f, 0.28f));
                ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
                Object.DestroyImmediate(ground.GetComponent<Collider>());
                preview.AddSingleGO(ground);

                var appearance = root.AddComponent<CharacterAppearance>();
                appearance.Library = library;
                appearance.Build(new CharacterProfile());
                float eyes = appearance.EyeHeight;
                root.transform.localScale = Vector3.one * (eyes > 0.5f ? 1.68f / eyes : 1f);
                preview.AddSingleGO(root);
                Animator animator = appearance.Animator;
                report.AppendLine($"eye height {eyes:0.00}, clips on controller: {animator.runtimeAnimatorController.animationClips.Length}");

                preview.camera.fieldOfView = 35f;
                preview.camera.nearClipPlane = 0.03f;
                preview.camera.farClipPlane = 50f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
                preview.lights[0].intensity = 1.3f;
                preview.lights[0].transform.rotation = Quaternion.Euler(45f, -30f, 0f);
                preview.lights[1].intensity = 0.5f;
                preview.ambientColor = new Color(0.35f, 0.35f, 0.38f);

                void Pose(float speed, bool seated, float time)
                {
                    animator.Rebind();
                    animator.SetFloat("Speed", speed);
                    animator.SetBool("Seated", seated);
                    appearance.Pose.Seated = seated ? 1f : 0f;
                    // Settle through the cross-fade, then advance into the cycle.
                    for (float t = 0f; t < time; t += 0.05f)
                        animator.Update(0.05f);
                }

                void Shot(string name, Vector3 camera, Vector3 target)
                {
                    // Outside Play mode skinning isn't redone between renders unless asked.
                    foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                        skin.forceMatrixRecalculationPerRender = true;
                    preview.BeginStaticPreview(new Rect(0, 0, 600, 760));
                    preview.camera.transform.position = camera;
                    preview.camera.transform.LookAt(target);
                    preview.Render(true);
                    Texture2D texture = preview.EndStaticPreview();
                    File.WriteAllBytes($"{Folder}/{name}.png", texture.EncodeToPNG());
                    Object.DestroyImmediate(texture);
                    Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                    Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    report.AppendLine($"{name}: hips y {hips.position.y:0.00} z {hips.position.z:0.00}, head y {head.position.y:0.00} z {head.position.z:0.00}, foot y {foot.position.y:0.00} z {foot.position.z:0.00}");
                }

                Pose(0f, false, 1.2f);
                Shot("1-idle", new Vector3(1.6f, 1.4f, 3.2f), new Vector3(0f, 0.95f, 0f));
                Shot("1b-idle-back", new Vector3(-1.4f, 1.6f, -2.6f), new Vector3(0f, 1.1f, 0f));
                Shot("1c-boots", new Vector3(0.5f, 0.45f, 1.1f), new Vector3(0f, 0.1f, 0.05f));
                Pose(2.2f, false, 1.35f);
                Shot("2-walk", new Vector3(3.4f, 1.2f, 0.6f), new Vector3(0f, 0.9f, 0f));
                Pose(4.6f, false, 1.1f);
                Shot("3-sprint", new Vector3(3.4f, 1.2f, 0.6f), new Vector3(0f, 0.9f, 0f));

                appearance.SetBootsOn(false);
                boots = appearance.BuildBootsAndSocks();
                boots.transform.SetPositionAndRotation(new Vector3(0.5f, 0f, 0.15f), Quaternion.Euler(0f, 15f, 0f));
                preview.AddSingleGO(boots);
                Pose(0f, true, 1.5f);
                Shot("4-seated-side", new Vector3(2.6f, 0.9f, 0.8f), new Vector3(0f, 0.4f, 0.3f));
                Shot("5-seated-front", new Vector3(0.9f, 1.1f, 2.4f), new Vector3(0f, 0.35f, 0.2f));
                appearance.SetFirstPerson(true);
                appearance.transform.localPosition = new Vector3(0f, 0f, 0.2f);
                Pose(0f, true, 1.5f);
                Shot("6-seated-eyes", new Vector3(0f, 0.9f, 0f), new Vector3(0.15f, 0f, 0.75f));
                return report.ToString();
            }
            finally
            {
                preview.Cleanup();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(ground);
                if (boots != null)
                    Object.DestroyImmediate(boots);
                Object.DestroyImmediate(groundMaterial);
            }
        }
    }
}
