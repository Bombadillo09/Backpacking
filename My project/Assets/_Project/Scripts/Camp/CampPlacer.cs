using System.Collections.Generic;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Backpacking.Camp
{
    public enum CampItem
    {
        Tent,
        FireRing,
        Stove,
        Snare,
        /// <summary>Not gear: a spot to clear of brush with the machete.</summary>
        Clearing,
        Chair,
    }

    /// <summary>
    /// Places camp gear in the world. Shows a see-through preview where you're looking, green when the
    /// spot is flat and clear. Left-click or E to place, right-click to cancel.
    /// </summary>
    public class CampPlacer : MonoBehaviour
    {
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] Transform viewPoint;
        [SerializeField] Backpack backpack;
        [SerializeField] PlayerActivity activity;
        [SerializeField] Material previewMaterial;
        [SerializeField] float maxDistance = 6f;

        [Header("Prefabs")]
        [SerializeField] GameObject tentPrefab;
        [SerializeField] GameObject fireRingPrefab;
        [SerializeField] GameObject stovePrefab;
        [SerializeField] GameObject snarePrefab;
        [SerializeField] GameObject chairPrefab;
        [Tooltip("The see-through disc shown while choosing a spot to clear.")]
        [SerializeField] GameObject clearingPrefab;

        [Header("Clearing")]
        [SerializeField] GroundClearing clearing;
        [SerializeField] Vitals vitals;

        [Header("Rules")]
        [Tooltip("Game minutes to unpack the tent and lay it out flat; the poles and fabric are separate steps.")]
        [SerializeField] float layOutMinutes = 3f;
        [SerializeField] float fireRingMinutes = 5f;
        [SerializeField] int fireRingFirewood = 3;

        static readonly Color ValidColour = new(0.2f, 1f, 0.3f, 0.45f);
        static readonly Color InvalidColour = new(1f, 0.2f, 0.15f, 0.45f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly List<(CampItem kind, GameObject instance)> placed = new();
        InputAction interactAction;
        GameObject preview;
        CampItem placing;
        string problem;
        MaterialPropertyBlock tint;
        FirstPersonController player;
        Label hint;

        public bool IsPlacing => preview != null;

        /// <summary>Gear currently set up in the world (packed-up gear drops out of the list).</summary>
        public IEnumerable<(CampItem kind, GameObject instance)> PlacedItems
        {
            get
            {
                placed.RemoveAll(entry => entry.instance == null);
                return placed;
            }
        }

        /// <summary>Creates set-up gear in the world, e.g. when placing it or loading a save.</summary>
        public GameObject Spawn(CampItem item, Vector3 position, Quaternion rotation)
        {
            GameObject instance = Instantiate(PrefabFor(item), position, rotation);
            placed.Add((item, instance));
            return instance;
        }

        void Awake()
        {
            interactAction = inputActions.FindActionMap("Player", true).FindAction("Interact", true);
            tint = new MaterialPropertyBlock();
            player = GetComponent<FirstPersonController>();
        }

        /// <summary>Why the item can't be placed at all right now (not carried, not enough wood), or null.</summary>
        public string RequirementProblem(CampItem item) => item switch
        {
            CampItem.Tent => PackHandling.Current != null && PackHandling.Current.TentBag != null ? null
                : !backpack.OwnsTent ? "You don't have a tent"
                : backpack.HasTent ? "Take your pack off and take the tent bag out of it first"
                : "Your tent is already out",
            CampItem.Stove => !backpack.OwnsStove ? "You don't have a stove"
                : !backpack.HasStove ? "Your stove is already out" : GearProblem(),
            CampItem.FireRing => backpack.Firewood >= fireRingFirewood ? null : $"Needs {fireRingFirewood} firewood",
            CampItem.Snare => backpack.Snares <= 0 ? "No snares left" : GearProblem(),
            CampItem.Chair => !backpack.HasChair ? "You don't have a chair. Trading posts sell them"
                : !backpack.ChairInPack ? "Your chair is already out" : GearProblem(),
            CampItem.Clearing => backpack.HasMachete ? null : "You need a machete",
            _ => null,
        };

        /// <summary>Camp gear lives inside the pack: take the pack off and stay beside it to get it out.</summary>
        string GearProblem() =>
            PackHandling.Current == null ? null
            : backpack.IsWorn ? "Take your pack off first, to get it out"
            : !PackHandling.Current.CanReachPack ? "Go back to your pack to get it out"
            : null;

        public void BeginPlacement(CampItem item)
        {
            if (RequirementProblem(item) != null || activity.IsBusy)
                return;
            CancelPlacement();
            // Get up to place gear.
            if (RestMode.Current != null)
                RestMode.Current.StandUp();
            placing = item;
            preview = item == CampItem.Tent ? CreateTentPreview() : item == CampItem.Chair ? CreateChairPreview() : CreatePreview(PrefabFor(item));
            GameUI.ClaimEscape(this, CancelPlacement);
        }

        public void CancelPlacement()
        {
            if (preview != null)
                Destroy(preview);
            preview = null;
            GameUI.ReleaseEscape(this);
        }

        void Update()
        {
            if (!IsPlacing)
                return;

            // Right-click, B and Esc cancel through the UI's cancel handling.
            Mouse mouse = Mouse.current;
            bool valid = UpdatePreviewPose(out Vector3 position, out Quaternion rotation);
            foreach (Renderer previewRenderer in preview.GetComponentsInChildren<Renderer>())
            {
                tint.SetColor(BaseColorId, valid ? ValidColour : InvalidColour);
                previewRenderer.SetPropertyBlock(tint);
            }

            bool confirm = interactAction.WasPressedThisFrame() || (mouse != null && mouse.leftButton.wasPressedThisFrame);
            if (confirm && valid && !PlayerControlLock.MovementLocked)
                Place(position, rotation);
        }

        bool UpdatePreviewPose(out Vector3 position, out Quaternion rotation)
        {
            // Face the gear's front (+Z) back toward the player, so the tent door opens toward you.
            Vector3 facing = -viewPoint.forward;
            facing.y = 0f;
            rotation = facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : transform.rotation;
            position = default;

            // Triggers are included so the water surface stops the ray instead of the lake bed.
            Vector3 origin = player != null ? player.AimOrigin : viewPoint.position;
            if (!Physics.Raycast(origin, viewPoint.forward, out RaycastHit hit, maxDistance, ~0, QueryTriggerInteraction.Collide))
            {
                preview.SetActive(false);
                problem = "Too far away";
                return false;
            }

            preview.SetActive(true);
            position = hit.point;
            preview.transform.SetPositionAndRotation(position, rotation);

            problem = PlacementProblem(hit, position, rotation);
            return problem == null;
        }

        string PlacementProblem(RaycastHit hit, Vector3 position, Quaternion rotation)
        {
            if (hit.collider.GetComponent<WaterSource>() != null)
                return "Can't place it in water";
            if (hit.collider is not TerrainCollider)
                return "Place it on the ground";

            float slope = Vector3.Angle(hit.normal, Vector3.up);
            float maxSlope = placing is CampItem.Tent or CampItem.Chair ? 15f : 25f;
            if (slope > maxSlope)
                return "Ground is too steep";

            // Clearing is how you deal with whatever's in the way.
            if (placing == CampItem.Clearing)
                return null;
            // A tent or fire needs bare ground, not brush and deadfall.
            if (placing is CampItem.Tent or CampItem.FireRing && clearing != null && clearing.HasBrush(position))
                return "Too much brush here. Clear a campsite first (Backpack > Clear campsite)";

            if (FootprintBlocked(position, rotation))
                return "Something is in the way";

            return null;
        }

        bool FootprintBlocked(Vector3 position, Quaternion rotation)
        {
            Bounds bounds = FootprintOf(preview);
            Vector3 centre = position + rotation * bounds.center + Vector3.up * 0.15f;
            Vector3 halfExtents = bounds.extents - new Vector3(0.02f, 0.15f, 0.02f);
            foreach (Collider overlap in Physics.OverlapBox(centre, halfExtents, rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (overlap is TerrainCollider || overlap.transform.IsChildOf(transform))
                    continue;
                // Your own pack and tent bag can be moved out of the way.
                if (overlap.GetComponentInParent<GroundPack>() != null || overlap.GetComponentInParent<TentBag>() != null)
                    continue;
                return true;
            }
            return false;
        }

        /// <summary>Local-space bounds of the preview's renderers, relative to its root.</summary>
        static Bounds FootprintOf(GameObject root)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Bounds mesh = filter.sharedMesh.bounds;
                Matrix4x4 toRoot = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(i % 2 == 0 ? mesh.min.x : mesh.max.x, i / 2 % 2 == 0 ? mesh.min.y : mesh.max.y, i / 4 == 0 ? mesh.min.z : mesh.max.z);
                    Vector3 point = toRoot.MultiplyPoint3x4(corner);
                    if (first)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        first = false;
                    }
                    else
                        bounds.Encapsulate(point);
                }
            }
            return bounds;
        }

        void Place(Vector3 position, Quaternion rotation)
        {
            CampItem item = placing;
            CancelPlacement();

            switch (item)
            {
                case CampItem.Tent:
                    PackHandling.Current.ConsumeTentBag();
                    activity.Begin("Unpacking the tent and spreading it out", layOutMinutes, () =>
                    {
                        Spawn(item, position, rotation).GetComponent<Tent>().Setup(backpack.TentModel, TentStage.LaidOut);
                        Notifications.Post("Tent laid out. Look at it to assemble and set the poles.", 4f);
                    });
                    break;
                case CampItem.FireRing:
                    backpack.TryUseFirewood(fireRingFirewood);
                    activity.Begin("Building a fire ring", fireRingMinutes, () =>
                        Spawn(item, position, rotation).GetComponent<Campfire>().Build(fireRingFirewood));
                    break;
                case CampItem.Stove:
                    backpack.HasStove = false;
                    Spawn(item, position, rotation);
                    Notifications.Post("Stove set up.");
                    break;
                case CampItem.Clearing:
                    (float minutes, int _) = clearing.Estimate(position);
                    activity.Begin("Clearing a campsite", minutes, () => clearing.Clear(position, backpack, vitals));
                    break;
                case CampItem.Chair:
                    backpack.ChairInPack = false;
                    Spawn(item, position, rotation).GetComponent<CampChair>().Setup(ChairStage.Packed);
                    Notifications.Post("Chair out of its sack. Look at it to put the frame together.", 3.5f);
                    break;
                case CampItem.Snare:
                    backpack.TryUseSnare();
                    Spawn(item, position, rotation);
                    Notifications.Post("Snare set. Animals won't come near while you're close, so leave it be for a while.");
                    break;
            }
        }

        GameObject PrefabFor(CampItem item) => item switch
        {
            CampItem.Tent => tentPrefab,
            CampItem.FireRing => fireRingPrefab,
            CampItem.Snare => snarePrefab,
            CampItem.Clearing => clearingPrefab,
            CampItem.Chair => chairPrefab,
            _ => stovePrefab,
        };

        /// <summary>The chair, set up and see-through, to choose a spot for it.</summary>
        GameObject CreateChairPreview()
        {
            var root = new GameObject("Chair (preview)");
            CampChair.Build(root.transform, ChairStage.Ready, previewMaterial, previewMaterial);
            foreach (Renderer previewRenderer in root.GetComponentsInChildren<Renderer>())
                previewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        /// <summary>Your tent as it'll stand once pitched, see-through, to choose a spot for it.</summary>
        GameObject CreateTentPreview()
        {
            var root = new GameObject("Tent (preview)");
            TentDesign.Build(root.transform, backpack.TentModel, TentStage.Pitched, tentPrefab.GetComponent<Tent>().Materials);
            foreach (Renderer previewRenderer in root.GetComponentsInChildren<Renderer>())
            {
                previewRenderer.sharedMaterial = previewMaterial;
                previewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return root;
        }

        /// <summary>
        /// A visual-only copy of a prefab. It's created under an inactive parent so none of its scripts
        /// wake up, then stripped of scripts, colliders, lights and effects.
        /// </summary>
        GameObject CreatePreview(GameObject prefab)
        {
            var holder = new GameObject("Placement Preview Holder");
            holder.SetActive(false);
            GameObject copy = Instantiate(prefab, holder.transform);

            foreach (MonoBehaviour script in copy.GetComponentsInChildren<MonoBehaviour>(true))
                DestroyImmediate(script);
            foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(collider);
            foreach (ParticleSystem particles in copy.GetComponentsInChildren<ParticleSystem>(true))
                DestroyImmediate(particles.gameObject);
            foreach (Light light in copy.GetComponentsInChildren<Light>(true))
                DestroyImmediate(light);
            foreach (Renderer previewRenderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[previewRenderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = previewMaterial;
                previewRenderer.sharedMaterials = materials;
                previewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            copy.name = $"{prefab.name} (preview)";
            copy.transform.SetParent(null, false);
            Destroy(holder);
            return copy;
        }

        void Start()
        {
            hint = UIBuild.Text("", "hint", "shadowed");
            hint.SetVisible(false);
            GameUI.Current.Hud.Add(hint.IgnoreMouse());
        }

        void LateUpdate()
        {
            if (hint == null)
                return;
            hint.SetVisible(IsPlacing);
            if (IsPlacing)
                hint.SetText($"{problem ?? PlaceText()}     ·     Right-click, B or Esc to cancel");
        }

        string PlaceText()
        {
            if (placing != CampItem.Clearing || clearing == null || !preview.activeSelf)
                return "Left-click, E or Y to place";
            (float minutes, int trees) = clearing.Estimate(preview.transform.position);
            string saplings = trees == 0 ? "" : trees == 1 ? ", fells a sapling" : $", fells {trees} saplings";
            return $"Left-click, E or Y to clear here (about {minutes:0} min{saplings})";
        }
    }
}
