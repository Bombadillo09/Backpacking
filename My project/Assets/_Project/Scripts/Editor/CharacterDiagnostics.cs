using System.Text;
using Backpacking.Character;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>Measures a built hiker in the editor: where the feet, hips and head end up, posed and unposed.</summary>
    public static class CharacterDiagnostics
    {
        public static string Measure()
        {
            CharacterLibrary library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>("Assets/_Project/Settings/CharacterLibrary.asset");
            var report = new StringBuilder();
            foreach (bool female in new[] { false, true })
            {
                GameObject body = female ? library.femaleBody : library.maleBody;
                GameObject instance = Object.Instantiate(body);
                try
                {
                    report.AppendLine($"== {body.name}: root scale {instance.transform.localScale}, children {instance.transform.childCount}");
                    Describe(instance, report, "rest pose");
                    var animator = instance.GetComponent<Animator>();
                    report.AppendLine($"  animator {(animator != null)} avatar {(animator != null && animator.avatar != null ? animator.avatar.name + " human=" + animator.avatar.isHuman : "none")}");
                    if (animator != null)
                    {
                        animator.runtimeAnimatorController = library.animator;
                        animator.applyRootMotion = false;
                        animator.Rebind();
                        animator.Update(0.5f);
                        Describe(instance, report, "after idle");
                        report.AppendLine($"  humanScale {animator.humanScale:0.000}");
                    }
                    foreach (SkinnedMeshRenderer part in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                        report.AppendLine($"  mesh {part.name} bounds {part.bounds.min.y:0.00}..{part.bounds.max.y:0.00} root {(part.rootBone ? part.rootBone.name : "none")}");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
            return report.ToString();
        }

        static void Describe(GameObject instance, StringBuilder report, string label)
        {
            report.Append($"  {label}:");
            foreach (string bone in new[] { "root", "pelvis", "Head", "foot_l", "ball_l" })
            {
                Transform t = Find(instance.transform, bone);
                report.Append(t != null ? $" {bone}={t.position.y:0.00}" : $" {bone}=missing");
            }
            report.AppendLine();
        }

        static Transform Find(Transform parent, string name)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>())
                if (t.name == name)
                    return t;
            return null;
        }
    }
}
