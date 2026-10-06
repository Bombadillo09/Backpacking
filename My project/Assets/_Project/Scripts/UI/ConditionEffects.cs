using Backpacking.Audio;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.UI
{
    /// <summary>
    /// Lets the player feel their condition without reading the bars: frost at the screen edges and
    /// shivering when cold, a red heartbeat when hurt, tunnel vision when starving or parched, and
    /// heavy eyelids and swaying when exhausted.
    /// </summary>
    public class ConditionEffects : MonoBehaviour
    {
        [SerializeField] Vitals vitals;
        [SerializeField] FirstPersonController player;
        [SerializeField] PlayerActivity activity;

        [Header("Thresholds")]
        [Tooltip("Warmth below which frost and shivering start.")]
        [SerializeField] float coldStart = 30f;
        [Tooltip("Health below which the heartbeat vignette starts.")]
        [SerializeField] float hurtStart = 40f;
        [Tooltip("Energy below which your eyes start to close.")]
        [SerializeField] float drowsyStart = 15f;

        [Header("Strength")]
        [Tooltip("Shiver amplitude in degrees at zero warmth.")]
        [SerializeField] float maxShiver = 0.8f;
        [Tooltip("Sway in degrees when completely exhausted.")]
        [SerializeField] float maxSway = 2.5f;

        [Header("Sound")]
        [Tooltip("Leave empty to use a generated heartbeat.")]
        [SerializeField] AudioClip heartbeatClip;
        [SerializeField, Range(0f, 1f)] float heartbeatVolume = 0.8f;

        static readonly Color Frost = new(0.78f, 0.88f, 1f);
        static readonly Color Blood = new(0.6f, 0f, 0f);

        Texture2D vignette;
        VisualElement weakness, frost, blood, drowsiness, eyelids;
        float nextBlinkTime;
        float blinkStartTime = -10f;
        float lastBeatTime = -10f;
        AudioSource heartbeat;

        float Cold => Mathf.InverseLerp(coldStart, 0f, vitals.Warmth);
        float Hurt => Mathf.InverseLerp(hurtStart, 0f, vitals.Health);
        float Drowsy => Mathf.InverseLerp(drowsyStart, 0f, vitals.Energy);
        float Weak => Mathf.Max(
            Mathf.InverseLerp(vitals.CriticalThreshold, 0f, vitals.Satiety),
            Mathf.InverseLerp(vitals.CriticalThreshold, 0f, vitals.Hydration));

        void Awake()
        {
            vignette = CreateVignette(128);
            heartbeat = gameObject.AddComponent<AudioSource>();
            heartbeat.playOnAwake = false;
            heartbeat.spatialBlend = 0f;
            heartbeat.clip = heartbeatClip != null ? heartbeatClip : SoundSynth.Heartbeat();
        }

        void OnDestroy()
        {
            if (vignette != null)
                Destroy(vignette);
        }

        void OnDisable() => player.ViewOffset = Vector3.zero;

        void Update()
        {
            if (activity.IsSleeping)
            {
                player.ViewOffset = Vector3.zero;
                return;
            }

            float t = Time.time;

            // A heartbeat that quickens as health falls, heard and seen together.
            float hurt = Hurt;
            if (hurt > 0f && t - lastBeatTime >= 1f / Mathf.Lerp(1.1f, 2f, hurt))
            {
                lastBeatTime = t;
                heartbeat.PlayOneShot(heartbeat.clip, heartbeatVolume * Mathf.Lerp(0.3f, 1f, hurt));
            }

            // Fast, jittery noise for shivering; a slow lean for exhaustion.
            float shiver = Cold * maxShiver;
            float sway = Drowsy * maxSway;
            player.ViewOffset = new Vector3(
                (Mathf.PerlinNoise(t * 22f, 0f) - 0.5f) * 2f * shiver + Mathf.Sin(t * 0.45f) * sway * 0.6f,
                (Mathf.PerlinNoise(0f, t * 22f) - 0.5f) * 2f * shiver,
                Mathf.Sin(t * 0.7f) * sway);

            // Blink more often, and for longer, the more tired you are.
            if (Drowsy > 0f && t >= nextBlinkTime)
            {
                blinkStartTime = t;
                nextBlinkTime = t + Mathf.Lerp(9f, 2.5f, Drowsy) * Random.Range(0.7f, 1.3f);
            }
        }

        void Start()
        {
            weakness = Vignette(Color.black);
            frost = Vignette(Frost);
            blood = Vignette(Blood);
            drowsiness = Vignette(Color.black);
            eyelids = UIBuild.Layer("blackout");
            GameUI.Current.Effects.With(weakness, frost, blood, drowsiness, eyelids);
        }

        VisualElement Vignette(Color colour)
        {
            VisualElement element = UIBuild.Layer("vignette");
            element.style.backgroundImage = vignette;
            element.style.unityBackgroundImageTintColor = colour;
            element.style.opacity = 0f;
            return element;
        }

        void LateUpdate()
        {
            if (eyelids == null)
                return;

            bool awake = !activity.IsSleeping;
            float hurt = Hurt, drowsy = Drowsy;
            weakness.style.opacity = awake ? Weak * 0.85f : 0f;
            frost.style.opacity = awake ? Cold * 0.6f : 0f;

            float beat = Mathf.Exp(-(Time.time - lastBeatTime) * 9f);
            blood.style.opacity = awake ? Mathf.Clamp01(hurt * (0.45f + 0.35f * beat)) : 0f;

            // Eyelids drift shut for a moment, then snap open.
            float blinkLength = Mathf.Lerp(0.35f, 0.9f, drowsy);
            float progress = (Time.time - blinkStartTime) / blinkLength;
            float closed = drowsy > 0f && progress is > 0f and < 1f ? Mathf.Sin(progress * Mathf.PI) : 0f;
            drowsiness.style.opacity = awake ? drowsy * 0.5f : 0f;
            eyelids.style.opacity = awake ? closed * Mathf.Lerp(0.6f, 0.95f, drowsy) : 0f;
        }

        /// <summary>White, clear in the middle and opaque toward the edges. Stretched to the screen, it becomes an oval.</summary>
        static Texture2D CreateVignette(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.35f, Mathf.Sqrt(u * u + v * v)));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(edge * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
