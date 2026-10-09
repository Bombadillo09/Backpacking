using System;
using System.Linq;

namespace Backpacking.EditorTools
{
    /// <summary>Diagnostics for the rebuild hook: which networking assemblies the editor has loaded.</summary>
    public static class PackageCheck
    {
        public static string Run()
        {
            string[] wanted = { "Unity.Netcode.Runtime", "Unity.Networking.Transport", "Facepunch.Steamworks.Win64", "com.community.netcode.transport.facepunch" };
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet();
            return string.Join(", ", wanted.Select(name => $"{name}: {(loaded.Contains(name) ? "loaded" : "missing")}"));
        }

        /// <summary>A picture down into the cab from above (the roof cut away), with a ball on each seat and the steering wheel.</summary>
        public static string TruckSeatsPicture()
        {
            var truck = UnityEngine.Object.FindAnyObjectByType<Vehicles.Pickup>();
            if (truck == null)
                return "no truck";
            var made = new System.Collections.Generic.List<UnityEngine.GameObject>();
            UnityEngine.Color[] colours = { UnityEngine.Color.red, UnityEngine.Color.green, UnityEngine.Color.blue, UnityEngine.Color.yellow };
            for (int i = 0; i < truck.SeatCount; i++)
            {
                var ball = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Sphere);
                ball.transform.position = truck.SeatAt(i).position + UnityEngine.Vector3.up * 0.5f;
                ball.transform.localScale = UnityEngine.Vector3.one * 0.25f;
                var material = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetColor("_BaseColor", colours[i]);
                ball.GetComponent<UnityEngine.Renderer>().sharedMaterial = material;
                made.Add(ball);
            }
            var go = new UnityEngine.GameObject("Seat Camera");
            made.Add(go);
            var camera = go.AddComponent<UnityEngine.Camera>();
            UnityEngine.Transform t = truck.transform;
            go.transform.SetPositionAndRotation(t.position + t.up * 5f, UnityEngine.Quaternion.LookRotation(-t.up, t.forward));
            camera.nearClipPlane = 3.35f;
            camera.fieldOfView = 50f;
            var target = new UnityEngine.RenderTexture(1000, 1000, 24);
            camera.targetTexture = target;
            camera.Render();
            UnityEngine.RenderTexture.active = target;
            var picture = new UnityEngine.Texture2D(1000, 1000, UnityEngine.TextureFormat.RGB24, false);
            picture.ReadPixels(new UnityEngine.Rect(0, 0, 1000, 1000), 0, 0);
            UnityEngine.RenderTexture.active = null;
            System.IO.File.WriteAllBytes("Logs/SceneSnapshots/truck-seats-top.png", UnityEngine.ImageConversion.EncodeToPNG(picture));
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(target);
            foreach (UnityEngine.GameObject thing in made)
                UnityEngine.Object.DestroyImmediate(thing);
            return "Logs/SceneSnapshots/truck-seats-top.png (front of the truck at the top; red driver, green front passenger, blue rear left, yellow rear right)";
        }

        /// <summary>A hiker standing in the front passenger seat, seen from outside the truck and from the driver's seat.</summary>
        public static string PassengerPicture()
        {
            var truck = UnityEngine.Object.FindAnyObjectByType<Vehicles.Pickup>();
            var root = new UnityEngine.GameObject("Test Passenger");
            var avatar = new UnityEngine.GameObject("Avatar");
            avatar.transform.SetParent(root.transform, false);
            var appearance = avatar.AddComponent<Character.CharacterAppearance>();
            appearance.Library = CharacterSetup.GetOrCreateLibrary();
            appearance.Build(new Character.CharacterProfile { hiker = "Female_Adult_04" });
            root.transform.SetParent(truck.SeatAt(1), false);
            UnityEngine.Transform seat = truck.SeatAt(1), t = truck.transform;
            string outside = Render("passenger-outside", seat.position + t.right * 2.6f + UnityEngine.Vector3.up * 1.3f + t.forward * 0.6f, seat.position + UnityEngine.Vector3.up * 0.9f);
            string inside = Render("passenger-inside", truck.SeatAt(0).position + UnityEngine.Vector3.up * truck.Eye.y, seat.position + UnityEngine.Vector3.up * 0.9f);
            int renderers = root.GetComponentsInChildren<UnityEngine.Renderer>().Length;
            UnityEngine.Object.DestroyImmediate(root);
            return $"{renderers} renderers; {outside}, {inside}";
        }

        static string Render(string name, UnityEngine.Vector3 from, UnityEngine.Vector3 at)
        {
            var go = new UnityEngine.GameObject("Test Camera");
            var camera = go.AddComponent<UnityEngine.Camera>();
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.05f;
            go.transform.SetPositionAndRotation(from, UnityEngine.Quaternion.LookRotation(at - from));
            var target = new UnityEngine.RenderTexture(960, 540, 24);
            camera.targetTexture = target;
            camera.Render();
            UnityEngine.RenderTexture.active = target;
            var picture = new UnityEngine.Texture2D(960, 540, UnityEngine.TextureFormat.RGB24, false);
            picture.ReadPixels(new UnityEngine.Rect(0, 0, 960, 540), 0, 0);
            UnityEngine.RenderTexture.active = null;
            string path = $"Logs/SceneSnapshots/{name}.png";
            System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(picture));
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(go);
            return path;
        }

        /// <summary>Which materials the scene's truck parts use, and what the model's own parts use.</summary>
        public static string TruckMaterials()
        {
            var truck = UnityEngine.Object.FindAnyObjectByType<Vehicles.Pickup>();
            var lines = new System.Collections.Generic.List<string>();
            if (truck != null)
                lines.Add("scene: " + string.Join(", ", truck.GetComponentsInChildren<UnityEngine.Renderer>()
                    .SelectMany(r => r.sharedMaterials.Where(m => m != null).Select(m => m.name)).GroupBy(n => n).Select(g => $"{g.Key} x{g.Count()}")));
            var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/PickupTruck/Assets/Prefabs/Pickup.prefab");
            if (model != null)
                lines.Add("model: " + string.Join(", ", model.GetComponentsInChildren<UnityEngine.Renderer>(true)
                    .Select(r => $"{r.name}[{string.Join("/", r.sharedMaterials.Select(m => m != null ? m.name : "null"))}]")));
            return string.Join(" | ", lines);
        }
    }
}
