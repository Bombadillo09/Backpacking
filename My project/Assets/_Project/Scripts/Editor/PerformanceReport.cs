using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// What the open scene costs to run: objects, scripts that tick every frame, renderers and triangles,
    /// colliders, lights, terrain and shadow settings. "run:Backpacking.EditorTools.PerformanceReport.Scene".
    /// </summary>
    public static class PerformanceReport
    {
        public static string Scene()
        {
            var report = new StringBuilder();
            GameObject[] all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            report.AppendLine($"GameObjects: {all.Length} ({all.Count(g => g.activeInHierarchy)} active)");

            // Scripts with per-frame methods, by type.
            var ticking = new Dictionary<string, int>();
            foreach (MonoBehaviour script in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                System.Type type = script.GetType();
                const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                if (type.GetMethod("Update", any) == null && type.GetMethod("LateUpdate", any) == null && type.GetMethod("FixedUpdate", any) == null)
                    continue;
                ticking[type.Name] = ticking.TryGetValue(type.Name, out int n) ? n + 1 : 1;
            }
            report.AppendLine($"Scripts ticking each frame: {ticking.Values.Sum()} — " + string.Join(", ", ticking.OrderByDescending(t => t.Value).Take(12).Select(t => $"{t.Key} {t.Value}")));

            // Renderers and triangles, grouped by mesh name, most triangles first.
            var meshTris = new Dictionary<string, (int count, long tris, bool shadows)>();
            int renderers = 0, shadowCasters = 0, lodGroups = Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Length;
            long triangles = 0;
            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                var filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;
                renderers++;
                if (renderer.shadowCastingMode != ShadowCastingMode.Off)
                    shadowCasters++;
                long tris = 0;
                for (int s = 0; s < mesh.subMeshCount; s++)
                    tris += mesh.GetIndexCount(s) / 3;
                triangles += tris;
                meshTris.TryGetValue(mesh.name, out var entry);
                meshTris[mesh.name] = (entry.count + 1, entry.tris + tris, renderer.shadowCastingMode != ShadowCastingMode.Off);
            }
            report.AppendLine($"Mesh renderers: {renderers} ({shadowCasters} cast shadows), LOD groups: {lodGroups}, triangles if all drawn: {triangles:N0}");
            foreach (var mesh in meshTris.OrderByDescending(m => m.Value.tris).Take(12))
                report.AppendLine($"  {mesh.Key}: {mesh.Value.count} × {mesh.Value.tris / Mathf.Max(1, mesh.Value.count):N0} tris = {mesh.Value.tris:N0}{(mesh.Value.shadows ? ", shadows" : "")}");

            // Colliders.
            var colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            report.AppendLine("Colliders: " + string.Join(", ", colliders.GroupBy(c => c.GetType().Name).Select(g => $"{g.Key} {g.Count()}")));

            // Lights.
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                report.AppendLine($"Light {light.name}: {light.type}, shadows {light.shadows}, {(light.enabled ? "on" : "off")}");
            report.AppendLine($"Particle systems: {Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length}, audio sources: {Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length}");

            // Terrain.
            foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                TerrainData data = terrain.terrainData;
                long details = 0;
                for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
                {
                    int[,] map = data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, layer);
                    foreach (int value in map)
                        details += value;
                }
                report.AppendLine($"Terrain {terrain.name}: {data.size}, heightmap {data.heightmapResolution}, trees {data.treeInstanceCount} ({data.treePrototypes.Length} kinds), " +
                                  $"detail layers {data.detailPrototypes.Length} at {data.detailWidth}² ({details:N0} plants), detail density {terrain.detailObjectDensity}, " +
                                  $"detail distance {terrain.detailObjectDistance}, tree distance {terrain.treeDistance}, billboards from {terrain.treeBillboardDistance}, " +
                                  $"pixel error {terrain.heightmapPixelError}, basemap {terrain.basemapDistance}, shadows {terrain.shadowCastingMode}, " +
                                  $"draw instanced {terrain.drawInstanced}, layers {data.terrainLayers.Length}, alphamap {data.alphamapResolution}");
                foreach (TreePrototype tree in data.treePrototypes)
                {
                    GameObject prefab = tree.prefab;
                    var lod = prefab != null ? prefab.GetComponent<LODGroup>() : null;
                    long tris = 0;
                    if (prefab != null)
                        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>())
                            if (filter.sharedMesh != null)
                                tris += filter.sharedMesh.triangles.Length / 3;
                    report.AppendLine($"  tree {prefab?.name}: {tris:N0} tris (all LODs), LOD group {(lod != null ? lod.lodCount.ToString() : "none")}");
                    if (lod != null)
                        foreach (LOD level in lod.GetLODs())
                            report.AppendLine($"    LOD down to {level.screenRelativeTransitionHeight:0.###} of screen: " + string.Join("; ", level.renderers.Where(r => r != null).Select(r =>
                                $"{r.name} {(r.GetComponent<MeshFilter>()?.sharedMesh is Mesh m ? m.triangles.Length / 3 : 0)} tris, shadows {r.shadowCastingMode}, mats " +
                                string.Join("/", r.sharedMaterials.Where(x => x != null).Select(x => $"{x.name} [{x.shader.name}, instancing {x.enableInstancing}]")))));
                }
            }

            // Render pipeline.
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                report.AppendLine($"URP: shadow distance {urp.shadowDistance}, cascades {urp.shadowCascadeCount}, main shadow res {urp.mainLightShadowmapResolution}, " +
                                  $"soft shadows {urp.supportsSoftShadows}, render scale {urp.renderScale}, MSAA {urp.msaaSampleCount}, HDR {urp.supportsHDR}, " +
                                  $"SRP batcher {urp.useSRPBatcher}, additional lights {urp.additionalLightsRenderingMode}");
            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                report.AppendLine($"Camera {camera.name}: far {camera.farClipPlane}, enabled {camera.enabled}");
            report.AppendLine($"Quality level {QualitySettings.names[QualitySettings.GetQualityLevel()]}: vsync {QualitySettings.vSyncCount}, LOD bias {QualitySettings.lodBias}");
            return report.ToString();
        }
    }
}
