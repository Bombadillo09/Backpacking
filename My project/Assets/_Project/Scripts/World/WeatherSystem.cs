using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Backpacking.World
{
    public enum WeatherKind
    {
        Clear,
        Cloudy,
        Rain,
        Storm,
        // New kinds go on the end: saves store the number.
        Fog,
    }

    /// <summary>A stretch of one kind of weather. The upcoming ones form the forecast.</summary>
    [Serializable]
    public class WeatherSpell
    {
        public WeatherKind kind;
        public float hours;
        /// <summary>A cold snap rides along with this spell, lowering temperatures.</summary>
        public bool coldSnap;
    }

    [Serializable]
    public class WeatherState
    {
        public WeatherSpell current;
        public float hoursLeft;
        public List<WeatherSpell> upcoming = new();
        public float overcast, rain, wind, fog;
        /// <summary>How far below the mountain tops fresh snow lies, in metres (0 = none).</summary>
        public float snowCover;
        public float windHeading;
    }

    /// <summary>
    /// Clear skies, clouds, fog, rain and storms, with the odd cold snap. Weather is planned a couple of days
    /// ahead as a queue of spells, so forecasts at trading posts are real. Changes blend in over game time
    /// and drive the sky, fog, rain and snow effects, temperature and wind.
    /// <para>
    /// Where it's below freezing the rain falls as snow, and while it snows the snow line creeps down the
    /// mountains (melting back up once it's warmer). In grey weather the cloud sits on the high ground: walk
    /// up into it and the fog closes in.
    /// </para>
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] AmbientTemperature temperature;
        [SerializeField] Transform player;
        [SerializeField] ParticleSystem rainEffect;
        [SerializeField] ParticleSystem snowEffect;
        [Tooltip("Drifting wisps of fog around the player, shown in thick fog.")]
        [SerializeField] ParticleSystem fogWisps;

        [Header("Planning")]
        [Tooltip("How far ahead the weather is planned, in game hours.")]
        [SerializeField] float planAheadHours = 54f;
        [SerializeField] Vector2 spellHours = new(3f, 10f);
        [SerializeField, Range(0f, 1f)] float coldSnapChance = 0.12f;
        [SerializeField] float coldSnapTemperature = -7f;

        [Header("Blending")]
        [Tooltip("Game hours for the weather to fully change over.")]
        [SerializeField] float blendHours = 1f;
        [SerializeField] float maxRainParticles = 2500f;
        [SerializeField] float maxSnowParticles = 1600f;
        [SerializeField] float maxFogWisps = 6f;

        [Header("Snow")]
        [Tooltip("Metres the snow line comes down per game hour in heavy snow.")]
        [SerializeField] float snowfallMetresPerHour = 70f;
        [Tooltip("Metres the snow line melts back up per game hour once it's above freezing (faster in sun and rain).")]
        [SerializeField] float meltMetresPerHour = 12f;

        float overcast, rain, wind, fog;
        float windHeading = 250f;
        float snowCover;
        WeatherSpell current;
        float hoursLeft;
        readonly List<WeatherSpell> upcoming = new();
        Terrain terrain;

        public WeatherKind Current => current.kind;
        public bool IsColdSnap => current.coldSnap;
        /// <summary>Cloud cover, 0 clear to 1 dark storm.</summary>
        public float Overcast => overcast;
        /// <summary>Rain (or snow) strength, 0 dry to 1 downpour.</summary>
        public float RainIntensity => rain;
        /// <summary>Wind speed in km/h, including gusts.</summary>
        public float WindKmh => wind * (0.8f + 0.4f * Mathf.PerlinNoise(Time.time * 0.3f, 7f));
        /// <summary>The way the wind is blowing (towards), flat.</summary>
        public Vector3 WindDirection => Quaternion.Euler(0f, windHeading, 0f) * Vector3.forward;
        /// <summary>A foggy spell's thickness, 0–1 (not counting cloud on the high ground).</summary>
        public float FogAmount => fog;
        /// <summary>How much of what falls at the player is snow rather than rain, 0–1.</summary>
        public float SnowFraction { get; private set; }
        public bool IsSnowing => rain > 0.1f && SnowFraction > 0.5f;
        /// <summary>Height (world y) where the air is at freezing.</summary>
        public float FreezingLevel { get; private set; } = float.MaxValue;
        /// <summary>Height (world y) down to which fresh snow lies; above the mountains when there's none.</summary>
        public float SnowLine => TerrainTop - snowCover;
        /// <summary>Height (world y) of the bottom of the cloud. Above it, you're in the fog.</summary>
        public float CloudBase { get; private set; } = float.MaxValue;

        float TerrainTop => terrain != null ? terrain.transform.position.y + terrain.terrainData.size.y + 20f : 10000f;

        /// <summary>°C added to the air temperature: clouds cool the day and warm the night, cold snaps chill everything.</summary>
        public float TemperatureOffset =>
            Mathf.Lerp(2f, -3f, timeOfDay.Daylight) * overcast + (current.coldSnap ? coldSnapTemperature : 0f);

        void Awake()
        {
            current = RandomSpell(WeatherKind.Clear);
            hoursLeft = current.hours;
            FillPlan();
            (overcast, rain, wind, fog) = Targets(current.kind);
            terrain = Terrain.activeTerrain;
            foreach (ParticleSystem effect in new[] { rainEffect, snowEffect, fogWisps })
                if (effect != null)
                    effect.Play();
        }

        void Update()
        {
            float hours = Time.deltaTime * timeOfDay.HoursPerSecond;
            hoursLeft -= hours;
            while (hoursLeft <= 0f)
            {
                current = upcoming[0];
                upcoming.RemoveAt(0);
                hoursLeft += current.hours;
                FillPlan();
            }

            (float targetOvercast, float targetRain, float targetWind, float targetFog) = Targets(current.kind);
            // Fog burns off into haze through the middle of the day.
            float hour = timeOfDay.Hour;
            targetFog *= 1f - 0.55f * Mathf.InverseLerp(9f, 13f, hour) * Mathf.InverseLerp(20f, 16f, hour);
            float step = blendHours > 0f ? hours / blendHours : 1f;
            overcast = Mathf.MoveTowards(overcast, targetOvercast, step);
            rain = Mathf.MoveTowards(rain, targetRain, step);
            wind = Mathf.MoveTowards(wind, targetWind, step * 40f);
            fog = Mathf.MoveTowards(fog, targetFog, step);
            // The wind veers slowly over the day, mostly from the west; storms swing it about more.
            windHeading += (Mathf.PerlinNoise(timeOfDay.TotalHours * 0.05f, 3f) - 0.5f) * hours * (current.kind == WeatherKind.Storm ? 60f : 20f);
            windHeading = Mathf.Lerp(windHeading, 80f, hours * 0.02f);

            UpdateSnowAndCloud(hours);
            timeOfDay.Overcast = overcast;
            timeOfDay.WeatherFog = Mathf.Max(fog, InCloud());
            UpdateEffects();
        }

        /// <summary>Rain turns to snow below the freezing level, which also sets the snow line and the cloud base.</summary>
        void UpdateSnowAndCloud(float hours)
        {
            if (temperature != null)
            {
                // Air cools steadily with height: find where it reaches 0 °C.
                float atBase = temperature.GetTemperature(Vector3.zero);
                float coolingPerMetre = (atBase - temperature.GetTemperature(new Vector3(0f, 1000f, 0f))) / 1000f;
                FreezingLevel = coolingPerMetre > 1e-5f ? atBase / coolingPerMetre : float.MaxValue;
                SnowFraction = player != null
                    ? Mathf.InverseLerp(1.5f, -0.5f, temperature.GetTemperature(player.position))
                    : 0f;
            }

            float top = TerrainTop;
            float line = top - snowCover;
            if (rain > 0.15f && FreezingLevel < line)
                // Snowing on the high ground: the fresh snow reaches down towards the freezing level.
                line = Mathf.MoveTowards(line, FreezingLevel, snowfallMetresPerHour * rain * hours);
            else if (FreezingLevel > line)
            {
                // Above freezing: it melts back up the mountain, faster in sunshine or warm rain.
                float sun = timeOfDay.Daylight * (1f - overcast);
                line += meltMetresPerHour * (1f + 2f * sun + 2f * rain) * hours;
            }
            snowCover = Mathf.Max(0f, top - line);

            // In grey weather the cloud comes down onto the high ground.
            if (terrain != null)
            {
                TerrainData data = terrain.terrainData;
                float lowering = Mathf.InverseLerp(0.45f, 1f, overcast);
                CloudBase = terrain.transform.position.y + data.size.y * Mathf.Lerp(1.4f, 0.6f, lowering);
            }
        }

        /// <summary>How deep in the cloud the player is, 0–1.</summary>
        float InCloud()
        {
            if (player == null || CloudBase == float.MaxValue)
                return 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CloudBase - 30f, CloudBase + 40f, player.position.y)) * 0.95f;
        }

        void UpdateEffects()
        {
            // Fetch the modules each time: cached ones go stale when Play starts without a domain reload.
            Vector3 drift = WindDirection * (WindKmh / 3.6f);
            if (rainEffect != null)
            {
                ParticleSystem.EmissionModule emission = rainEffect.emission;
                emission.rateOverTime = rain * (1f - SnowFraction) * maxRainParticles;
                SetDrift(rainEffect, drift * 0.5f);
            }
            if (snowEffect != null)
            {
                ParticleSystem.EmissionModule emission = snowEffect.emission;
                emission.rateOverTime = rain * SnowFraction * maxSnowParticles;
                // Light flakes ride the wind.
                SetDrift(snowEffect, drift * 0.8f);
            }
            if (fogWisps != null)
            {
                ParticleSystem.EmissionModule emission = fogWisps.emission;
                emission.rateOverTime = maxFogWisps * timeOfDay.WeatherFog;
                SetDrift(fogWisps, drift * 0.3f);
            }
        }

        static void SetDrift(ParticleSystem effect, Vector3 velocity)
        {
            ParticleSystem.VelocityOverLifetimeModule module = effect.velocityOverLifetime;
            if (!module.enabled)
                return;
            module.x = velocity.x;
            module.z = velocity.z;
        }

        static (float overcast, float rain, float wind, float fog) Targets(WeatherKind kind) => kind switch
        {
            WeatherKind.Clear => (0.1f, 0f, 6f, 0f),
            WeatherKind.Cloudy => (0.6f, 0f, 12f, 0f),
            WeatherKind.Fog => (0.55f, 0f, 2f, 1f),
            WeatherKind.Rain => (0.85f, 0.6f, 16f, 0.1f),
            _ => (1f, 1f, 40f, 0f),
        };

        void FillPlan()
        {
            float planned = hoursLeft;
            foreach (WeatherSpell spell in upcoming)
                planned += spell.hours;
            while (planned < planAheadHours)
            {
                WeatherSpell last = upcoming.Count > 0 ? upcoming[^1] : current;
                WeatherSpell next = RandomSpell(NextKind(last.kind));
                // Cold snaps last a while once they arrive.
                next.coldSnap = last.coldSnap ? Random.value < 0.6f : Random.value < coldSnapChance;
                upcoming.Add(next);
                planned += next.hours;
            }
        }

        WeatherSpell RandomSpell(WeatherKind kind) => new()
        {
            kind = kind,
            hours = Random.Range(spellHours.x, spellHours.y) * (kind == WeatherKind.Storm ? 0.5f : kind == WeatherKind.Fog ? 0.7f : 1f),
        };

        /// <summary>Which weather tends to follow which: storms build out of rain and clear through cloud; fog forms in still, clear weather.</summary>
        static WeatherKind NextKind(WeatherKind from)
        {
            float roll = Random.value;
            return from switch
            {
                WeatherKind.Clear => roll < 0.4f ? WeatherKind.Clear : roll < 0.55f ? WeatherKind.Fog : roll < 0.9f ? WeatherKind.Cloudy : WeatherKind.Rain,
                WeatherKind.Cloudy => roll < 0.25f ? WeatherKind.Clear : roll < 0.35f ? WeatherKind.Fog : roll < 0.6f ? WeatherKind.Cloudy : roll < 0.9f ? WeatherKind.Rain : WeatherKind.Storm,
                WeatherKind.Fog => roll < 0.5f ? WeatherKind.Clear : roll < 0.85f ? WeatherKind.Cloudy : WeatherKind.Rain,
                WeatherKind.Rain => roll < 0.1f ? WeatherKind.Clear : roll < 0.45f ? WeatherKind.Cloudy : roll < 0.55f ? WeatherKind.Fog : roll < 0.85f ? WeatherKind.Rain : WeatherKind.Storm,
                _ => roll < 0.6f ? WeatherKind.Rain : WeatherKind.Cloudy,
            };
        }

        public static string Describe(WeatherKind kind) => kind switch
        {
            WeatherKind.Clear => "clear",
            WeatherKind.Cloudy => "cloudy",
            WeatherKind.Fog => "fog",
            WeatherKind.Rain => "rain",
            _ => "storms",
        };

        /// <summary>The weather where the player is right now: snow instead of rain when it's freezing, and "in the clouds" up high.</summary>
        public string DescribeHere()
        {
            if (rain > 0.1f && SnowFraction > 0.5f)
                return current.kind == WeatherKind.Storm ? "blizzard" : "snow";
            if (InCloud() > 0.5f && fog < 0.5f)
                return "in the clouds";
            return Describe(current.kind);
        }

        /// <summary>Plain-language forecast for the next day or so, the way a shopkeeper would put it.</summary>
        public string ForecastText(float hoursAhead = 36f)
        {
            var text = new StringBuilder($"Forecast: {Describe(current.kind)} now");
            float at = timeOfDay.TotalHours + hoursLeft;
            bool wet = current.kind is WeatherKind.Rain or WeatherKind.Storm;
            foreach (WeatherSpell spell in upcoming)
            {
                if (at - timeOfDay.TotalHours > hoursAhead)
                    break;
                text.Append($", then {Describe(spell.kind)} from {DescribeWhen(at)}");
                wet |= spell.kind is WeatherKind.Rain or WeatherKind.Storm;
                at += spell.hours;
            }
            text.Append('.');
            if (current.coldSnap || upcoming.Exists(spell => spell.coldSnap))
                text.Append(" A cold snap is coming. Bring warm layers.");
            if (wet && (current.coldSnap || upcoming.Exists(spell => spell.coldSnap) || FreezingLevel < TerrainTop - 100f))
                text.Append(" Expect snow on the high ground.");
            if (current.kind == WeatherKind.Storm || upcoming.Exists(spell => spell.kind == WeatherKind.Storm))
                text.Append(" Keep off the ridges in a thunderstorm.");
            return text.ToString();
        }

        string DescribeWhen(float totalHours)
        {
            int dayOffset = (int)(totalHours / 24f) - (timeOfDay.Day - 1);
            float hour = totalHours % 24f;
            string part = hour < 5f ? "night" : hour < 12f ? "morning" : hour < 17f ? "afternoon" : hour < 21f ? "evening" : "night";
            return dayOffset switch
            {
                <= 0 => part == "night" ? "tonight" : $"this {part}",
                1 => part == "night" ? "tomorrow night" : $"tomorrow {part}",
                _ => $"the day after tomorrow ({part})",
            };
        }

        // ---------- Testing ----------

        /// <summary>Switches to <paramref name="kind"/> straight away (for testing and screenshots), with fresh snow
        /// lying <paramref name="snowMetres"/> down from the tops.</summary>
        public void Force(WeatherKind kind, bool coldSnap = false, float snowMetres = -1f)
        {
            current = new WeatherSpell { kind = kind, hours = 6f, coldSnap = coldSnap };
            hoursLeft = current.hours;
            upcoming.Clear();
            FillPlan();
            (overcast, rain, wind, fog) = Targets(kind);
            if (snowMetres >= 0f)
                snowCover = snowMetres;
        }

        // ---------- Saving ----------

        public WeatherState CaptureState() => new()
        {
            current = current,
            hoursLeft = hoursLeft,
            upcoming = new List<WeatherSpell>(upcoming),
            overcast = overcast,
            rain = rain,
            wind = wind,
            fog = fog,
            snowCover = snowCover,
            windHeading = windHeading,
        };

        public void RestoreState(WeatherState state)
        {
            if (state?.current == null)
                return;
            current = state.current;
            hoursLeft = state.hoursLeft;
            upcoming.Clear();
            upcoming.AddRange(state.upcoming);
            overcast = state.overcast;
            rain = state.rain;
            wind = state.wind;
            fog = state.fog;
            snowCover = state.snowCover;
            if (state.windHeading != 0f)
                windHeading = state.windHeading;
            FillPlan();
        }
    }
}
