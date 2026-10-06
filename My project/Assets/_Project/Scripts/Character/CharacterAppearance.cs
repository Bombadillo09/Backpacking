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
        readonly List<GameObject> wornBoots = new();
        bool bootsOn = true;

        public CharacterLibrary Library { get => library; set => library = value; }
        public Animator Animator { get; private set; }
        /// <summary>Head turning, feet on slopes and sitting on the ground, layered over the animation.</summary>
        public HikerPose Pose { get; private set; }
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
                Discard(model);
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
            Pose = model.AddComponent<HikerPose>();
            bootsOn = true;

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
            fullBody = Body.sharedMesh;
            Vector3[] posed = StandOnGround(bones);
            if (posed == null)
                return;

            // Gear is fitted to the posed body: the pack against the back, straps over the shoulders, the hip
            // belt round the waist (hiding the seam between jacket and trousers), boots on the feet.
            string[] owners = OwningBones(Body.sharedMesh, Body.bones);
            bool[] torso = System.Array.ConvertAll(owners, bone => bone.StartsWith("spine") || bone == "pelvis" || bone.StartsWith("clavicle"));
            Material bag = Tinted(library.pack, profile.packColour);
            Material webbing = Tinted(library.pack, Color.Lerp(profile.packColour, Color.black, 0.55f));
            AddPack(bones, posed, torso, bag, webbing);
            AddBoots(bones, posed, owners);
            AddCuffs(bones, posed, owners, profile);
        }

        /// <summary>Overall height of the posed hiker, in the model's own scale.</summary>
        public float Height { get; private set; } = 1.8f;

        /// <summary>
        /// Poses the hiker in its idle stance, measures the posed body, and lifts it so the soles of the feet rest
        /// on the ground at this object's origin. The eye height is measured from the posed head.
        /// </summary>
        /// <returns>The posed body's vertices in this object's space, standing on the ground; null if unmeasurable.</returns>
        Vector3[] StandOnGround(Dictionary<string, Transform> bones)
        {
            if (Animator != null && Animator.runtimeAnimatorController != null)
            {
                Animator.Rebind();
                Animator.Update(0f);
            }

            var baked = new Mesh();
            Body.BakeMesh(baked, true);
            Matrix4x4 toLocal = transform.worldToLocalMatrix * Body.transform.localToWorldMatrix;
            Vector3[] posed = baked.vertices;
            float lowest = float.MaxValue, highest = float.MinValue;
            for (int i = 0; i < posed.Length; i++)
            {
                posed[i] = toLocal.MultiplyPoint3x4(posed[i]);
                lowest = Mathf.Min(lowest, posed[i].y);
                highest = Mathf.Max(highest, posed[i].y);
            }
            Discard(baked);
            if (lowest == float.MaxValue)
                return null;

            model.transform.localPosition -= new Vector3(0f, lowest, 0f);
            for (int i = 0; i < posed.Length; i++)
                posed[i].y -= lowest;
            Height = highest - lowest;
            EyeHeight = bones.TryGetValue("Head", out Transform head)
                ? transform.InverseTransformPoint(head.position).y + 0.09f
                : Height * 0.93f;
            return posed;
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
                Discard(copy);
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
            Discard(copy);
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
            };
        }

        /// <summary>Boots on the feet, or bare feet (the boots are set down elsewhere).</summary>
        public void SetBootsOn(bool on)
        {
            if (on == bootsOn)
                return;
            bootsOn = on;
            foreach (GameObject boot in wornBoots)
                if (boot != null)
                    boot.SetActive(on);
        }

        /// <summary>
        /// The hiker's boots with the socks pulled off, to set down beside them: one boot standing, one tipped on
        /// its side with a sock stuffed in it, the other sock lying on the ground. Origin on the ground, facing +Z.
        /// The caller owns the object; its materials belong to this hiker.
        /// </summary>
        public GameObject BuildBootsAndSocks()
        {
            var pile = new GameObject("Boots and Socks");
            Material leather = library.boots;
            Material sole = Tinted(library.boots, new Color(0.08f, 0.07f, 0.06f));
            Material wool = Tinted(library.clothing, new Color(0.62f, 0.6f, 0.55f));

            Boot(pile.transform, new Vector3(-0.09f, 0f, 0f), Quaternion.Euler(0f, -8f, 0f), leather, sole);
            Transform tipped = Boot(pile.transform, new Vector3(0.11f, 0.055f, 0.03f), Quaternion.Euler(0f, 24f, 78f), leather, sole);
            // A sock stuffed into the tipped boot, its end hanging out of the top.
            Part(tipped, PrimitiveType.Capsule, new Vector3(0f, 0.2f, -0.01f), Quaternion.Euler(20f, 0f, 0f), new Vector3(0.07f, 0.06f, 0.07f), wool);
            // The other sock dropped on the ground in front, leg and foot at an angle.
            var sock = new GameObject("Sock").transform;
            sock.SetParent(pile.transform, false);
            sock.localPosition = new Vector3(-0.05f, 0.012f, 0.24f);
            sock.localRotation = Quaternion.Euler(0f, 60f, 0f);
            Part(sock, PrimitiveType.Capsule, new Vector3(0f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.075f, 0.1f, 0.025f), wool);
            Part(sock, PrimitiveType.Capsule, new Vector3(0.05f, 0f, 0.1f), Quaternion.Euler(90f, 50f, 0f), new Vector3(0.07f, 0.065f, 0.025f), wool);
            return pile;
        }

        /// <summary>
        /// A hiking boot: a rounded upper and toe box over a chunky sole, and an ankle shaft with a padded collar.
        /// Origin on the ground under the ankle, toes towards +Z.
        /// </summary>
        static Transform Boot(Transform parent, Vector3 position, Quaternion rotation, Material leather, Material sole)
        {
            var boot = new GameObject("Boot").transform;
            boot.SetParent(parent, false);
            boot.SetLocalPositionAndRotation(position, rotation);
            // Sole and heel: a capsule squashed flat, so the toe and heel are rounded.
            Part(boot, PrimitiveType.Capsule, new Vector3(0f, 0.016f, 0.075f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.108f, 0.15f, 0.032f), sole);
            // The upper over the foot, rising towards the ankle, with a full toe box down to the sole.
            Part(boot, PrimitiveType.Capsule, new Vector3(0f, 0.055f, 0.08f), Quaternion.Euler(84f, 0f, 0f), new Vector3(0.1f, 0.135f, 0.095f), leather);
            Part(boot, PrimitiveType.Sphere, new Vector3(0f, 0.045f, 0.165f), Quaternion.identity, new Vector3(0.102f, 0.08f, 0.13f), leather);
            Part(boot, PrimitiveType.Sphere, new Vector3(0f, 0.055f, -0.02f), Quaternion.identity, new Vector3(0.1f, 0.1f, 0.1f), leather);
            // Ankle shaft and padded collar.
            Part(boot, PrimitiveType.Cylinder, new Vector3(0f, 0.115f, 0f), Quaternion.Euler(-8f, 0f, 0f), new Vector3(0.098f, 0.06f, 0.1f), leather);
            Part(boot, PrimitiveType.Cylinder, new Vector3(0f, 0.175f, -0.008f), Quaternion.Euler(-8f, 0f, 0f), new Vector3(0.106f, 0.014f, 0.108f), sole);
            return boot;
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

        /// <summary>
        /// A pack on the upper back with its lid, front pocket and a sleeping bag strapped under it; shoulder straps
        /// over the shoulders and down the chest with a sternum strap; and a padded hip belt with a buckle.
        /// </summary>
        void AddPack(Dictionary<string, Transform> bones, Vector3[] posed, bool[] torso, Material bag, Material webbing)
        {
            if (!bones.TryGetValue("spine_03", out Transform chest))
                return;
            float chestY = transform.InverseTransformPoint(chest.position).y;

            Transform pack = Holder("Backpack");
            float backZ = Surface(posed, torso, 0f, chestY - 0.1f, false);
            Vector3 back = new(0f, chestY - 0.12f, (float.IsNaN(backZ) ? -0.12f : backZ) - 0.105f);
            Part(pack, PrimitiveType.Cube, back, Quaternion.identity, new Vector3(0.34f, 0.5f, 0.2f), bag);
            Part(pack, PrimitiveType.Cube, back + new Vector3(0f, 0.27f, 0.01f), Quaternion.identity, new Vector3(0.36f, 0.08f, 0.23f), bag);
            Part(pack, PrimitiveType.Cube, back + new Vector3(0f, -0.08f, -0.11f), Quaternion.identity, new Vector3(0.24f, 0.22f, 0.05f), bag);
            Part(pack, PrimitiveType.Cylinder, back + new Vector3(0f, -0.33f, 0.02f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.16f, 0.2f, 0.16f),
                Tinted(library.pack, new Color(0.25f, 0.27f, 0.22f)));

            // Shoulder straps: from the top of the pack, over each shoulder, down the chest and out towards the armpit.
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

            if (bones.TryGetValue("spine_01", out Transform waist) && bones.TryGetValue("pelvis", out Transform pelvis))
            {
                Transform belt = Holder("Hip Belt");
                float y = transform.InverseTransformPoint(waist.position).y;
                Vector3[] ring = Ring(posed, torso, y, 16);
                if (ring != null)
                {
                    for (int k = 0; k < ring.Length; k++)
                        Strap(belt, ring[k], ring[(k + 1) % ring.Length], 0.075f, 0.016f, webbing, ring);
                    Part(belt, PrimitiveType.Cube, ring[0] + new Vector3(0f, 0f, 0.012f), Quaternion.identity, new Vector3(0.05f, 0.04f, 0.012f),
                        Tinted(library.pack, new Color(0.1f, 0.1f, 0.1f)));
                }
                belt.SetParent(pelvis, true);
            }
        }

        /// <summary>A boot on each foot, sized to the foot and following the foot bone.</summary>
        void AddBoots(Dictionary<string, Transform> bones, Vector3[] posed, string[] owners)
        {
            wornBoots.Clear();
            Material sole = Tinted(library.boots, new Color(0.08f, 0.07f, 0.06f));
            foreach ((string foot, string toes) in new[] { ("foot_l", "ball_l"), ("foot_r", "ball_r") })
            {
                if (!bones.TryGetValue(foot, out Transform ankle))
                    continue;
                Vector3 at = transform.InverseTransformPoint(ankle.position);
                at.y = 0f;
                Vector3 forward = Vector3.forward;
                if (bones.TryGetValue(toes, out Transform ball))
                    forward = Vector3.ProjectOnPlane(transform.InverseTransformPoint(ball.position) - at, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward);

                // The foot's extent from heel to toe and side to side, below the ankle.
                float heel = float.MaxValue, toe = float.MinValue, inner = float.MaxValue, outer = float.MinValue;
                for (int i = 0; i < posed.Length; i++)
                {
                    if (owners[i] != foot && owners[i] != toes)
                        continue;
                    Vector3 offset = posed[i] - at;
                    float along = Vector3.Dot(offset, forward), across = Vector3.Dot(offset, right);
                    heel = Mathf.Min(heel, along);
                    toe = Mathf.Max(toe, along);
                    inner = Mathf.Min(inner, across);
                    outer = Mathf.Max(outer, across);
                }
                if (heel == float.MaxValue)
                    (heel, toe, inner, outer) = (-0.06f, 0.2f, -0.045f, 0.045f);

                // The boot is modelled 0.3 m long (heel at -0.075) and 0.108 m wide; stretch it round the foot.
                float length = toe - heel + 0.035f, width = outer - inner + 0.025f;
                var size = new Vector3(width / 0.108f, Mathf.Lerp(1f, length / 0.3f, 0.5f), length / 0.3f);
                Vector3 origin = at + forward * (heel - 0.015f + 0.075f * size.z) + right * ((inner + outer) / 2f);
                Transform holder = Holder("Boot");
                Boot(holder, origin, Quaternion.LookRotation(forward), library.boots, sole).localScale = size;
                holder.SetParent(ankle, true);
                holder.gameObject.SetActive(bootsOn);
                wornBoots.Add(holder.gameObject);
            }
        }

        /// <summary>Hemmed cuffs where the trousers end above the ankles and the sleeves end at the wrists.</summary>
        void AddCuffs(Dictionary<string, Transform> bones, Vector3[] posed, string[] owners, CharacterProfile profile)
        {
            Material trousers = Tinted(library.clothing, Color.Lerp(profile.pantsColour, Color.black, 0.2f));
            Material sleeves = Tinted(library.clothing, Color.Lerp(profile.jacketColour, Color.black, 0.2f));
            foreach ((string bone, Material material) in new[] { ("calf_l", trousers), ("calf_r", trousers), ("lowerarm_l", sleeves), ("lowerarm_r", sleeves) })
            {
                if (!bones.TryGetValue(bone, out Transform limb))
                    continue;
                bool[] mask = System.Array.ConvertAll(owners, owner => owner == bone);
                // The garment ends at the lowest vertex of the limb (legs stand and arms hang straight down).
                float hem = float.MaxValue;
                for (int i = 0; i < posed.Length; i++)
                    if (mask[i])
                        hem = Mathf.Min(hem, posed[i].y);
                if (hem == float.MaxValue)
                    continue;
                Vector3[] ring = Ring(posed, mask, hem + 0.035f, 12, 0.02f);
                if (ring == null)
                    continue;
                Transform cuff = Holder("Cuff");
                for (int k = 0; k < ring.Length; k++)
                    Strap(cuff, ring[k], ring[(k + 1) % ring.Length], 0.045f, 0.012f, material, ring);
                cuff.SetParent(limb, true);
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

        // ---------- Measuring the posed body ----------

        /// <summary>The name of the bone that moves each vertex most ("" if none).</summary>
        static string[] OwningBones(Mesh body, Transform[] bones)
        {
            BoneWeight[] weights = body.boneWeights;
            var owners = new string[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                int strongest = Strongest(weights[i]);
                owners[i] = strongest < bones.Length && bones[strongest] != null ? bones[strongest].name : "";
            }
            return owners;
        }

        /// <summary>The front (largest z) or back (smallest z) of the torso near (x, y); NaN if nothing is there.</summary>
        static float Surface(Vector3[] posed, bool[] torso, float x, float y, bool front, float reach = 0.03f)
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
                if (torso[i] && Mathf.Abs(posed[i].x - x) < 0.025f && (float.IsNaN(best) || posed[i].y > best))
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

        /// <summary>Destroy, or DestroyImmediate when built outside Play mode (editor snapshots).</summary>
        static void Discard(Object thing)
        {
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

        void ClearMaterials()
        {
            foreach (Material material in ownedMaterials)
                if (material != null)
                    Discard(material);
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
                int strongest = Strongest(weights[i]);
                regions[i] = strongest < bones.Length && bones[strongest] != null ? RegionOf(bones[strongest].name) : Region.Skin;
            }
            return regions;
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

        /// <summary>Jacket and trousers as two submeshes of one shell, each pushed out along the normals.</summary>
        static Mesh Clothes(Mesh body, Transform[] bones)
        {
            if (derivedMeshes.TryGetValue(body, out (Mesh clothes, Mesh headless) cached) && cached.clothes != null)
                return cached.clothes;

            Region[] regions = VertexRegions(body, bones);
            // Loose enough to soften the body's muscle lines; the jacket is a fleece, the trousers lighter.
            float[] thickness = { 0.022f, 0.015f };
            Vector3[] vertices = body.vertices;
            Vector3[] normals = body.normals;
            for (int i = 0; i < vertices.Length; i++)
            {
                int region = (int)regions[i];
                if (region is >= 0 and < 2)
                    vertices[i] += normals[i] * thickness[region];
            }

            var pieces = new[] { new List<int>(), new List<int>() };
            for (int sub = 0; sub < body.subMeshCount; sub++)
            {
                int[] triangles = body.GetTriangles(sub);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    // A triangle belongs to a garment only if all its corners do: taking triangles that merely
                    // reach into it leaves spiky hems. The waist seam is under the hip belt, the ankles in the boots.
                    Region a = regions[triangles[t]], b = regions[triangles[t + 1]], c = regions[triangles[t + 2]];
                    Region region = a == b && b == c ? a : Region.Skin;
                    if (region is Region.Jacket or Region.Pants)
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
