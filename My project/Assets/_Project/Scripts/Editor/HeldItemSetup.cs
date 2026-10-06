using System.Collections.Generic;
using System.IO;
using Backpacking.Player;
using Backpacking.Survival;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Turns the Poly Haven models in Art/Held into hand-ready prefabs for <see cref="HeldItemLibrary"/>: a URP
    /// material each (with Poly Haven's packed AO/roughness/metal map repacked the way URP reads it), the model
    /// scaled and shifted so the grip sits at the origin. Run by the scene builder; safe to run again.
    /// </summary>
    public static class HeldItemSetup
    {
        const string ArtFolder = "Assets/_Project/Art/Held";
        const string MaterialFolder = "Assets/_Project/Generated/Held";
        const string PrefabFolder = "Assets/_Project/Prefabs/Held";
        const string LibraryPath = "Assets/_Project/Settings/HeldItemLibrary.asset";

        /// <summary>
        /// Each item: the model, which part of it (null for all), its scale, and where it's gripped, in the
        /// model's own space (metres, as imported), plus a turn so +Y runs along it and +Z faces front.
        /// </summary>
        static readonly (HotbarKind kind, FoodKind food, string model, string part, float scale, Vector3 grip, Vector3 turn, float smoothness)[] Items =
        {
            // The machete stands blade-up from its handle at the bottom.
            (HotbarKind.Machete, default, "machete", null, 1f, new Vector3(0f, -0.13f, -0.011f), Vector3.zero, 0.55f),
            // A green camp thermos as the water bottle, a bit smaller than the model's 32 cm.
            // Turned so its carry handle faces away from the palm, out to the right.
            (HotbarKind.Water, default, "plastic_thermos", null, 0.8f, new Vector3(-0.016f, 0.13f, 0f), new Vector3(0f, 180f, 0f), 0.45f),
            // A roll of medical tape stands in for the bandage roll.
            (HotbarKind.Bandage, default, "medical_tape", null, 1.4f, Vector3.zero, new Vector3(90f, 0f, 0f), 0.2f),
            // The green first-aid case, shrunk to a pocket pill tin.
            (HotbarKind.Antibiotics, default, "medical_box", null, 0.2f, new Vector3(0f, 0.04f, 0f), new Vector3(90f, 0f, 0f), 0.5f),
            // The dehydrated meal is the tin of baked beans from the long-life food set.
            (HotbarKind.Food, FoodKind.TrailMeal, "long_life_food", "long_life_food_beans", 1f, new Vector3(0.06f, 0.074f, 0f), Vector3.zero, 0.55f),
        };

        public static HeldItemLibrary GetOrCreateLibrary()
        {
            Ensure(MaterialFolder);
            Ensure(PrefabFolder);
            var library = AssetDatabase.LoadAssetAtPath<HeldItemLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<HeldItemLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var entries = new List<HeldItemLibrary.Entry>();
            foreach (var item in Items)
            {
                GameObject prefab = BuildPrefab(item.model, item.part, item.scale, item.grip, item.turn, item.smoothness);
                if (prefab != null)
                    entries.Add(new HeldItemLibrary.Entry { kind = item.kind, food = item.food, prefab = prefab });
            }
            library.entries = entries.ToArray();
            library.plain = PlainMaterial();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        static void Ensure(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            Ensure(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static GameObject BuildPrefab(string model, string part, float scale, Vector3 grip, Vector3 turn, float smoothness)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArtFolder}/{model}/{model}.fbx");
            if (source == null)
            {
                Debug.LogWarning($"Held item model {model} not found in {ArtFolder}.");
                return null;
            }
            Material material = ItemMaterial(model, smoothness);

            var root = new GameObject($"Held {(part ?? model)}");
            try
            {
                // turn, then scale, about the grip: the model sits so the grip lands on the origin.
                var pivot = new GameObject("Model").transform;
                pivot.SetParent(root.transform, false);
                pivot.localRotation = Quaternion.Euler(turn);
                pivot.localScale = Vector3.one * scale;
                GameObject copy = (GameObject)PrefabUtility.InstantiatePrefab(source, pivot);
                PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                copy.transform.localPosition = -grip;
                foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))
                {
                    if (part != null && renderer.name != part)
                    {
                        Object.DestroyImmediate(renderer.gameObject);
                        continue;
                    }
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/Held {(part ?? model)}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static Texture2D Texture(string model, string kind) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtFolder}/{model}/textures/{model}_{kind}_1k.jpg");

        static Material ItemMaterial(string model, float smoothness)
        {
            string path = $"{MaterialFolder}/{model}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", Texture(model, "diff"));
            material.SetColor("_BaseColor", Color.white);
            Texture2D normal = Texture(model, "nor_gl");
            material.SetTexture("_BumpMap", normal);
            if (normal != null)
                material.EnableKeyword("_NORMALMAP");
            Texture2D mask = Mask(model);
            if (mask != null)
            {
                material.SetTexture("_MetallicGlossMap", mask);
                material.SetTexture("_OcclusionMap", mask);
                material.SetFloat("_Smoothness", 1f);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.EnableKeyword("_OCCLUSIONMAP");
            }
            else
                material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Poly Haven packs AO, roughness and metalness into R, G and B; URP Lit reads metal from R, occlusion
        /// from G and smoothness from A. Writes the repacked map once.
        /// </summary>
        static Texture2D Mask(string model)
        {
            string path = $"{MaterialFolder}/{model}_mask.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;
            Texture2D arm = Texture(model, "arm");
            if (arm == null || !arm.isReadable)
                return null;
            Color[] source = arm.GetPixels();
            var pixels = new Color[source.Length];
            for (int i = 0; i < source.Length; i++)
                pixels[i] = new Color(source[i].b, source[i].r, 0f, 1f - source[i].g);
            var mask = new Texture2D(arm.width, arm.height, TextureFormat.RGBA32, false, true);
            mask.SetPixels(pixels);
            File.WriteAllBytes(path, mask.EncodeToPNG());
            Object.DestroyImmediate(mask);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.sRGBTexture = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material PlainMaterial()
        {
            string path = $"{MaterialFolder}/HeldPlain.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.35f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
