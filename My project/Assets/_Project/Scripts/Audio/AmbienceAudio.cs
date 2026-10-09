using System.Collections.Generic;
using Backpacking.Camp;
using Backpacking.Survival;
using Backpacking.UI;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Audio
{
    /// <summary>
    /// The sound of the world around the player: wind that follows the weather, rain (muffled inside the
    /// tent), birds by day with a dawn chorus, crickets on mild evenings, owls after dark, thunder after each lightning strike and water
    /// lapping at lakes. Indoors it's all muffled. The settings' ambience volume scales the lot. Leave any clip empty
    /// to use the recordings in Resources/Sounds (or a generated placeholder).
    /// </summary>
    public class AmbienceAudio : MonoBehaviour
    {
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] WeatherSystem weather;
        [SerializeField] AmbientTemperature temperature;
        [SerializeField] Vitals vitals;

        [Header("Clips (empty = recording, else generated)")]
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
        [SerializeField, Range(0f, 1f)] float owlVolume = 0.5f;

        [Header("Wildlife")]
        [Tooltip("Bird calls per real minute in full daylight. The dawn chorus triples it.")]
        [SerializeField] float birdCallsPerMinute = 8f;
        [Tooltip("Owl hoots per real minute in full darkness.")]
        [SerializeField] float owlCallsPerMinute = 1.2f;
        [Tooltip("Air temperature (°C) range over which crickets go from silent to full song.")]
        [SerializeField] Vector2 cricketTemperature = new(4f, 11f);
        [Tooltip("Speed of sound, m/s: thunder arrives this long after the flash.")]
        [SerializeField] float speedOfSound = 343f;

        AudioSource wind, rain, crickets;
        AudioLowPassFilter rainMuffle;
        AudioSource[] birds;
        AudioSource owl;
        int nextBird;
        // Two thunder voices, so a new clap can start while the last still rolls.
        readonly AudioSource[] thunder = new AudioSource[2];
        readonly AudioLowPassFilter[] thunderMuffle = new AudioLowPassFilter[2];
        int nextThunder;
        readonly List<(float time, float distance)> pendingThunder = new();
        readonly List<AudioSource> lakes = new();
        // How much of the outdoors comes through: 1 outside, less indoors (eased as you go in and out).
        float outdoors = 1f;
        float nextIndoorCheck;
        bool indoors;

        /// <summary>The settings' ambience volume.</summary>
        static float Level => GameSettings.AmbienceVolume;

        void Start()
        {
            wind = CreateLoop("Wind", windClip != null ? windClip : Sounds.Wind());
            rain = CreateLoop("Rain", rainClip != null ? rainClip : Sounds.Rain());
            rainMuffle = rain.gameObject.AddComponent<AudioLowPassFilter>();
            crickets = CreateLoop("Crickets", cricketsClip != null ? cricketsClip : Sounds.Crickets());
            for (int i = 0; i < thunder.Length; i++)
            {
                thunder[i] = CreateSource($"Thunder {i}", transform);
                thunderMuffle[i] = thunder[i].gameObject.AddComponent<AudioLowPassFilter>();
            }

            // Birds call from out in the trees, so they get positioned sources that live in the world.
            var birdRoot = new GameObject("Bird Calls").transform;
            birds = new AudioSource[3];
            for (int i = 0; i < birds.Length; i++)
            {
                birds[i] = CreateSource($"Bird {i}", birdRoot);
                MakeSpatial(birds[i], 8f, 90f);
            }
            owl = CreateSource("Owl", birdRoot);
            MakeSpatial(owl, 15f, 160f);

            AudioClip water = waterClip != null ? waterClip : Sounds.Water();
            foreach (WaterSource lake in FindObjectsByType<WaterSource>())
                if (!lake.Flowing)
                    AddLakeSound(lake, water);
            // Rivers and streams are many short stretches: one voice follows the nearest bit of running water.
            river = CreateSource("River", birdRoot);
            MakeSpatial(river, 5f, 55f);
            river.rolloffMode = AudioRolloffMode.Linear;
            river.clip = water;
            river.loop = true;
            river.pitch = 1.25f;
            river.Play();
        }

        AudioSource river;
        float nextRiverCheck;

        /// <summary>Puts the river's voice on the closest point of the closest running water.</summary>
        void UpdateRiver()
        {
            if (river == null)
                return;
            river.volume = waterVolume * 1.3f * Level;
            if (Time.time < nextRiverCheck)
                return;
            nextRiverCheck = Time.time + 0.3f;
            Vector3 here = transform.position;
            float best = float.MaxValue;
            Vector3 nearest = here + Vector3.up * 500f;
            foreach (WaterSource water in WaterSource.All)
            {
                if (water == null || !water.Flowing || !water.TryGetComponent(out Collider area))
                    continue;
                Vector3 point = area.ClosestPoint(here);
                float distance = (point - here).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    nearest = point;
                }
            }
            river.transform.position = nearest;
        }

        void Update()
        {
            float rainAmount = weather != null ? weather.RainIntensity : 0f;
            float windKmh = weather != null ? weather.WindKmh : 5f;
            bool inTent = vitals.IsSleeping && vitals.IsSheltered;
            float windiness = Mathf.InverseLerp(0f, 45f, windKmh);
            if (Time.time >= nextIndoorCheck)
            {
                nextIndoorCheck = Time.time + 0.4f;
                indoors = Building.IsIndoors(transform.position);
            }
            outdoors = Mathf.MoveTowards(outdoors, indoors ? 0.3f : 1f, Time.deltaTime * 1.5f);
            float level = Level;

            Fade(wind, windVolume * (0.12f + 0.88f * windiness) * (inTent ? 0.5f : 1f) * outdoors * level);
            wind.pitch = 0.85f + 0.3f * windiness;

            Fade(rain, rainVolume * rainAmount * Mathf.Lerp(0.5f, 1f, outdoors) * level);
            // The tent fabric dulls the rain to a patter; a roof, to a drumming.
            float rainCutoff = inTent ? 1100f : indoors ? 1800f : 22000f;
            rainMuffle.cutoffFrequency = Mathf.MoveTowards(rainMuffle.cutoffFrequency, rainCutoff, 40000f * Time.deltaTime);

            float night = 1f - timeOfDay.Daylight;
            float warmth = Mathf.InverseLerp(cricketTemperature.x, cricketTemperature.y, temperature.GetTemperature(transform.position));
            Fade(crickets, cricketsVolume * night * warmth * (1f - rainAmount) * (1f - 0.7f * windiness) * outdoors * level);
            foreach (AudioSource lake in lakes)
                if (lake != null)
                    lake.volume = waterVolume * level;

            UpdateRiver();
            UpdateBirds(rainAmount, windiness);
            UpdateOwls(rainAmount, windiness);
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
            source.PlayOneShot(BirdClip(), birdVolume * Random.Range(0.5f, 1f) * outdoors * Level);
        }

        AudioClip BirdClip()
        {
            if (birdClips != null && birdClips.Length > 0)
                return birdClips[Random.Range(0, birdClips.Length)];
            return Sounds.BirdCall();
        }

        /// <summary>Now and then after dark, an owl hoots from somewhere out in the woods.</summary>
        void UpdateOwls(float rainAmount, float windiness)
        {
            float night = 1f - timeOfDay.Daylight;
            float rate = owlCallsPerMinute * night * night * (1f - rainAmount) * (1f - 0.7f * windiness);
            if (Random.value >= rate / 60f * Time.deltaTime)
                return;
            AudioClip hoot = Sounds.Owl();
            if (hoot == null)
                return;

            Vector2 direction = Random.insideUnitCircle.normalized * Random.Range(30f, 80f);
            owl.transform.position = transform.position + new Vector3(direction.x, Random.Range(6f, 14f), direction.y);
            owl.pitch = Random.Range(0.96f, 1.04f);
            owl.PlayOneShot(hoot, owlVolume * Random.Range(0.6f, 1f) * outdoors * Level);
        }

        /// <summary>A lightning strike <paramref name="distance"/> metres away: its thunder follows once the sound arrives.</summary>
        public void HearThunder(float distance) => pendingThunder.Add((Time.time + distance / speedOfSound, distance));

        void UpdateThunder()
        {
            for (int i = pendingThunder.Count - 1; i >= 0; i--)
            {
                (float time, float distance) = pendingThunder[i];
                if (Time.time < time)
                    continue;
                pendingThunder.RemoveAt(i);
                // Close by: a loud, sharp crack. Far off: a low, quiet rumble with the high notes lost on the way.
                float far = Mathf.InverseLerp(150f, 5000f, distance);
                AudioSource voice = thunder[nextThunder];
                thunderMuffle[nextThunder].cutoffFrequency = Mathf.Lerp(12000f, 700f, Mathf.Sqrt(far));
                nextThunder = (nextThunder + 1) % thunder.Length;
                voice.pitch = Mathf.Lerp(1.2f, 0.7f, far) * Random.Range(0.92f, 1.08f);
                voice.PlayOneShot(thunderClip != null ? thunderClip : Sounds.Thunder(), thunderVolume * Mathf.Lerp(1f, 0.3f, far) * Mathf.Lerp(0.6f, 1f, outdoors) * Mathf.Lerp(0.5f, 1f, Level));
            }
        }

        void AddLakeSound(WaterSource lake, AudioClip clip)
        {
            // Audible from a little way back from the shore, loudest at the water's edge.
            float radius = lake.TryGetComponent(out Renderer surface) ? Mathf.Max(surface.bounds.extents.x, surface.bounds.extents.z) : 20f;
            var source = lake.gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = waterVolume * Level;
            lakes.Add(source);
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
