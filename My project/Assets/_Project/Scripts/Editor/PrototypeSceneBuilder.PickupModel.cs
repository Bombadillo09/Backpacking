using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// The pickup's body: the free "Pickup Truck" model from the Asset Store (Assets/PickupTruck, git-ignored like the
    /// other store packs) when it's in the project, else the truck built from shapes. The model is fitted to the
    /// truck's space (scaled to real size, turned to face +Z, its wheels found and given pivots to spin on, the cab
    /// found from its windows) and repainted in rusty, faded red with URP materials.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        /// <summary>The built-from-shapes truck, with its layout.</summary>
        static TruckLayout BuildCodeTruck(GameObject truck, Material paint, Material glass)
        {
            Material trim = GetOrCreateMaterial("TruckTrim", new Color(0.62f, 0.63f, 0.64f), 0.6f);
            Material black = GetOrCreateMaterial("TruckBlack", new Color(0.06f, 0.06f, 0.06f), 0.2f);
            Material interior = GetOrCreateMaterial("TruckInterior", new Color(0.2f, 0.19f, 0.18f), 0.1f);
            Material bedLiner = GetOrCreateMaterial("TruckBedLiner", new Color(0.12f, 0.12f, 0.12f), 0.05f);
            Material headlight = GetOrCreateEmissiveMaterial("TruckHeadlight", new Color(1f, 0.95f, 0.8f));
            Material taillight = GetOrCreateEmissiveMaterial("TruckTaillight", new Color(0.7f, 0.05f, 0.03f));

            BuildCab(truck, paint, black, interior, glass, trim);
            BuildFront(truck, paint, black, trim, headlight);
            BuildBed(truck, paint, bedLiner, trim, taillight, black);
            BuildDetails(truck, paint, black, trim, interior, headlight, taillight);
            BuildMoreDetails(truck, paint, black, trim, interior);

            var layout = new TruckLayout
            {
                WheelRadius = WheelRadius,
                Seat = new Vector3(-0.42f, CabFloor, 0.12f),
                DriverExit = new Vector3(-1.55f, 0f, 0.4f),
                PassengerExit = new Vector3(1.55f, 0f, 0.4f),
                Headlights = new[] { new Vector3(-0.68f, 1f, 2.7f), new Vector3(0.68f, 1f, 2.7f) },
                BedCentre = new Vector3(0f, 0.95f, -1.5f),
                SteeringWheel = BuildSteeringWheel(truck, black),
                LeftDoor = (new Vector3(-0.99f, 1.15f, 0.47f), new Vector3(0.1f, 1.2f, 1.4f)),
                RightDoor = (new Vector3(0.99f, 1.15f, 0.47f), new Vector3(0.1f, 1.2f, 1.4f)),
                Tailgate = (new Vector3(0f, 1.08f, -2.68f), new Vector3(1.8f, 0.55f, 0.12f)),
            };
            Vector2[] corners = { new(-TrackX, AxleZ), new(TrackX, AxleZ), new(-TrackX, -AxleZ), new(TrackX, -AxleZ) };
            for (int i = 0; i < 4; i++)
                layout.WheelCentres[i] = new Vector3(corners[i].x, WheelRadius, corners[i].y);
            layout.Boxes.AddRange(new[]
            {
                (new Vector3(0f, 1.27f, 0.47f), new Vector3(1.95f, 1.38f, 1.65f)),
                (new Vector3(0f, 1.0f, 1.97f), new Vector3(1.95f, 0.56f, 1.35f)),
                (new Vector3(0f, 0.7f, 1.97f), new Vector3(1.4f, 0.3f, 1.35f)),
                (new Vector3(0f, 0.88f, -1.5f), new Vector3(1.95f, 0.1f, 2.3f)),
                (new Vector3(-0.94f, 1.08f, -1.5f), new Vector3(0.07f, 0.5f, 2.3f)),
                (new Vector3(0.94f, 1.08f, -1.5f), new Vector3(0.07f, 0.5f, 2.3f)),
                (new Vector3(0f, 1.08f, -2.62f), new Vector3(1.95f, 0.5f, 0.07f)),
                (new Vector3(0f, 0.62f, -1.5f), new Vector3(1.2f, 0.18f, 2.6f)),
            });
            return layout;
        }

        /// <summary>The Asset Store truck fitted into the truck's space, or null if it isn't in the project.</summary>
        static TruckLayout BuildModelTruck(GameObject truck, Material paint, Material glass)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TruckModelPrefab);
            if (prefab == null)
                return null;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(truck.transform, false);
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;
            foreach (Component extra in model.GetComponentsInChildren<Component>(true))
                if (extra is Collider or Rigidbody or Light or Camera or AudioSource or MonoBehaviour)
                    Object.DestroyImmediate(extra);

            // Remember what each part was made of (the names say what it is), then repaint it.
            var parts = new List<(Renderer renderer, HashSet<string> materials)>();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                Material[] materials = renderer.sharedMaterials;
                var names = new HashSet<string>();
                foreach (Material material in materials)
                    if (material != null)
                        names.Add(material.name);
                parts.Add((renderer, names));
                // The cab and doors use the model file's own material, which can come in empty (it depends on how
                // Unity imports the .obj without its .mtl): those panels are bodywork too.
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = materials[i] == null ? paint : ModelMaterial(materials[i].name, paint, glass);
                renderer.sharedMaterials = materials;
            }

            // Real size, and facing +Z: the headlights are at the front.
            Bounds whole = LocalBounds(truck.transform, parts.ConvertAll(part => part.renderer));
            float scale = TruckLength / Mathf.Max(whole.size.x, whole.size.z);
            model.transform.localScale = Vector3.one * scale;
            List<Renderer> lamps = parts.FindAll(part => part.materials.Contains("Lights")).ConvertAll(part => part.renderer);
            if (lamps.Count > 0 && LocalBounds(truck.transform, lamps).center.z < LocalBounds(truck.transform, parts.ConvertAll(part => part.renderer)).center.z)
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Sit the tyres on the ground with the truck's origin midway between the axles.
            List<Renderer> tyres = parts.FindAll(part => part.materials.Contains("Tires")).ConvertAll(part => part.renderer);
            if (tyres.Count != 4)
            {
                Debug.LogWarning($"The pickup model has {tyres.Count} tyres, not 4; using the built truck instead.");
                Object.DestroyImmediate(model);
                return null;
            }
            Vector3 middle = Vector3.zero;
            float bottom = float.MaxValue;
            foreach (Renderer tyre in tyres)
            {
                Bounds bounds = LocalBounds(truck.transform, new List<Renderer> { tyre });
                middle += bounds.center / 4f;
                bottom = Mathf.Min(bottom, bounds.min.y);
            }
            model.transform.localPosition -= new Vector3(middle.x, bottom, middle.z);

            var layout = new TruckLayout { WheelVisuals = new Transform[4] };
            var centres = new List<(Vector3 centre, float radius)>();
            foreach (Renderer tyre in tyres)
            {
                Bounds bounds = LocalBounds(truck.transform, new List<Renderer> { tyre });
                centres.Add((bounds.center, bounds.extents.y));
            }
            // Front left, front right, rear left, rear right.
            centres.Sort((a, b) => a.centre.z > 0f == b.centre.z > 0f ? a.centre.x.CompareTo(b.centre.x) : b.centre.z.CompareTo(a.centre.z));
            for (int i = 0; i < 4; i++)
            {
                layout.WheelCentres[i] = centres[i].centre;
                layout.WheelRadius += centres[i].radius / 4f;
            }

            // Everything that turns with a wheel (tyre, rim, nuts, brake disc) goes on a pivot at its centre.
            for (int i = 0; i < 4; i++)
            {
                var pivot = new GameObject($"Wheel Visual {i}").transform;
                pivot.SetParent(truck.transform, false);
                pivot.localPosition = layout.WheelCentres[i];
                layout.WheelVisuals[i] = pivot;
            }
            foreach ((Renderer renderer, HashSet<string> _) in parts)
            {
                Bounds bounds = LocalBounds(truck.transform, new List<Renderer> { renderer });
                for (int i = 0; i < 4; i++)
                {
                    Vector3 offset = bounds.center - layout.WheelCentres[i];
                    if (new Vector2(offset.y, offset.z).magnitude < layout.WheelRadius * 0.9f && Mathf.Abs(offset.x) < 0.2f
                        && bounds.size.y < layout.WheelRadius * 2.3f && bounds.size.z < layout.WheelRadius * 2.3f)
                    {
                        renderer.transform.SetParent(layout.WheelVisuals[i], true);
                        break;
                    }
                }
            }

            // The cab is where the windows are (the glass up high, not the lamp covers).
            Bounds body = LocalBounds(truck.transform, parts.FindAll(part => part.materials.Contains("Body")).ConvertAll(part => part.renderer));
            whole = LocalBounds(truck.transform, parts.ConvertAll(part => part.renderer));
            float high = whole.min.y + whole.size.y * 0.55f;
            List<Renderer> windows =  parts.FindAll(part => part.materials.Contains("Glass") && part.renderer.bounds.center.y - truck.transform.position.y > high)
                .ConvertAll(part => part.renderer);
            Bounds cab = windows.Count > 0 ? LocalBounds(truck.transform, windows) : new Bounds(new Vector3(0f, whole.max.y - 0.35f, whole.center.z + 0.4f), new Vector3(1.6f, 0.6f, 1.6f));
            float halfWidth = body.size.x > 0.1f ? body.extents.x : whole.extents.x;
            float belt = cab.min.y, roof = whole.max.y, front = whole.max.z, rear = whole.min.z;
            float bedFloor = Mathf.Lerp(layout.WheelRadius * 2f, belt, 0.25f);
            float cabMiddle = cab.center.z;

            layout.Eye = new Vector3(0f, 1.12f, 0f);
            float eyeHeight = belt + 0.3f;
            // The front seats' cushions are just ahead of the middle of the cab's glass; the back seat sits against the
            // rear of the cab (both checked from above with PackageCheck.TruckSeatsPicture).
            float rearSeat = cabMiddle - 0.1f - Mathf.Clamp(cabMiddle - 0.1f - (cab.min.z + 0.4f), 0.6f, 1f);
            layout.Seat = new Vector3(-halfWidth * 0.42f, eyeHeight - layout.Eye.y, cabMiddle + 0.1f);
            layout.RearSeatBack = layout.Seat.z - rearSeat;
            layout.DriverExit = new Vector3(-halfWidth - 0.65f, 0f, cabMiddle);
            layout.PassengerExit = new Vector3(halfWidth + 0.65f, 0f, cabMiddle);
            float lampHeight = lamps.Count > 0 ? LocalBounds(truck.transform, lamps).center.y : belt - 0.3f;
            layout.Headlights = new[] { new Vector3(-halfWidth + 0.3f, lampHeight, front - 0.05f), new Vector3(halfWidth - 0.3f, lampHeight, front - 0.05f) };
            layout.BedCentre = new Vector3(0f, bedFloor + 0.1f, (rear + cab.min.z) / 2f);
            layout.CentreOfMass = new Vector3(0f, layout.WheelRadius * 1.3f, cabMiddle * 0.4f);

            float under = layout.WheelRadius * 1.25f;
            float cabBack = cab.min.z - 0.05f, cabFront = cab.max.z + 0.1f;
            layout.Boxes.AddRange(new[]
            {
                // Chassis the whole length, the cab, the hood, and the bed's floor, sides and tailgate.
                (new Vector3(0f, (under + bedFloor) / 2f, (front + rear) / 2f), new Vector3(halfWidth * 2f - 0.1f, bedFloor - under, front - rear - 0.1f)),
                (new Vector3(0f, (bedFloor + roof) / 2f, (cabBack + cabFront) / 2f), new Vector3(halfWidth * 2f, roof - bedFloor, cabFront - cabBack)),
                (new Vector3(0f, (bedFloor + belt) / 2f, (cabFront + front) / 2f), new Vector3(halfWidth * 2f, belt - bedFloor, front - cabFront)),
                (new Vector3(-halfWidth + 0.04f, (bedFloor + belt) / 2f, (rear + cabBack) / 2f), new Vector3(0.08f, belt - bedFloor, cabBack - rear)),
                (new Vector3(halfWidth - 0.04f, (bedFloor + belt) / 2f, (rear + cabBack) / 2f), new Vector3(0.08f, belt - bedFloor, cabBack - rear)),
                (new Vector3(0f, (bedFloor + belt) / 2f, rear + 0.05f), new Vector3(halfWidth * 2f, belt - bedFloor, 0.1f)),
            });
            float doorLength = (cab.max.z - cab.min.z) * 0.75f;
            layout.LeftDoor = (new Vector3(-halfWidth - 0.03f, (bedFloor + roof) / 2f, cabMiddle), new Vector3(0.12f, roof - bedFloor - 0.2f, doorLength));
            layout.RightDoor = (new Vector3(halfWidth + 0.03f, (bedFloor + roof) / 2f, cabMiddle), new Vector3(0.12f, roof - bedFloor - 0.2f, doorLength));
            layout.Tailgate = (new Vector3(0f, (bedFloor + belt) / 2f, rear - 0.04f), new Vector3(halfWidth * 1.8f, belt - bedFloor + 0.2f, 0.14f));
            Debug.Log($"Pickup model fitted: scale {scale:0.###}, wheel radius {layout.WheelRadius:0.00} m, track {layout.WheelCentres[1].x - layout.WheelCentres[0].x:0.00} m, "
                      + $"wheelbase {layout.WheelCentres[0].z - layout.WheelCentres[2].z:0.00} m, belt {belt:0.00} m, roof {roof:0.00} m.");
            return layout;
        }

        /// <summary>The bounds of some renderers in a transform's space (it's axis-aligned there).</summary>
        static Bounds LocalBounds(Transform space, List<Renderer> renderers)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = world.center + Vector3.Scale(world.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Vector3 local = space.InverseTransformPoint(point);
                    if (!any)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        any = true;
                    }
                    else
                        bounds.Encapsulate(local);
                }
            }
            return bounds;
        }

        /// <summary>Our URP material for one of the model's (built-in pipeline) materials, by its name.</summary>
        static Material ModelMaterial(string name, Material paint, Material glass) => name switch
        {
            "Body" => paint,
            "Glass" => glass,
            "Tires" => GetOrCreateMaterial("TruckBlack", new Color(0.06f, 0.06f, 0.06f), 0.2f),
            "Wheel" => GetOrCreateMaterial("TruckRim", new Color(0.32f, 0.3f, 0.28f), 0.35f),
            "Interior" => GetOrCreateMaterial("TruckInterior", new Color(0.2f, 0.19f, 0.18f), 0.1f),
            "Lights" or "Lights2" => GetOrCreateEmissiveMaterial("TruckHeadlight", new Color(1f, 0.95f, 0.8f)),
            "Brake1" or "Brake2" or "RearLights" => GetOrCreateEmissiveMaterial("TruckTaillight", new Color(0.7f, 0.05f, 0.03f)),
            "TurnLights" => GetOrCreateEmissiveMaterial("TruckMarker", new Color(1f, 0.55f, 0.1f)),
            "Mirror" => GetOrCreateMaterial("Mirror", new Color(0.6f, 0.68f, 0.72f), 0.95f),
            "Front2" => GetOrCreateMaterial("TruckBlack", new Color(0.06f, 0.06f, 0.06f), 0.2f),
            // Chrome gone dull with age.
            "Front" or "FrontBar" or "Screws" => GetOrCreateMaterial("TruckOldChrome", new Color(0.5f, 0.49f, 0.46f), 0.45f),
            "Brakes" => GetOrCreateMaterial("TruckBrakeDisc", new Color(0.36f, 0.3f, 0.26f), 0.3f),
            _ => GetOrCreateMaterial("TruckGrey", new Color(0.36f, 0.36f, 0.36f), 0.3f),
        };

        // ---------- Rusty, faded red paint ----------

        /// <summary>
        /// Old red paint, sun-faded to a chalky brick red, worn through to rust in patches (darker brown pits with
        /// orange edges) and to grey primer here and there. Generated once into Generated/TruckRust.png.
        /// </summary>
        static Material GetOrCreateRustPaint()
        {
            string texturePath = $"{GeneratedFolder}/TruckRust.png";
            if (!File.Exists(texturePath))
            {
                File.WriteAllBytes(texturePath, RustTexture(1024).EncodeToPNG());
                AssetDatabase.ImportAsset(texturePath);
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            Material material = GetOrCreateMaterial("TruckPaint", Color.white, 0.25f);
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.22f);
            material.SetFloat("_Metallic", 0.15f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D RustTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color[size * size];
            var faded = new Color(0.58f, 0.24f, 0.2f);
            var bright = new Color(0.66f, 0.2f, 0.15f);
            var chalk = new Color(0.68f, 0.42f, 0.38f);
            var rust = new Color(0.36f, 0.18f, 0.08f);
            var rustEdge = new Color(0.62f, 0.32f, 0.12f);
            var primer = new Color(0.52f, 0.5f, 0.47f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float tone = TileableValueNoise(u, v, 6) * 0.6f + TileableValueNoise(u, v, 24) * 0.4f;
                Color colour = Color.Lerp(bright, faded, tone);
                // Chalky oxidation: the old paint's faded, powdery bloom.
                colour = Color.Lerp(colour, chalk, Mathf.SmoothStep(0.55f, 0.85f, TileableValueNoise(u, v, 12)) * 0.5f);
                // Rust patches: blotches where the paint's gone, with pitted dark cores and orange edges.
                float patches = TileableValueNoise(u, v, 10) * 0.65f + TileableValueNoise(u, v, 40) * 0.25f + TileableValueNoise(u, v, 128) * 0.1f;
                float edge = Mathf.InverseLerp(0.58f, 0.64f, patches);
                float core = Mathf.InverseLerp(0.64f, 0.75f, patches);
                colour = Color.Lerp(colour, rustEdge, edge * 0.85f);
                colour = Color.Lerp(colour, rust * (0.8f + 0.4f * TileableValueNoise(u, v, 128)), core);
                // A few scuffs back to grey primer.
                float scuff = TileableValueNoise(u * 3f % 1f, v, 64);
                if (scuff > 0.86f && patches < 0.55f)
                    colour = Color.Lerp(colour, primer, Mathf.InverseLerp(0.86f, 0.95f, scuff));
                // Fine grain so it never looks like flat plastic.
                colour *= 0.92f + 0.16f * TileableValueNoise(u, v, 256);
                pixels[y * size + x] = colour;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
