using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Wires free Asset Store packs into the biome art settings, each only if it's been imported:
    /// <list type="bullet">
    /// <item>"Grass Flowers FREE" (ALP): its grass and flower textures become the terrain grass and meadow wildflowers.</item>
    /// <item>"Free 3D Vegetation" (ZNS3D): its HDRP materials are converted to URP, and its grass clumps and
    /// shrub join the ground plants.</item>
    /// <item>"Animals FREE" (ithappy): a clean copy of its deer, without the demo's player-control scripts,
    /// replaces the placeholder deer.</item>
    /// <item>"Living Birds": its songbirds, without their own bird controller and physics, driven by
    /// <see cref="Wildlife.BirdFlock"/> instead, with a <see cref="Wildlife.Songbird"/> holding each one's songs.</item>
    /// <item>"Furry Squirrel" and "Butterfly (Animated)": clean, life-size copies.</item>
    /// </list>
    /// Like the nature pack, these stay out of git (Asset Store license); without them the builder uses placeholders.
    /// </summary>
    public static class ImportedPacksSetup
    {
        const string BiomeArtAssetPath = "Assets/_Project/Settings/BiomeArt.asset";

        const string GrassFolder = "Assets/ALP_Assets/GrassFlowersFREE/Textures/GrassFlowers";
        const string VegetationFolder = "Assets/ZNS3D/FREE_VEGETATION_PACK";
        const string VegetationOutput = "Assets/ZNS3D/BackpackingVariants";
        const string AnimalsFolder = "Assets/ithappy/Animals_FREE";
        const string AnimalsOutput = "Assets/ithappy/BackpackingVariants";
        const string BirdsFolder = "Assets/living birds/resources";
        const string BirdsOutput = "Assets/living birds/BackpackingVariants";
        const string SquirrelPrefab = "Assets/Furry Squirrel/Prefab/Squirrel_URP.prefab";
        const string SquirrelOutput = "Assets/Furry Squirrel/BackpackingVariants";
        const string ButterflyPrefab = "Assets/Butterfly (Animated)/Prefab/Butterfly.prefab";
        const string ButterflyOutput = "Assets/Butterfly (Animated)/BackpackingVariants";

        /// <summary>The Living Birds kinds used, with each one's real length (beak to tail) in metres.</summary>
        static readonly (string prefab, float length)[] Songbirds =
        {
            ("lb_robin", 0.25f), ("lb_blueJay", 0.28f), ("lb_cardinal", 0.22f),
            ("lb_chickadee", 0.13f), ("lb_sparrow", 0.15f), ("lb_goldFinch", 0.12f),
        };

        static readonly string[] VegetationGrass = { "Grass_1", "Grass_2", "Grass_3", "Grass_4" };
        static readonly string[] VegetationShrubs = { "Shrub" };

        [MenuItem("Backpacking/Use Imported Packs (Grass, Vegetation, Animals)")]
        public static void Apply() => Apply(interactive: true);

        /// <summary>Without <paramref name="interactive"/>, shows no dialogs and doesn't offer to rebuild. Returns a summary.</summary>
        public static string Apply(bool interactive)
        {
            var art = AssetDatabase.LoadAssetAtPath<BiomeArtSettings>(BiomeArtAssetPath);
            if (art == null)
            {
                if (interactive)
                    EditorUtility.DisplayDialog("Biome art not found",
                        "Run Backpacking > Build Prototype Scene once first, so the biome art settings exist.", "OK");
                return "Imported packs: biome art settings not found, skipped.";
            }

            var report = new List<string>();
            SetUpGrass(art, report);
            SetUpVegetation(art, report);
            SetUpAnimals(art, report);

            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            string summary = string.Join("\n", report);
            Debug.Log("Imported packs set up:\n" + summary);
            if (interactive && EditorUtility.DisplayDialog("Imported packs ready", $"{summary}\n\nRebuild the prototype scene now?", "Rebuild now", "Later"))
                PrototypeSceneBuilder.Build();
            return summary;
        }

        // ---------- Grass and flowers ----------

        static void SetUpGrass(BiomeArtSettings art, List<string> report)
        {
            if (!AssetDatabase.IsValidFolder(GrassFolder))
            {
                report.Add("Grass Flowers: not imported, skipped.");
                return;
            }

            Texture2D[] grass = LoadTextures("grass0");
            Texture2D[] flowers = LoadTextures("grassFlower");
            if (grass.Length == 0)
            {
                report.Add($"Grass Flowers: no grass textures found in {GrassFolder}.");
                return;
            }

            art.grassTexture = grass[0];
            art.extraGrassTextures = grass.Skip(1).ToArray();
            art.flowerTextures = flowers;
            // The textures are already green, so tint only lightly: lush to straw-coloured.
            art.grassHealthy = new Color(0.92f, 0.97f, 0.88f);
            art.grassDry = new Color(0.98f, 0.9f, 0.68f);
            art.grassHeight = new Vector2(0.45f, 0.95f);
            art.grassWidth = new Vector2(0.7f, 1.3f);
            // Lush: about two tufts per square metre in full meadow.
            art.grassDensity = 6f;
            art.flowerHeight = new Vector2(0.35f, 0.7f);
            art.flowerWidth = new Vector2(0.35f, 0.7f);
            report.Add($"Grass Flowers: {grass.Length} grass and {flowers.Length} flower textures.");
        }

        static Texture2D[] LoadTextures(string prefix)
        {
            var textures = new List<Texture2D>();
            foreach (string path in Directory.GetFiles(GrassFolder, prefix + "*.tga").OrderBy(p => p))
            {
                string assetPath = path.Replace('\\', '/');
                // Terrain grass needs clean transparent edges and no tiling.
                if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer
                    && (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp))
                {
                    importer.alphaIsTransparency = true;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.SaveAndReimport();
                }
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (texture != null)
                    textures.Add(texture);
            }
            return textures.ToArray();
        }

        // ---------- Vegetation ----------

        static void SetUpVegetation(BiomeArtSettings art, List<string> report)
        {
            if (!AssetDatabase.IsValidFolder(VegetationFolder))
            {
                report.Add("Free Vegetation: not imported, skipped.");
                return;
            }
            EnsureFolder(VegetationOutput);

            int converted = ConvertHdrpMaterials(VegetationFolder);
            GameObject[] grass = VegetationGrass.Select(name => CombinedCopy(name)).Where(prefab => prefab != null).ToArray();
            GameObject[] shrubs = VegetationShrubs.Select(name => CombinedCopy(name)).Where(prefab => prefab != null).ToArray();
            art.meadowPlants = Merge(art.meadowPlants, grass);
            art.understoryShrubs = Merge(art.understoryShrubs, shrubs);
            report.Add($"Free Vegetation: {converted} materials converted to URP, {grass.Length} grass clumps and {shrubs.Length} shrub added.");
        }

        /// <summary>
        /// The pack ships HDRP materials, which draw pink in URP. Switches them to URP Lit, keeping their textures.
        /// Also repairs materials an earlier version of this converted without their textures.
        /// </summary>
        /// <summary>
        /// Converts materials made for the built-in render pipeline (Standard, Diffuse, Cutout...) in
        /// <paramref name="folder"/> to URP Lit, which this project renders with; otherwise they draw magenta.
        /// Keeps the texture, colour and normal map, and alpha cut-outs (feather and wing edges).
        /// </summary>
        static int ConvertBuiltInMaterials(string folder)
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || material.shader == urpLit || material.shader.name.StartsWith("Universal Render Pipeline"))
                    continue;
                string shaderName = material.shader.name;
                var saved = new SerializedObject(material).FindProperty("m_SavedProperties");
                Texture baseMap = SavedTexture(saved, "_MainTex");
                Texture normalMap = SavedTexture(saved, "_BumpMap");
                Color colour = SavedColour(saved, "_Color") ?? Color.white;
                // Standard's rendering mode: 1 cutout, 2 fade, 3 transparent. Legacy shaders say so in their names.
                float mode = SavedFloat(saved, "_Mode") ?? 0f;
                bool cutout = mode >= 1f || shaderName.Contains("Cutout") || shaderName.Contains("Transparent");
                float cutoff = SavedFloat(saved, "_Cutoff") ?? 0.5f;

                material.shader = urpLit;
                if (baseMap != null)
                    material.SetTexture("_BaseMap", baseMap);
                colour.a = 1f;
                material.SetColor("_BaseColor", colour);
                material.SetFloat("_Smoothness", 0.15f);
                if (normalMap != null)
                {
                    material.SetTexture("_BumpMap", normalMap);
                    material.EnableKeyword("_NORMALMAP");
                }
                if (cutout)
                {
                    material.SetFloat("_AlphaClip", 1f);
                    material.SetFloat("_Cutoff", Mathf.Clamp(cutoff, 0.3f, 0.6f));
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                    // Wings and feather cards are single sheets.
                    material.SetFloat("_Cull", 0f);
                }
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                count++;
            }
            return count;
        }

        static int ConvertHdrpMaterials(string folder)
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null)
                    continue;

                // HDRP isn't installed, so its shader can't report these values; read them from the material's
                // saved properties instead, which keep every value whatever the shader.
                var saved = new SerializedObject(material).FindProperty("m_SavedProperties");
                Texture baseMap = SavedTexture(saved, "_BaseColorMap");
                if (baseMap == null)
                    continue;
                bool alreadyDone = material.shader == urpLit && material.GetTexture("_BaseMap") == baseMap;
                if (alreadyDone)
                    continue;

                Texture normalMap = SavedTexture(saved, "_NormalMap");
                Color colour = SavedColour(saved, "_BaseColor") ?? Color.white;
                bool cutout = (SavedFloat(saved, "_AlphaCutoffEnable") ?? 0f) > 0.5f;
                float cutoff = SavedFloat(saved, "_AlphaCutoff") ?? 0.5f;
                float smoothness = SavedFloat(saved, "_Smoothness") ?? 0.3f;

                material.shader = urpLit;
                material.SetTexture("_BaseMap", baseMap);
                material.SetColor("_BaseColor", colour);
                // Foliage looks plasticky at HDRP's default smoothness.
                material.SetFloat("_Smoothness", smoothness * 0.4f);
                if (normalMap != null)
                {
                    material.SetTexture("_BumpMap", normalMap);
                    material.EnableKeyword("_NORMALMAP");
                }
                if (cutout)
                {
                    material.SetFloat("_AlphaClip", 1f);
                    material.SetFloat("_Cutoff", cutoff);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                    // Leaves and grass blades are single cards; show both sides.
                    material.SetFloat("_Cull", 0f);
                }
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                count++;
            }
            return count;
        }

        static Texture SavedTexture(SerializedProperty saved, string name)
        {
            SerializedProperty entry = FindSaved(saved.FindPropertyRelative("m_TexEnvs"), name);
            return entry?.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
        }

        static float? SavedFloat(SerializedProperty saved, string name) =>
            FindSaved(saved.FindPropertyRelative("m_Floats"), name)?.FindPropertyRelative("second").floatValue;

        static Color? SavedColour(SerializedProperty saved, string name) =>
            FindSaved(saved.FindPropertyRelative("m_Colors"), name)?.FindPropertyRelative("second").colorValue;

        /// <summary>Finds a name/value pair in one of a material's saved property lists.</summary>
        static SerializedProperty FindSaved(SerializedProperty list, string name)
        {
            if (list == null)
                return null;
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue == name)
                    return entry;
            }
            return null;
        }

        /// <summary>
        /// A copy of a vegetation prefab with all its child meshes merged onto the root (one submesh per
        /// material), as terrain detail painting needs.
        /// </summary>
        static GameObject CombinedCopy(string prefabName)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{VegetationFolder}/PREFABS/{prefabName}.prefab");
            if (source == null)
                return null;
            string prefabPath = $"{VegetationOutput}/Detail_{prefabName}.prefab";

            var parts = new List<CombineInstance>();
            var materials = new List<Material>();
            Matrix4x4 toRoot = source.transform.worldToLocalMatrix;
            foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || !filter.TryGetComponent(out MeshRenderer meshRenderer))
                    continue;
                Material[] shared = meshRenderer.sharedMaterials;
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
                {
                    parts.Add(new CombineInstance
                    {
                        mesh = filter.sharedMesh,
                        subMeshIndex = sub,
                        transform = toRoot * filter.transform.localToWorldMatrix,
                    });
                    materials.Add(shared[Mathf.Min(sub, shared.Length - 1)]);
                }
            }
            if (parts.Count == 0)
                return null;

            var mesh = new Mesh { name = $"{prefabName}_Detail", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), mergeSubMeshes: false, useMatrices: true);
            string meshPath = $"{VegetationOutput}/Detail_{prefabName}_Mesh.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var root = new GameObject(prefabName);
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static GameObject[] Merge(GameObject[] existing, GameObject[] added)
        {
            var merged = new List<GameObject>();
            if (existing != null)
                merged.AddRange(existing.Where(prefab => prefab != null));
            foreach (GameObject prefab in added)
                if (!merged.Exists(other => other.name == prefab.name))
                    merged.Add(prefab);
            return merged.ToArray();
        }

        // ---------- Animals ----------

        static void SetUpAnimals(BiomeArtSettings art, List<string> report)
        {
            var deer = AssetDatabase.LoadAssetAtPath<GameObject>($"{AnimalsFolder}/Prefabs/Deer_001.prefab");
            if (deer == null)
                report.Add("Animals FREE: not imported, skipped.");
            else
            {
                EnsureFolder(AnimalsOutput);
                art.deerModel = WildCopy(deer, "Deer");
                report.Add("Animals FREE: animated deer.");
            }
            SetUpSongbirds(art, report);
            SetUpSquirrelAndButterfly(art, report);
        }

        static void SetUpSongbirds(BiomeArtSettings art, List<string> report)
        {
            if (AssetDatabase.IsValidFolder("Assets/living birds/Materials-Textures"))
                ConvertBuiltInMaterials("Assets/living birds/Materials-Textures");
            var birds = new List<GameObject>();
            foreach ((string prefab, float length) in Songbirds)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{BirdsFolder}/{prefab}.prefab");
                if (source == null)
                    continue;
                EnsureFolder(BirdsOutput);
                birds.Add(SongbirdCopy(source, prefab.Substring(3), length));
            }
            art.songbirdModels = birds.ToArray();
            report.Add(birds.Count > 0 ? $"Living Birds: {birds.Count} songbirds." : "Living Birds: not imported, skipped.");
        }

        /// <summary>
        /// A copy of a Living Birds bird with its model, Animator and voice, but in place of the pack's own bird script
        /// (which needs its controller, tags and physics) a Songbird holding its songs, at its real size.
        /// </summary>
        static GameObject SongbirdCopy(GameObject source, string birdName, float length)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var songs = new List<AudioClip>();
                var takeOff = new List<AudioClip>();
                foreach (MonoBehaviour script in instance.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    var serialized = new SerializedObject(script);
                    foreach (string field in new[] { "song1", "song2" })
                        if (serialized.FindProperty(field)?.objectReferenceValue is AudioClip clip)
                            songs.Add(clip);
                    foreach (string field in new[] { "flyAway1", "flyAway2" })
                        if (serialized.FindProperty(field)?.objectReferenceValue is AudioClip clip)
                            takeOff.Add(clip);
                    Object.DestroyImmediate(script);
                }
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(collider);
                foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
                    Object.DestroyImmediate(body);
                var songbird = instance.AddComponent<Wildlife.Songbird>();
                songbird.songs = songs.ToArray();
                songbird.takeOff = takeOff.ToArray();
                ScaleTo(instance, length, birdName, lengthwise: true);
                instance.name = birdName;
                return PrefabUtility.SaveAsPrefabAsset(instance, $"{BirdsOutput}/{birdName}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        static void SetUpSquirrelAndButterfly(BiomeArtSettings art, List<string> report)
        {
            var squirrel = AssetDatabase.LoadAssetAtPath<GameObject>(SquirrelPrefab);
            if (squirrel != null)
            {
                EnsureFolder(SquirrelOutput);
                // A red squirrel is about 40 cm nose to tail tip.
                art.squirrelModel = WildCopy(squirrel, "Squirrel", SquirrelOutput, 0.4f);
                report.Add("Furry Squirrel: animated squirrel.");
            }
            else
                report.Add("Furry Squirrel: not imported, skipped.");

            var butterfly = AssetDatabase.LoadAssetAtPath<GameObject>(ButterflyPrefab);
            if (butterfly != null)
            {
                ConvertBuiltInMaterials("Assets/Butterfly (Animated)");
                EnsureFolder(ButterflyOutput);
                // About a 7 cm wingspan.
                // The model faces along +X.
                art.butterflyModel = WildCopy(butterfly, "Butterfly", ButterflyOutput, 0.07f, turn: -90f);
                report.Add("Butterfly (Animated): animated butterfly.");
            }
            else
                report.Add("Butterfly (Animated): not imported, skipped.");
        }

        /// <summary>
        /// Scales a model so its largest horizontal size (or, <paramref name="lengthwise"/>, its length front to back)
        /// is <paramref name="size"/> metres (packs are modelled at all sorts of scales), and logs what it measured.
        /// </summary>
        static void ScaleTo(GameObject instance, float size, string label, bool lengthwise = false)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            float measured = lengthwise ? bounds.size.z : Mathf.Max(bounds.size.x, bounds.size.z);
            if (measured <= 1e-4f)
                return;
            instance.transform.localScale *= size / measured;
            Debug.Log($"[Wildlife] {label}: measured {bounds.size} m, scaled by {size / measured:0.###} to {size} m.");
        }

        /// <summary>
        /// A copy of a pack animal with only its model and Animator: the demo's player-control scripts read the
        /// old Input Manager (which this project doesn't use) and its CharacterController would block the player.
        /// </summary>
        static GameObject WildCopy(GameObject source, string animalName, string folder = AnimalsOutput, float size = 0f, float turn = 0f)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                // Newest first: the input script requires the mover, which requires the CharacterController.
                foreach (MonoBehaviour script in instance.GetComponentsInChildren<MonoBehaviour>(true).Reverse())
                    Object.DestroyImmediate(script);
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(collider);
                foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
                    Object.DestroyImmediate(body);
                if (instance.TryGetComponent(out Animator animator))
                    animator.applyRootMotion = false;
                if (size > 0f)
                {
                    // Scaled on a new parent: an animation that keys the model's own root (the butterfly's) would
                    // undo a scale set there.
                    var holder = new GameObject(animalName);
                    // Turned to face forward (+Z), as the game moves animals; on a node of its own, since the
                    // model's animation may key its root's rotation, and the game turns the holder.
                    var facing = new GameObject("Facing").transform;
                    facing.SetParent(holder.transform, false);
                    facing.localRotation = Quaternion.Euler(0f, turn, 0f);
                    instance.transform.SetParent(facing, false);
                    ScaleTo(holder, size, animalName);
                    instance = holder;
                }
                instance.name = animalName;
                return PrefabUtility.SaveAsPrefabAsset(instance, $"{folder}/{animalName}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }
    }
}
