using System;
using System.Collections.Generic;
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
        /// <summary>Game hours since midnight at the start of day 1. Use for timers that span days.</summary>
        public float TotalHours => (day - 1) * 24f + hour;
        /// <summary>0 at night, 1 in full daylight, in between at dawn and dusk.</summary>
        public float Daylight { get; private set; }
        /// <summary>Current clock speed-up: the largest of all active requests, or 1.</summary>
        public float TimeMultiplier { get; private set; } = 1f;
        /// <summary>Game hours per real second at normal speed.</summary>
        public float BaseHoursPerSecond => 24f / (realMinutesPerGameDay * 60f);
        /// <summary>Game hours per real second right now, including any speed-up.</summary>
        public float HoursPerSecond => BaseHoursPerSecond * TimeMultiplier;
        public string ClockText => FormatClock(hour);

        readonly Dictionary<object, float> speedRequests = new();

        public static string FormatClock(float hourOfDay)
        {
            int totalMinutes = Mathf.FloorToInt(Mathf.Repeat(hourOfDay, 24f) * 60f);
            return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
        }

        public void SetTime(float newHour)
        {
            hour = Mathf.Repeat(newHour, 24f);
            ApplyLighting();
        }

        /// <summary>Asks for the clock to run faster (sleeping, timed tasks, debug). Cleared with <see cref="ClearSpeed"/>.</summary>
        public void RequestSpeed(object owner, float multiplier)
        {
            speedRequests[owner] = multiplier;
            RecalculateMultiplier();
        }

        public void ClearSpeed(object owner)
        {
            if (speedRequests.Remove(owner))
                RecalculateMultiplier();
        }

        void RecalculateMultiplier()
        {
            float multiplier = 1f;
            foreach (float requested in speedRequests.Values)
                multiplier = Mathf.Max(multiplier, requested);
            TimeMultiplier = multiplier;
        }

        void Start() => ApplyLighting();

        void Update()
        {
            hour += Time.deltaTime * HoursPerSecond;
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
