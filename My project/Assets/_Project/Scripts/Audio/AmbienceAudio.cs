using Backpacking.Camp;
using Backpacking.Survival;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Audio
{
    /// <summary>
    /// The sound of the world around the player: wind that follows the weather, rain (muffled inside the
    /// tent), birds by day with a dawn chorus, crickets on mild evenings, thunder in storms and water
    /// lapping at lakes. Leave any clip empty to use a generated placeholder.
    /// </summary>
    public class AmbienceAudio : MonoBehaviour
    {
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] WeatherSystem weather;
        [SerializeField] AmbientTemperature temperature;
        [SerializeField] Vitals vitals;

        [Header("Clips (empty = generated placeholder)")]
        [SerializeField] AudioClip windClip;
        [SerializeField] AudioClip rainClip;
        [SerializeField] AudioClip cricketsClip;
        [SerializeField] AudioClip waterClip;
        [SerializeField] AudioClip thunderClip;
        [SerializeField] AudioClip[] birdClips;

        [Header("Levels")]
        [SerializeField, Range(0f, 1f)] float windVolume = 0.55f;
        [SerializeField, Range(0f, 1f)] float rainVolume = 0.7f;
        [SerializeField, Range(0f, 1f)] float cricketsVolume = 0.25f;
        [SerializeField, Range(0f, 1f)] float birdVolume = 0.5f;
        [SerializeField, Range(0f, 1f)] float thunderVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] float waterVolume = 0.45f;

        [Header("Wildlife")]
        [Tooltip("Bird calls per real minute in full daylight. The dawn chorus triples it.")]
        [SerializeField] float birdCallsPerMinute = 8f;
        [Tooltip("Air temperature (°C) range over which crickets go from silent to full song.")]
        [SerializeField] Vector2 cricketTemperature = new(4f, 11f);
        [Tooltip("Real seconds between thunderclaps during a storm.")]
        [SerializeField] Vector2 thunderInterval = new(12f, 40f);

        AudioSource wind, rain, crickets, thunder;
        AudioLowPassFilter rainMuffle;
        AudioSource[] birds;
        int nextBird;
        float nextThunderTime;

        void Start()
        {
            wind = CreateLoop("Wind", windClip != null ? windClip : SoundSynth.Wind());
            rain = CreateLoop("Rain", rainClip != null ? rainClip : SoundSynth.Rain());
            rainMuffle = rain.gameObject.AddComponent<AudioLowPassFilter>();
            crickets = CreateLoop("Crickets", cricketsClip != null ? cricketsClip : SoundSynth.Crickets());
            thunder = CreateSource("Thunder", transform);

            // Birds call from out in the trees, so they get positioned sources that live in the world.
            var birdRoot = new GameObject("Bird Calls").transform;
            birds = new AudioSource[3];
            for (int i = 0; i < birds.Length; i++)
            {
                birds[i] = CreateSource($"Bird {i}", birdRoot);
                MakeSpatial(birds[i], 8f, 90f);
            }

            AudioClip water = waterClip != null ? waterClip : SoundSynth.Water();
            foreach (WaterSource lake in FindObjectsByType<WaterSource>())
                AddLakeSound(lake, water);

            nextThunderTime = Time.time + Random.Range(thunderInterval.x, thunderInterval.y);
        }

        void Update()
        {
            float rainAmount = weather != null ? weather.RainIntensity : 0f;
            float windKmh = weather != null ? weather.WindKmh : 5f;
            bool inTent = vitals.IsSleeping && vitals.IsSheltered;
            float windiness = Mathf.InverseLerp(0f, 45f, windKmh);

            Fade(wind, windVolume * (0.12f + 0.88f * windiness) * (inTent ? 0.5f : 1f));
            wind.pitch = 0.85f + 0.3f * windiness;

            Fade(rain, rainVolume * rainAmount);
            // The tent fabric dulls the rain to a patter.
            rainMuffle.cutoffFrequency = Mathf.MoveTowards(rainMuffle.cutoffFrequency, inTent ? 1100f : 22000f, 40000f * Time.deltaTime);

            float night = 1f - timeOfDay.Daylight;
            float warmth = Mathf.InverseLerp(cricketTemperature.x, cricketTemperature.y, temperature.GetTemperature(transform.position));
            Fade(crickets, cricketsVolume * night * warmth * (1f - rainAmount) * (1f - 0.7f * windiness));

            UpdateBirds(rainAmount, windiness);
            UpdateThunder();
        }

        void UpdateBirds(float rainAmount, float windiness)
        {
            // The dawn chorus peaks around 06:30.
            float sinceDawn = (timeOfDay.Hour - 6.5f) / 1.2f;
            float chorus = Mathf.Exp(-sinceDawn * sinceDawn);
            float rate = birdCallsPerMinute * timeOfDay.Daylight * (1f + 2f * chorus) * (1f - rainAmount) * (1f - 0.6f * windiness);
            // Rate is per real minute; check this frame's share of it.
            if (Random.value >= rate / 60f * Time.deltaTime)
                return;

            AudioSource source = birds[nextBird];
            nextBird = (nextBird + 1) % birds.Length;
            Vector2 direction = Random.insideUnitCircle.normalized * Random.Range(12f, 45f);
            source.transform.position = transform.position + new Vector3(direction.x, Random.Range(3f, 12f), direction.y);
            source.pitch = Random.Range(0.9f, 1.12f);
            source.PlayOneShot(BirdClip(), birdVolume * Random.Range(0.5f, 1f));
        }

        AudioClip BirdClip()
        {
            if (birdClips != null && birdClips.Length > 0)
                return birdClips[Random.Range(0, birdClips.Length)];
            return SoundSynth.BirdCall(Random.Range(0, SoundSynth.BirdVariants));
        }

        void UpdateThunder()
        {
            if (weather == null || weather.Current != WeatherKind.Storm || Time.time < nextThunderTime)
                return;
            nextThunderTime = Time.time + Random.Range(thunderInterval.x, thunderInterval.y);
            thunder.pitch = Random.Range(0.75f, 1.1f);
            thunder.PlayOneShot(thunderClip != null ? thunderClip : SoundSynth.Thunder(), thunderVolume * Random.Range(0.4f, 1f));
        }

        void AddLakeSound(WaterSource lake, AudioClip clip)
        {
            // Audible from a little way back from the shore, loudest at the water's edge.
            float radius = lake.TryGetComponent(out Renderer surface) ? Mathf.Max(surface.bounds.extents.x, surface.bounds.extents.z) : 20f;
            var source = lake.gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = waterVolume;
            MakeSpatial(source, radius * 0.8f, radius + 45f);
            source.rolloffMode = AudioRolloffMode.Linear;
            source.time = Random.Range(0f, clip.length);
            source.Play();
        }

        AudioSource CreateLoop(string sourceName, AudioClip clip)
        {
            AudioSource source = CreateSource(sourceName, transform);
            source.clip = clip;
            source.loop = true;
            source.volume = 0f;
            source.Play();
            return source;
        }

        static AudioSource CreateSource(string sourceName, Transform parent)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(parent, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        static void MakeSpatial(AudioSource source, float minDistance, float maxDistance)
        {
            source.spatialBlend = 1f;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
        }

        /// <summary>Eases a loop's volume toward a target, so weather changes swell in and out.</summary>
        static void Fade(AudioSource source, float target) =>
            source.volume = Mathf.MoveTowards(source.volume, target, 0.25f * Time.deltaTime);
    }
}
