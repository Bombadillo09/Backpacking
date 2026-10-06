using System.Collections.Generic;
using Backpacking.Audio;
using Backpacking.Interaction;
using Backpacking.Survival;
using Backpacking.UI;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// A fire ring. Light it with a match once it has wood; it burns through its fuel over game time,
    /// giving off heat and light, and can boil water and cook while lit.
    /// </summary>
    public class Campfire : MonoBehaviour, IInteractable
    {
        [Tooltip("Game hours of burning each piece of firewood adds.")]
        [SerializeField] float hoursPerPiece = 0.5f;
        [SerializeField] float maxFuelHours = 4f;
        [SerializeField] float lightingMinutes = 3f;
        [SerializeField] float minutesPerLitre = 12f;
        [SerializeField] float mealMinutes = 15f;

        [Header("Parts")]
        [SerializeField] GameObject woodVisual;
        [SerializeField] ParticleSystem flames;
        [SerializeField] Light fireLight;
        [SerializeField] HeatSource heat;

        [Header("Rain")]
        [Tooltip("Chance a match fails in a full downpour.")]
        [SerializeField, Range(0f, 1f)] float downpourLightFailChance = 0.7f;
        [Tooltip("How much faster the fire burns through wood in a full downpour (1 = twice as fast).")]
        [SerializeField] float downpourExtraBurn = 1f;

        [Header("Sound")]
        [Tooltip("Leave empty to use a generated crackle.")]
        [SerializeField] AudioClip crackleClip;
        [SerializeField, Range(0f, 1f)] float crackleVolume = 0.7f;

        AudioSource crackle;
        TimeOfDay timeOfDay;
        WeatherSystem weather;
        float fuelHours;
        bool burning;
        float baseLightIntensity;

        public bool IsBurning => burning;
        public float FuelHours => fuelHours;

        /// <summary>Restores a saved fire.</summary>
        public void Restore(float savedFuelHours, bool savedBurning)
        {
            fuelHours = Mathf.Clamp(savedFuelHours, 0f, maxFuelHours);
            SetBurning(savedBurning && fuelHours > 0f);
        }
        public string DisplayName => burning ? "Campfire" : "Fire ring";

        /// <summary>Called when the fire ring is built, with the firewood used to build it.</summary>
        public void Build(int firewoodPieces) => AddFuel(firewoodPieces);

        void Awake()
        {
            timeOfDay = FindAnyObjectByType<TimeOfDay>();
            weather = FindAnyObjectByType<WeatherSystem>();
            baseLightIntensity = fireLight.intensity;

            crackle = gameObject.AddComponent<AudioSource>();
            crackle.clip = crackleClip != null ? crackleClip : SoundSynth.Fire();
            crackle.loop = true;
            crackle.playOnAwake = false;
            crackle.spatialBlend = 1f;
            crackle.minDistance = 2f;
            crackle.maxDistance = 30f;
            crackle.rolloffMode = AudioRolloffMode.Linear;
            crackle.dopplerLevel = 0f;
            SetBurning(false);
        }

        void Update()
        {
            if (!burning)
                return;

            float rain = weather != null ? weather.RainIntensity : 0f;
            fuelHours -= Time.deltaTime * timeOfDay.HoursPerSecond * (1f + downpourExtraBurn * rain);
            if (fuelHours <= 0f)
            {
                fuelHours = 0f;
                SetBurning(false);
                Notifications.Post(rain > 0.3f ? "The rain has put the fire out." : "The fire has burned out.");
                return;
            }

            // Flicker, dimming as the fire burns low.
            float strength = Mathf.Clamp01(fuelHours / 0.5f) * 0.6f + 0.4f;
            fireLight.intensity = baseLightIntensity * strength * (0.85f + 0.3f * Mathf.PerlinNoise(Time.time * 6f, 0f));
            crackle.volume = crackleVolume * strength;
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            Backpack backpack = interactor.Backpack;

            if (!burning)
            {
                string lightProblem = fuelHours <= 0f ? "Add firewood first" : backpack.Matches <= 0 ? "No matches left" : null;
                float failChance = (weather != null ? weather.RainIntensity : 0f) * downpourLightFailChance * HikerTraits.FireFailFactor;
                string odds = failChance > 0.05f ? $", {Mathf.RoundToInt(failChance * 100f)}% chance the rain wins" : "";
                options.Add(new InteractionOption($"Light fire (1 match, {backpack.Matches} left{odds})", () =>
                    interactor.Activity.Begin("Lighting the fire", lightingMinutes, () =>
                    {
                        backpack.TryUseMatch();
                        if (Random.value < failChance)
                            Notifications.Post("The rain smothers your match. Try again.");
                        else
                            SetBurning(true);
                    }), lightProblem));
            }

            string addProblem = backpack.Firewood <= 0 ? "No firewood. Gather some nearby."
                : fuelHours + hoursPerPiece > maxFuelHours ? "The fire is full" : null;
            options.Add(new InteractionOption($"Add firewood (+{hoursPerPiece * 60f:0} min, {backpack.Firewood} carried)", () =>
            {
                backpack.TryUseFirewood(1);
                AddFuel(1);
            }, addProblem));

            if (burning)
            {
                CampCooking.AddOptions(interactor, options, minutesPerLitre, mealMinutes, canSmoke: true,
                    minutes => fuelHours * 60f < minutes ? $"Needs {minutes / 60f:0.#} h of fire. Add wood." : null,
                    _ => { });
                // Sitting barefoot by the fire holds your feet in its smoke (see RestMode).
                options.Add(new InteractionOption("Sit by the fire (then E: boots off, to warm, dry and smoke your feet)", () =>
                {
                    if (RestMode.Current != null)
                        RestMode.Current.SitDown();
                }));
                options.Add(new InteractionOption($"Fire will burn for about {FuelText()}", () => { }, ""));
            }
            else
            {
                int recoverable = Mathf.FloorToInt(fuelHours / hoursPerPiece);
                options.Add(new InteractionOption($"Dismantle fire ring (recover {recoverable} firewood)", () =>
                {
                    backpack.AddFirewood(recoverable);
                    Destroy(gameObject);
                }));
            }
        }

        void AddFuel(int pieces)
        {
            fuelHours = Mathf.Min(maxFuelHours, fuelHours + pieces * hoursPerPiece);
            woodVisual.SetActive(true);
        }

        string FuelText()
        {
            int minutes = Mathf.RoundToInt(fuelHours * 60f);
            return minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min" : $"{minutes} min";
        }

        void SetBurning(bool value)
        {
            burning = value;
            fireLight.enabled = value;
            heat.enabled = value;
            woodVisual.SetActive(fuelHours > 0f);
            if (value)
            {
                flames.Play();
                crackle.time = Random.Range(0f, crackle.clip.length);
                crackle.Play();
            }
            else
            {
                flames.Stop();
                crackle.Stop();
            }
        }
    }
}
