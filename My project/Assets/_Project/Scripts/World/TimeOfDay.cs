using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backpacking.World
{
    /// <summary>
    /// The in-game clock, plus the sun, moon, ambient light and fog that follow it.
    /// World axes: +Z is north, +X is east. The sun rises at 06:00 and sets at 18:00.
    /// </summary>
    public class TimeOfDay : MonoBehaviour
    {
        [Header("Clock")]
        [SerializeField, Range(0f, 24f)] float hour = 7f;
        [SerializeField, Min(1)] int day = 1;
        [Tooltip("Real-time minutes for one full in-game day.")]
        [SerializeField, Min(0.1f)] float realMinutesPerGameDay = 24f;

        [Header("Sun & Moon")]
        [SerializeField] Light sun;
        [SerializeField] Light moon;
        [SerializeField] float sunIntensity = 1.3f;
        [SerializeField] float moonIntensity = 0.12f;
        [Tooltip("How far the sun's path leans toward the south, in degrees. 0 passes straight overhead.")]
        [SerializeField, Range(0f, 80f)] float southwardTilt = 35f;
        [Tooltip("Sun color from the horizon (left) to high in the sky (right).")]
        [SerializeField] Gradient sunColor = DefaultSunColor();

        [Header("Ambient & Fog")]
        [SerializeField] Color daySky = new(0.55f, 0.65f, 0.8f);
        [SerializeField] Color dayHorizon = new(0.5f, 0.52f, 0.5f);
        [SerializeField] Color dayGround = new(0.25f, 0.23f, 0.2f);
        [SerializeField] Color nightSky = new(0.03f, 0.04f, 0.08f);
        [SerializeField] Color nightHorizon = new(0.02f, 0.025f, 0.04f);
        [SerializeField] Color nightGround = new(0.01f, 0.01f, 0.015f);
        [SerializeField] Color dayFog = new(0.66f, 0.72f, 0.78f);
        [SerializeField] Color nightFog = new(0.03f, 0.035f, 0.05f);

        /// <summary>Raised when the clock passes midnight, with the new day number.</summary>
        public event Action<int> DayStarted;

        public float Hour => hour;
        public int Day => day;
        /// <summary>0 at night, 1 in full daylight, in between at dawn and dusk.</summary>
        public float Daylight { get; private set; }
        /// <summary>Speeds the clock up (e.g. sleeping or debug fast-forward).</summary>
        public float TimeMultiplier { get; set; } = 1f;
        public string ClockText => $"{(int)hour:00}:{(int)(hour % 1f * 60f):00}";

        public void SetTime(float newHour)
        {
            hour = Mathf.Repeat(newHour, 24f);
            ApplyLighting();
        }

        void Start() => ApplyLighting();

        void Update()
        {
            hour += Time.deltaTime * TimeMultiplier * 24f / (realMinutesPerGameDay * 60f);
            while (hour >= 24f)
            {
                hour -= 24f;
                day++;
                DayStarted?.Invoke(day);
            }
            ApplyLighting();
        }

        void ApplyLighting()
        {
            if (sun == null)
                return;

            // Spin the sun around the east-west axis (pointing level and westward at 06:00,
            // straight down at noon), then lean the whole path toward the south.
            float spin = hour / 24f * 360f - 90f;
            Quaternion sunRotation = Quaternion.AngleAxis(-southwardTilt, Vector3.right)
                                     * Quaternion.Euler(0f, -90f, 0f)
                                     * Quaternion.Euler(spin, 0f, 0f);
            sun.transform.rotation = sunRotation;

            // Sine of the sun's angle above the horizon.
            float elevation = -sun.transform.forward.y;
            Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.1f, 0.15f, elevation));

            sun.intensity = sunIntensity * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.02f, 0.1f, elevation));
            sun.color = sunColor.Evaluate(Mathf.Clamp01(elevation / 0.5f));
            sun.enabled = sun.intensity > 0.001f;

            if (moon != null)
            {
                moon.transform.rotation = Quaternion.LookRotation(-sun.transform.forward);
                moon.intensity = moonIntensity * (1f - Daylight);
                moon.enabled = moon.intensity > 0.001f;
            }

            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(nightSky, daySky, Daylight);
            RenderSettings.ambientEquatorColor = Color.Lerp(nightHorizon, dayHorizon, Daylight);
            RenderSettings.ambientGroundColor = Color.Lerp(nightGround, dayGround, Daylight);
            RenderSettings.fogColor = Color.Lerp(nightFog, dayFog, Daylight);
        }

        static Gradient DefaultSunColor()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.45f, 0.2f), 0f),
                    new GradientColorKey(new Color(1f, 0.8f, 0.6f), 0.3f),
                    new GradientColorKey(new Color(1f, 0.97f, 0.92f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }
    }
}
