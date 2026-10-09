using Backpacking.Character;
using Backpacking.Player;
using Backpacking.UI;
using Backpacking.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.Net
{
    /// <summary>
    /// Another player's hiker, as seen in this game: their body from their profile, animated from what their game
    /// sends (walking, crouching, sitting, busy at a task, swinging the machete, riding in the truck), with their
    /// pack and what's strapped to it, and their name above them. Solid, so you bump into each other.
    /// </summary>
    public class RemoteHikerBody : MonoBehaviour
    {
        const float EyeHeight = 1.68f;
        const float NameRange = 60f;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int CrouchId = Animator.StringToHash("Crouch");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int BusyId = Animator.StringToHash("Busy");
        static readonly int SwingId = Animator.StringToHash("Swing");
        static readonly int SeatedId = Animator.StringToHash("Seated");

        CoopHiker hiker;
        CharacterAppearance appearance;
        CapsuleCollider capsule;
        Label nameTag;
        HikerState state;
        bool hasState;
        Vector3 target, velocity;
        float lastReceived;
        float yaw;
        float seated;
        byte swings;
        int seatShown = -1;

        public HikerState State => state;
        public CharacterAppearance Appearance => appearance;

        public static RemoteHikerBody Create(CoopHiker hiker, CharacterLibrary library)
        {
            var root = new GameObject("Co-op hiker");
            var body = root.AddComponent<RemoteHikerBody>();
            body.hiker = hiker;
            body.capsule = root.AddComponent<CapsuleCollider>();
            body.capsule.height = 1.8f;
            body.capsule.radius = 0.3f;
            body.capsule.center = new Vector3(0f, 0.9f, 0f);
            var avatar = new GameObject("Avatar");
            avatar.transform.SetParent(root.transform, false);
            body.appearance = avatar.AddComponent<CharacterAppearance>();
            body.appearance.Library = library;
            return body;
        }

        public void Build(CharacterProfile profile)
        {
            name = $"Co-op hiker ({profile.name})";
            appearance.Build(profile);
            appearance.SetFirstPerson(false);
            float eyes = appearance.EyeHeight;
            appearance.transform.localScale = Vector3.one * (eyes > 0.5f ? EyeHeight / eyes : 1f);
            if (nameTag == null && GameUI.Current != null)
            {
                nameTag = UIBuild.Text("", "name-tag", "shadowed");
                nameTag.style.position = Position.Absolute;
                nameTag.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-100f));
                GameUI.Current.Hud.Add(nameTag.IgnoreMouse());
            }
            if (nameTag != null)
                nameTag.text = profile.name;
        }

        void OnDestroy()
        {
            nameTag?.RemoveFromHierarchy();
        }

        /// <summary>A new state from their game.</summary>
        public void Receive(HikerState next)
        {
            if (hasState)
            {
                // How fast they're going, to carry on smoothly between updates.
                float gap = Mathf.Max(Time.time - lastReceived, 0.02f);
                velocity = Vector3.ClampMagnitude((next.position - state.position) / gap, 30f);
            }
            else
            {
                transform.position = next.position;
                yaw = next.yaw;
            }
            state = next;
            target = next.position;
            lastReceived = Time.time;
            hasState = true;
        }

        void Update()
        {
            if (!hasState || !appearance.IsBuilt)
                return;
            UpdatePlace();
            UpdateAnimation();
        }

        void LateUpdate() => UpdateNameTag();

        void UpdatePlace()
        {
            Pickup truck = Pickup.Current;
            int seatIndex = truck != null && state.seat >= 0 && state.seat < truck.SeatCount ? state.seat : -1;
            if (seatIndex != seatShown)
            {
                seatShown = seatIndex;
                if (seatIndex >= 0)
                {
                    // Riding: sit in the seat in this game's truck, so they move with it exactly.
                    transform.SetParent(truck.SeatAt(seatIndex), false);
                    transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                }
                else
                {
                    transform.SetParent(null, true);
                    transform.position = target;
                }
                capsule.enabled = seatIndex < 0;
            }
            if (seatIndex >= 0)
                return;

            // Run ahead on their last speed for a moment (updates come a few times a second), then settle.
            float sinceUpdate = Time.time - lastReceived;
            Vector3 predicted = target + velocity * Mathf.Min(sinceUpdate, 0.25f);
            float ease = 1f - Mathf.Exp(-14f * Time.deltaTime);
            if ((transform.position - predicted).sqrMagnitude > 100f)
                transform.position = predicted;
            else
                transform.position = Vector3.Lerp(transform.position, predicted, ease);
            yaw = Mathf.LerpAngle(yaw, state.yaw, ease);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void UpdateAnimation()
        {
            Animator animator = appearance.Animator;
            if (animator == null)
                return;
            bool sitting = state.Has(HikerState.Flag.Seated);
            bool inChair = state.Has(HikerState.Flag.InChair);
            bool busy = state.Has(HikerState.Flag.Busy);
            animator.SetFloat(SpeedId, state.seat >= 0 ? 0f : state.speed, 0.1f, Time.deltaTime);
            animator.SetBool(CrouchId, state.Has(HikerState.Flag.Crouching));
            animator.SetBool(GroundedId, state.Has(HikerState.Flag.Grounded));
            animator.SetBool(BusyId, busy);
            animator.SetBool(SeatedId, sitting);
            if (state.swings != swings)
            {
                swings = state.swings;
                animator.SetTrigger(SwingId);
            }

            HikerPose pose = appearance.Pose;
            if (pose != null)
            {
                seated = Mathf.MoveTowards(seated, sitting ? 1f : 0f, Time.deltaTime * 2.5f);
                pose.Seated = inChair ? 0f : seated;
                pose.GroundFeet = state.seat < 0 && state.Has(HikerState.Flag.Grounded) && !busy;
                // The head turns where they're looking.
                Vector3 eyes = transform.position + Vector3.up * EyeHeight;
                pose.LookTarget = busy ? null : eyes + Quaternion.Euler(state.pitch, state.yaw, 0f) * Vector3.forward * 20f;
            }

            bool packOn = state.Has(HikerState.Flag.PackOnBack);
            appearance.SetPackWorn(packOn);
            if (appearance.PackGear != null && packOn)
                appearance.PackGear.Show(state.Has(HikerState.Flag.Pad), state.Has(HikerState.Flag.Tent), state.Has(HikerState.Flag.Chair),
                    state.Has(HikerState.Flag.Bottle), state.Has(HikerState.Flag.Machete));
        }

        void UpdateNameTag()
        {
            if (nameTag == null)
                return;
            Camera view = Camera.main;
            Vector3 head = transform.position + Vector3.up * 2.05f;
            bool show = view != null && !PlayerControlLock.CursorNeeded;
            if (show)
            {
                Vector3 toHead = head - view.transform.position;
                show = toHead.magnitude < NameRange && Vector3.Dot(toHead, view.transform.forward) > 0.1f;
            }
            nameTag.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show || nameTag.panel == null)
                return;
            Vector2 at = RuntimePanelUtils.CameraTransformWorldToPanel(nameTag.panel, head, view);
            nameTag.style.left = at.x;
            nameTag.style.top = at.y;
            // Fainter further away.
            nameTag.style.opacity = Mathf.Lerp(1f, 0.35f, (head - view.transform.position).magnitude / NameRange);
        }
    }
}
