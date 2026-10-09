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
    }
}
