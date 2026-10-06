using Backpacking.Audio;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Backpacking.Camp
{
    /// <summary>
    /// Pushing through the woods off the trail. Thick brush slows you to a crawl; swing the machete
    /// (click, or X on a gamepad) to hack a way through. What you cut stays cut.
    /// </summary>
    public class Undergrowth : MonoBehaviour
    {
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] FirstPersonController player;
        [SerializeField] GroundClearing clearing;
        [SerializeField] Backpack backpack;
        [SerializeField] Vitals vitals;
        [SerializeField] CampPlacer placer;

        [Tooltip("Walking speed kept in the thickest brush.")]
        [SerializeField, Range(0.05f, 1f)] float slowestSpeed = 0.2f;
        [Tooltip("Seconds between swings of the machete.")]
        [SerializeField] float swingSeconds = 0.55f;
        [Tooltip("How far ahead of you a swing lands, in metres.")]
        [SerializeField] float reach = 1.3f;
        [Tooltip("Brush thicker than this shows the hint.")]
        [SerializeField, Range(0f, 1f)] float hintAbove = 0.35f;
        [SerializeField, Range(0f, 1f)] float volume = 0.7f;

        InputAction attack;
        AudioSource source;
        Label hint;
        float density;
        float nextSwing;

        /// <summary>How thick the brush is where the player stands, 0–1. Footsteps get louder in it.</summary>
        public static float Density { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Density = 0f;
            Swung = null;
        }

        void Awake()
        {
            attack = inputActions.FindActionMap("Player", true).FindAction("Attack", true);
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        void Start()
        {
            hint = UIBuild.Text("", "hint", "shadowed");
            hint.style.top = Length.Percent(80f);
            hint.SetVisible(false);
            GameUI.Current.Hud.Add(hint.IgnoreMouse());
        }

        void OnDisable()
        {
            player.GroundSpeedMultiplier = 1f;
            Density = 0f;
        }

        void Update()
        {
            float target = clearing != null ? clearing.BrushDensity(player.transform.position) : 0f;
            density = Mathf.MoveTowards(density, target, 3f * Time.deltaTime);
            Density = density;
            player.GroundSpeedMultiplier = Mathf.Lerp(1f, slowestSpeed, density);

            bool free = !PlayerControlLock.MovementLocked && !placer.IsPlacing && UnityEngine.Cursor.lockState == CursorLockMode.Locked;
            if (free && attack.WasPressedThisFrame() && Time.time >= nextSwing && backpack.HasMachete)
                Swing();

            if (hint == null)
                return;
            bool showHint = free && density > hintAbove;
            hint.SetVisible(showHint);
            if (showHint)
                hint.SetText(backpack.HasMachete
                    ? "Thick brush. Click (or X) to hack through with your machete"
                    : "Thick brush slows you down. Stick to the trail");
        }

        /// <summary>Raised on every swing of the machete, hit or miss.</summary>
        public static event System.Action Swung;

        void Swing()
        {
            nextSwing = Time.time + swingSeconds;
            Swung?.Invoke();
            Vector3 ahead = player.transform.position + player.transform.forward * reach;
            int cut = clearing.Chop(ahead, vitals);
            source.pitch = Random.Range(0.9f, 1.1f);
            source.PlayOneShot(SoundSynth.Swish(), volume * 0.6f);
            if (cut > 0)
                source.PlayOneShot(SoundSynth.Chop(Random.Range(0, SoundSynth.ChopVariants)), volume);
        }
    }
}
