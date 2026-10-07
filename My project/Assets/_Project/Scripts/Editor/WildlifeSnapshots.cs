using System.IO;
using System.Linq;
using Backpacking.Wildlife;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Renders each small animal (rabbit, squirrel, songbirds, butterfly) on a patch of ground, from the side and
    /// three-quarter front, to Temp/WildlifeSnapshots/*.png, with a 10 cm grid square for scale.
    /// Run with the AutoRebuild request "run:Backpacking.EditorTools.WildlifeSnapshots.Render".
    /// </summary>
    public static class WildlifeSnapshots
    {
        const string Folder = "Temp/WildlifeSnapshots";

        public static string Render()
        {
            Directory.CreateDirectory(Folder);
            var art = AssetDatabase.LoadAssetAtPath<BiomeArtSettings>("Assets/_Project/Settings/BiomeArt.asset");
            var plain = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            int count = 0;

            // The preview takes the objects it renders and destroys them afterwards.
            count += Shoot("rabbit", SmallAnimals.Rabbit(plain), 0.6f, null);
            GameObject alert = SmallAnimals.Rabbit(plain);
            alert.GetComponent<SmallAnimalRig>().Pose(0f, 0f, 0f, false, true, instant: true);
            count += Shoot("rabbit-alert", alert, 0.6f, null);

            if (art.squirrelModel != null)
                count += Shoot("squirrel", Object.Instantiate(art.squirrelModel), 0.6f, "Idle", destroy: true);
            foreach (GameObject bird in art.songbirdModels ?? new GameObject[0])
                count += Shoot($"bird-{bird.name}", Object.Instantiate(bird), 0.4f, "idle", destroy: true);
            if (art.butterflyModel != null)
                count += Shoot("butterfly", Object.Instantiate(art.butterflyModel), 0.2f, "", destroy: true);
            Object.DestroyImmediate(plain);
            return $"Rendered {count} wildlife snapshots to {Folder}.";
        }

        /// <summary>Poses the animal on the first clip whose name contains <paramref name="clipName"/>, then renders it.</summary>
        static int Shoot(string name, GameObject animal, float frame, string clipName, bool destroy = false)
        {
            var preview = new PreviewRenderUtility();
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            try
            {
                if (clipName != null)
                    Sample(animal, clipName);
                foreach (SkinnedMeshRenderer skin in animal.GetComponentsInChildren<SkinnedMeshRenderer>())
                    skin.forceMatrixRecalculationPerRender = true;

                ground.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                ground.transform.localScale = Vector3.one * frame * 3f;
                var check = new Texture2D(2, 2) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                check.SetPixels(new[] { new Color(0.32f, 0.36f, 0.24f), new Color(0.27f, 0.31f, 0.2f), new Color(0.27f, 0.31f, 0.2f), new Color(0.32f, 0.36f, 0.24f) });
                check.Apply();
                var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                groundMaterial.SetTexture("_BaseMap", check);
                // Each check is 10 cm.
                groundMaterial.SetTextureScale("_BaseMap", Vector2.one * frame * 3f / 0.2f);
                ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;

                preview.AddSingleGO(animal);
                preview.AddSingleGO(ground);
                preview.camera.fieldOfView = 30f;
                preview.camera.nearClipPlane = 0.01f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.58f, 0.68f, 0.78f);
                preview.lights[0].intensity = 1.3f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40f, -40f, 0f);
                preview.lights[1].intensity = 0.6f;
                preview.ambientColor = new Color(0.45f, 0.45f, 0.48f);

                Renderer[] renderers = animal.GetComponentsInChildren<Renderer>();
                Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one * 0.1f);
                foreach (Renderer renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                Vector3 centre = bounds.center;
                Debug.Log($"[WildlifeSnapshots] {name}: bounds {bounds.size} at {bounds.center}");
                foreach ((string view, Vector3 from) in new[] { ("side", new Vector3(1f, 0.25f, 0f)), ("front", new Vector3(0.6f, 0.45f, 0.75f)) })
                {
                    preview.camera.transform.position = centre + from.normalized * frame * 2.2f;
                    preview.camera.transform.LookAt(centre);
                    // A first render compiles any new shader variants; the second is the picture.
                    preview.BeginStaticPreview(new Rect(0, 0, 600, 600));
                    preview.Render(true);
                    Object.DestroyImmediate(preview.EndStaticPreview());
                    preview.BeginStaticPreview(new Rect(0, 0, 600, 600));
                    preview.Render(true);
                    Texture2D texture = preview.EndStaticPreview();
                    File.WriteAllBytes($"{Folder}/{name}-{view}.png", texture.EncodeToPNG());
                    Object.DestroyImmediate(texture);
                }
                Object.DestroyImmediate(groundMaterial);
                Object.DestroyImmediate(check);
                return 1;
            }
            finally
            {
                preview.Cleanup();
                Object.DestroyImmediate(ground);
                if (destroy)
                    Object.DestroyImmediate(animal);
            }
        }

        static void Sample(GameObject animal, string clipName)
        {
            AnimationClip clip = null;
            Animator animator = animal.GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
                clip = animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name.ToLowerInvariant().Contains(clipName.ToLowerInvariant()));
            Animation legacy = animal.GetComponentInChildren<Animation>();
            if (clip == null && legacy != null)
                clip = legacy.clip;
            if (clip == null)
                return;
            GameObject target = animator != null ? animator.gameObject : legacy.gameObject;
            clip.SampleAnimation(target, clip.length * 0.3f);
        }
    }
}
