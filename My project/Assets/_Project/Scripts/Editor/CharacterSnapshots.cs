using System.IO;
using System.Linq;
using System.Text;
using Backpacking.Character;
using Backpacking.Player;
using Backpacking.Survival;
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
                    float neck = root.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Neck).position).y;
                    var owners = appearance.VertexOwners;
                    var headRegion = Enumerable.Range(0, posed.Count).Where(i => posed[i].y > neck && Mathf.Abs(posed[i].x) < 0.15f);
                    report.AppendLine("  above the neck: " + string.Join(", ", headRegion.GroupBy(i => owners[i]).Select(g => $"{g.Key} {g.Count()}")));
                    var weights = appearance.Body.sharedMesh.boneWeights;
                    var bones = appearance.Body.bones;
                    report.AppendLine("  unmapped bones there: " + string.Join(", ", headRegion.Where(i => owners[i] == HumanBodyBones.LastBone)
                        .Select(i => bones[weights[i].boneIndex0].name + " < " + bones[weights[i].boneIndex0].parent?.name).Distinct().Take(12)));
                    report.AppendLine($"  slice at y {y:0.00}: {slice.Count} vertices, z {(slice.Count > 0 ? slice.Min(v => v.z) : 0):0.00}..{(slice.Count > 0 ? slice.Max(v => v.z) : 0):0.00}");
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
            return report.ToString();
        }

        /// <summary>The hiker holding each hotbar item: from outside, and through the eyes (held-*.png).</summary>
        public static string RenderHeld()
        {
            var library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>("Assets/_Project/Settings/CharacterLibrary.asset");
            var heldLibrary = AssetDatabase.LoadAssetAtPath<HeldItemLibrary>("Assets/_Project/Settings/HeldItemLibrary.asset");
            Directory.CreateDirectory(Folder);
            var report = new StringBuilder();
            var preview = new PreviewRenderUtility();
            var root = new GameObject("Snapshot Hiker");
            var owned = new System.Collections.Generic.List<Material>();
            GameObject item = null;
            try
            {
                var appearance = root.AddComponent<CharacterAppearance>();
                appearance.Library = library;
                appearance.Build(new CharacterProfile { hiker = "Male_Adult_05" });
                float eyes = appearance.EyeHeight;
                root.transform.localScale = Vector3.one * (eyes > 0.5f ? 1.68f / eyes : 1f);
                appearance.SetFirstPerson(false);
                preview.AddSingleGO(root);
                preview.camera.fieldOfView = 70f;
                preview.camera.nearClipPlane = 0.02f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
                preview.lights[0].intensity = 1.3f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40f, -30f, 0f);
                preview.lights[1].intensity = 0.5f;
                preview.ambientColor = new Color(0.4f, 0.4f, 0.42f);
                Animator animator = appearance.Animator;

                (HotbarSlot slot, string name)[] items =
                {
                    (new HotbarSlot(HotbarKind.Machete), "machete"), (new HotbarSlot(HotbarKind.Water), "water"),
                    (new HotbarSlot(HotbarKind.Bandage), "bandage"), (new HotbarSlot(HotbarKind.Antibiotics), "antibiotics"),
                    (new HotbarSlot(HotbarKind.Food, FoodKind.TrailMeal), "meal"), (new HotbarSlot(HotbarKind.Food, FoodKind.TrailMix), "trailmix"),
                    (new HotbarSlot(HotbarKind.Food, FoodKind.CookedFish), "fish"), (new HotbarSlot(HotbarKind.Food, FoodKind.Berries), "berries"),
                };
                Vector3 eye = new(0f, 1.68f, 0f);
                Quaternion look = Quaternion.Euler(12f, 0f, 0f);
                foreach ((HotbarSlot slot, string name) in items)
                {
                    if (item != null)
                        Object.DestroyImmediate(item);
                    GameObject prefab = heldLibrary.PrefabFor(slot);
                    item = prefab != null ? Object.Instantiate(prefab) : HeldFood.Build(slot.food, heldLibrary.plain, owned);
                    preview.AddSingleGO(item);

                    void Pose(bool firstPerson)
                    {
                        // As in the game: in first person the head is hidden and the body sits a little behind the eyes.
                        appearance.SetFirstPerson(firstPerson);
                        root.transform.position = new Vector3(0f, 0f, firstPerson ? -0.12f : 0f);
                        appearance.Pose.HandTarget = eye + look * new Vector3(0.22f, -0.22f, 0.42f);
                        appearance.Pose.HandWeight = 1f;
                        appearance.Pose.HandFrame = look * Quaternion.Euler(-15f, 0f, -8f);
                        animator.Rebind();
                        for (int k = 0; k < 20; k++)
                            animator.Update(0.05f);
                        HeldItemView.Place(item.transform, animator, appearance.Pose.HandFrame);
                        foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                            skin.forceMatrixRecalculationPerRender = true;
                    }
                    Pose(false);

                    void Shot(string file, Vector3 camera, Vector3 target)
                    {
                        preview.BeginStaticPreview(new Rect(0, 0, 640, 640));
                        preview.camera.transform.position = camera;
                        preview.camera.transform.LookAt(target);
                        preview.Render(true);
                        Texture2D texture = preview.EndStaticPreview();
                        File.WriteAllBytes($"{Folder}/held-{name}-{file}.png", texture.EncodeToPNG());
                        Object.DestroyImmediate(texture);
                    }
                    Vector3 at = item.transform.position;
                    preview.camera.fieldOfView = 35f;
                    Shot("outside", at + new Vector3(0.55f, 0.15f, 0.75f), at);
                    preview.camera.fieldOfView = 70f;
                    Pose(true);
                    Shot("eyes", eye, eye + look * Vector3.forward);
                    report.AppendLine($"{name}: item at {at:F2}, hand {animator.GetBoneTransform(HumanBodyBones.RightHand).position:F2}");
                }
                return report.ToString();
            }
            finally
            {
                preview.Cleanup();
                Object.DestroyImmediate(root);
                if (item != null)
                    Object.DestroyImmediate(item);
                foreach (Material material in owned)
                    Object.DestroyImmediate(material);
            }
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
                {
                    Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    Vector3 at = root.transform.InverseTransformPoint(hand.position);
                    Shot("1e-hand-front", root.transform.TransformPoint(at + new Vector3(0.15f, 0.05f, 0.35f)), hand.position);
                    Shot("1f-hand-side", root.transform.TransformPoint(at + new Vector3(0.4f, 0.05f, 0f)), hand.position);
                    foreach (HumanBodyBones bone in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftThumbProximal, HumanBodyBones.RightHand, HumanBodyBones.RightThumbProximal })
                    {
                        Transform t = animator.GetBoneTransform(bone);
                        report.AppendLine($"{bone}: {(t != null ? t.name + " at " + root.transform.InverseTransformPoint(t.position).ToString("F2") : "unmapped")}");
                    }
                    // Idle, arms down: the thumb should be forward (+z) of the hand, the index finger forward of the little finger.
                    foreach (bool left in new[] { true, false })
                    {
                        Transform h = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                        Transform thumb = animator.GetBoneTransform(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
                        Transform index = animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
                        Transform little = animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
                        Transform middle = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
                        Vector3 L(Transform t) => root.transform.InverseTransformDirection(t.position - h.position).normalized;
                        report.AppendLine($"{(left ? "left" : "right")} hand idle: thumb dir {L(thumb):F2}, index-little {root.transform.InverseTransformDirection(index.position - little.position).normalized:F2}, fingers dir {L(middle):F2}");
                    }
                }
                if (rosterOnly)
                    return report.ToString();
                // Standing in first person, as the player sees it: body set back behind the eyes, looking down.
                appearance.SetFirstPerson(true);
                appearance.transform.localPosition = new Vector3(0f, 0f, -0.12f);
                Pose(0f, false, 1.2f);
                Shot("0a-eyes-down", new Vector3(0f, 1.68f, 0f), new Vector3(0f, 0f, 0.45f));
                Shot("0b-eyes-ahead-down", new Vector3(0f, 1.68f, 0f), new Vector3(0f, 0.9f, 1.4f));
                Shot("0c-outside", new Vector3(1.2f, 1.9f, 1.2f), new Vector3(0f, 1.5f, 0f));
                appearance.SetFirstPerson(false);
                appearance.transform.localPosition = Vector3.zero;
                Shot("1b-idle-back", new Vector3(-1.4f, 1.6f, -2.6f), new Vector3(0f, 1.1f, 0f));
                Shot("1c-boots", new Vector3(0.5f, 0.45f, 1.1f), new Vector3(0f, 0.1f, 0.05f));
                Pose(2.2f, false, 1.35f);
                Shot("2-walk", new Vector3(3.4f, 1.2f, 0.6f), new Vector3(0f, 0.9f, 0f));
                Pose(4.6f, false, 1.1f);
                Shot("3-sprint", new Vector3(3.4f, 1.2f, 0.6f), new Vector3(0f, 0.9f, 0f));

                Pose(0f, true, 1.5f);
                Shot("4a-seated-shoes-on", new Vector3(0.9f, 1.1f, 2.4f), new Vector3(0f, 0.35f, 0.2f));
                Shot("4b-seated-shoes-close", new Vector3(0.6f, 0.45f, 1.5f), new Vector3(0f, 0.12f, 0.6f));
                Shot("4c-seated-feet-side", new Vector3(0.9f, 0.2f, 0.65f), new Vector3(0f, 0.12f, 0.65f));
                Transform toe = animator.GetBoneTransform(HumanBodyBones.LeftToes), foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                report.AppendLine($"seated left foot {foot.position} rot {foot.eulerAngles}, toes {(toe != null ? toe.name + " " + toe.position + " local " + toe.localEulerAngles : "unmapped")}");
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
