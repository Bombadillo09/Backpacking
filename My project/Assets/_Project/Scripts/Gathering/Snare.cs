using System.Collections.Generic;
using Backpacking.Interaction;
using Backpacking.Survival;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Gathering
{
    /// <summary>
    /// A wire snare. Over game time it may catch a rabbit, but only while the player is far enough away
    /// that animals aren't scared off. Set it, leave, and come back to check it.
    /// </summary>
    public class Snare : MonoBehaviour, IInteractable
    {
        [Tooltip("Chance per game hour of catching something while undisturbed.")]
        [SerializeField, Range(0f, 1f)] float catchChancePerHour = 0.07f;
        [Tooltip("Rabbits move more at night, dawn and dusk.")]
        [SerializeField] float lowLightMultiplier = 1.8f;
        [Tooltip("Animals won't come near while the player is within this many metres.")]
        [SerializeField] float disturbanceRadius = 25f;
        [SerializeField] GameObject caughtVisual;

        TimeOfDay timeOfDay;
        Transform player;
        bool caught;

        public string DisplayName => "Snare";

        public bool HasCatch
        {
            get => caught;
            set
            {
                caught = value;
                caughtVisual.SetActive(value);
            }
        }

        void Awake()
        {
            timeOfDay = FindAnyObjectByType<TimeOfDay>();
            GameObject playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null)
                player = playerObject.transform;
            caughtVisual.SetActive(false);
        }

        void Update()
        {
            if (caught || player == null)
                return;
            if ((player.position - transform.position).sqrMagnitude < disturbanceRadius * disturbanceRadius)
                return;

            float hours = Time.deltaTime * timeOfDay.HoursPerSecond;
            float hour = timeOfDay.Hour;
            bool lowLight = hour < 8f || hour > 18f;
            float chancePerHour = Mathf.Clamp01(catchChancePerHour * (lowLight ? lowLightMultiplier : 1f));
            // Chance of at least one catch over this frame's slice of game time.
            float chance = 1f - Mathf.Pow(1f - chancePerHour, hours);
            if (Random.value < chance)
            {
                caught = true;
                caughtVisual.SetActive(true);
            }
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            Backpack backpack = interactor.Backpack;
            if (caught)
            {
                options.Add(new InteractionOption("Take the rabbit and reset the snare", () =>
                {
                    backpack.AddFood(FoodKind.RabbitCarcass);
                    caught = false;
                    caughtVisual.SetActive(false);
                }));
            }
            else
                options.Add(new InteractionOption("Nothing caught yet", () => { }, "Leave the area and come back later"));

            options.Add(new InteractionOption(caught ? "Take the rabbit and pick up the snare" : "Pick up snare", () =>
            {
                if (caught)
                    backpack.AddFood(FoodKind.RabbitCarcass);
                backpack.AddSnare();
                Destroy(gameObject);
            }));
        }
    }
}
