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
        /// <summary>
        /// Current clock speed-up: the largest of all active requests, or 1; in co-op, the speed the group agreed on
        /// (<see cref="SharedMultiplier"/>).
        /// </summary>
        public float TimeMultiplier { get; private set; } = 1f;
        /// <summary>The speed-up this player is asking for: the largest of their requests, or 1.</summary>
        public float RequestedMultiplier { get; private set; } = 1f;
        /// <summary>
        /// In co-op, the clock runs at the speed everyone agrees on (the slowest anyone asks for), set by the network;
        /// null playing alone, when this player's requests set it.
        /// </summary>
        public float? SharedMultiplier
        {
            get => sharedMultiplier;
            set
            {
                sharedMultiplier = value;
                TimeMultiplier = value ?? RequestedMultiplier;
            }
        }
        float? sharedMultiplier;
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

        public void SetDayAndTime(int newDay, float newHour)
        {
            day = Mathf.Max(1, newDay);
            SetTime(newHour);
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
            RequestedMultiplier = multiplier;
            TimeMultiplier = sharedMultiplier ?? multiplier;
        }

        /// <summary>Cloud cover from 0 (clear) to 1 (storm), set by the weather. Dims the sun and thickens the fog.</summary>
        public float Overcast { get; set; }
        /// <summary>How closed the forest canopy is overhead, 0–1. Darkens the sky light and thickens the air.</summary>
        public float CanopyShade { get; set; }
        /// <summary>Ground mist, 0–1, e.g. at dawn. Thickens and pales the fog.</summary>
        public float Mist { get; set; }
        /// <summary>Thick weather fog, 0–1: a foggy spell, or being up in the cloud on high ground. Set by the weather.</summary>
        public float WeatherFog { get; set; }
        /// <summary>A lightning flash lighting everything up, 0–1. Set by the storm.</summary>
        public float Flash { get; set; }

        [Header("Forest & Mist")]
        [Tooltip("Sky and bounce light kept under a full canopy.")]
        [SerializeField, Range(0f, 1f)] float canopyAmbient = 0.45f;
        [Tooltip("Direct sun kept under a full canopy (the leaves' own shadows do the rest).")]
        [SerializeField, Range(0f, 1f)] float canopySun = 0.8f;
        [Tooltip("Fog density multiplier under a full canopy.")]
        [SerializeField] float canopyFog = 3.2f;
        [Tooltip("Fog density multiplier in full mist.")]
        [SerializeField] float mistFog = 3.5f;
        [SerializeField] Color forestFog = new(0.36f, 0.42f, 0.38f);
        [SerializeField] Color mistColour = new(0.8f, 0.82f, 0.84f);

        [Header("Weather")]
        [SerializeField] Color overcastAmbient = new(0.42f, 0.44f, 0.46f);
        [SerializeField] Color overcastFog = new(0.5f, 0.53f, 0.56f);
        [Tooltip("Fog density is multiplied by up to this much under full cloud.")]
        [SerializeField] float overcastFogMultiplier = 3f;
        [Tooltip("Fog density is multiplied by up to this much in thick fog (about 70 m to see).")]
        [SerializeField] float thickFogMultiplier = 11f;
        [SerializeField] Color thickFogColour = new(0.7f, 0.72f, 0.74f);
        [SerializeField] Color flashColour = new(0.75f, 0.8f, 1f);

        float clearFogDensity;
        Material skybox;
        float clearSkyExposure, clearAtmosphere;

        static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        static readonly int AtmosphereId = Shader.PropertyToID("_AtmosphereThickness");

        void Start()
        {
            clearFogDensity = RenderSettings.fogDensity;
            // Work on a copy so weather changes never edit the skybox asset itself.
            if (RenderSettings.skybox != null)
            {
                skybox = new Material(RenderSettings.skybox);
                RenderSettings.skybox = skybox;
                clearSkyExposure = skybox.HasProperty(ExposureId) ? skybox.GetFloat(ExposureId) : 1f;
                clearAtmosphere = skybox.HasProperty(AtmosphereId) ? skybox.GetFloat(AtmosphereId) : 1f;
            }
            ApplyLighting();
        }

        void OnDestroy()
        {
            if (skybox != null)
                Destroy(skybox);
        }

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

            float cloudDimming = 1f - 0.75f * Overcast;
            float canopyDimming = Mathf.Lerp(1f, canopySun, CanopyShade);
            sun.intensity = sunIntensity * cloudDimming * canopyDimming * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.02f, 0.1f, elevation));
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
            // Under cloud, daytime light goes flat and grey.
            float grey = Overcast * 0.7f;
            Color daySkyNow = Color.Lerp(daySky, overcastAmbient, grey);
            Color dayHorizonNow = Color.Lerp(dayHorizon, overcastAmbient * 0.9f, grey);
            // Under the trees, the open sky is mostly hidden: much less light comes from above and around.
            float shade = Mathf.Lerp(1f, canopyAmbient, CanopyShade);
            RenderSettings.ambientSkyColor = Color.Lerp(nightSky, daySkyNow, Daylight) * shade;
            RenderSettings.ambientEquatorColor = Color.Lerp(nightHorizon, dayHorizonNow, Daylight) * Mathf.Lerp(1f, shade, 0.8f);
            RenderSettings.ambientGroundColor = Color.Lerp(nightGround, dayGround * (1f - 0.3f * Overcast), Daylight) * Mathf.Lerp(1f, shade, 0.6f);
            if (Flash > 0f)
            {
                // Lightning: a blue-white blink of light from the whole sky.
                RenderSettings.ambientSkyColor += flashColour * Flash * 1.6f;
                RenderSettings.ambientEquatorColor += flashColour * Flash * 1.1f;
                RenderSettings.ambientGroundColor += flashColour * Flash * 0.4f;
            }

            Color fog = Color.Lerp(nightFog, Color.Lerp(dayFog, overcastFog, Overcast), Daylight);
            // Green-grey haze among the trees, pale mist in the open; both fade into the night colour after dark.
            fog = Color.Lerp(fog, Color.Lerp(nightFog, forestFog, Daylight), CanopyShade * 0.6f);
            fog = Color.Lerp(fog, Color.Lerp(nightFog, mistColour, Daylight), Mist * 0.5f);
            // Thick fog is a flat white-grey wall by day (dark at night, and less green under the trees).
            fog = Color.Lerp(fog, Color.Lerp(nightFog * 1.5f, thickFogColour * (1f - 0.35f * Overcast), Daylight), WeatherFog * 0.85f);
            fog += flashColour * Flash * 0.5f;
            RenderSettings.fogColor = fog;

            if (clearFogDensity > 0f)
            {
                float density = clearFogDensity * Mathf.Lerp(1f, overcastFogMultiplier, Overcast)
                                * Mathf.Lerp(1f, canopyFog, CanopyShade) * Mathf.Lerp(1f, mistFog, Mist);
                // In thick fog the canopy and cloud don't add more; the fog itself sets how far you see.
                RenderSettings.fogDensity = Mathf.Max(density, Mathf.Lerp(density, clearFogDensity * thickFogMultiplier, WeatherFog));
            }
            if (skybox != null)
            {
                skybox.SetFloat(ExposureId, clearSkyExposure * (1f - 0.55f * Overcast) + Flash * 1.5f);
                // (A thick atmosphere turns the sky yellow; the cloud dome does the grey.)
                skybox.SetFloat(AtmosphereId, clearAtmosphere + 0.3f * Overcast);
            }
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
