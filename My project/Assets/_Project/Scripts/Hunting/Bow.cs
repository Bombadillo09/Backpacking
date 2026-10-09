using Backpacking.Audio;
using Backpacking.Camp;
using Backpacking.Character;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Trip;
using Backpacking.UI;
using Backpacking.Wildlife;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backpacking.Hunting
{
    /// <summary>
    /// Hunting with the bow. With it in hand (hotbar), hold the button (X on a gamepad) to draw and let go to shoot;
    /// let go before it's half drawn and you ease the string back down instead. The view narrows a little as you
    /// draw. Holding at full draw gets shaky after a few seconds, and more so when you're exhausted. A weak draw sends
    /// the arrow slower, so it drops sooner. The hiker holds the bow out in the left hand and pulls the string back
    /// to the face with the right.
    /// </summary>
    public class Bow : MonoBehaviour
    {
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] FirstPersonController player;
        [SerializeField] CharacterAppearance appearance;
        [SerializeField] Backpack backpack;
        [SerializeField] Vitals vitals;
        [SerializeField] PlayerActivity activity;
        [SerializeField] CampPlacer placer;

        [Tooltip("Seconds to come to full draw.")]
        [SerializeField] float drawSeconds = 0.9f;
        [Tooltip("Arrow speed at full draw, m/s.")]
        [SerializeField] float arrowSpeed = 66f;
        [Tooltip("Seconds at full draw before the arms start to shake.")]
        [SerializeField] float steadySeconds = 2.5f;
        [Tooltip("How much the view narrows at full draw (0.15 = 15%).")]
        [SerializeField, Range(0f, 0.4f)] float zoom = 0.15f;
        [SerializeField, Range(0f, 1f)] float volume = 0.8f;

        InputAction attack;
        AudioSource source;
        Camera view;
        GameObject bow, nocked;
        bool drawing;
        float draw, fullFor, raise, aim, nextShot;
        bool zoomed;

        /// <summary>How far the bow is raised to aim (0 lowered, 1 up), and how far the string is drawn.</summary>
        public float Aim => aim;
        public float Draw => draw;
        /// <summary>Counts shots, for others to see each one.</summary>
        public byte Shots { get; private set; }

        void Awake()
        {
            attack = inputActions.FindActionMap("Player", true).FindAction("Attack", true);
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            view = player.CameraPivot.GetComponent<Camera>();
        }

        void OnDisable()
        {
            Show(false);
            StopDrawing();
            player.AimSway = Vector2.zero;
        }

        void Update()
        {
            bool inHand = Hotbar.HoldingBow && !activity.IsBusy && !RestMode.SeatedNow;
            Show(inHand);
            bool free = inHand && !PlayerControlLock.MovementLocked && !placer.IsPlacing && Cursor.lockState == CursorLockMode.Locked;

            if (!free)
                StopDrawing();
            else if (!drawing && attack.WasPressedThisFrame() && Time.time >= nextShot)
            {
                if (backpack.Arrows <= 0)
                    Notifications.Post("No arrows left. Trading posts sell them; look for any you've shot.", 2.5f);
                else
                {
                    drawing = true;
                    fullFor = 0f;
                    source.pitch = Random.Range(0.95f, 1.05f);
                    if (Sounds.BowDraw() is { } creak)
                        source.PlayOneShot(creak, volume * 0.5f);
                }
            }
            else if (drawing)
            {
                // Tired arms draw more slowly.
                draw = Mathf.MoveTowards(draw, 1f, Time.deltaTime / (drawSeconds * (vitals.IsExhausted ? 1.6f : 1f)));
                if (draw >= 1f)
                    fullFor += Time.deltaTime;
                if (!attack.IsPressed())
                {
                    if (draw >= 0.5f)
                        Shoot();
                    StopDrawing();
                }
            }

            raise = Mathf.MoveTowards(raise, inHand ? 1f : 0f, Time.deltaTime * 4f);
            aim = Mathf.MoveTowards(aim, drawing ? 1f : 0f, Time.deltaTime * (drawing ? 5f : 3f));
            Sway();
            Zoom();
            Pose();
        }

        void StopDrawing()
        {
            drawing = false;
            draw = 0f;
            fullFor = 0f;
        }

        void Shoot()
        {
            if (!backpack.TryUseArrow())
                return;
            nextShot = Time.time + 0.7f;
            Shots++;
            float power = Mathf.Lerp(0.45f, 1f, Mathf.Pow(Mathf.InverseLerp(0.5f, 1f, draw), 1.3f));
            Transform eye = player.CameraPivot;
            Arrow.Shoot(player.AimOrigin + eye.forward * 0.4f, eye.forward * (arrowSpeed * power), transform);
            source.pitch = Random.Range(0.94f, 1.06f);
            source.PlayOneShot(Sounds.BowRelease(), volume);
            // The string's thrum carries a little way.
            Animal.StartleNear(transform.position, 8f);
            TripLog.Tally(TripStat.ArrowsShot);
        }

        /// <summary>The aim drifts a little with each breath; after a few seconds at full draw the arms begin to shake.</summary>
        void Sway()
        {
            if (aim <= 0f)
            {
                player.AimSway = Vector2.zero;
                return;
            }
            float strain = Mathf.Clamp(fullFor - steadySeconds, 0f, 4f) * 0.7f + (vitals.IsExhausted ? 0.8f : 0f);
            float amount = (0.2f + strain) * aim;
            float t = Time.time;
            var drift = new Vector2(Mathf.PerlinNoise(t * 0.7f, 3.1f) - 0.5f, Mathf.PerlinNoise(5.7f, t * 0.7f) - 0.5f) * (2f * amount);
            // Shaking: a quicker tremble on top once the arms tire.
            drift += new Vector2(Mathf.PerlinNoise(t * 9f, 1.3f) - 0.5f, Mathf.PerlinNoise(7.9f, t * 9f) - 0.5f) * (strain * 0.6f * aim);
            drift.y += Mathf.Sin(t * 1.6f) * 0.12f * aim;
            player.AimSway = drift;
        }

        void Zoom()
        {
            if (view == null)
                return;
            if (aim > 0f)
            {
                view.fieldOfView = GameSettings.FieldOfView * (1f - zoom * Mathf.SmoothStep(0f, 1f, draw) * aim);
                zoomed = true;
            }
            else if (zoomed)
            {
                view.fieldOfView = GameSettings.FieldOfView;
                zoomed = false;
            }
        }

        void Show(bool shown)
        {
            if (shown && bow == null)
            {
                bow = BowDesign.Bow();
                bow.name = "Held Bow";
                nocked = BowDesign.Arrow();
                nocked.name = "Nocked Arrow";
                nocked.transform.SetParent(bow.transform, false);
                bow.SetActive(false);
            }
            else if (!shown && bow != null)
            {
                Destroy(bow);
                bow = nocked = null;
            }
        }

        /// <summary>Where the bow is held and which way it faces, in the view's space: lowered at the side, or raised and drawn.</summary>
        void Pose()
        {
            HikerPose pose = appearance.Pose;
            if (pose == null)
                return;
            if (bow == null || raise <= 0f)
            {
                if (pose.LeftHandWeight > 0f || pose.LeftHandTarget.HasValue)
                {
                    pose.LeftHandTarget = null;
                    pose.LeftHandWeight = 0f;
                }
                return;
            }

            Transform eye = player.CameraPivot;
            bool firstPerson = !player.ThirdPerson;
            float pitch = eye.eulerAngles.x > 180f ? eye.eulerAngles.x - 360f : eye.eulerAngles.x;
            Quaternion look = firstPerson ? eye.rotation : Quaternion.Euler(Mathf.Clamp(pitch, -40f, 40f), player.transform.eulerAngles.y, 0f);
            Quaternion level = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
            Vector3 eyes = firstPerson ? eye.position : player.transform.position + Vector3.up * (appearance.EyeHeight * appearance.transform.lossyScale.y);

            Hold(eyes, look, level, aim, draw, out Vector3 grip, out Quaternion frame, out Vector3 drawHand);
            pose.LeftHandTarget = grip;
            pose.LeftHandWeight = raise;
            pose.LeftHandFrame = frame;

            Vector3 nock = BowDesign.SetDraw(bow, draw);
            float a = Mathf.SmoothStep(0f, 1f, aim);
            if (a > 0.01f)
            {
                pose.HandTarget = drawHand;
                pose.HandWeight = a;
                pose.HandFrame = frame;
            }
            else if (pose.HandWeight > 0f)
            {
                pose.HandTarget = null;
                pose.HandWeight = 0f;
            }

            bow.transform.SetPositionAndRotation(grip, frame);
            bow.SetActive(raise > 0.5f);
            nocked.SetActive(backpack.Arrows > 0 && Time.time >= nextShot);
            nocked.transform.SetLocalPositionAndRotation(nock + Vector3.forward * BowDesign.ArrowLength, Quaternion.identity);
        }

        /// <summary>
        /// Where the bow hand grips and which way the bow faces (+Y up the top limb, +Z toward the target), and where
        /// the drawing hand holds the string, for eyes looking <paramref name="look"/> (<paramref name="level"/> is the
        /// same with no pitch). <paramref name="aim"/> raises the bow from the side; <paramref name="draw"/> pulls the string.
        /// </summary>
        public static void Hold(Vector3 eyes, Quaternion look, Quaternion level, float aim, float draw,
            out Vector3 grip, out Quaternion frame, out Vector3 drawHand)
        {
            float a = Mathf.SmoothStep(0f, 1f, aim);
            // Raised: the arrow runs from the anchor at the corner of the mouth to the rest, just below and left of
            // where you're looking, so the riser sits to the right of the aim and the top limb cants away from it.
            Vector3 anchor = eyes + look * new Vector3(0.03f, -0.075f, 0.04f);
            Vector3 rest = anchor + look * (Quaternion.Euler(0.4f, -4f, 0f) * Vector3.forward) * (BowDesign.BraceHeight + BowDesign.DrawLength);
            Quaternion raised = Quaternion.LookRotation(rest - anchor, look * Vector3.up) * Quaternion.Euler(0f, 0f, -12f);
            Vector3 raisedGrip = rest - raised * BowDesign.ArrowRest;
            // Lowered: low at the left side, top limb tipped forward.
            grip = Vector3.Lerp(eyes + level * new Vector3(-0.22f, -0.62f, 0.3f), raisedGrip, a);
            frame = Quaternion.Slerp(level * Quaternion.Euler(35f, 0f, 20f), raised, a);
            Vector3 nock = BowDesign.NockPoint(draw);
            // The drawing hand: fingers on the string, just under the nock.
            drawHand = grip + frame * (nock + new Vector3(0.01f, -0.02f, -0.03f));
        }

        void LateUpdate()
        {
            // The bow sits in the left fist once the animation and IK have placed it, wherever the hand actually got to.
            if (bow == null || appearance.Animator == null || raise <= 0.5f)
                return;
            Transform hand = appearance.Animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform middle = appearance.Animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            if (hand == null)
                return;
            Vector3 fingers = middle != null ? (middle.position - hand.position).normalized : bow.transform.forward;
            bow.transform.position = hand.position + fingers * 0.045f;
        }
    }
}
