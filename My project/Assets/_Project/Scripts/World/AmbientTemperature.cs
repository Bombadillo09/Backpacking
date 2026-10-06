using UnityEngine;

namespace Backpacking.World
{
    /// <summary>
    /// Air temperature from time of day and altitude. Weather and biomes will feed in here later.
    /// </summary>
    public class AmbientTemperature : MonoBehaviour
    {
        [SerializeField] TimeOfDay timeOfDay;
        [Tooltip("Optional. Clouds, rain and cold snaps shift the temperature.")]
        [SerializeField] WeatherSystem weather;

        [Header("Daily cycle at world height 0 (°C)")]
        [SerializeField] float dailyLow = 2f;
        [SerializeField] float dailyHigh = 16f;
        [SerializeField, Range(0f, 24f)] float coldestHour = 5.5f;
        [SerializeField, Range(0f, 24f)] float warmestHour = 15f;

        [Header("Altitude")]
        [Tooltip("Real-world altitude in metres that world height 0 represents.")]
        [SerializeField] float altitudeAtWorldZero = 1200f;
        [Tooltip("°C lost per 1000 m of climb. The standard atmosphere is about 6.5.")]
        [SerializeField] float lapseRatePerKm = 6.5f;

        public float GetAltitude(Vector3 worldPosition) => altitudeAtWorldZero + worldPosition.y;

        public float GetTemperature(Vector3 worldPosition)
        {
            float altitudeDrop = worldPosition.y * lapseRatePerKm / 1000f;
            float weatherOffset = weather != null ? weather.TemperatureOffset : 0f;
            return GetDailyCycleTemperature(timeOfDay.Hour) - altitudeDrop + weatherOffset;
        }

        float GetDailyCycleTemperature(float hour)
        {
            // Warms from the coldest hour to the warmest, then cools for the rest of the day.
            float warmingHours = Mathf.Repeat(warmestHour - coldestHour, 24f);
            float sinceColdest = Mathf.Repeat(hour - coldestHour, 24f);
            float t = sinceColdest < warmingHours
                ? sinceColdest / warmingHours
                : 1f - (sinceColdest - warmingHours) / (24f - warmingHours);
            float eased = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI);
            return Mathf.Lerp(dailyLow, dailyHigh, eased);
        }
    }
}
