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
        public float overcast, rain, wind;
    }

    /// <summary>
    /// Clear skies, clouds, rain and storms, with the odd cold snap. Weather is planned a couple of days
    /// ahead as a queue of spells, so forecasts at trading posts are real. Changes blend in over game time
    /// and drive the sky, fog, rain effect, temperature and wind.
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] ParticleSystem rainEffect;

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

        float overcast, rain, wind;
        WeatherSpell current;
        float hoursLeft;
        readonly List<WeatherSpell> upcoming = new();
        ParticleSystem.EmissionModule rainEmission;
        ParticleSystem.VelocityOverLifetimeModule rainDrift;

        public WeatherKind Current => current.kind;
        public bool IsColdSnap => current.coldSnap;
        /// <summary>Cloud cover, 0 clear to 1 dark storm.</summary>
        public float Overcast => overcast;
        /// <summary>Rain strength, 0 dry to 1 downpour.</summary>
        public float RainIntensity => rain;
        /// <summary>Wind speed in km/h, including gusts.</summary>
        public float WindKmh => wind * (0.8f + 0.4f * Mathf.PerlinNoise(Time.time * 0.3f, 7f));

        /// <summary>°C added to the air temperature: clouds cool the day and warm the night, cold snaps chill everything.</summary>
        public float TemperatureOffset =>
            Mathf.Lerp(2f, -3f, timeOfDay.Daylight) * overcast + (current.coldSnap ? coldSnapTemperature : 0f);

        void Awake()
        {
            current = RandomSpell(WeatherKind.Clear);
            hoursLeft = current.hours;
            FillPlan();
            (overcast, rain, wind) = Targets(current.kind);
            if (rainEffect != null)
            {
                rainEmission = rainEffect.emission;
                rainDrift = rainEffect.velocityOverLifetime;
                rainEffect.Play();
            }
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

            (float targetOvercast, float targetRain, float targetWind) = Targets(current.kind);
            float step = blendHours > 0f ? hours / blendHours : 1f;
            overcast = Mathf.MoveTowards(overcast, targetOvercast, step);
            rain = Mathf.MoveTowards(rain, targetRain, step);
            wind = Mathf.MoveTowards(wind, targetWind, step * 40f);

            timeOfDay.Overcast = overcast;
            if (rainEffect != null)
            {
                rainEmission.rateOverTime = rain * maxRainParticles;
                rainDrift.x = WindKmh / 3.6f * 0.5f;
            }
        }

        static (float overcast, float rain, float wind) Targets(WeatherKind kind) => kind switch
        {
            WeatherKind.Clear => (0.1f, 0f, 6f),
            WeatherKind.Cloudy => (0.6f, 0f, 12f),
            WeatherKind.Rain => (0.85f, 0.6f, 16f),
            _ => (1f, 1f, 40f),
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
            hours = Random.Range(spellHours.x, spellHours.y) * (kind == WeatherKind.Storm ? 0.5f : 1f),
        };

        /// <summary>Which weather tends to follow which: storms build out of rain and clear through cloud.</summary>
        static WeatherKind NextKind(WeatherKind from)
        {
            float roll = Random.value;
            return from switch
            {
                WeatherKind.Clear => roll < 0.5f ? WeatherKind.Clear : roll < 0.9f ? WeatherKind.Cloudy : WeatherKind.Rain,
                WeatherKind.Cloudy => roll < 0.3f ? WeatherKind.Clear : roll < 0.6f ? WeatherKind.Cloudy : roll < 0.9f ? WeatherKind.Rain : WeatherKind.Storm,
                WeatherKind.Rain => roll < 0.1f ? WeatherKind.Clear : roll < 0.5f ? WeatherKind.Cloudy : roll < 0.85f ? WeatherKind.Rain : WeatherKind.Storm,
                _ => roll < 0.6f ? WeatherKind.Rain : WeatherKind.Cloudy,
            };
        }

        public static string Describe(WeatherKind kind) => kind switch
        {
            WeatherKind.Clear => "clear",
            WeatherKind.Cloudy => "cloudy",
            WeatherKind.Rain => "rain",
            _ => "storms",
        };

        /// <summary>Plain-language forecast for the next day or so, the way a shopkeeper would put it.</summary>
        public string ForecastText(float hoursAhead = 36f)
        {
            var text = new StringBuilder($"Forecast: {Describe(current.kind)} now");
            float at = timeOfDay.TotalHours + hoursLeft;
            foreach (WeatherSpell spell in upcoming)
            {
                if (at - timeOfDay.TotalHours > hoursAhead)
                    break;
                text.Append($", then {Describe(spell.kind)} from {DescribeWhen(at)}");
                at += spell.hours;
            }
            text.Append('.');
            if (current.coldSnap || upcoming.Exists(spell => spell.coldSnap))
                text.Append(" A cold snap is coming. Bring warm layers.");
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

        // ---------- Saving ----------

        public WeatherState CaptureState() => new()
        {
            current = current,
            hoursLeft = hoursLeft,
            upcoming = new List<WeatherSpell>(upcoming),
            overcast = overcast,
            rain = rain,
            wind = wind,
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
            FillPlan();
        }
    }
}
