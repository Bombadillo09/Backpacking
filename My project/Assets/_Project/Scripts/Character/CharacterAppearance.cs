using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Backpacking.Character
{
    /// <summary>
    /// Builds a hiker from a <see cref="CharacterProfile"/>: one of the Rocketbox people from the roster, dressed as
    /// they come, with a backpack fitted to their back. Taking boots off swaps their shoes and lower legs for bare
    /// feet borrowed from the swimwear model (same skeleton), and the cut-off shoes become the pair set down
    /// beside them. Body parts are found through the humanoid rig, so nothing depends on bone names.
    /// </summary>
    public class CharacterAppearance : MonoBehaviour
    {
        [SerializeField] CharacterLibrary library;

        [Flags]
        enum Cut { None = 0, Head = 1, Feet = 2 }

        static readonly Dictionary<(Mesh, Cut, float), Mesh> cutMeshes = new();

        readonly List<Material> ownedMaterials = new();
        readonly List<Object> ownedMeshes = new();
        GameObject model;
        CharacterLibrary.Hiker hiker;
        Mesh fullBody;
        SkinnedMeshRenderer shadowBody, bareFeet;
        HumanBodyBones[] owners;
        Vector3[] posed;
        Mesh leftShoe, rightShoe;
        bool firstPerson, bootsOn = true;

        public CharacterLibrary Library { get => library; set => library = value; }
        public Animator Animator { get; private set; }
        /// <summary>Head turning, feet on slopes and sitting on the ground, layered over the animation.</summary>
        public HikerPose Pose { get; private set; }
        public SkinnedMeshRenderer Body { get; private set; }
        /// <summary>Height of the eyes above the feet, in the model's own scale.</summary>
        public float EyeHeight { get; private set; } = 1.7f;
        /// <summary>Overall height of the posed hiker, in the model's own scale.</summary>
        public float Height { get; private set; } = 1.8f;
        public bool IsBuilt => model != null;
        /// <summary>The body's vertices as measured standing, in this object's space (for editor diagnostics).</summary>
        public IReadOnlyList<Vector3> MeasuredVertices => posed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cutMeshes.Clear();

        void OnDestroy() => ClearOwned();

        public void Build(CharacterProfile profile)
        {
            hiker = library != null ? library.Find(profile.hiker) : null;
            if (hiker == null)
                return;
            if (model != null)
                Discard(model);
            ClearOwned();
            Body = shadowBody = bareFeet = null;
            firstPerson = false;
            bootsOn = true;

            model = Instantiate(hiker.model, transform);
            model.name = "Model";
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;

            Animator = model.GetComponent<Animator>();
            if (Animator == null)
                Animator = model.AddComponent<Animator>();
            Animator.runtimeAnimatorController = library.animator;
            Animator.applyRootMotion = false;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Pose = model.AddComponent<HikerPose>();

            // Each model carries levels of detail; keep the finest. Its submeshes are body, head and, for people
            // with long hair or lashes, see-through hair cards.
            foreach (SkinnedMeshRenderer part in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (Body == null && part.name.ToLowerInvariant().Contains("hipoly"))
                    Body = part;
                else
                    Discard(part.gameObject);
            }
            if (Body == null)
                return;
            Body.gameObject.SetActive(true);
            Body.sharedMaterials = Body.sharedMesh.subMeshCount > 2
                ? new[] { hiker.body, hiker.head, hiker.hair }
                : new[] { hiker.body, hiker.head };
            Body.updateWhenOffscreen = true;
            fullBody = Body.sharedMesh;

            posed = StandOnGround();
            if (posed == null)
                return;
            owners = Owners(Body);
            Material bag = Tinted(library.pack, profile.packColour);
            Material webbing = Tinted(library.pack, Color.Lerp(profile.packColour, Color.black, 0.55f));
            AddPack(bag, webbing);
            AddBareFeet();
            leftShoe = ShoeMesh(LeftLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
            rightShoe = ShoeMesh(RightLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
        }

        /// <summary>
        /// Poses the hiker in its idle stance, measures the posed body, and lifts it so the soles of the feet rest
        /// on the ground at this object's origin. The eye height is measured from the posed head.
        /// </summary>
        /// <returns>The posed body's vertices in this object's space, standing on the ground; null if unmeasurable.</returns>
        Vector3[] StandOnGround()
        {
            if (Animator.runtimeAnimatorController != null)
            {
                Animator.Rebind();
                Animator.Update(0f);
            }

            Vector3[] vertices = Skinned(Body);
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            float lowest = float.MaxValue, highest = float.MinValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = toLocal.MultiplyPoint3x4(vertices[i]);
                lowest = Mathf.Min(lowest, vertices[i].y);
                highest = Mathf.Max(highest, vertices[i].y);
            }
            if (lowest == float.MaxValue)
                return null;

            model.transform.localPosition -= new Vector3(0f, lowest, 0f);
            for (int i = 0; i < vertices.Length; i++)
                vertices[i].y -= lowest;
            Height = highest - lowest;
            Transform head = Animator.isHuman ? Animator.GetBoneTransform(HumanBodyBones.Head) : null;
            EyeHeight = head != null ? transform.InverseTransformPoint(head.position).y + 0.09f : Height * 0.93f;
            return vertices;
        }

        /// <summary>
        /// In first person, the camera sits inside the head, so the visible body has no head (or hair); a separate
        /// shadow-only copy keeps the full silhouette on the ground.
        /// </summary>
        public void SetFirstPerson(bool on)
        {
            if (Body == null)
                return;
            firstPerson = on;
            if (on && shadowBody == null)
            {
                var shadow = new GameObject("Body Shadow");
                shadow.transform.SetParent(Body.transform.parent, false);
                shadow.transform.SetLocalPositionAndRotation(Body.transform.localPosition, Body.transform.localRotation);
                shadow.transform.localScale = Body.transform.localScale;
                shadow.layer = Body.gameObject.layer;
                shadowBody = shadow.AddComponent<SkinnedMeshRenderer>();
                shadowBody.bones = Body.bones;
                shadowBody.rootBone = Body.rootBone;
                shadowBody.sharedMaterials = Body.sharedMaterials;
                shadowBody.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                shadowBody.updateWhenOffscreen = true;
            }
            if (shadowBody != null)
                shadowBody.enabled = on;
            Body.shadowCastingMode = on ? ShadowCastingMode.Off : ShadowCastingMode.On;
            ApplyMeshes();
        }

        /// <summary>Shoes on, or bare feet and lower legs (the shoes are set down elsewhere).</summary>
        public void SetBootsOn(bool on)
        {
            if (on == bootsOn || Body == null)
                return;
            bootsOn = on;
            ApplyMeshes();
        }

        void ApplyMeshes()
        {
            bool barefoot = !bootsOn && bareFeet != null;
            Cut feet = barefoot ? Cut.Feet : Cut.None;
            Body.sharedMesh = CutMesh(feet | (firstPerson ? Cut.Head : Cut.None));
            if (shadowBody != null)
                shadowBody.sharedMesh = CutMesh(feet);
            if (bareFeet != null)
                bareFeet.enabled = barefoot;
        }

        /// <summary>Puts every part of the hiker on one layer, e.g. so only the preview camera sees it.</summary>
        public void SetLayer(int layer)
        {
            if (model == null)
                return;
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }

        /// <summary>
        /// Where each vertex of a skinned mesh is right now, in world space, worked out from its bones the way the
        /// GPU does it. (BakeMesh's result depends on the renderer's own rotation, which the Rocketbox meshes have.)
        /// </summary>
        static Vector3[] Skinned(SkinnedMeshRenderer renderer)
        {
            Mesh mesh = renderer.sharedMesh;
            Transform[] bones = renderer.bones;
            Matrix4x4[] bindposes = mesh.bindposes;
            var skinning = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++)
                skinning[b] = bones[b] != null && b < bindposes.Length ? bones[b].localToWorldMatrix * bindposes[b] : renderer.transform.localToWorldMatrix;

            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            for (int i = 0; i < vertices.Length; i++)
            {
                BoneWeight w = weights[i];
                Vector3 v = vertices[i];
                vertices[i] = skinning[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
                    + skinning[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                    + skinning[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
                    + skinning[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            }
            return vertices;
        }

        // ---------- Body parts by humanoid bone ----------

        static readonly HumanBodyBones[] LeftLeg = { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes };
        static readonly HumanBodyBones[] RightLeg = { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes };

        /// <summary>
        /// Arms, hands and legs. Everything else, including skin bound to bones outside the humanoid map (some
        /// of the men's backs), counts as torso.
        /// </summary>
        static bool IsLimb(HumanBodyBones bone) => bone is >= HumanBodyBones.LeftUpperLeg and <= HumanBodyBones.RightFoot
            or >= HumanBodyBones.LeftUpperArm and <= HumanBodyBones.RightHand
            or HumanBodyBones.LeftToes or HumanBodyBones.RightToes
            or >= HumanBodyBones.LeftThumbProximal and < HumanBodyBones.UpperChest;

        static bool IsHead(HumanBodyBones bone) => bone is HumanBodyBones.Head or HumanBodyBones.Neck or HumanBodyBones.Jaw
            or HumanBodyBones.LeftEye or HumanBodyBones.RightEye;

        /// <summary>
        /// For each vertex, the humanoid bone that moves it most: its strongest bone, or the nearest mapped bone
        /// above it (face and twist bones count as their parent). LastBone where nothing maps.
        /// </summary>
        static HumanBodyBones[] Owners(SkinnedMeshRenderer renderer)
        {
            Animator animator = renderer.GetComponentInParent<Animator>();
            var mapped = new Dictionary<Transform, HumanBodyBones>();
            if (animator != null && animator.isHuman)
                for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
                {
                    Transform t = animator.GetBoneTransform(bone);
                    if (t != null)
                        mapped.TryAdd(t, bone);
                }

            Transform[] bones = renderer.bones;
            var boneOwner = new HumanBodyBones[bones.Length];
            for (int b = 0; b < bones.Length; b++)
            {
                boneOwner[b] = HumanBodyBones.LastBone;
                for (Transform t = bones[b]; t != null; t = t.parent)
                    if (mapped.TryGetValue(t, out HumanBodyBones found))
                    {
                        boneOwner[b] = found;
                        break;
                    }
            }

            BoneWeight[] weights = renderer.sharedMesh.boneWeights;
            var owners = new HumanBodyBones[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                int strongest = Strongest(weights[i]);
                owners[i] = strongest < boneOwner.Length ? boneOwner[strongest] : HumanBodyBones.LastBone;
            }
            return owners;
        }

        static int Strongest(BoneWeight w)
        {
            int strongest = w.boneIndex0;
            float best = w.weight0;
            if (w.weight1 > best) { best = w.weight1; strongest = w.boneIndex1; }
            if (w.weight2 > best) { best = w.weight2; strongest = w.boneIndex2; }
            if (w.weight3 > best) strongest = w.boneIndex3;
            return strongest;
        }

        /// <summary>
        /// Footwear: everything the feet and toes carry (the whole shoe, whatever its shape), plus the lower legs
        /// below the top of the footwear (tall boots, and the trouser hems over the shoes).
        /// </summary>
        static bool IsFootwear(HumanBodyBones owner, float height, float top) =>
            owner is HumanBodyBones.LeftFoot or HumanBodyBones.RightFoot or HumanBodyBones.LeftToes or HumanBodyBones.RightToes
            || (owner is HumanBodyBones.LeftLowerLeg or HumanBodyBones.RightLowerLeg && height < top);

        bool BelowFootwear(int vertex, float top) => IsFootwear(owners[vertex], posed[vertex].y, top);

        /// <summary>The body with the head and/or the footwear cut away, made once per body and cut.</summary>
        Mesh CutMesh(Cut cut)
        {
            if (cut == Cut.None)
                return fullBody;
            float top = hiker.footwearTop;
            if (cutMeshes.TryGetValue((fullBody, cut, top), out Mesh cached) && cached != null)
                return cached;

            Mesh mesh = Instantiate(fullBody);
            mesh.name = $"{fullBody.name} without {cut}";
            for (int sub = 0; sub < fullBody.subMeshCount; sub++)
            {
                int[] triangles = fullBody.GetTriangles(sub);
                var kept = new List<int>(triangles.Length);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    if ((cut & Cut.Head) != 0 && (IsHead(owners[a]) || IsHead(owners[b]) || IsHead(owners[c])))
                        continue;
                    // Footwear goes if most of the triangle is in it; the bare legs reach a little higher.
                    if ((cut & Cut.Feet) != 0 && (BelowFootwear(a, top) ? 1 : 0) + (BelowFootwear(b, top) ? 1 : 0) + (BelowFootwear(c, top) ? 1 : 0) >= 2)
                        continue;
                    kept.Add(a);
                    kept.Add(b);
                    kept.Add(c);
                }
                mesh.SetTriangles(kept, sub);
            }
            cutMeshes[(fullBody, cut, top)] = mesh;
            return mesh;
        }

        // ---------- Bare feet ----------

        /// <summary>
        /// The swimwear model's feet and lower legs, bound to this hiker's bones by name (every Rocketbox person
        /// shares the skeleton) and tinted to their skin. Hidden until the boots come off.
        /// </summary>
        void AddBareFeet()
        {
            GameObject source = hiker.female ? library.femaleFeet : library.maleFeet;
            Material skin = hiker.female ? library.femaleFeetSkin : library.maleFeetSkin;
            if (source == null || skin == null)
                return;

            // Far below the world, so it's never seen in the moment it exists.
            GameObject donor = Instantiate(source, new Vector3(0f, -1000f, 0f), Quaternion.identity);
            try
            {
                SkinnedMeshRenderer donorBody = null;
                foreach (SkinnedMeshRenderer part in donor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (donorBody == null && part.name.ToLowerInvariant().Contains("hipoly"))
                        donorBody = part;
                if (donorBody == null)
                    return;

                var bones = new Dictionary<string, Transform>();
                foreach (Transform bone in model.GetComponentsInChildren<Transform>())
                    bones.TryAdd(bone.name, bone);
                var mapped = new Transform[donorBody.bones.Length];
                for (int i = 0; i < mapped.Length; i++)
                    if (donorBody.bones[i] != null && !bones.TryGetValue(donorBody.bones[i].name, out mapped[i]))
                        return;

                // Keep the leg below a little above the hiker's footwear, measured on the donor standing in its
                // rest pose from its lowest point.
                HumanBodyBones[] donorOwners = Owners(donorBody);
                Mesh donorMesh = donorBody.sharedMesh;
                Vector3[] vertices = Skinned(donorBody);
                var heights = new float[vertices.Length];
                float lowest = float.MaxValue;
                for (int i = 0; i < vertices.Length; i++)
                {
                    heights[i] = vertices[i].y;
                    lowest = Mathf.Min(lowest, heights[i]);
                }
                float top = hiker.footwearTop + 0.04f;
                bool Leg(int v) => IsFootwear(donorOwners[v], heights[v] - lowest, top);

                Mesh feet = Instantiate(donorMesh);
                feet.name = "Bare Feet";
                ownedMeshes.Add(feet);
                var kept = new List<int>();
                for (int sub = 0; sub < donorMesh.subMeshCount; sub++)
                {
                    int[] triangles = donorMesh.GetTriangles(sub);
                    for (int t = 0; t < triangles.Length; t += 3)
                        if (Leg(triangles[t]) && Leg(triangles[t + 1]) && Leg(triangles[t + 2]))
                            kept.AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
                }
                feet.subMeshCount = 1;
                feet.SetTriangles(kept, 0);

                var go = new GameObject("Bare Feet");
                go.transform.SetParent(Body.transform.parent, false);
                go.transform.SetLocalPositionAndRotation(Body.transform.localPosition, Body.transform.localRotation);
                go.layer = Body.gameObject.layer;
                bareFeet = go.AddComponent<SkinnedMeshRenderer>();
                bareFeet.sharedMesh = feet;
                bareFeet.bones = mapped;
                bareFeet.rootBone = Body.rootBone;
                bareFeet.sharedMaterial = Tinted(skin, hiker.skinTint);
                bareFeet.updateWhenOffscreen = true;
                bareFeet.enabled = false;
            }
            finally
            {
                Discard(donor);
            }
        }

        // ---------- Footwear set aside ----------

        /// <summary>
        /// The hiker's own shoes or boots with the socks pulled off, to set down beside them: one standing, one
        /// tipped on its side with a sock stuffed in it, the other sock lying on the ground. The shoes are the
        /// pieces cut from the body for bare feet, as they look standing. Origin on the ground, facing +Z.
        /// The caller owns the object; its materials and meshes belong to this hiker.
        /// </summary>
        public GameObject BuildBootsAndSocks()
        {
            var pile = new GameObject("Boots and Socks");
            if (Body == null || posed == null)
                return pile;
            Material wool = Tinted(library.socks, new Color(0.62f, 0.6f, 0.55f));

            Shoe(pile.transform, leftShoe, new Vector3(-0.1f, 0f, 0f), Quaternion.Euler(0f, -8f, 0f));
            Transform tipped = Shoe(pile.transform, rightShoe, new Vector3(0.12f, 0.05f, 0.03f), Quaternion.Euler(0f, 24f, 80f));
            if (tipped != null)
            {
                // A sock stuffed into its opening.
                Part(tipped, PrimitiveType.Capsule, new Vector3(0f, hiker.footwearTop + 0.02f, -0.01f), Quaternion.Euler(20f, 0f, 0f),
                    new Vector3(0.07f, 0.06f, 0.07f), wool);
            }
            // The other sock dropped on the ground in front, leg and foot at an angle.
            var sock = new GameObject("Sock").transform;
            sock.SetParent(pile.transform, false);
            sock.SetLocalPositionAndRotation(new Vector3(-0.05f, 0.012f, 0.26f), Quaternion.Euler(0f, 60f, 0f));
            Part(sock, PrimitiveType.Capsule, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new Vector3(0.07f, 0.1f, 0.022f), wool);
            Part(sock, PrimitiveType.Capsule, new Vector3(0.05f, 0f, 0.1f), Quaternion.Euler(90f, 50f, 0f), new Vector3(0.065f, 0.065f, 0.022f), wool);
            return pile;
        }

        Transform Shoe(Transform parent, Mesh mesh, Vector3 position, Quaternion rotation)
        {
            if (mesh == null)
                return null;
            var shoe = new GameObject("Shoe");
            shoe.transform.SetParent(parent, false);
            shoe.transform.SetLocalPositionAndRotation(position, rotation);
            shoe.AddComponent<MeshFilter>().sharedMesh = mesh;
            shoe.AddComponent<MeshRenderer>().sharedMaterials = Body.sharedMaterials;
            return shoe.transform;
        }

        /// <summary>
        /// One shoe cut from the standing body, re-centred under its ankle with the toes along +Z. Made while the
        /// bones are still in the idle pose the body was measured in.
        /// </summary>
        Mesh ShoeMesh(HumanBodyBones[] leg, HumanBodyBones foot, HumanBodyBones toes)
        {
            Transform ankle = Animator.GetBoneTransform(foot);
            if (ankle == null)
                return null;
            Transform ball = Animator.GetBoneTransform(toes);
            Vector3 at = transform.InverseTransformPoint(ankle.position);
            at.y = 0f;
            Vector3 forward = ball != null
                ? Vector3.ProjectOnPlane(transform.InverseTransformPoint(ball.position) - at, Vector3.up).normalized
                : Vector3.forward;
            if (forward.sqrMagnitude < 0.5f)
                forward = Vector3.forward;
            Quaternion frame = Quaternion.Inverse(Quaternion.LookRotation(forward));

            float top = hiker.footwearTop;
            bool InShoe(int v) => Array.IndexOf(leg, owners[v]) >= 0 && IsFootwear(owners[v], posed[v].y, top);
            var mesh = new Mesh { name = "Shoe", indexFormat = IndexFormat.UInt32 };
            ownedMeshes.Add(mesh);
            var vertices = new Vector3[posed.Length];
            for (int i = 0; i < posed.Length; i++)
                vertices[i] = frame * (posed[i] - at);
            mesh.vertices = vertices;
            mesh.uv = fullBody.uv;
            mesh.subMeshCount = fullBody.subMeshCount;
            for (int sub = 0; sub < fullBody.subMeshCount; sub++)
            {
                int[] triangles = fullBody.GetTriangles(sub);
                var kept = new List<int>();
                for (int t = 0; t < triangles.Length; t += 3)
                    if (InShoe(triangles[t]) && InShoe(triangles[t + 1]) && InShoe(triangles[t + 2]))
                        kept.AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
                mesh.SetTriangles(kept, sub);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- Pack ----------

        /// <summary>
        /// A pack on the upper back with its lid, front pocket and a sleeping bag strapped under it; shoulder straps
        /// over the shoulders and down the chest with a sternum strap; and a padded hip belt with a buckle.
        /// </summary>
        void AddPack(Material bag, Material webbing)
        {
            Transform chest = Animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null)
                chest = Animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null)
                return;
            bool[] torso = Array.ConvertAll(owners, owner => !IsHead(owner) && !IsLimb(owner));
            float chestY = transform.InverseTransformPoint(chest.position).y;

            Transform pack = Holder("Backpack");
            float backZ = Surface(posed, torso, 0f, chestY - 0.1f, false);
            Vector3 back = new(0f, chestY - 0.12f, (float.IsNaN(backZ) ? -0.12f : backZ) - 0.105f);
            Part(pack, PrimitiveType.Cube, back, Quaternion.identity, new Vector3(0.34f, 0.5f, 0.2f), bag);
            Part(pack, PrimitiveType.Cube, back + new Vector3(0f, 0.27f, 0.01f), Quaternion.identity, new Vector3(0.36f, 0.08f, 0.23f), bag);
            Part(pack, PrimitiveType.Cube, back + new Vector3(0f, -0.08f, -0.11f), Quaternion.identity, new Vector3(0.24f, 0.22f, 0.05f), bag);
            Part(pack, PrimitiveType.Cylinder, back + new Vector3(0f, -0.33f, 0.02f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.16f, 0.2f, 0.16f),
                Tinted(library.pack, new Color(0.25f, 0.27f, 0.22f)));

            // Shoulder straps: from inside the pack, over each shoulder, down the chest and out towards the armpit.
            var sternum = new Vector3[2];
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f, x = side * 0.1f;
                float top = Top(posed, torso, x);
                if (float.IsNaN(top))
                    continue;
                float frontHigh = Surface(posed, torso, x, top - 0.1f, true);
                float frontLow = Surface(posed, torso, x * 1.35f, top - 0.26f, true);
                float backHigh = Surface(posed, torso, x, top - 0.08f, false);
                if (float.IsNaN(frontHigh) || float.IsNaN(frontLow) || float.IsNaN(backHigh))
                    continue;
                float middle = (frontHigh + backHigh) / 2f;
                Vector3[] path =
                {
                    new(x, top - 0.14f, backHigh - 0.06f),
                    new(x, top + 0.012f, middle),
                    new(x, top - 0.1f, frontHigh + 0.012f),
                    new(x * 1.35f, top - 0.26f, frontLow + 0.012f),
                    new(x * 1.9f, top - 0.33f, frontLow - 0.05f),
                };
                for (int p = 0; p < path.Length - 1; p++)
                    Strap(pack, path[p], path[p + 1], 0.055f, 0.014f, webbing);
                sternum[i] = Vector3.Lerp(path[2], path[3], 0.45f);
            }
            if (sternum[0] != Vector3.zero && sternum[1] != Vector3.zero)
                Strap(pack, sternum[0], sternum[1], 0.02f, 0.01f, webbing);
            pack.SetParent(chest, true);

            Transform waist = Animator.GetBoneTransform(HumanBodyBones.Spine), hips = Animator.GetBoneTransform(HumanBodyBones.Hips);
            if (waist != null && hips != null)
            {
                Transform belt = Holder("Hip Belt");
                Vector3[] ring = Ring(posed, torso, transform.InverseTransformPoint(waist.position).y, 16);
                if (ring != null)
                {
                    for (int k = 0; k < ring.Length; k++)
                        Strap(belt, ring[k], ring[(k + 1) % ring.Length], 0.075f, 0.016f, webbing, ring);
                    Part(belt, PrimitiveType.Cube, ring[0] + new Vector3(0f, 0f, 0.012f), Quaternion.identity, new Vector3(0.05f, 0.04f, 0.012f),
                        Tinted(library.pack, new Color(0.1f, 0.1f, 0.1f)));
                }
                belt.SetParent(hips, true);
            }
        }

        /// <summary>An empty in this object's space, for building gear in posed-body coordinates before handing it to a bone.</summary>
        Transform Holder(string name)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(transform, false);
            return holder;
        }

        /// <summary>
        /// A flat strip from a to b, its face turned away from the body's centre line, or away from the middle of
        /// the ring it belongs to.
        /// </summary>
        static void Strap(Transform parent, Vector3 a, Vector3 b, float width, float thickness, Material material, Vector3[] ring = null)
        {
            Vector3 along = b - a;
            if (along.sqrMagnitude < 1e-6f)
                return;
            Vector3 middle = (a + b) / 2f;
            Vector3 outward = new(0f, 0f, middle.z);
            if (ring != null)
            {
                Vector3 centre = Vector3.zero;
                foreach (Vector3 point in ring)
                    centre += point / ring.Length;
                outward = Vector3.ProjectOnPlane(middle - centre, Vector3.up);
            }
            if (outward.sqrMagnitude < 1e-6f)
                outward = Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(along, Vector3.ProjectOnPlane(outward, along));
            // A little longer than the gap so neighbouring pieces overlap at the bends.
            Part(parent, PrimitiveType.Cube, middle, rotation, new Vector3(width, thickness, along.magnitude + thickness), material);
        }

        static void Part(Transform parent, PrimitiveType type, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            Discard(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.SetLocalPositionAndRotation(position, rotation);
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        // ---------- Measuring the posed body ----------

        /// <summary>The front (largest z) or back (smallest z) of the torso near (x, y); NaN if nothing is there.</summary>
        // Rocketbox meshes are sparse (a few thousand vertices), so look a hand's width around the point.
        static float Surface(Vector3[] posed, bool[] torso, float x, float y, bool front, float reach = 0.07f)
        {
            float best = float.NaN;
            for (int i = 0; i < posed.Length; i++)
            {
                if (!torso[i] || Mathf.Abs(posed[i].x - x) > reach || Mathf.Abs(posed[i].y - y) > reach)
                    continue;
                if (float.IsNaN(best) || (front ? posed[i].z > best : posed[i].z < best))
                    best = posed[i].z;
            }
            return best;
        }

        /// <summary>The top of the shoulder at x; NaN if nothing is there.</summary>
        static float Top(Vector3[] posed, bool[] torso, float x)
        {
            float best = float.NaN;
            for (int i = 0; i < posed.Length; i++)
                if (torso[i] && Mathf.Abs(posed[i].x - x) < 0.05f && (float.IsNaN(best) || posed[i].y > best))
                    best = posed[i].y;
            return best;
        }

        /// <summary>
        /// Points round the torso at height y, just outside its surface, starting at the front and going round.
        /// Null if the torso has no vertices there.
        /// </summary>
        static Vector3[] Ring(Vector3[] posed, bool[] torso, float y, int count, float clearance = 0.014f)
        {
            Vector2 centre = Vector2.zero;
            int n = 0;
            for (int i = 0; i < posed.Length; i++)
                if (torso[i] && Mathf.Abs(posed[i].y - y) < 0.03f)
                {
                    centre += new Vector2(posed[i].x, posed[i].z);
                    n++;
                }
            if (n < count)
                return null;
            centre /= n;

            var radius = new float[count];
            for (int i = 0; i < posed.Length; i++)
            {
                if (!torso[i] || Mathf.Abs(posed[i].y - y) >= 0.03f)
                    continue;
                Vector2 offset = new Vector2(posed[i].x, posed[i].z) - centre;
                float angle = Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg;
                int bin = Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / 360f * count) % count;
                radius[bin] = Mathf.Max(radius[bin], offset.magnitude);
            }
            var ring = new Vector3[count];
            for (int k = 0; k < count; k++)
            {
                // Bins the mesh missed borrow from their neighbours.
                float r = radius[k] > 0f ? radius[k] : Mathf.Max(radius[(k + 1) % count], radius[(k + count - 1) % count]);
                float angle = k * Mathf.PI * 2f / count;
                ring[k] = new Vector3(centre.x + Mathf.Sin(angle) * (r + clearance), y, centre.y + Mathf.Cos(angle) * (r + clearance));
            }
            return ring;
        }

        // ---------- Ownership ----------

        /// <summary>Destroy, or DestroyImmediate when built outside Play mode (editor snapshots).</summary>
        static void Discard(Object thing)
        {
            if (thing == null)
                return;
            if (Application.isPlaying)
                Destroy(thing);
            else
                DestroyImmediate(thing);
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

        void ClearOwned()
        {
            foreach (Material material in ownedMaterials)
                Discard(material);
            ownedMaterials.Clear();
            foreach (Object mesh in ownedMeshes)
                Discard(mesh);
            ownedMeshes.Clear();
        }
    }
}
