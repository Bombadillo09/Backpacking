using System.Collections.Generic;
using Backpacking.Camp;
using Backpacking.Character;
using Backpacking.Interaction;
using Backpacking.Survival;
using UnityEngine;

namespace Backpacking.Player
{
    /// <summary>
    /// Shows the hotbar item in the hiker's right hand. The arm is raised with IK to hold it out in front (low and
    /// to the right of the view in first person), and the item sits in the fist, following the hand. Swinging the
    /// machete chops it down through an arc; eating, drinking and bandaging bring the item up to the mouth.
    /// </summary>
    public class HeldItemView : MonoBehaviour
    {
        [SerializeField] Hotbar hotbar;
        [SerializeField] HeldItemLibrary library;
        [SerializeField] FirstPersonController player;
        [SerializeField] CharacterAppearance appearance;
        [SerializeField] PlayerActivity activity;
        [SerializeField] float swingSeconds = 0.32f;
        [SerializeField] float useSeconds = 0.9f;

        GameObject item;
        HotbarSlot shown;
        float raise, swingTime = -1f, useTime = -1f;
        readonly List<Material> owned = new();

        void OnEnable()
        {
            Undergrowth.Swung += OnSwung;
            Hotbar.Used += OnUsed;
        }

        void OnDisable()
        {
            Undergrowth.Swung -= OnSwung;
            Hotbar.Used -= OnUsed;
            Show(new HotbarSlot(HotbarKind.Empty));
        }

        void OnDestroy()
        {
            foreach (Material material in owned)
                if (material != null)
                    Destroy(material);
        }

        void OnSwung() => swingTime = 0f;
        void OnUsed(HotbarKind kind) => useTime = 0f;

        void Update()
        {
            // Put away while busy, asleep or sitting with a task in hand.
            HotbarSlot held = activity.IsBusy && useTime < 0f ? new HotbarSlot(HotbarKind.Empty) : hotbar.Held;
            if (!held.Same(shown))
                Show(held);

            raise = Mathf.MoveTowards(raise, item != null ? 1f : 0f, Time.deltaTime * 4f);
            if (swingTime >= 0f && (swingTime += Time.deltaTime / swingSeconds) > 1f)
                swingTime = -1f;
            if (useTime >= 0f && (useTime += Time.deltaTime / useSeconds) > 1f)
                useTime = -1f;

            HikerPose pose = appearance.Pose;
            if (pose == null)
                return;
            HoldFrame(out Vector3 target, out Quaternion frame);
            pose.HandTarget = raise > 0f ? target : null;
            pose.HandWeight = raise;
            pose.HandFrame = frame;
        }

        void LateUpdate()
        {
            if (item == null || appearance.Animator == null)
                return;
            HoldFrame(out _, out Quaternion frame);
            Place(item.transform, appearance.Animator, frame);
            item.SetActive(raise > 0.5f);
        }

        /// <summary>
        /// Where the right hand holds the item and which way the item points (its frame: +Y up along the item,
        /// +Z ahead), including any swing or bite in progress.
        /// </summary>
        void HoldFrame(out Vector3 target, out Quaternion frame)
        {
            Transform view = player.CameraPivot;
            bool firstPerson = !player.ThirdPerson;
            Quaternion look = firstPerson ? view.rotation
                : Quaternion.Euler(Mathf.Clamp(view.eulerAngles.x > 180f ? view.eulerAngles.x - 360f : view.eulerAngles.x, -30f, 30f) * 0.5f,
                    player.transform.eulerAngles.y, 0f);
            Vector3 eyes = firstPerson ? view.position : player.transform.position + Vector3.up * (appearance.EyeHeight * appearance.transform.lossyScale.y);
            target = eyes + look * new Vector3(0.22f, -0.22f, 0.42f);
            frame = look * Quaternion.Euler(-15f, 0f, -8f);

            if (swingTime >= 0f)
            {
                // A chop: up over the shoulder, then down and across.
                float t = swingTime;
                float wind = Mathf.Clamp01(t / 0.3f), strike = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.75f, t));
                float back = Mathf.InverseLerp(0.75f, 1f, t);
                float pitch = Mathf.Lerp(Mathf.Lerp(0f, -55f, wind), 75f, strike) * (1f - back);
                target += look * (new Vector3(-0.08f * strike, 0.18f * wind - 0.32f * strike, 0.1f * strike) * (1f - back));
                frame = frame * Quaternion.Euler(pitch, 0f, -20f * strike * (1f - back));
            }
            else if (useTime >= 0f)
            {
                // Up to the mouth and back, tipping it to drink or eat.
                float bell = Mathf.Sin(Mathf.Clamp01(useTime) * Mathf.PI);
                Vector3 mouth = eyes + look * new Vector3(0.03f, -0.1f, 0.18f);
                target = Vector3.Lerp(target, mouth, bell);
                frame = frame * Quaternion.Euler(-60f * bell, 0f, 0f);
            }
        }

        /// <summary>
        /// Puts an item in the right fist: at the palm, a little along the hand from the wrist, pointing along the
        /// hold frame but kept square to the hand.
        /// </summary>
        public static void Place(Transform item, Animator animator, Quaternion frame)
        {
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            if (hand == null)
                return;
            Vector3 fingers = middle != null ? (middle.position - hand.position).normalized : frame * Vector3.forward;
            Vector3 up = Vector3.ProjectOnPlane(frame * Vector3.up, fingers);
            if (up.sqrMagnitude < 1e-4f)
                up = frame * Vector3.up;
            Vector3 palm = hand.position + fingers * 0.055f;
            Vector3 ahead = Vector3.ProjectOnPlane(frame * Vector3.forward, up);
            item.SetPositionAndRotation(palm, Quaternion.LookRotation(ahead.sqrMagnitude > 1e-4f ? ahead.normalized : fingers, up.normalized));
        }

        void Show(HotbarSlot slot)
        {
            shown = slot;
            if (item != null)
                Destroy(item);
            item = null;
            if (slot.kind == HotbarKind.Empty || appearance.Animator == null)
                return;
            GameObject prefab = library != null ? library.PrefabFor(slot) : null;
            item = prefab != null ? Instantiate(prefab) : slot.kind == HotbarKind.Food ? HeldFood.Build(slot.food, library != null ? library.plain : null, owned) : null;
            if (item == null)
                return;
            item.name = "Held " + Hotbar.Describe(slot);
            foreach (Collider collider in item.GetComponentsInChildren<Collider>())
                Destroy(collider);
            item.SetActive(false);
        }
    }
}
