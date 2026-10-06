using System;
using Backpacking.UI;
using UnityEngine;

namespace Backpacking.Player
{
    /// <summary>
    /// The rhythm of walking. Tracks the stride as you move and bobs the view with it: a dip as each
    /// foot lands, a sway from foot to foot and a slight roll, deeper when sprinting or carrying a
    /// heavy pack. Landing from a jump or drop sinks the view, then it springs back. Raises
    /// <see cref="Stepped"/> on every footfall so footstep sounds land with the dip.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    public class HeadBob : MonoBehaviour
    {
        [Header("Bob (metres and degrees)")]
        [SerializeField] float walkHeight = 0.02f;
        [SerializeField] float sprintHeight = 0.035f;
        [SerializeField] float sway = 0.012f;
        [SerializeField] float roll = 0.35f;
        [Tooltip("Head nod as each foot lands.")]
        [SerializeField] float nod = 0.25f;
        [Tooltip("How much deeper every step is with a full pack (0.4 = 40% deeper).")]
        [SerializeField] float loadedExtra = 0.4f;
        [SerializeField, Range(0f, 1f)] float crouchScale = 0.55f;

        [Header("Weight")]
        [Tooltip("Downward push on the view from each footfall, in m/s.")]
        [SerializeField] float stepImpact = 0.1f;
        [Tooltip("Downward push on landing, per m/s of falling speed.")]
        [SerializeField] float landImpact = 0.09f;
        [SerializeField] float springStiffness = 140f;
        [SerializeField] float springDamping = 15f;

        FirstPersonController player;
        float phase;
        float bobAmount;
        float sprintAmount;
        float settle, settleVelocity;
        bool wasGrounded = true;
        float fallSpeed;

        /// <summary>A foot landed. The argument is how hard: about 0.2 creeping, 0.5 walking, 0.8 sprinting.</summary>
        public event Action<float> Stepped;
        /// <summary>Landed after a jump or drop. The argument is the falling speed in m/s.</summary>
        public event Action<float> Landed;

        void Awake() => player = GetComponent<FirstPersonController>();

        void OnDisable()
        {
            player.BobPosition = Vector3.zero;
            player.BobRotation = Vector3.zero;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            bool grounded = player.IsGrounded;
            float speed = player.HorizontalSpeed;
            float load = player.LoadFactor;
            TrackLanding(grounded);

            // Walk the stride: half a cycle of the bob per step, longer strides at speed.
            if (grounded && speed > 0.25f)
            {
                float stride = Mathf.Clamp(0.45f + 0.23f * speed, 0.6f, 1.5f);
                int stepBefore = Mathf.FloorToInt(phase / Mathf.PI);
                phase += speed * dt / stride * Mathf.PI;
                if (Mathf.FloorToInt(phase / Mathf.PI) != stepBefore)
                    Footfall(speed, load);
            }
            else if (speed <= 0.25f)
            {
                // Coming to rest: finish the current step rather than freezing mid-stride.
                float rest = Mathf.Round(phase / Mathf.PI) * Mathf.PI;
                phase = Mathf.MoveTowards(phase, rest, dt * 4f);
            }
            phase %= Mathf.PI * 64f;

            bobAmount = Mathf.MoveTowards(bobAmount, grounded ? Mathf.InverseLerp(0.25f, player.WalkSpeed, speed) : 0f, dt * 3f);
            sprintAmount = Mathf.MoveTowards(sprintAmount, player.IsSprinting ? 1f : 0f, dt * 2f);
            UpdateSpring(dt);

            float strength = GameSettings.HeadBob * bobAmount * (player.IsCrouching ? crouchScale : 1f) * (1f + loadedExtra * load);
            float height = Mathf.Lerp(walkHeight, sprintHeight, sprintAmount) * strength;
            float stance = Mathf.Abs(Mathf.Sin(phase));
            // Lowest as each foot strikes (a sharp dip), highest mid-stride; centred around the resting eye height.
            float y = height * (stance - 0.64f);
            float side = Mathf.Sin(phase);
            player.BobPosition = new Vector3(side * sway * strength, y + settle * GameSettings.HeadBob, 0f);
            player.BobRotation = new Vector3(Mathf.Pow(1f - stance, 4f) * nod * strength, 0f, -side * roll * strength);
        }

        void Footfall(float speed, float load)
        {
            float hardness = player.IsCrouching ? 0.2f : player.IsSprinting ? 0.8f : 0.5f;
            // Each step settles the body a little, more under a heavy pack.
            settleVelocity -= stepImpact * hardness * 2f * (1f + load) * bobAmount;
            Stepped?.Invoke(hardness);
        }

        void TrackLanding(bool grounded)
        {
            if (!grounded)
                fallSpeed = Mathf.Max(fallSpeed, -player.VerticalVelocity);
            else if (!wasGrounded)
            {
                if (fallSpeed > 2f)
                {
                    settleVelocity -= fallSpeed * landImpact * (1f + 0.5f * player.LoadFactor);
                    Landed?.Invoke(fallSpeed);
                }
                fallSpeed = 0f;
            }
            wasGrounded = grounded;
        }

        /// <summary>A damped spring pulls the view back to rest after footfalls and landings.</summary>
        void UpdateSpring(float dt)
        {
            float force = -springStiffness * settle - springDamping * settleVelocity;
            settleVelocity += force * dt;
            settle += settleVelocity * dt;
            settle = Mathf.Clamp(settle, -0.3f, 0.1f);
        }
    }
}
