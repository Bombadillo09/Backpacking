using System.Collections.Generic;
using System.IO;
using Backpacking.Camp;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Renders every tent model at every pitching stage, with the pack and tent bag, to Logs/CampSnapshots/*.png.
    /// Run with the AutoRebuild request "run:Backpacking.EditorTools.CampSnapshots.Render".
    /// </summary>
    public static class CampSnapshots
    {
        const string Folder = "Logs/CampSnapshots";

        /// <summary>The backpack on its own, with all its strapped-on gear, from four sides (pack-*.png).</summary>
        public static string RenderPack()
        {
            Directory.CreateDirectory(Folder);
            GearLibrary gear = GearSetup.GetOrCreateLibrary();
            var preview = new PreviewRenderUtility();
            var root = new GameObject("Pack");
            try
            {
                PackVisual visual = PackDesign.Build(root.transform, gear.pack, harness: true, gear.bottle, gear.machete);
                visual.Show(true, true, true, true, true);
                visual.Tint(TentDesign.Of(TentModel.OnePerson).Fly);
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", new Color(0.18f, 0.32f, 0.42f));
                foreach (Renderer part in root.GetComponentsInChildren<Renderer>())
                    if (part.name.StartsWith("Bag"))
                        part.SetPropertyBlock(block);
                preview.AddSingleGO(root);
                preview.camera.fieldOfView = 30f;
                preview.camera.nearClipPlane = 0.05f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.58f, 0.68f, 0.78f);
                preview.lights[0].intensity = 1.3f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40f, -40f, 0f);
                preview.lights[1].intensity = 0.6f;
                preview.ambientColor = new Color(0.4f, 0.4f, 0.43f);
                var centre = new Vector3(0f, 0.3f, 0.1f);
                foreach ((string name, Vector3 from) in new[]
                         {
                             ("front", new Vector3(0.9f, 0.6f, 1.7f)), ("side", new Vector3(1.8f, 0.45f, 0.1f)),
                             ("back", new Vector3(-0.8f, 0.6f, -1.6f)), ("top", new Vector3(0.4f, 1.8f, 0.9f)),
                         })
                {
                    preview.BeginStaticPreview(new Rect(0, 0, 700, 700));
                    preview.camera.transform.position = centre + from;
                    preview.camera.transform.LookAt(centre);
                    preview.Render(true);
                    Texture2D texture = preview.EndStaticPreview();
                    File.WriteAllBytes($"{Folder}/pack-{name}.png", texture.EncodeToPNG());
                    Object.DestroyImmediate(texture);
                }
                return "Rendered pack-*.png";
            }
            finally
            {
                preview.Cleanup();
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>From the command line (Logs survives a batch run; Temp does not): -executeMethod ...CampSnapshots.RenderBatch.</summary>
        public static void RenderBatch() => Debug.Log(Render());

        public static string Render()
        {
            Directory.CreateDirectory(Folder);
            var tentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Camp/Tent (staged).prefab");
            var packPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Camp/Ground Pack (detailed).prefab");
            var bagPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Camp/Tent Bag (detailed).prefab");
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
                var stovePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Camp/Camp Stove (detailed).prefab");
                var snarePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Camp/Snare (detailed).prefab");
                foreach ((GameObject prefab, Vector3 at) in new[]
                         {
                             (packPrefab, new Vector3(3.6f, 0f, 0.4f)), (bagPrefab, new Vector3(3.6f, 0f, -0.6f)),
                             (stovePrefab, new Vector3(4.2f, 0f, 0.1f)), (snarePrefab, new Vector3(4.2f, 0f, -0.5f)),
                         })
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
                // The pitched column from behind and from the side, and close up from standing height.
                Shot("pitched-back", new Vector3(5.5f, 2.2f, -8.5f), new Vector3(2.4f, 0.4f, 0f), 900, 700);
                Shot("pitched-2p-close", new Vector3(3.2f, 1.6f, 2.0f), new Vector3(2.4f, 0.5f, 0f), 900, 700);
                Shot("hem-4s", new Vector3(4.0f, 0.3f, -3.0f), new Vector3(3.0f, 0.08f, -3.4f), 900, 600);
                Shot("door-2p", new Vector3(2.6f, 0.9f, 2.6f), new Vector3(2.4f, 0.35f, 0.9f), 900, 700);
                // The 4-season tent without its fly, to see the inner tent and poles under it.
                foreach (GameObject thing in made)
                    if (thing.name == "FourSeason Pitched")
                        foreach (Transform part in thing.transform)
                            part.gameObject.SetActive(part.name != "Rainfly" && part.name != "Hem" && part.name != "Seams" && !part.name.StartsWith("Door"));
                Shot("inner-4s", new Vector3(5.6f, 1.0f, -3.4f), new Vector3(2.4f, 0.45f, -3.4f), 900, 600);
                foreach (GameObject thing in made)
                    foreach (Transform part in thing.transform)
                        part.gameObject.SetActive(true);
                // From inside, sitting up at the head of the bed and looking out of the door.
                Shot("inside-2p", new Vector3(2.15f, 0.72f, -0.75f), new Vector3(2.4f, 0.35f, 1.2f), 900, 600);
                Shot("inside-4s-up", new Vector3(2.4f, 0.55f, -3.6f), new Vector3(2.5f, 1.2f, -3.2f), 900, 600);
                Shot("pitched-4s-side", new Vector3(5.6f, 1.0f, -3.4f), new Vector3(2.4f, 0.45f, -3.4f), 900, 600);
                Shot("poles-2p", new Vector3(1.6f, 1.9f, 2.6f), new Vector3(0f, 0.3f, 0f), 900, 700);
                Shot("chair", new Vector3(-3.6f, 1.1f, 0.6f), new Vector3(-3.1f, 0.3f, 2.6f), 900, 600);
                Shot("gear", new Vector3(5.2f, 0.9f, 1.4f), new Vector3(3.9f, 0.2f, -0.1f), 900, 700);
                Shot("stove", new Vector3(4.55f, 0.35f, 0.45f), new Vector3(4.2f, 0.14f, 0.1f), 700, 700);
                Shot("snare", new Vector3(4.6f, 0.35f, -0.15f), new Vector3(4.2f, 0.12f, -0.45f), 700, 700);
                return "Rendered to Logs/CampSnapshots.";
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
