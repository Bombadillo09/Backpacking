using System;
using System.Collections.Generic;
using System.IO;
using Backpacking.Camp;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Renders a picture of every item from its model (held items, camp gear, foods, clothing) into
    /// Generated/Icons and fills <see cref="ItemIconLibrary"/>. Run by the scene builder; also on its own with
    /// "run:Backpacking.EditorTools.ItemIcons.Render".
    /// </summary>
    public static class ItemIcons
    {
        const string IconFolder = "Assets/_Project/Generated/Icons";
        const string LibraryPath = "Assets/_Project/Settings/ItemIconLibrary.asset";
        const int Size = 256;
        /// <summary>Matches the backpack screen's tiles, so each picture sits seamlessly in its tile.</summary>
        public static readonly Color Background = new(0.105f, 0.115f, 0.105f);

        public static ItemIconLibrary GetOrCreateLibrary() => Build();

        public static string Render()
        {
            ItemIconLibrary library = Build();
            return $"Rendered {library.entries.Length} item icons.";
        }

        static ItemIconLibrary Build()
        {
            if (!AssetDatabase.IsValidFolder(IconFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Generated", "Icons");
            var library = AssetDatabase.LoadAssetAtPath<ItemIconLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<ItemIconLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var held = AssetDatabase.LoadAssetAtPath<HeldItemLibrary>("Assets/_Project/Settings/HeldItemLibrary.asset");
            GearLibrary gear = GearSetup.GetOrCreateLibrary();
            var owned = new List<Object>();
            Material Tint(Material source, Color colour)
            {
                var material = new Material(source);
                material.SetColor("_BaseColor", colour);
                owned.Add(material);
                return material;
            }
            Material plain = held != null ? held.plain : gear.aluminium;

            GameObject Mesh(params (Mesh mesh, Material material, Vector3 position, Vector3 euler, Vector3 scale)[] parts)
            {
                var root = new GameObject("Icon");
                foreach (var part in parts)
                {
                    var child = new GameObject(part.mesh != null ? part.mesh.name : "part");
                    child.transform.SetParent(root.transform, false);
                    child.transform.SetLocalPositionAndRotation(part.position, Quaternion.Euler(part.euler));
                    child.transform.localScale = part.scale == Vector3.zero ? Vector3.one : part.scale;
                    child.AddComponent<MeshFilter>().sharedMesh = part.mesh;
                    child.AddComponent<MeshRenderer>().sharedMaterial = part.material;
                }
                return root;
            }
            GameObject Prefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path) is { } prefab ? Object.Instantiate(prefab) : null;
            GameObject Held(HotbarSlot slot)
            {
                GameObject prefab = held != null ? held.PrefabFor(slot) : null;
                if (prefab != null)
                    return Object.Instantiate(prefab);
                var materials = new List<Material>();
                GameObject model = slot.kind == HotbarKind.Food ? HeldFood.Build(slot.food, plain, materials) : HeldGear.Build(slot, false, plain, materials);
                owned.AddRange(materials);
                return model;
            }
            MeshBuilder Builder() => new();
            Mesh Built(Action<MeshBuilder> make, string name)
            {
                MeshBuilder b = Builder();
                make(b);
                Mesh mesh = b.Build(name);
                owned.Add(mesh);
                return mesh;
            }
            Mesh Owned(Mesh mesh)
            {
                owned.Add(mesh);
                return mesh;
            }

            Color olive = TentDesign.Of(TentModel.OnePerson).Fly;
            var items = new List<(string key, Func<GameObject> make, Vector3 view)>
            {
                ("water", () => Held(new HotbarSlot(HotbarKind.Water)), default),
                ("machete", () => Held(new HotbarSlot(HotbarKind.Machete)), new Vector3(1f, 0.15f, 0.25f)),
                ("bandage", () => Held(new HotbarSlot(HotbarKind.Bandage)), default),
                ("antibiotics", () => Held(new HotbarSlot(HotbarKind.Antibiotics)), default),
                ("flashlight", () => Held(new HotbarSlot(HotbarKind.Flashlight)), new Vector3(1f, 0.3f, 0.4f)),
                ("tent", () => Mesh((gear.stuffSack, Tint(gear.pack.gearFabric, olive), Vector3.zero, Vector3.zero, Vector3.one),
                    (gear.stuffSackStraps, gear.pack.webbing, Vector3.zero, Vector3.zero, Vector3.one)), default),
                ("sleepingbag", () => Mesh((gear.stuffSack, Tint(gear.pack.gearFabric, new Color(0.5f, 0.12f, 0.1f)), Vector3.zero, Vector3.zero, new Vector3(0.75f, 1.25f, 1.25f)),
                    (gear.stuffSackStraps, gear.pack.webbing, Vector3.zero, Vector3.zero, new Vector3(0.75f, 1.25f, 1.25f))), default),
                ("mat", () => Mesh((Owned(PackDesign.Roll(0.07f, 0.52f, "Pad")), Tint(gear.pack.gearFabric, new Color(0.85f, 0.75f, 0.2f)), Vector3.zero, Vector3.zero, Vector3.one),
                    (Owned(PackDesign.RollStraps(0.07f, 0.52f, 0.3f)), gear.pack.webbing, Vector3.zero, Vector3.zero, Vector3.one)), default),
                ("airmat", () => Mesh((gear.stuffSack, Tint(gear.pack.gearFabric, new Color(0.85f, 0.4f, 0.12f)), Vector3.zero, Vector3.zero, new Vector3(0.5f, 0.75f, 0.75f))), default),
                ("stove", () => Prefab("Assets/_Project/Prefabs/Camp/Camp Stove (detailed).prefab"), default),
                ("gas", () => Mesh((gear.canister, gear.canisterPaint, Vector3.zero, Vector3.zero, Vector3.one)), default),
                ("snare", () => Mesh((gear.stake, Tint(plain, new Color(0.33f, 0.22f, 0.13f)), Vector3.zero, Vector3.zero, Vector3.one),
                    (gear.noose, Tint(plain, new Color(0.72f, 0.56f, 0.28f)), Vector3.zero, Vector3.zero, Vector3.one)), default),
                ("chair", ChairPacked, default),
                ("firewood", () => Firewood(plain, Tint), default),
                ("matches", () => Mesh(
                    (Built(b => b.Box(Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, new Vector3(0.028f, 0.008f, 0.018f)), "Matchbox"), Tint(plain, new Color(0.75f, 0.12f, 0.1f)), Vector3.zero, Vector3.zero, Vector3.one),
                    (Built(b => b.Box(Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, new Vector3(0.029f, 0.0085f, 0.006f)), "Striker"), Tint(plain, new Color(0.25f, 0.18f, 0.12f)), new Vector3(0f, 0f, 0.012f), Vector3.zero, Vector3.one),
                    (Built(b => b.Box(Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, new Vector3(0.018f, 0.0082f, 0.012f)), "Label"), Tint(plain, new Color(0.93f, 0.88f, 0.75f)), new Vector3(-0.005f, 0f, -0.002f), Vector3.zero, Vector3.one)), new Vector3(0.3f, 1f, 0.5f)),
                ("fishing", () => Mesh((Built(b => GearDesign.Lathe(b, new[] { new Vector2(0.03f, 0f), new Vector2(0.03f, 0.004f), new Vector2(0.012f, 0.005f), new Vector2(0.012f, 0.023f), new Vector2(0.03f, 0.024f), new Vector2(0.03f, 0.028f) }), "Hand line"), Tint(plain, new Color(0.9f, 0.7f, 0.1f)), Vector3.zero, new Vector3(90f, 0f, 0f), Vector3.one),
                    (Built(b => GearDesign.Lathe(b, new[] { new Vector2(0.0125f, 0.006f), new Vector2(0.016f, 0.008f), new Vector2(0.016f, 0.02f), new Vector2(0.0125f, 0.022f) }), "Line"), Tint(plain, new Color(0.3f, 0.75f, 0.35f)), Vector3.zero, new Vector3(90f, 0f, 0f), Vector3.one)), new Vector3(0.2f, 0.3f, 1f)),
                ("rod", () => Mesh((Built(b => b.Tube(new[] { Vector3.zero, new Vector3(0.5f, 0.02f, 0f) }, 0.006f, 8), "Rod"), Tint(plain, new Color(0.15f, 0.15f, 0.17f)), Vector3.zero, new Vector3(0f, 0f, 30f), Vector3.one),
                    (Built(b => GearDesign.Lathe(b, new[] { new Vector2(0.024f, 0f), new Vector2(0.024f, 0.02f) }), "Reel"), Tint(plain, new Color(0.6f, 0.6f, 0.62f)), new Vector3(0.06f, 0.0f, 0.02f), new Vector3(90f, 0f, 0f), Vector3.one)), new Vector3(0f, 0.2f, 1f)),
                ("filter", () => Mesh((Built(b => GearDesign.Lathe(b, new[] { new Vector2(0f, 0f), new Vector2(0.03f, 0.01f), new Vector2(0.035f, 0.08f), new Vector2(0.02f, 0.11f), new Vector2(0.012f, 0.12f) }), "Pouch"), Tint(plain, new Color(0.2f, 0.45f, 0.75f)), Vector3.zero, Vector3.zero, Vector3.one),
                    (Built(b => GearDesign.Lathe(b, new[] { new Vector2(0.014f, 0.115f), new Vector2(0.014f, 0.16f), new Vector2(0.008f, 0.17f) }), "Filter"), Tint(plain, new Color(0.9f, 0.9f, 0.88f)), Vector3.zero, Vector3.zero, Vector3.one)), default),
                ("pelt", () => Mesh((Built(b => GearDesign.Lathe(b, new[] { new Vector2(0f, 0f), new Vector2(0.09f, 0.008f), new Vector2(0.1f, 0.018f), new Vector2(0.07f, 0.03f), new Vector2(0f, 0.034f) }), "Pelt"), Tint(plain, new Color(0.5f, 0.42f, 0.33f)), Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.7f))), new Vector3(0.3f, 1f, 0.6f)),
                ("boots", () => Boots(plain, Tint), default),
                ("bow", () =>
                {
                    // The bow in profile, with an arrow laid across it.
                    GameObject bow = Hunting.BowDesign.Bow();
                    GameObject arrow = Hunting.BowDesign.Arrow();
                    arrow.transform.SetParent(bow.transform, false);
                    arrow.transform.SetLocalPositionAndRotation(new Vector3(0.03f, 0.25f, 0.45f), Quaternion.Euler(35f, 0f, 0f));
                    // Laid diagonally, so the long, slim bow fills the square picture.
                    var root = new GameObject("Bow icon");
                    bow.transform.SetParent(root.transform, false);
                    bow.transform.localRotation = Quaternion.Euler(0f, 0f, -40f);
                    return root;
                }, new Vector3(0.75f, 0.05f, -1f)),
                ("hide", () => Mesh((Built(b => GearDesign.Lathe(b, new[] { new Vector2(0f, 0f), new Vector2(0.2f, 0.012f), new Vector2(0.24f, 0.03f), new Vector2(0.17f, 0.05f), new Vector2(0f, 0.055f) }), "Hide"), Tint(plain, new Color(0.62f, 0.45f, 0.3f)), Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.75f))), new Vector3(0.3f, 1f, 0.6f)),
                ("money", () => Mesh((Built(b => GearDesign.Lathe(b, new[] { new Vector2(0f, 0f), new Vector2(0.012f, 0f), new Vector2(0.012f, 0.003f), new Vector2(0f, 0.003f) }), "Coin"), Tint(gear.aluminium, new Color(0.85f, 0.7f, 0.35f)), Vector3.zero, new Vector3(70f, 0f, 0f), Vector3.one)), default),
            };
            foreach (FoodKind food in FoodCatalog.AllKinds)
            {
                FoodKind kind = food;
                items.Add(($"food-{kind}", () => Held(new HotbarSlot(HotbarKind.Food, kind)), default));
            }
            foreach ((string key, Color colour, bool puffy) in new[]
                     {
                         ("garment-base", new Color(0.35f, 0.4f, 0.45f), false), ("garment-fleece", new Color(0.8f, 0.42f, 0.15f), false),
                         ("garment-shell", new Color(0.15f, 0.35f, 0.6f), false), ("garment-down", new Color(0.7f, 0.15f, 0.12f), true),
                         ("garment-pants", new Color(0.25f, 0.27f, 0.25f), false),
                     })
                items.Add((key, () => FoldedGarment(colour, puffy, plain, Tint, owned), new Vector3(0.4f, 1f, 0.6f)));
            items.Add(("garment-hat", () => Hat(plain, Tint, owned), default));

            GameObject ChairPacked()
            {
                var root = new GameObject("Chair");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Camp/Camp Chair.prefab");
                var serialized = new SerializedObject(prefab.GetComponent<CampChair>());
                CampChair.Build(root.transform, ChairStage.Packed, (Material)serialized.FindProperty("frameMaterial").objectReferenceValue,
                    (Material)serialized.FindProperty("fabricMaterial").objectReferenceValue);
                return root;
            }

            var entries = new List<ItemIconLibrary.Entry>();
            var preview = new PreviewRenderUtility();
            try
            {
                preview.camera.fieldOfView = 25f;
                preview.camera.nearClipPlane = 0.01f;
                preview.camera.farClipPlane = 20f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = Background;
                preview.lights[0].intensity = 1.25f;
                preview.lights[0].transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                preview.lights[1].intensity = 0.65f;
                preview.lights[1].transform.rotation = Quaternion.Euler(-20f, 150f, 0f);
                preview.ambientColor = new Color(0.45f, 0.45f, 0.48f);

                foreach ((string key, Func<GameObject> make, Vector3 view) in items)
                {
                    GameObject model = null;
                    try
                    {
                        model = make();
                        if (model == null)
                            continue;
                        foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                            if (child.name == "Caught")
                                child.gameObject.SetActive(false);
                        preview.AddSingleGO(model);
                        Bounds bounds = Bounds(model);
                        Vector3 from = (view == default ? new Vector3(0.8f, 0.65f, 1f) : view).normalized;
                        float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
                        float distance = radius / Mathf.Sin(preview.camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;
                        preview.BeginStaticPreview(new Rect(0, 0, Size, Size));
                        preview.camera.transform.position = bounds.center + from * distance;
                        preview.camera.transform.LookAt(bounds.center);
                        preview.Render(true);
                        Texture2D texture = preview.EndStaticPreview();
                        string path = $"{IconFolder}/{key}.png";
                        File.WriteAllBytes(path, texture.EncodeToPNG());
                        Object.DestroyImmediate(texture);
                        AssetDatabase.ImportAsset(path);
                        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                        {
                            importer.mipmapEnabled = false;
                            importer.wrapMode = TextureWrapMode.Clamp;
                            importer.textureCompression = TextureImporterCompression.Uncompressed;
                            importer.SaveAndReimport();
                        }
                        entries.Add(new ItemIconLibrary.Entry { key = key, icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path) });
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"Couldn't render the icon for {key}: {exception.Message}");
                    }
                    finally
                    {
                        if (model != null)
                            Object.DestroyImmediate(model);
                    }
                }
            }
            finally
            {
                preview.Cleanup();
                foreach (Object thing in owned)
                    if (thing != null)
                        Object.DestroyImmediate(thing);
            }
            library.entries = entries.ToArray();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        static Bounds Bounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.one * 0.1f);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                if (renderer.gameObject.activeInHierarchy)
                    bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static GameObject Firewood(Material plain, Func<Material, Color, Material> tint)
        {
            var art = AssetDatabase.LoadAssetAtPath<BiomeArtSettings>("Assets/_Project/Settings/BiomeArt.asset");
            if (art != null && art.firewoodModels != null && art.firewoodModels.Length > 0 && art.firewoodModels[0] != null)
            {
                GameObject wood = Object.Instantiate(art.firewoodModels[0]);
                Bounds bounds = Bounds(wood);
                wood.transform.localScale *= 0.6f / Mathf.Max(bounds.size.x, bounds.size.z, 0.01f);
                // A small armful: three pieces.
                var root = new GameObject("Firewood");
                wood.transform.SetParent(root.transform, true);
                GameObject second = Object.Instantiate(wood, root.transform);
                second.transform.localPosition += new Vector3(0.03f, 0.05f, 0.06f);
                second.transform.localRotation *= Quaternion.Euler(0f, 20f, 0f);
                return root;
            }
            var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            log.transform.localScale = new Vector3(0.06f, 0.3f, 0.06f);
            log.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            log.GetComponent<Renderer>().sharedMaterial = tint(plain, new Color(0.33f, 0.22f, 0.13f));
            return log;
        }

        /// <summary>A garment folded into a neat square, a few layers showing at the edge; quilted if it's down.</summary>
        static GameObject FoldedGarment(Color colour, bool puffy, Material plain, Func<Material, Color, Material> tint, List<Object> owned)
        {
            var root = new GameObject("Garment");
            Material cloth = tint(plain, colour), darker = tint(plain, Color.Lerp(colour, Color.black, 0.25f));
            for (int layer = 0; layer < 3; layer++)
            {
                var b = new MeshBuilder();
                float y = layer * 0.012f, inset = layer * 0.004f;
                b.Box(new Vector3(0f, y + 0.006f, 0f), Vector3.right, Vector3.up, Vector3.forward, new Vector3(0.13f - inset, 0.006f, 0.1f - inset));
                Mesh mesh = b.Build("Fold");
                owned.Add(mesh);
                var part = new GameObject("Fold");
                part.transform.SetParent(root.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.AddComponent<MeshRenderer>().sharedMaterial = layer == 2 ? cloth : darker;
            }
            if (puffy)
                for (int baffle = 0; baffle < 4; baffle++)
                {
                    var b = new MeshBuilder();
                    b.Tube(new[] { new Vector3(-0.12f, 0.042f, -0.075f + baffle * 0.05f), new Vector3(0.12f, 0.042f, -0.075f + baffle * 0.05f) }, 0.016f, 10);
                    Mesh mesh = b.Build("Baffle");
                    owned.Add(mesh);
                    var part = new GameObject("Baffle");
                    part.transform.SetParent(root.transform, false);
                    part.transform.localScale = new Vector3(1f, 0.6f, 1f);
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    part.AddComponent<MeshRenderer>().sharedMaterial = cloth;
                }
            return root;
        }

        static GameObject Hat(Material plain, Func<Material, Color, Material> tint, List<Object> owned)
        {
            var b = new MeshBuilder();
            var profile = new List<Vector2>();
            for (int i = 0; i <= 10; i++)
            {
                float a = i / 10f * Mathf.PI * 0.5f;
                profile.Add(new Vector2(Mathf.Cos(a) * 0.09f, Mathf.Sin(a) * 0.1f + 0.03f));
            }
            profile.Insert(0, new Vector2(0.092f, 0f));
            GearDesign.Lathe(b, profile);
            Mesh mesh = b.Build("Hat");
            owned.Add(mesh);
            var root = new GameObject("Hat");
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = tint(plain, new Color(0.55f, 0.2f, 0.18f));
            return root;
        }

        /// <summary>A hiking boot from rounded parts: sole, upper, toe and ankle shaft with a padded collar.</summary>
        static GameObject Boots(Material plain, Func<Material, Color, Material> tint)
        {
            var root = new GameObject("Boot");
            Material leather = tint(plain, new Color(0.4f, 0.27f, 0.16f)), sole = tint(plain, new Color(0.08f, 0.07f, 0.06f));
            void Part(PrimitiveType type, Vector3 at, Vector3 euler, Vector3 scale, Material material)
            {
                GameObject part = GameObject.CreatePrimitive(type);
                Object.DestroyImmediate(part.GetComponent<Collider>());
                part.transform.SetParent(root.transform, false);
                part.transform.SetLocalPositionAndRotation(at, Quaternion.Euler(euler));
                part.transform.localScale = scale;
                part.GetComponent<Renderer>().sharedMaterial = material;
            }
            Part(PrimitiveType.Capsule, new Vector3(0f, 0.016f, 0.075f), new Vector3(90f, 0f, 0f), new Vector3(0.108f, 0.15f, 0.032f), sole);
            Part(PrimitiveType.Capsule, new Vector3(0f, 0.055f, 0.08f), new Vector3(84f, 0f, 0f), new Vector3(0.1f, 0.135f, 0.095f), leather);
            Part(PrimitiveType.Sphere, new Vector3(0f, 0.045f, 0.165f), Vector3.zero, new Vector3(0.102f, 0.08f, 0.13f), leather);
            Part(PrimitiveType.Sphere, new Vector3(0f, 0.055f, -0.02f), Vector3.zero, new Vector3(0.1f, 0.1f, 0.1f), leather);
            Part(PrimitiveType.Cylinder, new Vector3(0f, 0.115f, 0f), new Vector3(-8f, 0f, 0f), new Vector3(0.098f, 0.06f, 0.1f), leather);
            Part(PrimitiveType.Cylinder, new Vector3(0f, 0.175f, -0.008f), new Vector3(-8f, 0f, 0f), new Vector3(0.106f, 0.014f, 0.108f), sole);
            return root;
        }
    }
}
