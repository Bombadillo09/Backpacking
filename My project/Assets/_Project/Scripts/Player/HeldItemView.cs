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
        [SerializeField] float swingSeconds = 0.55f;
        [SerializeField] float useSeconds = 0.9f;

        GameObject item;
        HotbarSlot shown;
        float raise, swingTime = -1f, useTime = -1f;
        readonly List<Material> owned = new();

        public HeldItemLibrary Library => library;

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
            // The bow poses both hands itself (see Hunting.Bow).
            if (pose == null || held.kind == HotbarKind.Bow)
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
            // Mid-swing the blade follows the chop; otherwise it's kept square to the fist.
            Place(item.transform, appearance.Animator, frame, alignToHand: swingTime < 0f);
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
            Hold(eyes, look, swingTime, useTime, out target, out frame);
        }

        // A chop as poses in the view's space (offset from the eyes, and the item's tilt): at rest, wound up with the
        // blade back over the right shoulder, struck down and across to the left, followed through, and back.
        static readonly (float time, Vector3 offset, Vector3 tilt)[] Chop =
        {
            (0f, new Vector3(0.22f, -0.22f, 0.42f), new Vector3(-15f, 0f, -8f)),
            (0.38f, new Vector3(0.3f, -0.02f, 0.28f), new Vector3(-70f, 12f, -25f)),
            (0.58f, new Vector3(0.04f, -0.34f, 0.5f), new Vector3(95f, -18f, 12f)),
            (0.7f, new Vector3(-0.03f, -0.42f, 0.44f), new Vector3(118f, -22f, 18f)),
            (1f, new Vector3(0.22f, -0.22f, 0.42f), new Vector3(-15f, 0f, -8f)),
        };

        /// <summary>
        /// Where the right hand holds an item and the item's frame (+Y along it, +Z ahead), for eyes looking
        /// <paramref name="look"/>, partway through a swing or a bite (−1 for neither).
        /// </summary>
        public static void Hold(Vector3 eyes, Quaternion look, float swing, float use, out Vector3 target, out Quaternion frame)
        {
            Vector3 offset = Chop[0].offset, tilt = Chop[0].tilt;
            if (swing >= 0f)
            {
                for (int k = 0; k < Chop.Length - 1; k++)
                {
                    if (swing > Chop[k + 1].time)
                        continue;
                    // Ease in and out of each pose; the strike itself is the quick one.
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Chop[k].time, Chop[k + 1].time, swing));
                    offset = Vector3.Lerp(Chop[k].offset, Chop[k + 1].offset, t);
                    tilt = Vector3.Lerp(Chop[k].tilt, Chop[k + 1].tilt, t);
                    break;
                }
            }
            target = eyes + look * offset;
            frame = look * Quaternion.Euler(tilt);

            if (swing < 0f && use >= 0f)
            {
                // Up to the mouth and back, tipping it to drink or eat.
                float bell = Mathf.Sin(Mathf.Clamp01(use) * Mathf.PI);
                Vector3 mouth = eyes + look * new Vector3(0.03f, -0.1f, 0.18f);
                target = Vector3.Lerp(target, mouth, bell);
                frame = frame * Quaternion.Euler(-60f * bell, 0f, 0f);
            }
        }

        /// <summary>
        /// Puts an item in the right fist: at the palm, a little along the hand from the wrist, pointing along the
        /// hold frame but kept square to the hand.
        /// </summary>
        public static void Place(Transform item, Animator animator, Quaternion frame, bool alignToHand = true)
        {
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            if (hand == null)
                return;
            Vector3 fingers = middle != null ? (middle.position - hand.position).normalized : frame * Vector3.forward;
            Vector3 up = alignToHand ? Vector3.ProjectOnPlane(frame * Vector3.up, fingers) : frame * Vector3.up;
            if (up.sqrMagnitude < 1e-4f)
                up = frame * Vector3.up;
            // In the palm, a little along the hand from the wrist and toward the thumb, clear of the fingers.
            Vector3 palm = hand.position + fingers * 0.05f + frame * Vector3.left * 0.03f;
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
