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

        /// <summary>Your truck, wherever it's parked.</summary>
        public static Pickup Current { get; private set; }

        public string DisplayName => "Your pickup";
        public Backpack Bed => bed;
        public bool Driving { get; private set; }
        /// <summary>For tests: drives with this input (x steer, y throttle) instead of the player's, with no one aboard.</summary>
        public Vector2? ScriptedInput { get; set; }
        public float SpeedKmh => body != null ? Vector3.Dot(body.linearVelocity, transform.forward) * 3.6f : 0f;
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
            if (Driving)
                return;
            if (OnItsSide)
            {
                options.Add(new InteractionOption("Rock it back onto its wheels", RightItself));
                return;
            }
            options.Add(new InteractionOption("Drive", GetIn));
            Backpack pack = interactor.Backpack;
            if (bed != null && PackingView.Current != null)
            {
                string problem = pack.HasPack && !pack.IsWorn ? "Put your pack on first" : null;
                int things = bed.Contents().Count;
                string label = !pack.HasPack ? $"Look in the truck bed ({things})"
                    : things > 0 ? $"Pack your backpack ({things} in the truck bed)" : "Repack, or leave things in the truck bed";
                options.Add(new InteractionOption(label, () => PackingView.Current.Open(bed), problem));
            }
        }

        // ---------- Getting in and out ----------

        public void GetIn()
        {
            if (Driving || player == null)
                return;
            if (RestMode.SeatedNow)
                RestMode.Current.StandUp();
            Driving = true;
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
            player.MountAt(seat, eye);
            if (vitals != null)
                vitals.InVehicle = true;
            PlayDoor();
            engine.Play();
            SetHeadlights(true);
            body.WakeUp();
        }

        void TryGetOut()
        {
            if (Mathf.Abs(SpeedKmh) > exitSpeedKmh && !OnItsSide)
            {
                Notifications.Post("Stop the truck before getting out.", 3f);
                return;
            }
            if (!FindExit(driverExit, out Vector3 spot) && !FindExit(passengerExit, out spot))
            {
                Notifications.Post("There's no room to open a door here. Move the truck a little.", 4f);
                return;
            }
            GetOut(spot);
        }

        void GetOut(Vector3 spot)
        {
            Driving = false;
            player.Dismount(spot, transform.rotation);
            foreach (Collider part in playerColliders)
                if (part != null)
                    part.enabled = true;
            playerColliders.Clear();
            PlayerControlLock.Unlock(this);
            if (vitals != null)
                vitals.InVehicle = false;
            PlayDoor();
            engine.Stop();
            SetHeadlights(false);
            hint?.SetVisible(false);
        }

        /// <summary>A clear spot to stand on the ground at an exit point.</summary>
        bool FindExit(Transform exit, out Vector3 spot)
        {
            spot = default;
            if (exit == null)
                return false;
            Vector3 above = exit.position + Vector3.up * 2f;
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
            if (!Driving)
                return;
            bool menus = PlayerControlLock.CursorNeeded;
            if (!menus && Time.frameCount > enteredFrame + 1 && interact.WasPressedThisFrame())
            {
                TryGetOut();
                if (!Driving)
                    return;
            }

            float speed = Mathf.Abs(SpeedKmh);
            float revs = Mathf.Clamp01(speed / topSpeedKmh) + (menus ? 0f : Mathf.Abs(move.ReadValue<Vector2>().y) * 0.25f);
            engine.pitch = Mathf.Lerp(engine.pitch, 0.75f + 1.5f * revs, Time.deltaTime * 3f);
            UpdateHint(menus, speed);
        }

        void FixedUpdate()
        {
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
            if (Driving && player != null)
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
