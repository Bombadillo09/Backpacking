using System.IO;
using Backpacking.Camp;
using Backpacking.Player;
using Backpacking.Survival;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Makes the materials camp gear is built from out of the Poly Haven fabrics in Art/Gear (CC0): a tight weave for
    /// the pack, an open weave for its mesh pockets, ribbed webbing, tough patches; plus plastic and aluminium.
    /// Fills <see cref="GearLibrary"/>. Run by the scene builder; safe to run again.
    /// </summary>
    public static class GearSetup
    {
        const string FabricFolder = "Assets/_Project/Art/Gear/Fabrics";
        const string MaterialFolder = "Assets/_Project/Generated/Gear";
        const string LibraryPath = "Assets/_Project/Settings/GearLibrary.asset";

        public static GearLibrary GetOrCreateLibrary()
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Generated", "Gear");
            var library = AssetDatabase.LoadAssetAtPath<GearLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<GearLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            // Fabrics are white here (keeping the light and shade of the weave) and tinted per pack or item at runtime.
            library.pack = new PackMaterials
            {
                fabric = Fabric("PackFabric", "stretch_poplin", Color.white, keepColour: false),
                mesh = Fabric("PackMesh", "hessian_230", new Color(0.55f, 0.57f, 0.55f), keepColour: true),
                webbing = Fabric("PackWebbing", "rough_linen", new Color(0.1f, 0.1f, 0.11f), keepColour: false),
                patch = Fabric("PackPatch", "fabric_leather_01", new Color(0.22f, 0.21f, 0.2f), keepColour: false),
                plastic = Plain("PackPlastic", new Color(0.06f, 0.06f, 0.065f), 0.45f, 0f),
                gearFabric = Fabric("GearFabric", "stretch_poplin", Color.white, keepColour: false),
            };
            library.aluminium = Plain("Aluminium", new Color(0.7f, 0.71f, 0.73f), 0.6f, 1f);

            var held = AssetDatabase.LoadAssetAtPath<HeldItemLibrary>("Assets/_Project/Settings/HeldItemLibrary.asset");
            if (held != null)
            {
                library.bottle = held.PrefabFor(new HotbarSlot(HotbarKind.Water));
                library.machete = held.PrefabFor(new HotbarSlot(HotbarKind.Machete));
            }
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        static Texture2D Texture(string fabric, string kind) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{FabricFolder}/{fabric}/{fabric}_{kind}_1k.jpg");

        static Material GetOrCreate(string name)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.enableInstancing = true;
            return material;
        }

        /// <summary>
        /// A fabric: the weave as a normal map, roughness from the packed map. With <paramref name="keepColour"/> the
        /// colour texture is used as it is; otherwise a greyscale of it, so tints read true.
        /// </summary>
        static Material Fabric(string name, string fabric, Color colour, bool keepColour)
        {
            Material material = GetOrCreate(name);
            Texture2D diffuse = Texture(fabric, "diff");
            material.SetTexture("_BaseMap", keepColour ? diffuse : Greyscale(fabric, diffuse));
            material.SetColor("_BaseColor", colour);
            Texture2D normal = Texture(fabric, "nor_gl");
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            if (normal != null)
                material.EnableKeyword("_NORMALMAP");
            Texture2D mask = Mask(fabric);
            if (mask != null)
            {
                material.SetTexture("_MetallicGlossMap", mask);
                material.SetTexture("_OcclusionMap", mask);
                material.SetFloat("_Smoothness", 1f);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.EnableKeyword("_OCCLUSIONMAP");
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material Plain(string name, Color colour, float smoothness, float metallic)
        {
            Material material = GetOrCreate(name);
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>The colour texture in grey, normalised to a light grey, so a tint shows its true colour.</summary>
        static Texture2D Greyscale(string fabric, Texture2D source)
        {
            string path = $"{MaterialFolder}/{fabric}_grey.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null || source == null)
                return existing;
            Texture2D readable = Readable(source);
            Color[] pixels = readable.GetPixels();
            float mean = 0f;
            foreach (Color c in pixels)
                mean += c.grayscale;
            mean /= pixels.Length;
            for (int i = 0; i < pixels.Length; i++)
            {
                float g = Mathf.Clamp01(pixels[i].grayscale / Mathf.Max(0.05f, mean) * 0.85f);
                pixels[i] = new Color(g, g, g, 1f);
            }
            Texture2D result = Save(path, pixels, readable.width, readable.height, srgb: true);
            Object.DestroyImmediate(readable);
            return result;
        }

        /// <summary>Poly Haven packs AO, roughness and metal in RGB; URP reads metal in R, occlusion in G, smoothness in A.</summary>
        static Texture2D Mask(string fabric)
        {
            string path = $"{MaterialFolder}/{fabric}_mask.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;
            Texture2D arm = Texture(fabric, "arm");
            if (arm == null)
                return null;
            Texture2D readable = Readable(arm);
            Color[] source = readable.GetPixels();
            var pixels = new Color[source.Length];
            for (int i = 0; i < source.Length; i++)
                pixels[i] = new Color(source[i].b, source[i].r, 0f, 1f - source[i].g);
            Texture2D result = Save(path, pixels, readable.width, readable.height, srgb: false);
            Object.DestroyImmediate(readable);
            return result;
        }

        /// <summary>A readable copy of a texture, through a render texture (the imported one needn't be readable).</summary>
        static Texture2D Readable(Texture2D source)
        {
            RenderTexture temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, temporary);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = temporary;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            return copy;
        }

        static Texture2D Save(string path, Color[] pixels, int width, int height, bool srgb)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, !srgb);
            texture.SetPixels(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.sRGBTexture = srgb;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
