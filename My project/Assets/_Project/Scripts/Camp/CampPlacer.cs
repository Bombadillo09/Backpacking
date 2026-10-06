using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backpacking.Camp
{
    public enum CampItem
    {
        Tent,
        FireRing,
        Stove,
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

        [Header("Rules")]
        [SerializeField] float pitchMinutes = 15f;
        [SerializeField] float fireRingMinutes = 5f;
        [SerializeField] int fireRingFirewood = 3;

        static readonly Color ValidColour = new(0.2f, 1f, 0.3f, 0.45f);
        static readonly Color InvalidColour = new(1f, 0.2f, 0.15f, 0.45f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        InputAction interactAction;
        GameObject preview;
        CampItem placing;
        string problem;
        MaterialPropertyBlock tint;
        GUIStyle hintStyle;

        public bool IsPlacing => preview != null;

        void Awake()
        {
            interactAction = inputActions.FindActionMap("Player", true).FindAction("Interact", true);
            tint = new MaterialPropertyBlock();
        }

        /// <summary>Why the item can't be placed at all right now (not carried, not enough wood), or null.</summary>
        public string RequirementProblem(CampItem item) => item switch
        {
            CampItem.Tent => backpack.HasTent ? null : "Tent is already pitched",
            CampItem.Stove => backpack.HasStove ? null : "Stove is already set up",
            CampItem.FireRing => backpack.Firewood >= fireRingFirewood ? null : $"Needs {fireRingFirewood} firewood",
            _ => null,
        };

        public void BeginPlacement(CampItem item)
        {
            if (RequirementProblem(item) != null || activity.IsBusy)
                return;
            CancelPlacement();
            placing = item;
            preview = CreatePreview(PrefabFor(item));
        }

        public void CancelPlacement()
        {
            if (preview != null)
                Destroy(preview);
            preview = null;
        }

        void Update()
        {
            if (!IsPlacing)
                return;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                CancelPlacement();
                return;
            }

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
            if (!Physics.Raycast(viewPoint.position, viewPoint.forward, out RaycastHit hit, maxDistance, ~0, QueryTriggerInteraction.Collide))
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
            float maxSlope = placing == CampItem.Tent ? 15f : 25f;
            if (slope > maxSlope)
                return "Ground is too steep";

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
                    backpack.HasTent = false;
                    activity.Begin("Pitching tent", pitchMinutes, () => Instantiate(tentPrefab, position, rotation));
                    break;
                case CampItem.FireRing:
                    backpack.TryUseFirewood(fireRingFirewood);
                    activity.Begin("Building a fire ring", fireRingMinutes, () =>
                        Instantiate(fireRingPrefab, position, rotation).GetComponent<Campfire>().Build(fireRingFirewood));
                    break;
                case CampItem.Stove:
                    backpack.HasStove = false;
                    Instantiate(stovePrefab, position, rotation);
                    Notifications.Post("Stove set up.");
                    break;
            }
        }

        GameObject PrefabFor(CampItem item) => item switch
        {
            CampItem.Tent => tentPrefab,
            CampItem.FireRing => fireRingPrefab,
            _ => stovePrefab,
        };

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
            foreach (Transform child in copy.GetComponentsInChildren<Transform>(true))
                child.gameObject.SetActive(true);

            copy.name = $"{prefab.name} (preview)";
            copy.transform.SetParent(null, false);
            Destroy(holder);
            return copy;
        }

        void OnGUI()
        {
            if (!IsPlacing)
                return;
            hintStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            string text = problem ?? "Left-click or E to place";
            text += "     ·     Right-click to cancel";
            var rect = new Rect(0f, Screen.height * 0.72f, Screen.width, 30f);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, hintStyle);
            GUI.color = Color.white;
            GUI.Label(rect, text, hintStyle);
        }
    }
}
