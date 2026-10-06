using System.IO;
using System.Linq;
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

        /// <summary>Every hiker on the roster standing, one picture each (roster-*.png).</summary>
        public static string RenderRoster()
        {
            var library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>("Assets/_Project/Settings/CharacterLibrary.asset");
            var report = new StringBuilder();
            foreach (CharacterLibrary.Hiker hiker in library.hikers)
                report.AppendLine(RenderHiker(hiker.id, true));
            return report.ToString();
        }

        /// <summary>Lists each roster model's skinned meshes and their transforms.</summary>
        public static string Describe()
        {
            var library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>("Assets/_Project/Settings/CharacterLibrary.asset");
            var report = new StringBuilder();
            foreach (CharacterLibrary.Hiker hiker in library.hikers)
            {
                report.AppendLine($"{hiker.id}: root rot {hiker.model.transform.localEulerAngles} scale {hiker.model.transform.localScale}");
                foreach (SkinnedMeshRenderer part in hiker.model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    report.AppendLine($"  {part.name} (parent {part.transform.parent.name}) active {part.gameObject.activeSelf} pos {part.transform.localPosition} rot {part.transform.localEulerAngles} scale {part.transform.localScale} lossy {part.transform.lossyScale} subs {part.sharedMesh.subMeshCount} root {part.rootBone?.name}");
            }
            return report.ToString();
        }

        /// <summary>Measurements behind the pack fitting, per hiker: chest height and the torso's front and back.</summary>
        public static string Measure()
        {
            var library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>("Assets/_Project/Settings/CharacterLibrary.asset");
            var report = new StringBuilder();
            foreach (CharacterLibrary.Hiker hiker in library.hikers)
            {
                var root = new GameObject("Measure");
                try
                {
                    var appearance = root.AddComponent<CharacterAppearance>();
                    appearance.Library = library;
                    appearance.Build(new CharacterProfile { hiker = hiker.id });
                    Animator animator = appearance.Animator;
                    Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
                    Transform l = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), r = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                    Transform lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    report.AppendLine($"{hiker.id}: chest {chest?.name} {chest?.position}, L thigh {l?.name} {l?.position}, R thigh {r?.name} {r?.position}, L foot {lf?.name} {lf?.position}");
                    Transform pack = root.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Backpack");
                    if (pack != null)
                        report.AppendLine($"  pack under {pack.parent.name}, first part at {pack.GetChild(0).position}");
                    report.AppendLine($"  body bounds {appearance.Body.bounds.center} size {appearance.Body.bounds.size}");
                    var posed = appearance.MeasuredVertices;
                    float y = root.transform.InverseTransformPoint(chest.position).y - 0.1f;
                    var slice = posed.Where(v => Mathf.Abs(v.x) < 0.03f && Mathf.Abs(v.y - y) < 0.03f).ToList();
                    report.AppendLine($"  measured: {posed.Count} vertices, x {posed.Min(v => v.x):0.00}..{posed.Max(v => v.x):0.00} y {posed.Min(v => v.y):0.00}..{posed.Max(v => v.y):0.00} z {posed.Min(v => v.z):0.00}..{posed.Max(v => v.z):0.00}");
                    report.AppendLine($"  slice at y {y:0.00}: {slice.Count} vertices, z {(slice.Count > 0 ? slice.Min(v => v.z) : 0):0.00}..{(slice.Count > 0 ? slice.Max(v => v.z) : 0):0.00}");
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
            return report.ToString();
        }

        public static string Render() => RenderHiker("Male_Adult_05", false);

        /// <summary>The same shots of a hiker in knee boots, the hardest case for bare feet.</summary>
        public static string RenderTallBoots() => RenderHiker("Female_Adult_04", false);

        static string RenderHiker(string hikerId, bool rosterOnly)
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
                appearance.Build(new CharacterProfile { hiker = hikerId });
                float eyes = appearance.EyeHeight;
                root.transform.localScale = Vector3.one * (eyes > 0.5f ? 1.68f / eyes : 1f);
                preview.AddSingleGO(root);
                Animator animator = appearance.Animator;
                report.AppendLine($"{hikerId}: eye height {eyes:0.00}, height {appearance.Height:0.00}, human {animator.isHuman}, avatar valid {(animator.avatar != null && animator.avatar.isValid)}");

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
                    File.WriteAllBytes($"{Folder}/{(rosterOnly ? "roster-" + hikerId : name)}.png", texture.EncodeToPNG());
                    Object.DestroyImmediate(texture);
                    Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                    Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    report.AppendLine($"{name}: hips y {hips.position.y:0.00} z {hips.position.z:0.00}, head y {head.position.y:0.00} z {head.position.z:0.00}, foot y {foot.position.y:0.00} z {foot.position.z:0.00}");
                }

                Pose(0f, false, 1.2f);
                Shot("1-idle", new Vector3(1.6f, 1.4f, 3.2f), new Vector3(0f, 0.95f, 0f));
                if (rosterOnly)
                    return report.ToString();
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
