using System.Text;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>Lists each held-item model's parts and their bounds. "run:Backpacking.EditorTools.HeldItemDiagnostics.Describe".</summary>
    public static class HeldItemDiagnostics
    {
        public static string Describe()
        {
            var report = new StringBuilder();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Project/Art/Held" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                GameObject copy = Object.Instantiate(model);
                try
                {
                    report.AppendLine($"{path}: root scale {copy.transform.localScale} rot {copy.transform.localEulerAngles}");
                    foreach (Renderer part in copy.GetComponentsInChildren<Renderer>())
                        report.AppendLine($"  {part.name} (parent {part.transform.parent?.name}) centre {part.bounds.center:F3} size {part.bounds.size:F3} local pos {part.transform.localPosition:F3} rot {part.transform.localEulerAngles:F0} scale {part.transform.lossyScale:F3}");
                }
                finally
                {
                    Object.DestroyImmediate(copy);
                }
            }
            return report.ToString();
        }
    }
}
