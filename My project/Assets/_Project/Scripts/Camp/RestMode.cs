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
    /// <summary>
    /// Sitting down to rest (Z, or D-pad left). Seated, your feet recover faster than standing; take your boots
    /// and socks off (E) and they recover much faster, dry out and air. Sit close to a lit campfire barefoot and
    /// you hold your feet in its smoke, which draws out an infection. Walk, or press Z again, to stand up
    /// (your boots go back on). In a camp chair your feet rest faster still, and the boots come off just the same.
    /// </summary>
    public class RestMode : MonoBehaviour
    {
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] FirstPersonController player;
        [SerializeField] Vitals vitals;
        [SerializeField] PlayerActivity activity;
        [Tooltip("How close to a fire you need to be to hold your feet in its smoke, in metres.")]
        [SerializeField] float fireReach = 2.8f;

        static RestMode current;

        InputAction interact;
        Label hint;
        readonly List<Campfire> fires = new();
        float nextFireSearch;
        bool wasInChair;

        /// <summary>The player's rest mode, for systems that behave differently while sitting.</summary>
        public static RestMode Current => current;
        /// <summary>True while the player is sitting down.</summary>
        public static bool SeatedNow => current != null && current.IsSeated;

        public bool IsSeated { get; private set; }
        public bool BootsOff { get; private set; }
        public bool SmokingFeet { get; private set; }
        /// <summary>The camp chair you're sitting in, or null on the ground.</summary>
        public CampChair Chair { get; private set; }
        public bool InChair => IsSeated && Chair != null;

        /// <summary>Eye height sitting in a camp chair, in metres.</summary>
        const float ChairEyeHeight = 1.12f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => current = null;

        void Awake()
        {
            current = this;
            interact = inputActions.FindActionMap("Player", true).FindAction("Interact", true);
        }

        void OnDestroy()
        {
            if (current == this)
                current = null;
        }

        void Start()
        {
            hint = UIBuild.Text("", "hint", "shadowed");
            hint.style.top = Length.Percent(78f);
            hint.enableRichText = true;
            hint.SetVisible(false);
            GameUI.Current.Hud.Add(hint.IgnoreMouse());
        }

        /// <summary>Sits down where you are, e.g. from the rest key or "Sit by the fire".</summary>
        public void SitDown()
        {
            if (IsSeated || !player.IsGrounded || PlayerControlLock.MovementLocked)
                return;
            IsSeated = true;
        }

        /// <summary>Sits in a set-up camp chair: you're moved onto its seat, facing out.</summary>
        public void SitInChair(CampChair chair)
        {
            if (chair == null || chair.Stage != ChairStage.Ready || chair.Occupied || PlayerControlLock.MovementLocked)
                return;
            if (IsSeated)
                StandUp();
            Chair = chair;
            chair.SetOccupied(true);
            MovePlayer(chair.SeatPoint, chair.Facing);
            IsSeated = true;
        }

        /// <summary>Teleports the player (a character controller fights direct moves, so it's switched off).</summary>
        void MovePlayer(Vector3 position, Quaternion facing)
        {
            var controller = player.GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = false;
            player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, facing.eulerAngles.y, 0f));
            if (controller != null)
                controller.enabled = true;
        }

        /// <summary>Stands up, putting your boots back on if they're off.</summary>
        public void StandUp()
        {
            if (!IsSeated)
                return;
            if (BootsOff)
                Notifications.Post("You pull your socks and boots back on.", 3f);
            IsSeated = false;
            BootsOff = false;
            if (Chair != null)
            {
                // Step forward out of the chair.
                CampChair chair = Chair;
                Chair = null;
                MovePlayer(chair.SeatPoint + chair.Facing * Vector3.forward * 0.55f, chair.Facing);
                chair.SetOccupied(false);
            }
        }

        void Update()
        {
            bool menus = PlayerControlLock.CursorNeeded;
            if (GameInput.RestPressed && !menus)
            {
                if (IsSeated)
                    StandUp();
                else
                    SitDown();
            }

            // Tasks, sleep and collapses take over; walking off stands you up.
            if (IsSeated && activity.IsBusy)
                StandUp();
            if (IsSeated && !PlayerControlLock.MovementLocked && player.MoveInput.sqrMagnitude > 0.25f)
                StandUp();

            if (IsSeated && !menus && !PlayerControlLock.MovementLocked && interact.WasPressedThisFrame())
            {
                BootsOff = !BootsOff;
                Notifications.Post(BootsOff ? "You unlace your boots and peel off your socks." : "You pull your socks and boots back on.", 3f);
            }

            SmokingFeet = IsSeated && BootsOff && NearLitFire();
            // A chair taken down or packed away while you sat in it.
            if (IsSeated && Chair == null && wasInChair)
                StandUp();
            wasInChair = Chair != null;
            if (InChair && Chair.Stage != ChairStage.Ready)
                StandUp();
            player.Seated = IsSeated;
            player.SeatedEyeHeightNow = InChair ? ChairEyeHeight : FirstPersonController.SeatedEyeHeight;
            vitals.Seated = IsSeated;
            vitals.InChair = InChair;
            vitals.BootsOff = IsSeated && BootsOff;
            vitals.SmokingFeet = SmokingFeet;
            UpdateHint(menus);
        }

        bool NearLitFire()
        {
            if (Time.time >= nextFireSearch)
            {
                nextFireSearch = Time.time + 1f;
                fires.Clear();
                fires.AddRange(FindObjectsByType<Campfire>());
            }
            foreach (Campfire fire in fires)
                if (fire != null && fire.IsBurning && Vector3.Distance(fire.transform.position, transform.position) <= fireReach)
                    return true;
            return false;
        }

        void UpdateHint(bool menus)
        {
            if (hint == null)
                return;
            hint.SetVisible(IsSeated && !menus);
            if (!IsSeated || menus)
                return;

            string feet = SmokingFeet
                ? vitals.IsInfected ? "Holding your bare feet in the smoke: drawing out the infection" : "Warming and drying your bare feet in the smoke"
                : BootsOff ? InChair ? "In the chair, boots off: feet up, resting fast, drying and airing" : "Boots off: your feet are resting, drying and airing"
                : InChair ? "In the chair: your feet ease faster than on the ground. Take your boots off to rest them properly"
                : "Sitting: your feet ease a little. Take your boots off to rest them properly";
            string boots = BootsOff ? "put boots on" : "take boots off";
            hint.SetText($"{feet}\n<color=#E07B39><b>E</b></color> {boots}   ·   <color=#E07B39><b>Hold T</b></color> wait   ·   <color=#E07B39><b>Z</b></color> or walk to stand");
        }
    }
}
