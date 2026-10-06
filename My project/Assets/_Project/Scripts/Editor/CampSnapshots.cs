using System.Collections.Generic;
using System.IO;
using Backpacking.Camp;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Renders every tent model at every pitching stage, with the pack and tent bag, to Temp/CampSnapshots/*.png.
    /// Run with the AutoRebuild request "run:Backpacking.EditorTools.CampSnapshots.Render".
    /// </summary>
    public static class CampSnapshots
    {
        const string Folder = "Temp/CampSnapshots";

        public static string Render()
        {
            Directory.CreateDirectory(Folder);
            var tentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Camp/Tent (staged).prefab");
            var packPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Camp/Ground Pack.prefab");
            var bagPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Camp/Tent Bag.prefab");
            if (tentPrefab == null)
                return "Tent (staged) prefab not found; rebuild the scene first.";
            TentMaterials materials = tentPrefab.GetComponent<Tent>().Materials;

            var preview = new PreviewRenderUtility();
            var made = new List<GameObject>();
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            try
            {
                groundMaterial.SetColor("_BaseColor", new Color(0.33f, 0.4f, 0.26f));
                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.transform.localScale = new Vector3(3f, 1f, 3f);
                ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
                Object.DestroyImmediate(ground.GetComponent<Collider>());
                made.Add(ground);
                preview.AddSingleGO(ground);

                // Rows: 1-person, 2-person, 4-season. Columns: laid out, poles up, pitched.
                TentModel[] models = { TentModel.OnePerson, TentModel.TwoPerson, TentModel.FourSeason };
                TentStage[] stages = { TentStage.LaidOut, TentStage.Poled, TentStage.Pitched };
                for (int row = 0; row < models.Length; row++)
                for (int column = 0; column < stages.Length; column++)
                {
                    var tent = new GameObject($"{models[row]} {stages[column]}");
                    tent.transform.position = new Vector3((column - 1) * 2.4f, 0f, (1 - row) * 3.4f);
                    TentDesign.Build(tent.transform, models[row], stages[column], materials);
                    made.Add(tent);
                    preview.AddSingleGO(tent);
                }
                var chairPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Camp/Camp Chair.prefab");
                if (chairPrefab != null)
                {
                    var chair = chairPrefab.GetComponent<CampChair>();
                    var serialized = new SerializedObject(chair);
                    var frame = (Material)serialized.FindProperty("frameMaterial").objectReferenceValue;
                    var fabric = (Material)serialized.FindProperty("fabricMaterial").objectReferenceValue;
                    ChairStage[] chairStages = { ChairStage.Packed, ChairStage.Frame, ChairStage.Ready };
                    for (int k = 0; k < chairStages.Length; k++)
                    {
                        var copy = new GameObject($"Chair {chairStages[k]}");
                        copy.transform.SetPositionAndRotation(new Vector3(-4f + k * 0.9f, 0f, 2.6f), Quaternion.Euler(0f, 160f, 0f));
                        CampChair.Build(copy.transform, chairStages[k], frame, fabric);
                        made.Add(copy);
                        preview.AddSingleGO(copy);
                    }
                }
                foreach ((GameObject prefab, Vector3 at) in new[] { (packPrefab, new Vector3(3.6f, 0f, 0.4f)), (bagPrefab, new Vector3(3.6f, 0f, -0.6f)) })
                {
                    if (prefab == null)
                        continue;
                    GameObject copy = Object.Instantiate(prefab, at, Quaternion.Euler(0f, 200f, 0f));
                    made.Add(copy);
                    preview.AddSingleGO(copy);
                }

                preview.camera.fieldOfView = 35f;
                preview.camera.nearClipPlane = 0.05f;
                preview.camera.farClipPlane = 80f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.58f, 0.68f, 0.78f);
                preview.lights[0].intensity = 1.3f;
                preview.lights[0].transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                preview.lights[1].intensity = 0.5f;
                preview.ambientColor = new Color(0.38f, 0.38f, 0.42f);

                void Shot(string name, Vector3 camera, Vector3 target, int width = 1200, int height = 800)
                {
                    preview.BeginStaticPreview(new Rect(0, 0, width, height));
                    preview.camera.transform.position = camera;
                    preview.camera.transform.LookAt(target);
                    preview.Render(true);
                    Texture2D texture = preview.EndStaticPreview();
                    File.WriteAllBytes($"{Folder}/{name}.png", texture.EncodeToPNG());
                    Object.DestroyImmediate(texture);
                }

                Shot("overview", new Vector3(5f, 7.5f, 11f), new Vector3(0.6f, 0f, 0f));
                // Each model pitched, from the front three-quarters.
                Shot("pitched-1p", new Vector3(0.6f, 1.9f, 7.2f), new Vector3(2.4f, 0.35f, 3.7f), 900, 700);
                Shot("pitched-1p-side", new Vector3(6.2f, 0.9f, 3.8f), new Vector3(2.4f, 0.35f, 3.8f), 900, 600);
                Shot("poles-1p", new Vector3(-1.6f, 1.6f, 6.6f), new Vector3(0f, 0.3f, 3.6f), 900, 700);
                Shot("pitched-2p", new Vector3(4.4f, 1.7f, 3.4f), new Vector3(2.4f, 0.45f, 0f), 900, 700);
                Shot("pitched-4s", new Vector3(4.4f, 1.7f, 0f), new Vector3(2.4f, 0.45f, -3.4f), 900, 700);
                Shot("poles-2p", new Vector3(1.6f, 1.9f, 2.6f), new Vector3(0f, 0.3f, 0f), 900, 700);
                Shot("chair", new Vector3(-3.6f, 1.1f, 0.6f), new Vector3(-3.1f, 0.3f, 2.6f), 900, 600);
                Shot("gear", new Vector3(4.9f, 0.9f, 1.4f), new Vector3(3.6f, 0.2f, -0.1f), 900, 700);
                return "Rendered to Temp/CampSnapshots.";
            }
            finally
            {
                preview.Cleanup();
                foreach (GameObject thing in made)
                    if (thing != null)
                        Object.DestroyImmediate(thing);
                Object.DestroyImmediate(groundMaterial);
            }
        }
    }
}
