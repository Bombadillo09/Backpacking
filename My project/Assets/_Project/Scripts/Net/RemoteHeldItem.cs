using System.Collections.Generic;
using Backpacking.Character;
using Backpacking.Hunting;
using Backpacking.Player;
using Backpacking.Survival;
using UnityEngine;

namespace Backpacking.Net
{
    /// <summary>
    /// What another player's hiker has in hand, as <see cref="HeldItemView"/> and <see cref="Bow"/> show it on your
    /// own: the item in the right fist, swung when they chop and raised to the mouth when they eat or drink; the bow
    /// in the left, raised and drawn as they aim.
    /// </summary>
    public class RemoteHeldItem : MonoBehaviour
    {
        const float SwingSeconds = 0.55f;
        const float UseSeconds = 0.9f;

        RemoteHikerBody body;
        HeldItemLibrary library;
        GameObject item, bow, nocked;
        HotbarSlot shown;
        float raise, swingTime = -1f, useTime = -1f, aim, draw;
        byte swings, uses;
        bool counted;
        readonly List<Material> owned = new();

        public void Initialise(RemoteHikerBody hiker)
        {
            body = hiker;
            HeldItemView own = FindAnyObjectByType<HeldItemView>(FindObjectsInactive.Include);
            library = own != null ? own.Library : null;
        }

        void OnDestroy()
        {
            Show(new HotbarSlot(HotbarKind.Empty));
            ShowBow(false);
            foreach (Material material in owned)
                if (material != null)
                    Destroy(material);
        }

        void Update()
        {
            CharacterAppearance appearance = body != null ? body.Appearance : null;
            if (appearance == null || !appearance.IsBuilt)
                return;
            HikerState state = body.State;
            // A swing or a bite that arrives is played from its start; the first state just sets the count.
            if (counted && state.swings != swings)
                swingTime = 0f;
            if (counted && state.uses != uses)
                useTime = 0f;
            swings = state.swings;
            uses = state.uses;
            counted = true;

            // Put away while busy, asleep or riding.
            bool away = state.seat >= 0 || state.Has(HikerState.Flag.Sleeping) || (state.Has(HikerState.Flag.Busy) && useTime < 0f);
            var held = new HotbarSlot(away ? HotbarKind.Empty : (HotbarKind)state.held, (FoodKind)state.heldFood);
            bool bowInHand = held.kind == HotbarKind.Bow;
            if (!held.Same(shown) && !bowInHand)
                Show(held);
            else if (bowInHand && item != null)
                Show(new HotbarSlot(HotbarKind.Empty));
            ShowBow(bowInHand);

            raise = Mathf.MoveTowards(raise, item != null || bow != null ? 1f : 0f, Time.deltaTime * 4f);
            if (swingTime >= 0f && (swingTime += Time.deltaTime / SwingSeconds) > 1f)
                swingTime = -1f;
            if (useTime >= 0f && (useTime += Time.deltaTime / UseSeconds) > 1f)
                useTime = -1f;
            aim = Mathf.MoveTowards(aim, state.bowAim / 255f, Time.deltaTime * 6f);
            draw = Mathf.MoveTowards(draw, state.bowDraw / 255f, Time.deltaTime * 4f);

            HikerPose pose = appearance.Pose;
            if (pose == null)
                return;
            Look(state, out Vector3 eyes, out Quaternion look, out Quaternion level);
            if (bow != null)
            {
                Bow.Hold(eyes, look, level, aim, draw, out Vector3 grip, out Quaternion frame, out Vector3 drawHand);
                pose.LeftHandTarget = grip;
                pose.LeftHandWeight = raise;
                pose.LeftHandFrame = frame;
                float a = Mathf.SmoothStep(0f, 1f, aim);
                pose.HandTarget = a > 0.01f ? drawHand : null;
                pose.HandWeight = a;
                pose.HandFrame = frame;
                Vector3 nock = BowDesign.SetDraw(bow, draw);
                bow.transform.SetPositionAndRotation(grip, frame);
                nocked.transform.SetLocalPositionAndRotation(nock + Vector3.forward * BowDesign.ArrowLength, Quaternion.identity);
                return;
            }
            if (pose.LeftHandTarget.HasValue)
            {
                pose.LeftHandTarget = null;
                pose.LeftHandWeight = 0f;
            }
            HeldItemView.Hold(eyes, look, swingTime, useTime, out Vector3 target, out Quaternion holdFrame);
            pose.HandTarget = raise > 0f ? target : null;
            pose.HandWeight = raise;
            pose.HandFrame = holdFrame;
        }

        void LateUpdate()
        {
            CharacterAppearance appearance = body != null ? body.Appearance : null;
            if (appearance == null || appearance.Animator == null)
                return;
            if (item != null)
            {
                Look(body.State, out Vector3 eyes, out Quaternion look, out _);
                HeldItemView.Hold(eyes, look, swingTime, useTime, out _, out Quaternion frame);
                HeldItemView.Place(item.transform, appearance.Animator, frame, alignToHand: swingTime < 0f);
                item.SetActive(raise > 0.5f);
            }
            if (bow != null)
            {
                // In the left fist, wherever the IK got the hand to.
                Transform hand = appearance.Animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform middle = appearance.Animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
                if (hand != null)
                {
                    Vector3 fingers = middle != null ? (middle.position - hand.position).normalized : bow.transform.forward;
                    bow.transform.position = hand.position + fingers * 0.045f;
                }
                bow.SetActive(raise > 0.5f);
            }
        }

        /// <summary>Their eyes and where they look, as seen from outside (the pitch eased, as in third person).</summary>
        void Look(HikerState state, out Vector3 eyes, out Quaternion look, out Quaternion level)
        {
            eyes = body.transform.position + Vector3.up * RemoteHikerBody.EyeHeight;
            level = Quaternion.Euler(0f, body.transform.eulerAngles.y, 0f);
            look = Quaternion.Euler(Mathf.Clamp(state.pitch, -40f, 40f) * (bow != null ? 1f : 0.5f), body.transform.eulerAngles.y, 0f);
        }

        void Show(HotbarSlot slot)
        {
            shown = slot;
            if (item != null)
                Destroy(item);
            item = null;
            if (slot.kind is HotbarKind.Empty or HotbarKind.Bow)
                return;
            GameObject prefab = library != null ? library.PrefabFor(slot) : null;
            item = prefab != null ? Instantiate(prefab)
                : slot.kind == HotbarKind.Food ? HeldFood.Build(slot.food, library != null ? library.plain : null, owned) : null;
            if (item == null)
                return;
            item.name = "Held " + Hotbar.Describe(slot);
            foreach (Collider collider in item.GetComponentsInChildren<Collider>())
                Destroy(collider);
            item.SetActive(false);
        }

        void ShowBow(bool shown)
        {
            if (shown && bow == null)
            {
                bow = BowDesign.Bow();
                bow.name = "Held Bow (co-op)";
                nocked = BowDesign.Arrow();
                nocked.transform.SetParent(bow.transform, false);
                foreach (Collider collider in bow.GetComponentsInChildren<Collider>())
                    Destroy(collider);
                bow.SetActive(false);
            }
            else if (!shown && bow != null)
            {
                Destroy(bow);
                bow = nocked = null;
                CharacterAppearance appearance = body != null ? body.Appearance : null;
                if (appearance != null && appearance.Pose != null)
                {
                    appearance.Pose.LeftHandTarget = null;
                    appearance.Pose.LeftHandWeight = 0f;
                    appearance.Pose.HandTarget = null;
                    appearance.Pose.HandWeight = 0f;
                }
            }
        }
    }
}
