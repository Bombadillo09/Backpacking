using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backpacking.Character
{
    /// <summary>
    /// Builds a hiker from a <see cref="CharacterProfile"/>: the chosen body with its skin tone, a hairstyle and
    /// beard attached to the same skeleton, clothes and a backpack. The base models wear only underwear, so
    /// the clothes are shells cut from the body mesh itself (jacket over the torso and arms, pants over the
    /// hips and legs, boots over the feet), pushed out slightly so they sit on top and bend with the body.
    /// </summary>
    public class CharacterAppearance : MonoBehaviour
    {
        [SerializeField] CharacterLibrary library;

        static readonly Dictionary<Mesh, (Mesh clothes, Mesh headless)> derivedMeshes = new();

        readonly List<Material> ownedMaterials = new();
        readonly List<Renderer> headParts = new();
        GameObject model;
        Mesh fullBody, headlessBody;
        SkinnedMeshRenderer shadowBody;

        public CharacterLibrary Library { get => library; set => library = value; }
        public Animator Animator { get; private set; }
        public SkinnedMeshRenderer Body { get; private set; }
        /// <summary>Height of the eyes above the feet, in the model's own scale.</summary>
        public float EyeHeight { get; private set; } = 1.7f;
        public bool IsBuilt => model != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => derivedMeshes.Clear();

        void OnDestroy() => ClearMaterials();

        public void Build(CharacterProfile profile)
        {
            if (library == null || library.maleBody == null)
                return;
            if (model != null)
                Destroy(model);
            ClearMaterials();
            headParts.Clear();
            shadowBody = null;

            model = Instantiate(profile.female ? library.femaleBody : library.maleBody, transform);
            model.name = "Model";
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;

            Animator = model.GetComponent<Animator>();
            if (Animator == null)
                Animator = model.AddComponent<Animator>();
            Animator.runtimeAnimatorController = library.animator;
            Animator.applyRootMotion = false;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var bones = new Dictionary<string, Transform>();
            foreach (Transform bone in model.GetComponentsInChildren<Transform>())
                bones.TryAdd(bone.name, bone);

            Color hair = profile.hairColour;
            foreach (SkinnedMeshRenderer part in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                string original = part.sharedMaterial != null ? part.sharedMaterial.name : "";
                if (original.Contains("Superhero"))
                {
                    Body = part;
                    part.sharedMaterial = Tinted(profile.female ? library.femaleSkin : library.maleSkin, CharacterProfile.SkinTint(profile.skinTone));
                }
                else
                {
                    part.sharedMaterial = original.Contains("Eyes") ? library.eyes
                        : Tinted(original.Contains("Hair_2") ? library.hairLong : library.hairShort, hair);
                    headParts.Add(part);
                }
            }
            if (Body == null)
                return;

            CharacterLibrary.HairStyle style = library.hairStyles != null && profile.hairStyle >= 0 && profile.hairStyle < library.hairStyles.Length
                ? library.hairStyles[profile.hairStyle] : null;
            if (style != null && style.model != null)
                headParts.Add(AttachSkinned(style.model, bones, Tinted(style.name == "Long" || style.name == "Buns" ? library.hairLong : library.hairShort, hair)));
            if (profile.beard && !profile.female && library.beard != null)
                headParts.Add(AttachSkinned(library.beard, bones, Tinted(library.hairShort, hair)));

            AddClothes(profile);
            AddPack(profile, bones);

            fullBody = Body.sharedMesh;
            if (bones.TryGetValue("Head", out Transform head))
                EyeHeight = model.transform.InverseTransformPoint(head.position).y + 0.09f;
        }

        /// <summary>
        /// In first person, the camera sits inside the head, so the head and hair only cast shadows and the visible
        /// body has no head; a separate shadow-only copy keeps the full silhouette on the ground.
        /// </summary>
        public void SetFirstPerson(bool firstPerson)
        {
            if (Body == null)
                return;
            if (firstPerson && shadowBody == null)
            {
                var shadow = new GameObject("Body Shadow");
                shadow.transform.SetParent(Body.transform.parent, false);
                shadow.layer = Body.gameObject.layer;
                shadowBody = shadow.AddComponent<SkinnedMeshRenderer>();
                shadowBody.sharedMesh = fullBody;
                shadowBody.bones = Body.bones;
                shadowBody.rootBone = Body.rootBone;
                shadowBody.sharedMaterial = Body.sharedMaterial;
                shadowBody.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                shadowBody.updateWhenOffscreen = true;
            }
            if (shadowBody != null)
                shadowBody.enabled = firstPerson;

            Body.sharedMesh = firstPerson ? Headless(fullBody, Body.bones) : fullBody;
            Body.shadowCastingMode = firstPerson ? ShadowCastingMode.Off : ShadowCastingMode.On;
            Body.updateWhenOffscreen = true;
            foreach (Renderer part in headParts)
                if (part != null)
                    part.shadowCastingMode = firstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        /// <summary>Puts every part of the hiker on one layer, e.g. so only the preview camera sees it.</summary>
        public void SetLayer(int layer)
        {
            if (model == null)
                return;
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }

        // ---------- Parts ----------

        /// <summary>Takes the skinned mesh out of a hair model and binds it to this body's bones by name.</summary>
        Renderer AttachSkinned(GameObject source, Dictionary<string, Transform> bones, Material material)
        {
            GameObject copy = Instantiate(source, model.transform);
            SkinnedMeshRenderer part = copy.GetComponentInChildren<SkinnedMeshRenderer>();
            if (part == null)
            {
                Destroy(copy);
                return null;
            }
            var mapped = new Transform[part.bones.Length];
            for (int i = 0; i < mapped.Length; i++)
                mapped[i] = part.bones[i] != null && bones.TryGetValue(part.bones[i].name, out Transform bone) ? bone : null;
            part.bones = mapped;
            if (part.rootBone != null && bones.TryGetValue(part.rootBone.name, out Transform root))
                part.rootBone = root;
            part.sharedMaterial = material;
            part.updateWhenOffscreen = true;
            part.transform.SetParent(model.transform, false);
            Destroy(copy);
            return part;
        }

        void AddClothes(CharacterProfile profile)
        {
            var go = new GameObject("Clothes");
            go.transform.SetParent(Body.transform.parent, false);
            var clothes = go.AddComponent<SkinnedMeshRenderer>();
            clothes.sharedMesh = Clothes(Body.sharedMesh, Body.bones);
            clothes.bones = Body.bones;
            clothes.rootBone = Body.rootBone;
            clothes.updateWhenOffscreen = true;
            clothes.sharedMaterials = new[]
            {
                Tinted(library.clothing, profile.jacketColour),
                Tinted(library.clothing, profile.pantsColour),
                library.boots,
            };
        }

        /// <summary>A simple pack strapped to the upper back: main bag, lid, and a sleeping bag rolled underneath.</summary>
        void AddPack(CharacterProfile profile, Dictionary<string, Transform> bones)
        {
            if (!bones.TryGetValue("spine_03", out Transform chest))
                return;
            var pack = new GameObject("Backpack");
            pack.transform.SetParent(model.transform, false);
            Material bag = Tinted(library.pack, profile.packColour);
            Material roll = Tinted(library.pack, new Color(0.25f, 0.27f, 0.22f));
            Vector3 back = model.transform.InverseTransformPoint(chest.position) + new Vector3(0f, -0.12f, -0.22f);
            PackPart(pack.transform, PrimitiveType.Cube, back, new Vector3(0.34f, 0.5f, 0.2f), bag);
            PackPart(pack.transform, PrimitiveType.Cube, back + new Vector3(0f, 0.27f, 0.01f), new Vector3(0.36f, 0.08f, 0.23f), bag);
            PackPart(pack.transform, PrimitiveType.Cube, back + new Vector3(0f, -0.08f, -0.11f), new Vector3(0.24f, 0.22f, 0.05f), bag);
            GameObject sleepingBag = PackPart(pack.transform, PrimitiveType.Cylinder, back + new Vector3(0f, -0.33f, 0.02f), new Vector3(0.16f, 0.2f, 0.16f), roll);
            sleepingBag.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            // Follow the chest as it moves.
            pack.transform.SetParent(chest, true);
        }

        static GameObject PackPart(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }

        Material Tinted(Material source, Color colour)
        {
            if (source == null)
                return null;
            var material = new Material(source);
            material.SetColor("_BaseColor", colour);
            ownedMaterials.Add(material);
            return material;
        }

        void ClearMaterials()
        {
            foreach (Material material in ownedMaterials)
                if (material != null)
                    Destroy(material);
            ownedMaterials.Clear();
        }

        // ---------- Meshes cut from the body ----------

        enum Region { Skin = -1, Jacket, Pants, Boots, Head }

        static Region RegionOf(string bone)
        {
            if (bone.StartsWith("spine") || bone.StartsWith("clavicle") || bone.StartsWith("upperarm") || bone.StartsWith("lowerarm"))
                return Region.Jacket;
            if (bone == "pelvis" || bone.StartsWith("thigh") || bone.StartsWith("calf"))
                return Region.Pants;
            if (bone.StartsWith("foot") || bone.StartsWith("ball"))
                return Region.Boots;
            if (bone == "Head" || bone.StartsWith("neck"))
                return Region.Head;
            return Region.Skin;
        }

        /// <summary>Which region each vertex belongs to, by the bone that moves it most.</summary>
        static Region[] VertexRegions(Mesh body, Transform[] bones)
        {
            BoneWeight[] weights = body.boneWeights;
            var regions = new Region[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight w = weights[i];
                int strongest = w.boneIndex0;
                float best = w.weight0;
                if (w.weight1 > best) { best = w.weight1; strongest = w.boneIndex1; }
                if (w.weight2 > best) { best = w.weight2; strongest = w.boneIndex2; }
                if (w.weight3 > best) strongest = w.boneIndex3;
                regions[i] = strongest < bones.Length && bones[strongest] != null ? RegionOf(bones[strongest].name) : Region.Skin;
            }
            return regions;
        }

        /// <summary>Jacket, pants and boots as three submeshes of one shell, each pushed out along the normals.</summary>
        static Mesh Clothes(Mesh body, Transform[] bones)
        {
            if (derivedMeshes.TryGetValue(body, out (Mesh clothes, Mesh headless) cached) && cached.clothes != null)
                return cached.clothes;

            Region[] regions = VertexRegions(body, bones);
            float[] thickness = { 0.012f, 0.009f, 0.016f };
            Vector3[] vertices = body.vertices;
            Vector3[] normals = body.normals;
            for (int i = 0; i < vertices.Length; i++)
            {
                int region = (int)regions[i];
                if (region is >= 0 and < 3)
                    vertices[i] += normals[i] * thickness[region];
            }

            var pieces = new[] { new List<int>(), new List<int>(), new List<int>() };
            for (int sub = 0; sub < body.subMeshCount; sub++)
            {
                int[] triangles = body.GetTriangles(sub);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    // A triangle belongs to a garment if at least two of its corners do.
                    Region a = regions[triangles[t]], b = regions[triangles[t + 1]], c = regions[triangles[t + 2]];
                    Region region = a == b || a == c ? a : b == c ? b : Region.Skin;
                    if (region is >= 0 and < Region.Head)
                        pieces[(int)region].AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
                }
            }

            var mesh = new Mesh { name = body.name + " Clothes", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = body.uv;
            mesh.boneWeights = body.boneWeights;
            mesh.bindposes = body.bindposes;
            mesh.subMeshCount = pieces.Length;
            for (int i = 0; i < pieces.Length; i++)
                mesh.SetTriangles(pieces[i], i);
            mesh.RecalculateBounds();
            derivedMeshes[body] = (mesh, cached.headless);
            return mesh;
        }

        /// <summary>The body without its head and neck, for the first-person view.</summary>
        static Mesh Headless(Mesh body, Transform[] bones)
        {
            if (derivedMeshes.TryGetValue(body, out (Mesh clothes, Mesh headless) cached) && cached.headless != null)
                return cached.headless;

            Region[] regions = VertexRegions(body, bones);
            Mesh mesh = Instantiate(body);
            mesh.name = body.name + " Headless";
            for (int sub = 0; sub < body.subMeshCount; sub++)
            {
                int[] triangles = body.GetTriangles(sub);
                var kept = new List<int>(triangles.Length);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    if (regions[triangles[t]] == Region.Head || regions[triangles[t + 1]] == Region.Head || regions[triangles[t + 2]] == Region.Head)
                        continue;
                    kept.AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
                }
                mesh.SetTriangles(kept, sub);
            }
            derivedMeshes[body] = (cached.clothes, mesh);
            return mesh;
        }
    }
}
