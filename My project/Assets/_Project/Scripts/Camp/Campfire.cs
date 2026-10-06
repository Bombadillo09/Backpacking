using System.Collections.Generic;
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

        TimeOfDay timeOfDay;
        float fuelHours;
        bool burning;
        float baseLightIntensity;

        public bool IsBurning => burning;
        public string DisplayName => burning ? "Campfire" : "Fire ring";

        /// <summary>Called when the fire ring is built, with the firewood used to build it.</summary>
        public void Build(int firewoodPieces) => AddFuel(firewoodPieces);

        void Awake()
        {
            timeOfDay = FindAnyObjectByType<TimeOfDay>();
            baseLightIntensity = fireLight.intensity;
            SetBurning(false);
        }

        void Update()
        {
            if (!burning)
                return;

            fuelHours -= Time.deltaTime * timeOfDay.HoursPerSecond;
            if (fuelHours <= 0f)
            {
                fuelHours = 0f;
                SetBurning(false);
                Notifications.Post("The fire has burned out.");
                return;
            }

            // Flicker, dimming as the fire burns low.
            float strength = Mathf.Clamp01(fuelHours / 0.5f) * 0.6f + 0.4f;
            fireLight.intensity = baseLightIntensity * strength * (0.85f + 0.3f * Mathf.PerlinNoise(Time.time * 6f, 0f));
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            Backpack backpack = interactor.Backpack;

            if (!burning)
            {
                string lightProblem = fuelHours <= 0f ? "Add firewood first" : backpack.Matches <= 0 ? "No matches left" : null;
                options.Add(new InteractionOption($"Light fire (1 match, {backpack.Matches} left)", () =>
                    interactor.Activity.Begin("Lighting the fire", lightingMinutes, () =>
                    {
                        backpack.TryUseMatch();
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
                CampCooking.AddOptions(interactor, options, minutesPerLitre, mealMinutes,
                    minutes => fuelHours * 60f < minutes ? "The fire is too low. Add wood." : null,
                    _ => { });
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
                flames.Play();
            else
                flames.Stop();
        }
    }
}
