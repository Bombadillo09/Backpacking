using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Turns the CC0 Poly Haven models in Art/Home (furniture and household things, FBX with 1k maps) into prefabs
    /// for furnishing the house: a URP material for each of a model's texture sets (colour, normal, and the metal
    /// and roughness maps packed the way URP reads them; cut-outs where there's an opacity map; glass where there's
    /// only alpha), with the model's base centred on the prefab's origin. Real-world size, as Poly Haven makes them.
    /// Prefabs are cached; delete Prefabs/Home to rebuild them.
    /// </summary>
    public static class HomeModelSetup
    {
        const string ArtFolder = "Assets/_Project/Art/Home";
        const string GeneratedFolder = "Assets/_Project/Generated/Home";
        const string PrefabFolder = "Assets/_Project/Prefabs/Home";

        static readonly Dictionary<string, GameObject> made = new();

        /// <summary>The prefab for a Poly Haven model id, or null if it isn't in Art/Home.</summary>
        public static GameObject Prefab(string id)
        {
            if (made.TryGetValue(id, out GameObject cached) && cached != null)
                return cached;
            string prefabPath = $"{PrefabFolder}/{id}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
                return made[id] = existing;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArtFolder}/{id}/{id}.fbx");
            if (source == null)
            {
                Debug.LogWarning($"Home model {id} isn't in {ArtFolder}.");
                return null;
            }
            Ensure(GeneratedFolder);
            Ensure(PrefabFolder);

            var root = new GameObject(id);
            try
            {
                var copy = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                        materials[i] = MaterialFor(id, materials[i] != null ? materials[i].name : id);
                    renderer.sharedMaterials = materials;
                }
                // Base on the origin, centred.
                Bounds bounds = WorldBounds(copy);
                copy.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                return made[id] = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        public static Bounds WorldBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        // ---------- Materials ----------

        /// <summary>The texture sets in a model's folder, by prefix (e.g. "lantern_01_brass"), each with its maps.</summary>
        static Dictionary<string, Dictionary<string, string>> Sets(string id)
        {
            var sets = new Dictionary<string, Dictionary<string, string>>();
            string folder = $"{ArtFolder}/{id}/textures";
            if (!Directory.Exists(folder))
                return sets;
            foreach (string file in Directory.GetFiles(folder, "*_1k.*"))
            {
                if (file.EndsWith(".meta"))
                    continue;
                string name = Path.GetFileNameWithoutExtension(file);
                name = name.Substring(0, name.Length - "_1k".Length);
                foreach (string map in new[] { "diff", "nor_gl", "roughness", "rough", "metallic", "metal", "opacity", "alpha" })
                    if (name.EndsWith("_" + map))
                    {
                        string prefix = name.Substring(0, name.Length - map.Length - 1);
                        if (!sets.TryGetValue(prefix, out var maps))
                            sets[prefix] = maps = new Dictionary<string, string>();
                        maps[map switch { "roughness" => "rough", "metallic" => "metal", "alpha" => "opacity", _ => map }] = file.Replace('\\', '/');
                        break;
                    }
            }
            return sets;
        }

        /// <summary>The texture set a model's material uses: the one whose name it shares, else the model's own.</summary>
        static string SetFor(string id, string materialName, Dictionary<string, Dictionary<string, string>> sets)
        {
            string material = materialName.ToLowerInvariant();
            string best = null;
            foreach (string prefix in sets.Keys)
            {
                string part = prefix.Length > id.Length ? prefix.Substring(id.Length).Trim('_').ToLowerInvariant() : "";
                if (part.Length > 0 && material.Contains(part) && (best == null || prefix.Length > best.Length))
                    best = prefix;
            }
            if (best != null)
                return best;
            foreach (string prefix in sets.Keys)
                if (prefix.Length == id.Length)
                    return prefix;
            foreach (string prefix in sets.Keys)
                if (sets[prefix].ContainsKey("diff"))
                    return prefix;
            return null;
        }

        static Material MaterialFor(string id, string materialName)
        {
            var sets = Sets(id);
            string set = SetFor(id, materialName, sets);
            if (set == null)
                return Plain(id);
            string path = $"{GeneratedFolder}/{set}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            Dictionary<string, string> maps = sets[set];

            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            bool glass = !maps.ContainsKey("diff") && maps.ContainsKey("opacity");
            if (glass)
            {
                // Glass: a faint tint, plain alpha blending (see the truck glass for why not premultiplied).
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_BlendModePreserveSpecular", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetColor("_BaseColor", new Color(0.85f, 0.9f, 0.92f, 0.12f));
                material.SetFloat("_Smoothness", 0.85f);
                AssetDatabase.CreateAsset(material, path);
                return material;
            }

            Texture2D colour = maps.ContainsKey("opacity") ? CombineColourAndOpacity(set, maps["diff"], maps["opacity"]) : Load(maps["diff"]);
            material.SetTexture("_BaseMap", colour);
            material.SetColor("_BaseColor", Color.white);
            if (maps.ContainsKey("opacity"))
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.5f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetFloat("_Cull", (float)CullMode.Off);
            }
            if (maps.TryGetValue("nor_gl", out string normalPath))
            {
                material.SetTexture("_BumpMap", NormalMap(normalPath));
                material.EnableKeyword("_NORMALMAP");
            }
            Texture2D mask = Mask(set, maps.GetValueOrDefault("metal"), maps.GetValueOrDefault("rough"));
            if (mask != null)
            {
                material.SetTexture("_MetallicGlossMap", mask);
                material.SetFloat("_Smoothness", 1f);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            else
                material.SetFloat("_Smoothness", 0.3f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Material Plain(string id)
        {
            string path = $"{GeneratedFolder}/{id}_plain.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(0.6f, 0.58f, 0.55f));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Texture2D Load(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        static Texture2D NormalMap(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            return Load(path);
        }

        /// <summary>
        /// A readable, linear copy of a texture (any format Unity imports, EXR included), read through its importer:
        /// made readable and linear for the copy, then put back.
        /// </summary>
        static Texture2D Read(string path)
        {
            if (path == null || AssetImporter.GetAtPath(path) is not TextureImporter importer)
                return null;
            bool readable = importer.isReadable, sRGB = importer.sRGBTexture;
            TextureImporterCompression compression = importer.textureCompression;
            importer.isReadable = true;
            importer.sRGBTexture = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            Texture2D source = Load(path);
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            copy.SetPixels(source.GetPixels());
            importer.isReadable = readable;
            importer.sRGBTexture = sRGB;
            importer.textureCompression = compression;
            importer.SaveAndReimport();
            return copy;
        }

        /// <summary>URP Lit reads metal from R and smoothness from A: packed from Poly Haven's separate maps.</summary>
        static Texture2D Mask(string set, string metalPath, string roughPath)
        {
            if (metalPath == null && roughPath == null)
                return null;
            string path = $"{GeneratedFolder}/{set}_mask.png";
            var existing = Load(path);
            if (existing != null)
                return existing;
            Texture2D metal = Read(metalPath), rough = Read(roughPath);
            Texture2D size = rough ?? metal;
            var mask = new Texture2D(size.width, size.height, TextureFormat.RGBA32, false, true);
            var pixels = new Color[size.width * size.height];
            for (int y = 0; y < size.height; y++)
            for (int x = 0; x < size.width; x++)
            {
                float u = (x + 0.5f) / size.width, v = (y + 0.5f) / size.height;
                float metalness = metal != null ? metal.GetPixelBilinear(u, v).r : 0f;
                float roughness = rough != null ? rough.GetPixelBilinear(u, v).r : 0.7f;
                pixels[y * size.width + x] = new Color(metalness, 1f, 0f, 1f - roughness);
            }
            mask.SetPixels(pixels);
            File.WriteAllBytes(path, mask.EncodeToPNG());
            Object.DestroyImmediate(mask);
            if (metal != null)
                Object.DestroyImmediate(metal);
            if (rough != null)
                Object.DestroyImmediate(rough);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.sRGBTexture = false;
                importer.SaveAndReimport();
            }
            return Load(path);
        }

        /// <summary>The colour map with the opacity map in its alpha, for cut-outs (a lantern's frame, a picture's mount).</summary>
        static Texture2D CombineColourAndOpacity(string set, string colourPath, string opacityPath)
        {
            string path = $"{GeneratedFolder}/{set}_colour.png";
            var existing = Load(path);
            if (existing != null)
                return existing;
            Texture2D colour = Read(colourPath), opacity = Read(opacityPath);
            if (colour == null)
                return Load(colourPath);
            var combined = new Texture2D(colour.width, colour.height, TextureFormat.RGBA32, false);
            Color[] pixels = colour.GetPixels();
            for (int y = 0; y < colour.height; y++)
            for (int x = 0; x < colour.width; x++)
            {
                Color pixel = pixels[y * colour.width + x];
                pixel.a = opacity != null ? opacity.GetPixelBilinear((x + 0.5f) / colour.width, (y + 0.5f) / colour.height).r : 1f;
                pixels[y * colour.width + x] = pixel;
            }
            combined.SetPixels(pixels);
            File.WriteAllBytes(path, combined.EncodeToPNG());
            Object.DestroyImmediate(combined);
            Object.DestroyImmediate(colour);
            if (opacity != null)
                Object.DestroyImmediate(opacity);
            AssetDatabase.ImportAsset(path);
            return Load(path);
        }

        static void Ensure(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            Ensure(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
