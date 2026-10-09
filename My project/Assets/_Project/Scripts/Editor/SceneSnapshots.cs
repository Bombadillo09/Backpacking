using System.Collections.Generic;
using System.IO;
using Backpacking.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Renders views of the built prototype scene to Logs/SceneSnapshots/*.png, for checking the world without
    /// playing (Logs, not Temp: Unity clears Temp when a batch run quits). Run with the AutoRebuild request
    /// "run:Backpacking.EditorTools.SceneSnapshots.RenderDrive", or from the command line with
    /// -executeMethod Backpacking.EditorTools.SceneSnapshots.RenderDriveBatch. The scene isn't saved afterwards.
    /// </summary>
    public static class SceneSnapshots
    {
        const string Folder = "Logs/SceneSnapshots";
        const string ScenePath = "Assets/_Project/Scenes/Prototype.unity";

        /// <summary>Home, the store, the road and the trailhead parking, inside and out.</summary>
        public static string RenderDrive()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var views = new List<(string name, Vector3 from, Vector3 at)>();

            GameObject home = GameObject.Find("Road/Home");
            if (home != null)
            {
                Transform t = home.transform;
                views.Add(("home-outside", t.TransformPoint(new Vector3(-5f, 2f, 14f)), t.TransformPoint(new Vector3(0f, 1.6f, 0f))));
                views.Add(("home-living", t.TransformPoint(new Vector3(0.3f, 1.7f, 3f)), t.TransformPoint(new Vector3(-3f, 0.8f, 1.3f))));
                views.Add(("home-kitchen", t.TransformPoint(new Vector3(-0.5f, 1.7f, 0.8f)), t.TransformPoint(new Vector3(3.3f, 1f, 2.4f))));
                views.Add(("home-bedroom", t.TransformPoint(new Vector3(0.4f, 1.7f, -0.4f)), t.TransformPoint(new Vector3(-2.2f, 0.6f, -2.6f))));
                views.Add(("home-bathroom", t.TransformPoint(new Vector3(1.3f, 1.7f, -0.4f)), t.TransformPoint(new Vector3(3.3f, 0.8f, -2.6f))));
            }
            GameObject store = GameObject.Find($"Road/{Trip.TripLog.Outfitter}");
            if (store != null)
            {
                Transform t = store.transform;
                views.Add(("store-outside", t.TransformPoint(new Vector3(5f, 1.7f, 19f)), t.TransformPoint(new Vector3(0f, 2.5f, 0f))));
                views.Add(("store-inside", t.TransformPoint(new Vector3(0f, 1.75f, 3.8f)), t.TransformPoint(new Vector3(0f, 1.2f, -3f))));
                views.Add(("store-above", t.TransformPoint(new Vector3(-40f, 45f, 70f)), t.TransformPoint(new Vector3(0f, 0f, 10f))));
            }
            RoadPath road = Object.FindAnyObjectByType<RoadPath>();
            if (road != null && road.Points.Count > 10)
            {
                int middle = road.Points.Count / 3;
                Vector3 here = road.Points[middle] + Vector3.up * 1.7f;
                views.Add(("road", here, road.Points[middle + 6] + Vector3.up * 1.2f));
            }
            GameObject kiosk = GameObject.Find("Road/Trailhead Kiosk");
            if (kiosk != null)
            {
                Transform t = kiosk.transform;
                views.Add(("parking", t.TransformPoint(new Vector3(4f, 1.7f, 16f)), t.TransformPoint(new Vector3(6f, 1.2f, 0f))));
            }
            GameObject truck = GameObject.Find("Pickup");
            if (truck != null)
            {
                Transform t = truck.transform;
                views.Add(("truck", t.TransformPoint(new Vector3(-4.5f, 1.8f, 5.5f)), t.TransformPoint(new Vector3(0f, 1f, 0f))));
                views.Add(("truck-back", t.TransformPoint(new Vector3(-6f, 2.4f, -5f)), t.TransformPoint(new Vector3(0f, 0.9f, -1f))));
                // From the driver's eyes, looking out of the windscreen.
                Transform seat = t.Find("Driver Seat");
                Vector3 eye = seat != null ? seat.position + t.up * 1.12f : t.TransformPoint(new Vector3(-0.42f, 0.58f + 1.12f, 0.12f));
                views.Add(("truck-cab", eye, eye + t.forward * 10f - t.up * 0.8f));
                views.Add(("truck-cab-side", eye, eye + t.forward * 4f + t.right * 6f - t.up * 1.5f));
            }
            if (views.Count == 0)
                return "Nothing to render: no road in the scene. Rebuild it first.";

            // The sun is set by the time of day at runtime; light the views as mid-morning.
            Light sun = GameObject.Find("Sun")?.GetComponent<Light>();
            if (sun != null)
                sun.transform.rotation = Quaternion.Euler(40f, 140f, 0f);

            Directory.CreateDirectory(Folder);
            var cameraObject = new GameObject("Snapshot Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 3000f;
            camera.fieldOfView = 65f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                foreach ((string name, Vector3 from, Vector3 at) in views)
                {
                    camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    image.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes($"{Folder}/{name}.png", image.EncodeToPNG());
                }
            }
            finally
            {
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
            }
            return $"Rendered {views.Count} views to {Folder}.";
        }

        public static void RenderDriveBatch() => Debug.Log(RenderDrive());

        /// <summary>
        /// Home's things to take (the daypack, and the fridge and cupboard standing open with the water and trail mix),
        /// and a lit campfire by day and by night: "run:Backpacking.EditorTools.SceneSnapshots.RenderHomeAndFire".
        /// </summary>
        public static string RenderHomeAndFire()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject home = GameObject.Find("Road/Home");
            if (home == null)
                return "No home in the scene. Rebuild it first.";
            Transform t = home.transform;
            // Swing the fridge and cupboard doors open (Door only moves them in play), and build the daypack's look.
            foreach (Door door in home.GetComponentsInChildren<Door>())
            {
                if (door.DisplayName is not ("Fridge" or "Kitchen cupboard"))
                    continue;
                SerializedProperty leaves = new SerializedObject(door).FindProperty("leaves");
                for (int i = 0; i < leaves.arraySize; i++)
                {
                    SerializedProperty leaf = leaves.GetArrayElementAtIndex(i);
                    var hinge = (Transform)leaf.FindPropertyRelative("hinge").objectReferenceValue;
                    hinge.localRotation = Quaternion.Euler(0f, leaf.FindPropertyRelative("openAngle").floatValue, 0f);
                }
            }
            foreach (Interaction.ItemPickup pickup in home.GetComponentsInChildren<Interaction.ItemPickup>())
                typeof(Interaction.ItemPickup).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(pickup, null);

            var views = new List<(string name, Vector3 from, Vector3 at)>
            {
                ("home-fridge-open", t.TransformPoint(new Vector3(1.6f, 1.55f, 0.9f)), t.TransformPoint(new Vector3(3.4f, 0.8f, 0.45f))),
                ("home-cupboard-open", t.TransformPoint(new Vector3(1.9f, 1.75f, 1.6f)), t.TransformPoint(new Vector3(3.6f, 1.75f, 1.9f))),
                ("home-daypack", t.TransformPoint(new Vector3(-0.7f, 1.65f, -1.6f)), t.TransformPoint(new Vector3(-0.6f, 0.35f, -0.25f))),
            };

            // A fire, lit, on open ground in front of the house.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Camp/Fire Ring (layered).prefab");
            GameObject fire = prefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab) : null;
            Vector3 firePlace = t.TransformPoint(new Vector3(-8f, 0f, 1f));
            firePlace.y = GroundCover.HeightAt(firePlace);
            if (fire != null)
            {
                fire.transform.position = firePlace;
                Transform coals = fire.transform.Find("Coals");
                if (coals != null)
                    coals.gameObject.SetActive(true);
                fire.GetComponentInChildren<Light>().enabled = true;
                ParticleSystem flames = fire.transform.Find("Flames").GetComponent<ParticleSystem>();
                flames.Simulate(3f, true, true);
                views.Add(("fire-day", firePlace + new Vector3(1.8f, 1.2f, 1.6f), firePlace + Vector3.up * 0.45f));
                views.Add(("fire-close", firePlace + new Vector3(0.9f, 0.7f, 0.8f), firePlace + Vector3.up * 0.3f));
            }

            Light sun = GameObject.Find("Sun")?.GetComponent<Light>();
            if (sun != null)
                sun.transform.rotation = Quaternion.Euler(40f, 140f, 0f);
            string result = Render(views, "changes");
            if (fire != null && sun != null)
            {
                // Night: no sun, and only a little light from the sky.
                sun.enabled = false;
                Color ambient = RenderSettings.ambientLight;
                float intensity = RenderSettings.ambientIntensity;
                RenderSettings.ambientLight = new Color(0.04f, 0.05f, 0.08f);
                RenderSettings.ambientIntensity = 0.15f;
                result += " " + Render(new List<(string, Vector3, Vector3)> { ("fire-night", firePlace + new Vector3(2.2f, 1.4f, 2f), firePlace + Vector3.up * 0.45f) }, "night");
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientIntensity = intensity;
            }
            return result;
        }

        /// <summary>
        /// The rivers, a footbridge, the crags and a lantern lit at night: "run:Backpacking.EditorTools.SceneSnapshots.RenderLandscape",
        /// or -executeMethod Backpacking.EditorTools.SceneSnapshots.RenderLandscapeBatch.
        /// </summary>
        public static string RenderLandscape()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrain = Object.FindAnyObjectByType<Terrain>();
            var views = new List<(string name, Vector3 from, Vector3 at)>();
            Vector3 Ground(Vector3 at) => new(at.x, terrain.SampleHeight(at) + terrain.transform.position.y, at.z);

            Transform rivers = GameObject.Find("Rivers")?.transform;
            if (rivers != null)
            {
                Transform bridge = null;
                foreach (Transform child in rivers)
                    if (child.name == "Footbridge" && bridge == null)
                        bridge = child;
                if (bridge != null)
                {
                    views.Add(("river-bridge", bridge.position - bridge.forward * 9f + bridge.right * 5f + Vector3.up * 2.2f, bridge.position));
                    views.Add(("river-bridge-on", bridge.position - bridge.forward * 3f + Vector3.up * 1.7f, bridge.position + bridge.forward * 3f + bridge.right * 4f - Vector3.up * 0.6f));
                }
                // Partway down each river and a stream, looking along the water.
                int shown = 0;
                foreach (Transform river in rivers)
                {
                    if (river.name == "Footbridge" || shown >= 3)
                        continue;
                    var stretches = new List<Transform>();
                    foreach (Transform part in river)
                        if (part.GetComponent<Camp.WaterSource>() != null)
                            stretches.Add(part);
                    if (stretches.Count < 10)
                        continue;
                    Transform middle = stretches[stretches.Count / 2];
                    Vector3 bank = Ground(middle.position + middle.right * 9f);
                    views.Add(($"water-{shown}-{river.name.ToLowerInvariant()}", bank + Vector3.up * 1.7f - middle.forward * 6f, middle.position + middle.forward * 14f));
                    shown++;
                }
            }
            Transform crags = GameObject.Find("Crags")?.transform;
            if (crags != null && crags.childCount > 0)
                for (int i = 0; i < 2; i++)
                {
                    Transform rock = crags.GetChild(crags.childCount * (i + 1) / 3);
                    Vector3 from = Ground(rock.position + new Vector3(45f, 0f, 30f)) + Vector3.up * 1.8f;
                    views.Add(($"crag-{i}", from, rock.position + Vector3.up * 3f));
                }

            Light sun = GameObject.Find("Sun")?.GetComponent<Light>();
            if (sun != null)
                sun.transform.rotation = Quaternion.Euler(40f, 140f, 0f);
            string result = Render(views, "landscape");

            // A lantern at night, by the house.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Camp/Camp Lantern.prefab");
            GameObject home = GameObject.Find("Road/Home");
            if (prefab != null && home != null && sun != null)
            {
                Vector3 place = Ground(home.transform.TransformPoint(new Vector3(-8f, 0f, 1f)));
                var lantern = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                lantern.transform.position = place;
                lantern.GetComponentInChildren<Light>().enabled = true;
                sun.enabled = false;
                RenderSettings.ambientLight = new Color(0.04f, 0.05f, 0.08f);
                RenderSettings.ambientIntensity = 0.15f;
                result += " " + Render(new List<(string, Vector3, Vector3)>
                {
                    ("lantern-night", place + new Vector3(2.5f, 1.6f, 2.5f), place + Vector3.up * 0.2f),
                    ("lantern-close", place + new Vector3(0.5f, 0.45f, 0.5f), place + Vector3.up * 0.16f),
                }, "lantern");
            }
            return result;
        }

        public static void RenderLandscapeBatch() => Debug.Log(RenderLandscape());

        /// <summary>The haze among the trees, run on for a while at points on the trail in the woods, by day: -executeMethod ...RenderForestHazeBatch.</summary>
        public static void RenderForestHazeBatch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var trail = Object.FindAnyObjectByType<TrailPath>();
            ParticleSystem haze = GameObject.Find("Forest Haze")?.GetComponent<ParticleSystem>();
            if (trail == null || haze == null)
            {
                Debug.Log("No trail or haze: rebuild first.");
                return;
            }
            Light sun = GameObject.Find("Sun")?.GetComponent<Light>();
            if (sun != null)
                sun.transform.rotation = Quaternion.Euler(40f, 140f, 0f);
            // Under the trees, the air is the woods' green-grey (as TimeOfDay sets it there).
            RenderSettings.fogColor = new Color(0.36f, 0.42f, 0.38f);
            RenderSettings.fogDensity *= 3.2f;
            Transform player = haze.transform.parent;
            var views = new List<(string, Vector3, Vector3)>();
            IReadOnlyList<Vector3> points = trail.Points;
            ParticleSystem.EmissionModule emission = haze.emission;
            emission.rateOverTime = 5f;
            ParticleSystem.MainModule main = haze.main;
            main.startColor = new Color(0.59f, 0.68f, 0.62f, 0.1f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 at = points[points.Count * (i + 1) / 4];
                Vector3 ahead = points[Mathf.Min(points.Count - 1, points.Count * (i + 1) / 4 + 3)];
                player.position = at;
                haze.Clear();
                haze.Simulate(25f, true, true);
                Debug.Log(Render(new List<(string, Vector3, Vector3)> { ($"forest-haze-{i}", at + Vector3.up * 1.7f, ahead + Vector3.up * 1.4f) }, "haze"));
            }
        }

        static string Render(List<(string name, Vector3 from, Vector3 at)> views, string what)
        {
            Directory.CreateDirectory(Folder);
            var cameraObject = new GameObject("Snapshot Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 3000f;
            camera.fieldOfView = 65f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                foreach ((string name, Vector3 from, Vector3 at) in views)
                {
                    camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    image.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes($"{Folder}/{name}.png", image.EncodeToPNG());
                }
            }
            finally
            {
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
            }
            return $"Rendered {views.Count} {what} views to {Folder}.";
        }
    }
}
