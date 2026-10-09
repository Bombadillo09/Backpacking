using System.Collections.Generic;
using Backpacking.Camp;
using Backpacking.Character;
using Backpacking.Interaction;
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
    /// pack and what's strapped to it, and their name above them. Solid, so you bump into each other. Look at them
    /// and press E to look after them: hand them things or money, give them a drink, bandage a cut, give
    /// antibiotics, or help them up when they're down (see CoopWorld.Help).
    /// </summary>
    public class RemoteHikerBody : MonoBehaviour, IInteractable
    {
        /// <summary>Every remote hiker is scaled to this eye height.</summary>
        public const float EyeHeight = 1.68f;
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
        // Asleep in a tent: which tent and place, and whether they're hidden in their sleeping bag.
        Tent sleepingIn;
        int sleepingSlot = -1;
        bool hidden;

        public HikerState State => state;
        public string DisplayName => hiker != null ? hiker.HikerName : "Hiker";

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            CoopWorld world = CoopWorld.Current;
            if (world == null || hiker == null)
                return;
            ulong friend = hiker.OwnerClientId;
            string name = hiker.HikerName;
            Survival.Backpack backpack = interactor.Backpack;
            if (state.Has(HikerState.Flag.Downed))
                options.Add(new InteractionOption($"Help {name} up", () => world.HelpUp(friend, name)));
            if (state.Has(HikerState.Flag.Bleeding))
                options.Add(new InteractionOption($"Bandage {name}'s cut (1 of your {backpack.Bandages} bandages)", () => world.Bandage(friend, name),
                    backpack.Bandages <= 0 ? "You have no bandages" : null));
            if (state.Has(HikerState.Flag.Infected) && !state.Has(HikerState.Flag.OnAntibiotics))
                options.Add(new InteractionOption($"Give {name} a course of antibiotics", () => world.GiveAntibiotics(friend),
                    backpack.Antibiotics <= 0 ? "You have no antibiotics" : null));
            options.Add(new InteractionOption($"Hand {name} something from your pack", () => world.OpenGift(friend, name),
                !backpack.HasPack ? "You have no pack to give from" : null));
            options.Add(new InteractionOption($"Give {name} a drink ({backpack.SipLitres:0.00} L of your water)", () => world.GiveDrink(friend),
                backpack.SafeWater < backpack.SipLitres ? "You have no safe water" : null));
            options.Add(new InteractionOption($"Give {name} $10", () => world.GiveMoney(friend, 10), backpack.Money < 10 ? "You have less than $10" : null));
        }
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
            root.AddComponent<RemoteHeldItem>().Initialise(body);
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
            if (sleepingIn != null)
                sleepingIn.SetSleeper(sleepingSlot, false);
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
            UpdateSleeping();
        }

        /// <summary>
        /// Asleep in a tent: in their sleeping bag, they're hidden and the bag looks filled out; without one, they lie
        /// on their back along their place, head at the head end.
        /// </summary>
        void UpdateSleeping()
        {
            Tent tent = null;
            if (state.Has(HikerState.Flag.Sleeping))
                foreach (Tent candidate in Tent.All)
                    if (candidate != null && candidate.Contains(transform.position))
                    {
                        tent = candidate;
                        break;
                    }
            int slot = tent != null ? tent.SlotAt(transform.position) : -1;
            bool inBag = tent != null && tent.Beds[slot].bag;
            if (sleepingIn != tent || sleepingSlot != slot)
            {
                if (sleepingIn != null)
                    sleepingIn.SetSleeper(sleepingSlot, false);
                sleepingIn = tent;
                sleepingSlot = slot;
            }
            if (tent != null)
                tent.SetSleeper(slot, inBag);
            SetHidden(inBag);

            if (tent == null)
            {
                appearance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                return;
            }
            // Facing down the bed from the head end (as the seat does), tipped onto their back, feet toward the foot.
            transform.rotation = Quaternion.Euler(0f, tent.transform.eulerAngles.y + 180f, 0f);
            yaw = transform.eulerAngles.y;
            appearance.transform.SetLocalPositionAndRotation(new Vector3(0f, 0.17f, 1.25f), Quaternion.Euler(-90f, 0f, 0f));
        }

        void SetHidden(bool hide)
        {
            if (hide == hidden)
                return;
            hidden = hide;
            foreach (Renderer part in appearance.GetComponentsInChildren<Renderer>(true))
                part.enabled = !hide;
        }

        bool Lying => sleepingIn != null;

        void UpdateAnimation()
        {
            Animator animator = appearance.Animator;
            if (animator == null)
                return;
            bool sitting = state.Has(HikerState.Flag.Seated) && !Lying;
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
                pose.GroundFeet = state.seat < 0 && state.Has(HikerState.Flag.Grounded) && !busy && !Lying;
                // The head turns where they're looking.
                Vector3 eyes = transform.position + Vector3.up * EyeHeight;
                pose.LookTarget = busy || Lying ? null : eyes + Quaternion.Euler(state.pitch, state.yaw, 0f) * Vector3.forward * 20f;
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
