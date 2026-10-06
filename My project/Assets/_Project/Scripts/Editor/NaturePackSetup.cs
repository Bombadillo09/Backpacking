using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Wires the Asset Store "Essential Nature Pack" (Polyeler) into the biome art settings: trees, ground
    /// plants, boulders and firewood models.
    /// <list type="bullet">
    /// <item>Unity's terrain only gives trees collision from a capsule collider, but the pack's trees use box
    /// colliders, so each tree gets a variant with a trunk-sized capsule.</item>
    /// <item>Terrain detail painting needs the mesh on the prefab's root, but the pack keeps it on a child,
    /// so each ground plant gets a detail-ready copy.</item>
    /// </list>
    /// The pack and everything made from it stay out of git (Asset Store license); without them the scene
    /// builder falls back to placeholder art.
    /// </summary>
    public static class NaturePackSetup
    {
        const string PackPrefabs = "Assets/Polyeler/EssentialNaturePack/Prefabs";
        const string OutputFolder = "Assets/Polyeler/BackpackingTreeVariants";
        const string BiomeArtAssetPath = "Assets/_Project/Settings/BiomeArt.asset";

        // The pack's trees are about 13–14 m; real mature pines and oaks are more like 20–30 m.
        const float TreeScale = 1.7f;

        static readonly string[] Conifers = { "Pine", "JapaneseCedar", "JapaneseCypress" };
        static readonly string[] LowlandTrees = { "SawtoothOak", "PlaneTree" };
        // Dawn redwoods grow in wet valley bottoms; plane trees (sycamores) line rivers.
        static readonly string[] ValleyTrees = { "DawnRedwood", "PlaneTree" };
        static readonly string[] ForestFloorPlants = { "FallenLeaves" };
        static readonly string[] MeadowPlants = { "Mugwort", "ViolaGrypoceras" };
        static readonly string[] UnderstoryShrubs = { "JapaneseEurya", "JapaneseLaurel" };
        // The two lowest-poly rocks, to paint as small stones.
        static readonly string[] ForestStones = { "Rock_B", "Rock_D" };
        static readonly string[] Boulders = { "Rock_A", "Rock_B", "Rock_C", "Rock_D" };
        static readonly string[] FirewoodModels = { "Decayed_wood_A", "Decayed_wood_B", "Decayed_wood_C", "Decayed_wood_D", "Decayed_wood_E" };

        [MenuItem("Backpacking/Use Essential Nature Pack")]
        public static void Apply()
        {
            if (!AssetDatabase.IsValidFolder(PackPrefabs))
            {
                EditorUtility.DisplayDialog("Essential Nature Pack not found",
                    $"Import the pack first (Window > Package Manager > My Assets). Expected its prefabs in {PackPrefabs}.", "OK");
                return;
            }
            var art = AssetDatabase.LoadAssetAtPath<BiomeArtSettings>(BiomeArtAssetPath);
            if (art == null)
            {
                EditorUtility.DisplayDialog("Biome art not found",
                    "Run Backpacking > Build Prototype Scene once first, so the biome art settings exist.", "OK");
                return;
            }
            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(OutputFolder).Replace('\\', '/'), Path.GetFileName(OutputFolder));

            var report = new List<string>();
            var problems = new List<string>();

            // Only replace a slot when something was actually set up for it, so a failure leaves the old art in place.
            Assign(ref art.conifers, Collect(Conifers, TreeVariant, report, problems));
            Assign(ref art.lowlandTrees, Collect(LowlandTrees, TreeVariant, report, problems));
            Assign(ref art.valleyTrees, Collect(ValleyTrees, TreeVariant, report, problems));
            Assign(ref art.forestFloorPlants, Collect(ForestFloorPlants, DetailCopy, report, problems));
            Assign(ref art.meadowPlants, Collect(MeadowPlants, DetailCopy, report, problems));
            Assign(ref art.understoryShrubs, Collect(UnderstoryShrubs, DetailCopy, report, problems));
            Assign(ref art.boulders, Collect(Boulders, PackPrefab, report, problems));
            Assign(ref art.firewoodModels, Collect(FirewoodModels, PackPrefab, report, problems));
            // The same decayed wood, at full size, makes the fallen trunks in the forest,
            // and shrunk down, the branches and sticks on the forest floor. Small rocks become stones.
            art.fallenLogs = art.firewoodModels;
            Assign(ref art.forestDebris, Collect(FirewoodModels, DetailCopy, report, problems));
            Assign(ref art.forestStones, Collect(ForestStones, DetailCopy, report, problems));
            art.treeScale = TreeScale;

            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();

            string summary = string.Join("\n", report);
            if (problems.Count > 0)
            {
                summary += "\n\nProblems:\n" + string.Join("\n", problems);
                Debug.LogWarning("Nature pack set up with problems:\n" + summary);
            }
            else
                Debug.Log("Nature pack set up:\n" + summary);

            if (EditorUtility.DisplayDialog(problems.Count > 0 ? "Nature pack set up, with problems" : "Nature pack ready",
                    $"{summary}\n\nTrees are scaled ×{TreeScale}. Rebuild the prototype scene now?", "Rebuild now", "Later"))
                PrototypeSceneBuilder.Build();
        }

        static void Assign(ref GameObject[] slot, GameObject[] prefabs)
        {
            if (prefabs.Length > 0)
                slot = prefabs;
        }

        static GameObject[] Collect(string[] names, Func<string, GameObject, List<string>, GameObject> prepare,
            List<string> report, List<string> problems)
        {
            var results = new List<GameObject>();
            foreach (string assetName in names)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{PackPrefabs}/{assetName}.prefab");
                if (source == null)
                {
                    problems.Add($"{assetName}: not found in the pack");
                    continue;
                }
                try
                {
                    GameObject prepared = prepare(assetName, source, report);
                    if (prepared != null)
                        results.Add(prepared);
                }
                catch (Exception exception)
                {
                    problems.Add($"{assetName}: {exception.Message}");
                    Debug.LogException(exception);
                }
            }
            return results.ToArray();
        }

        static GameObject PackPrefab(string assetName, GameObject source, List<string> report) => source;

        /// <summary>A variant of the pack's tree with its box collider swapped for a trunk capsule.</summary>
        static GameObject TreeVariant(string treeName, GameObject source, List<string> report)
        {
            string variantPath = $"{OutputFolder}/{treeName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
            if (existing != null && existing.GetComponent<CapsuleCollider>() != null)
            {
                if (!report.Exists(line => line.StartsWith(treeName + ":")))
                    report.Add($"{treeName}: {existing.GetComponent<CapsuleCollider>().height:0.0} m tree (existing variant)");
                return existing;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                Bounds bounds = DetailedMeshBounds(instance);
                foreach (Collider collider in instance.GetComponents<Collider>())
                    Object.DestroyImmediate(collider);

                var trunk = instance.AddComponent<CapsuleCollider>();
                float height = bounds.size.y;
                trunk.radius = Mathf.Clamp(height * 0.025f, 0.15f, 0.6f);
                trunk.height = height;
                trunk.center = new Vector3(0f, bounds.center.y - instance.transform.position.y, 0f);

                GameObject variant = PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
                report.Add($"{treeName}: {height:0.0} m tree, becomes about {height * TreeScale:0} m");
                return variant;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// A copy of a small plant with its mesh moved onto the root, as terrain detail painting needs.
        /// The child's offset, rotation and scale are baked into a mesh copy.
        /// </summary>
        static GameObject DetailCopy(string plantName, GameObject source, List<string> report)
        {
            string prefabPath = $"{OutputFolder}/Detail_{plantName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
            {
                report.Add($"{plantName}: ground plant (existing copy)");
                return existing;
            }

            MeshFilter filter = null;
            foreach (MeshFilter candidate in source.GetComponentsInChildren<MeshFilter>())
            {
                // Prefer the most detailed LOD if there are several.
                if (filter == null || candidate.name.Contains("LOD0"))
                    filter = candidate;
            }
            if (filter == null || filter.sharedMesh == null)
                throw new InvalidOperationException("no mesh found");
            Material[] materials = filter.GetComponent<MeshRenderer>().sharedMaterials;

            Matrix4x4 toRoot = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Mesh mesh = BakeMesh(filter.sharedMesh, toRoot);
            mesh.name = $"{plantName}_Detail";
            AssetDatabase.CreateAsset(mesh, $"{OutputFolder}/Detail_{plantName}_Mesh.asset");

            var root = new GameObject(plantName);
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterials = materials;
                foreach (Material material in materials)
                {
                    // Detail meshes are drawn with GPU instancing.
                    if (material != null && !material.enableInstancing)
                    {
                        material.enableInstancing = true;
                        EditorUtility.SetDirty(material);
                    }
                }
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                report.Add($"{plantName}: ground plant, {mesh.bounds.size.y:0.00} m tall");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static Mesh BakeMesh(Mesh source, Matrix4x4 transform)
        {
            Mesh mesh = Object.Instantiate(source);
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            Matrix4x4 normalMatrix = transform.inverse.transpose;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = transform.MultiplyPoint3x4(vertices[i]);
            for (int i = 0; i < normals.Length; i++)
                normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 direction = transform.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
                tangents[i] = new Vector4(direction.x, direction.y, direction.z, tangents[i].w);
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>World-space bounds of the tree's most detailed meshes.</summary>
        static Bounds DetailedMeshBounds(GameObject tree)
        {
            Renderer[] renderers = tree.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(tree.transform.position, Vector3.zero);
            foreach (Renderer meshRenderer in renderers)
            {
                // LOD0 is the full tree; distant LODs are flat cards that can be oddly sized.
                if (renderers.Length > 1 && !meshRenderer.name.Contains("LOD0"))
                    continue;
                bounds.Encapsulate(meshRenderer.bounds);
            }
            return bounds;
        }
    }
}
