using System;
using System.Collections.Generic;
using Backpacking.Audio;
using Backpacking.Camp;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.UI;
using Backpacking.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Backpacking.Vehicles
{
    /// <summary>Where the truck is, for the save file.</summary>
    [Serializable]
    public class PickupState
    {
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public bool driving;
    }

    /// <summary>
    /// Your pickup truck. Look at it and press E to get in and drive: W/S (left stick) for throttle, brakes and
    /// reverse, A/D to steer, Space for the handbrake, E to get out once you've stopped. It's at home on the gravel
    /// road; off it the ground is rough and slow, and a little way into the woods it won't go any further.
    /// Sitting in the cab you're out of the rain and wind, the heater's on, and your feet rest.
    /// It's a crew cab: in co-op, friends ride in the passenger seats, and the truck follows whoever is driving it
    /// (see <see cref="Simulated"/>).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Pickup : MonoBehaviour, IInteractable
    {
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] FirstPersonController player;
        [SerializeField] Vitals vitals;
        [SerializeField] RoadPath road;
        [Tooltip("What's in the truck bed: everything bought at the outdoor store that isn't packed yet.")]
        [SerializeField] Backpack bed;

        [Header("Parts")]
        [Tooltip("Front left, front right, rear left, rear right.")]
        [SerializeField] WheelCollider[] wheels = new WheelCollider[4];
        [SerializeField] Transform[] wheelVisuals = new Transform[4];
        [Tooltip("The driver's seat, at floor level: your feet go here.")]
        [SerializeField] Transform seat;
        [Tooltip("Passenger seats, at floor level: front right, rear left, rear right.")]
        [SerializeField] Transform[] passengerSeats = Array.Empty<Transform>();
        [Tooltip("Your eyes, relative to the seat.")]
        [SerializeField] Vector3 eye = new(0f, 1.12f, 0f);
        [SerializeField] Transform steeringWheel;
        [Tooltip("Where you step out: beside the driver's door, or the passenger's if that's blocked.")]
        [SerializeField] Transform driverExit, passengerExit;
        [SerializeField] Light[] headlights = Array.Empty<Light>();

        [Header("Driving")]
        [Tooltip("Torque on each rear wheel at full throttle, in N·m.")]
        [SerializeField] float motorTorque = 900f;
        [SerializeField] float brakeTorque = 3000f;
        [SerializeField] float maxSteer = 34f;
        [SerializeField] float topSpeedKmh = 70f;
        [SerializeField] float reverseTopSpeedKmh = 15f;
        [Tooltip("Top speed over the roughest ground off the road.")]
        [SerializeField] float roughTopSpeedKmh = 12f;
        [Tooltip("Metres off the gravel where the ground is as rough as it gets.")]
        [SerializeField] float roughAfter = 6f;
        [Tooltip("Metres off the gravel beyond which the truck won't go any further from the road.")]
        [SerializeField] float offRoadLimit = 25f;
        [Tooltip("Faster than this (km/h) and you can't get out.")]
        [SerializeField] float exitSpeedKmh = 4f;

        Rigidbody body;
        InputAction move, handbrake, interact;
        AudioSource engine;
        Label hint;
        Quaternion steeringRest;
        float steer;
        int enteredFrame = -10;
        float nextRoughMessage;
        readonly List<Collider> playerColliders = new();
        // Following another player's truck: where it is, and where the wheels are turned and rolled to.
        Vector3 poseTarget, poseVelocity;
        Quaternion poseRotation = Quaternion.identity;
        float poseSteer, poseSpeed, wheelRoll;
        bool hasPose;

        /// <summary>Your truck, wherever it's parked.</summary>
        public static Pickup Current { get; private set; }

        public string DisplayName => "Your pickup";
        public Backpack Bed => bed;
        /// <summary>This player is in the driver's seat.</summary>
        public bool Driving { get; private set; }
        /// <summary>The seat this player sits in: 0 the driver's, then front right, rear left, rear right; -1 not aboard.</summary>
        public int LocalSeat { get; private set; } = -1;
        public bool Aboard => LocalSeat >= 0;
        public int SeatCount => 1 + passengerSeats.Length;
        public Transform SeatAt(int index) => index == 0 ? seat : passengerSeats[index - 1];
        /// <summary>Your eyes, relative to a seat.</summary>
        public Vector3 Eye => eye;
        /// <summary>In co-op, whether someone else is sitting in a seat; null playing alone.</summary>
        public Func<int, bool> SeatTakenByOther { get; set; }
        /// <summary>
        /// Whether this game runs the truck's physics. In co-op only the driver's game does (the host's, while it's
        /// parked); everyone else's truck follows it (<see cref="FollowPose"/>).
        /// </summary>
        public bool Simulated
        {
            get => body == null || !body.isKinematic;
            set
            {
                if (body == null || body.isKinematic == !value)
                    return;
                body.isKinematic = !value;
                body.interpolation = value ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
                hasPose = false;
                if (value)
                {
                    body.linearVelocity = poseVelocity;
                    body.WakeUp();
                }
            }
        }
        /// <summary>The steering angle, for other players' views of this truck.</summary>
        public float SteerAngle => steer;
        /// <summary>Raised when this player gets in or out (or changes seat).</summary>
        public static event Action<Pickup> LocalSeatChanged;
        /// <summary>For tests: drives with this input (x steer, y throttle) instead of the player's, with no one aboard.</summary>
        public Vector2? ScriptedInput { get; set; }
        public float SpeedKmh => body == null ? 0f : (Simulated ? Vector3.Dot(body.linearVelocity, transform.forward) : poseSpeed) * 3.6f;
        /// <summary>The truck's velocity in m/s, wherever it comes from.</summary>
        public Vector3 Velocity => body == null ? Vector3.zero : Simulated ? body.linearVelocity : poseVelocity;
        bool OnItsSide => transform.up.y < 0.4f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }

        void Awake()
        {
            Current = this;
            body = GetComponent<Rigidbody>();
            InputActionMap map = inputActions.FindActionMap("Player", true);
            move = map.FindAction("Move", true);
            handbrake = map.FindAction("Jump", true);
            interact = map.FindAction("Interact", true);
            if (steeringWheel != null)
                steeringRest = steeringWheel.localRotation;
            // Steadier wheel physics at low speed, where WheelColliders tend to jitter.
            if (wheels.Length > 0 && wheels[0] != null)
                wheels[0].ConfigureVehicleSubsteps(5f, 12, 15);

            engine = gameObject.AddComponent<AudioSource>();
            engine.clip = Sounds.Engine();
            engine.loop = true;
            engine.spatialBlend = 1f;
            engine.minDistance = 3f;
            engine.maxDistance = 60f;
            engine.volume = 0.45f;
            SetHeadlights(false);
        }

        void Start()
        {
            hint = UIBuild.Text("", "hint", "shadowed");
            hint.style.top = Length.Percent(78f);
            hint.enableRichText = true;
            hint.SetVisible(false);
            GameUI.Current.Hud.Add(hint.IgnoreMouse());
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            if (Aboard)
                return;
            AddDoorOption(options);
            if (!OnItsSide)
                AddBedOption(interactor, options);
        }

        /// <summary>
        /// At a door: get in and drive, or ride in a free seat on that side (or, if it's over, push it back onto its
        /// wheels). <paramref name="side"/> is -1 for a left door, 1 for a right one, 0 if it doesn't matter.
        /// </summary>
        public void AddDoorOption(List<InteractionOption> options, int side = 0)
        {
            if (OnItsSide)
            {
                options.Add(new InteractionOption("Rock it back onto its wheels", RightItself));
                return;
            }
            int free = FreeSeat(side);
            if (free < 0)
                options.Add(new InteractionOption("Get in", null, "Every seat is taken"));
            else
            {
                int chosen = free;
                options.Add(new InteractionOption(chosen == 0 ? "Get in and drive" : chosen == 1 ? "Get in the front passenger seat" : "Get in the back seat",
                    () => GetIn(chosen)));
            }
        }

        bool SeatFree(int index) => index < SeatCount && SeatAt(index) != null && (SeatTakenByOther == null || !SeatTakenByOther(index));

        /// <summary>
        /// The seat a door leads to. Alone, any door gets you behind the wheel; in co-op, the left doors lead to the
        /// driver's seat and the seat behind it, the right ones to the front passenger seat and the one behind that.
        /// </summary>
        int FreeSeat(int side)
        {
            int[] order = SeatTakenByOther == null ? new[] { 0, 1, 2, 3 }
                : side > 0 ? new[] { 1, 3, 0, 2 } : new[] { 0, 2, 1, 3 };
            foreach (int index in order)
                if (SeatFree(index))
                    return index;
            return -1;
        }

        /// <summary>At the tailgate: the truck bed, to pack your backpack from or leave things in.</summary>
        public void AddBedOption(Interactor interactor, List<InteractionOption> options)
        {
            Backpack pack = interactor.Backpack;
            if (bed == null || PackingView.Current == null)
                return;
            string problem = pack.HasPack && !pack.IsWorn ? "Put your pack on first" : null;
            int things = bed.Contents().Count;
            string label = !pack.HasPack ? $"Look in the truck bed ({things})"
                : things > 0 ? $"Pack your backpack ({things} in the truck bed)" : "Open the truck bed (repack, or leave things in it)";
            options.Add(new InteractionOption(label, () => PackingView.Current.Open(bed), problem));
        }

        // ---------- Getting in and out ----------

        /// <summary>Gets in behind the wheel.</summary>
        public void GetIn() => GetIn(0);

        /// <summary>Gets in a seat: 0 to drive, 1�3 to ride.</summary>
        public void GetIn(int seatIndex)
        {
            if (Aboard || player == null || !SeatFree(seatIndex))
                return;
            if (RestMode.SeatedNow)
                RestMode.Current.StandUp();
            LocalSeat = seatIndex;
            Driving = seatIndex == 0;
            enteredFrame = Time.frameCount;
            PlayerControlLock.Lock(this, needsCursor: false);
            // The player's own colliders would join the truck's body while riding in it.
            playerColliders.Clear();
            foreach (Collider part in player.GetComponentsInChildren<Collider>())
                if (part.enabled)
                {
                    part.enabled = false;
                    playerColliders.Add(part);
                }
            player.MountAt(SeatAt(seatIndex), eye);
            if (vitals != null)
                vitals.InVehicle = true;
            PlayDoor();
            if (Driving)
            {
                SetEngine(true);
                body.WakeUp();
            }
            LocalSeatChanged?.Invoke(this);
        }

        /// <summary>The engine running and the headlights on (while anyone is driving).</summary>
        public void SetEngine(bool on)
        {
            if (engine == null || engine.isPlaying == on)
                return;
            if (on)
                engine.Play();
            else
                engine.Stop();
            SetHeadlights(on);
        }

        void TryGetOut()
        {
            if (Mathf.Abs(SpeedKmh) > exitSpeedKmh && !OnItsSide)
            {
                Notifications.Post("Stop the truck before getting out.", 3f);
                return;
            }
            // Out of your own side's door, or the other side's if that's blocked.
            bool rightSide = LocalSeat == 1 || LocalSeat == 3;
            float along = LocalSeat >= 2 ? SeatAt(LocalSeat).localPosition.z - seat.localPosition.z : 0f;
            if (!FindExit(rightSide ? passengerExit : driverExit, along, out Vector3 spot) && !FindExit(rightSide ? driverExit : passengerExit, along, out spot))
            {
                Notifications.Post("There's no room to open a door here. Move the truck a little.", 4f);
                return;
            }
            GetOut(spot);
        }

        /// <summary>Gets out at once, whatever the speed (co-op: someone else was given the seat).</summary>
        public void LeaveSeat()
        {
            if (!Aboard)
                return;
            bool rightSide = LocalSeat == 1 || LocalSeat == 3;
            if (!FindExit(rightSide ? passengerExit : driverExit, 0f, out Vector3 spot) && !FindExit(rightSide ? driverExit : passengerExit, 0f, out spot))
                spot = transform.position + transform.right * (rightSide ? 2f : -2f);
            GetOut(spot);
        }

        void GetOut(Vector3 spot)
        {
            bool wasDriving = Driving;
            Driving = false;
            LocalSeat = -1;
            player.Dismount(spot, transform.rotation);
            foreach (Collider part in playerColliders)
                if (part != null)
                    part.enabled = true;
            playerColliders.Clear();
            PlayerControlLock.Unlock(this);
            if (vitals != null)
                vitals.InVehicle = false;
            PlayDoor();
            if (wasDriving)
                SetEngine(false);
            hint?.SetVisible(false);
            LocalSeatChanged?.Invoke(this);
        }

        /// <summary>A clear spot to stand on the ground at an exit point, moved <paramref name="along"/> metres forward (for the back doors).</summary>
        bool FindExit(Transform exit, float along, out Vector3 spot)
        {
            spot = default;
            if (exit == null)
                return false;
            Vector3 above = exit.position + transform.forward * along + Vector3.up * 2f;
            if (!Physics.Raycast(above, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore) || hit.transform.IsChildOf(transform))
                return false;
            spot = hit.point + Vector3.up * 0.05f;
            const float radius = 0.35f;
            foreach (Collider blocker in Physics.OverlapCapsule(spot + Vector3.up * (radius + 0.15f), spot + Vector3.up * 1.6f, radius, ~0, QueryTriggerInteraction.Ignore))
                if (!blocker.transform.IsChildOf(player.transform))
                    return false;
            return true;
        }

        /// <summary>Pushes an overturned truck back upright.</summary>
        void RightItself()
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(transform.position + Vector3.up * 0.8f, Quaternion.LookRotation(forward, Vector3.up));
            Notifications.Post("You rock the truck back onto its wheels. It's a good thing it's an old one.", 4f);
        }

        void PlayDoor() => AudioSource.PlayClipAtPoint(Sounds.DoorShut(), seat != null ? seat.position : transform.position, 0.8f);

        void SetHeadlights(bool on)
        {
            foreach (Light lamp in headlights)
                if (lamp != null)
                    lamp.enabled = on;
        }

        // ---------- Driving ----------

        void Update()
        {
            if (!Simulated)
                Follow();
            if (!Aboard)
                return;
            bool menus = PlayerControlLock.CursorNeeded;
            if (!menus && Time.frameCount > enteredFrame + 1 && interact.WasPressedThisFrame())
            {
                TryGetOut();
                if (!Aboard)
                    return;
            }
            if (!Driving)
            {
                UpdatePassengerHint(menus);
                return;
            }

            float speed = Mathf.Abs(SpeedKmh);
            float revs = Mathf.Clamp01(speed / topSpeedKmh) + (menus ? 0f : Mathf.Abs(move.ReadValue<Vector2>().y) * 0.25f);
            engine.pitch = Mathf.Lerp(engine.pitch, 0.75f + 1.5f * revs, Time.deltaTime * 3f);
            UpdateHint(menus, speed);
        }

        void FixedUpdate()
        {
            if (!Simulated)
                return;
            Vector2 input = ScriptedInput ?? (Driving && !PlayerControlLock.CursorNeeded ? Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f) : Vector2.zero);
            bool holdingHandbrake = ScriptedInput == null && (!Driving || (!PlayerControlLock.CursorNeeded && handbrake.IsPressed()));
            float speed = Vector3.Dot(body.linearVelocity, transform.forward);

            // Throttle pulls forward; pushing the other way brakes first, then reverses.
            float throttle = 0f, brake = 0f;
            if (input.y > 0.05f)
            {
                if (speed < -0.5f)
                    brake = brakeTorque * input.y;
                else
                    throttle = input.y;
            }
            else if (input.y < -0.05f)
            {
                if (speed > 0.5f)
                    brake = brakeTorque * -input.y;
                else
                    throttle = input.y;
            }
            else
                // Engine braking and rolling resistance, so the truck doesn't coast forever.
                brake = brakeTorque * 0.04f;

            Vector3 towardRoad = Vector3.zero;
            float offRoad = road != null ? road.OffRoad(transform.position, out towardRoad) : 0f;
            float rough = Mathf.Clamp01(offRoad / roughAfter);
            float top = (throttle >= 0f ? topSpeedKmh : reverseTopSpeedKmh) / 3.6f;
            top = Mathf.Lerp(top, Mathf.Min(top, roughTopSpeedKmh / 3.6f), rough);
            // Ease off near top speed, and hold it there going downhill.
            throttle *= Mathf.Clamp01((top - Mathf.Abs(speed)) / (top * 0.2f));
            if (Mathf.Abs(speed) > top + 0.5f)
                brake = Mathf.Max(brake, brakeTorque * 0.3f * Mathf.Clamp01(Mathf.Abs(speed) - top));

            // Too far into the woods: it won't go any further from the road, only back toward it.
            if (offRoad > offRoadLimit && throttle != 0f && Vector3.Dot(transform.forward * Mathf.Sign(throttle), towardRoad) < 0.2f)
            {
                throttle = 0f;
                brake = Mathf.Max(brake, brakeTorque * 0.5f);
                if (Driving && Time.time >= nextRoughMessage)
                {
                    nextRoughMessage = Time.time + 6f;
                    Notifications.Post("The ground's too rough and the trees too close to drive any further this way. Head back toward the road.", 5f);
                }
            }

            // Steering tightens at speed.
            float steerLimit = maxSteer * Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(Mathf.Abs(speed) / (topSpeedKmh / 3.6f)));
            steer = Mathf.MoveTowards(steer, input.x * steerLimit, 90f * Time.fixedDeltaTime);

            for (int i = 0; i < wheels.Length; i++)
            {
                WheelCollider wheel = wheels[i];
                if (wheel == null)
                    continue;
                bool front = i < 2;
                if (front)
                    wheel.steerAngle = steer;
                wheel.motorTorque = front ? 0f : throttle * motorTorque;
                wheel.brakeTorque = holdingHandbrake && !front ? brakeTorque * 2f : brake;
            }
        }

        void LateUpdate()
        {
            if (!Simulated)
            {
                // Following: turn and roll the wheels where they are.
                wheelRoll += poseSpeed / 0.37f * Mathf.Rad2Deg * Time.deltaTime;
                for (int i = 0; i < wheelVisuals.Length; i++)
                    if (wheelVisuals[i] != null)
                        wheelVisuals[i].localRotation = Quaternion.Euler(0f, i < 2 ? poseSteer : 0f, 0f) * Quaternion.Euler(wheelRoll, 0f, 0f);
                steer = poseSteer;
            }
            else
                for (int i = 0; i < wheels.Length && i < wheelVisuals.Length; i++)
                {
                    if (wheels[i] == null || wheelVisuals[i] == null)
                        continue;
                    wheels[i].GetWorldPose(out Vector3 position, out Quaternion rotation);
                    wheelVisuals[i].SetPositionAndRotation(position, rotation);
                }
            if (steeringWheel != null)
                steeringWheel.localRotation = steeringRest * Quaternion.AngleAxis(-steer * 12f, Vector3.forward);
        }

        // ---------- Following another player's truck ----------

        /// <summary>Where the driving player's truck is (co-op): this one glides there, carrying anyone sitting in it.</summary>
        public void FollowPose(Vector3 position, Quaternion rotation, Vector3 velocity, float steerAngle)
        {
            poseTarget = position;
            poseRotation = rotation;
            poseVelocity = velocity;
            poseSteer = steerAngle;
            poseSpeed = Vector3.Dot(velocity, rotation * Vector3.forward);
            if (!hasPose || (transform.position - position).sqrMagnitude > 25f)
                transform.SetPositionAndRotation(position, rotation);
            hasPose = true;
        }

        void Follow()
        {
            if (!hasPose)
                return;
            // Run ahead on the last known speed between updates, and ease out the difference.
            poseTarget += poseVelocity * Time.deltaTime;
            float ease = 1f - Mathf.Exp(-12f * Time.deltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, poseTarget, ease), Quaternion.Slerp(transform.rotation, poseRotation, ease));
        }

        void UpdatePassengerHint(bool menus)
        {
            if (hint == null)
                return;
            hint.SetVisible(!menus);
            if (!menus)
                hint.SetText($"<b>{Mathf.Abs(SpeedKmh):0}</b> km/h\nRiding along   �   <color=#E07B39><b>E</b></color> get out once it stops");
        }

        void UpdateHint(bool menus, float speed)
        {
            if (hint == null)
                return;
            hint.SetVisible(!menus);
            if (menus)
                return;
            const string Key = "<color=#E07B39><b>";
            const string End = "</b></color>";
            hint.SetText($"<b>{speed:0}</b> km/h\n{Key}W{End}/{Key}S{End} drive and brake   ·   {Key}A{End}/{Key}D{End} steer   ·   "
                         + $"{Key}Space{End} handbrake   ·   {Key}E{End} get out");
        }

        // ---------- Saving ----------

        public PickupState CaptureState() => new() { position = transform.position, rotation = transform.rotation, driving = Driving };

        public void RestoreState(PickupState state)
        {
            if (state == null)
                return;
            if (Aboard && player != null)
                GetOut(player.transform.position);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(state.position, state.rotation);
            body.position = state.position;
            body.rotation = state.rotation;
            if (state.driving)
                GetIn();
        }
    }
}
