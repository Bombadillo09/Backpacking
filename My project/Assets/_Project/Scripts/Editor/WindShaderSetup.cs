using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Makes trees, shrubs and meadow plants move in the wind. Generates "Backpacking/Wind Lit" from the installed
    /// URP Lit shader (so it always matches the URP version): the same shader, except that each pass's vertex
    /// function first runs the vertex through <c>WindDisplace</c> (Shaders/Wind.hlsl). Then switches the plants'
    /// materials over to it, with leaf flutter on materials with cut-out edges (leaves, needles, grass).
    /// Run on every rebuild; does nothing when it's all up to date.
    /// </summary>
    public static class WindShaderSetup
    {
        const string ShaderPath = "Assets/_Project/Generated/Shaders/WindLit.shader";
        const string WindInclude = "Assets/_Project/Shaders/Wind.hlsl";
        const string BiomeArtAssetPath = "Assets/_Project/Settings/BiomeArt.asset";
        public const string ShaderName = "Backpacking/Wind Lit";

        /// <summary>URP Lit's vertex functions, each wrapped by a wind version.</summary>
        static readonly string[] VertexFunctions =
        {
            "LitPassVertex", "ShadowPassVertex", "LitGBufferPassVertex", "DepthOnlyVertex", "DepthNormalsVertex",
        };

        [MenuItem("Backpacking/Set Up Wind in Trees")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            Shader wind = GenerateShader();
            if (wind == null)
                return "Wind: couldn't generate the wind shader (URP Lit not found).";

            var art = AssetDatabase.LoadAssetAtPath<BiomeArtSettings>(BiomeArtAssetPath);
            if (art == null)
                return "Wind: biome art settings not found, skipped.";
            var plants = new List<GameObject>();
            foreach (GameObject[] group in new[] { art.lowlandTrees, art.conifers, art.valleyTrees, art.understoryShrubs, art.meadowPlants })
                if (group != null)
                    plants.AddRange(group.Where(prefab => prefab != null));

            var materials = new HashSet<Material>();
            foreach (GameObject plant in plants)
            foreach (Renderer renderer in plant.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials)
                if (material != null)
                    materials.Add(material);

            int changed = 0;
            foreach (Material material in materials)
            {
                bool isLit = material.shader.name is "Universal Render Pipeline/Lit" or ShaderName;
                if (!isLit)
                    continue;
                // Leaves, needles and grass blades are cut-out cards; trunks and branches are solid.
                bool leafy = material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f
                             || Regex.IsMatch(material.name, "leaf|leaves|needle|foliage|grass|fern|flower", RegexOptions.IgnoreCase);
                bool wasWind = material.shader == wind;
                bool hadFlutter = material.IsKeywordEnabled("_WIND_FLUTTER");
                if (wasWind && hadFlutter == leafy)
                    continue;
                // Same properties as URP Lit, so switching keeps every texture and setting.
                material.shader = wind;
                if (leafy)
                    material.EnableKeyword("_WIND_FLUTTER");
                else
                    material.DisableKeyword("_WIND_FLUTTER");
                EditorUtility.SetDirty(material);
                changed++;
            }
            AssetDatabase.SaveAssets();
            return $"Wind: {materials.Count} plant materials sway in the wind ({changed} updated).";
        }

        /// <summary>
        /// The name of the vertex position in a pass's Attributes (most of URP's passes call it positionOS, but not
        /// all), read from the pass's own include.
        /// </summary>
        static string PositionField(string pass)
        {
            foreach (Match include in Regex.Matches(pass, "#include \"(Packages/[^\"]+Pass\\.hlsl)\""))
            {
                string file = Path.GetFullPath(include.Groups[1].Value);
                if (!File.Exists(file))
                    continue;
                Match attributes = Regex.Match(File.ReadAllText(file), @"struct Attributes\s*\{[^}]*?float4\s+(\w+)\s*:\s*POSITION");
                if (attributes.Success)
                    return attributes.Groups[1].Value;
            }
            return "positionOS";
        }

        /// <summary>Writes the wind shader from URP Lit if it's missing or out of date, and returns it.</summary>
        static Shader GenerateShader()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            string litPath = lit != null ? AssetDatabase.GetAssetPath(lit) : null;
            if (string.IsNullOrEmpty(litPath))
                return null;
            string source = File.ReadAllText(Path.GetFullPath(litPath));

            string shader = source.Replace("Shader \"Universal Render Pipeline/Lit\"", $"Shader \"{ShaderName}\"");
            foreach (string function in VertexFunctions)
            {
                string pragma = $"#pragma vertex {function}";
                int at = shader.IndexOf(pragma, System.StringComparison.Ordinal);
                if (at < 0)
                    continue;
                shader = shader.Remove(at, pragma.Length).Insert(at,
                    $"#pragma vertex Wind{function}\n            #pragma shader_feature_local_vertex _WIND_FLUTTER");
                // The wrapper goes at the end of the pass, after the include that defines the original.
                int end = shader.IndexOf("ENDHLSL", at, System.StringComparison.Ordinal);
                string position = PositionField(shader.Substring(at, end - at));
                string wrapper =
                    $"            #include \"{WindInclude}\"\n" +
                    $"            Varyings Wind{function}(Attributes input)\n" +
                    "            {\n" +
                    $"                input.{position}.xyz = WindDisplace(input.{position}.xyz);\n" +
                    $"                return {function}(input);\n" +
                    "            }\n";
                shader = shader.Insert(shader.LastIndexOf('\n', end) + 1, wrapper);
            }
            shader = "// Generated by WindShaderSetup from URP's Lit shader: do not edit; rebuild instead.\n" + shader;

            string full = Path.GetFullPath(ShaderPath);
            if (!File.Exists(full) || File.ReadAllText(full) != shader)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, shader);
                AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport);
            }
            return AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        }
    }
}
