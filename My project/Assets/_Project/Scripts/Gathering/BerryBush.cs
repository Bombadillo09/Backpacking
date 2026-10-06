using System.Collections.Generic;
using Backpacking.Interaction;
using Backpacking.Survival;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Gathering
{
    /// <summary>A bush with wild berries to pick. Once picked it takes a few game days to fruit again.</summary>
    public class BerryBush : MonoBehaviour, IInteractable
    {
        [SerializeField, Min(1)] int minBerries = 3;
        [SerializeField, Min(1)] int maxBerries = 6;
        [SerializeField] float pickMinutes = 5f;
        [SerializeField] float regrowHours = 72f;
        [SerializeField] GameObject berriesVisual;

        TimeOfDay timeOfDay;
        float pickedAtHour = float.NegativeInfinity;

        public string DisplayName => "Berry bush";
        bool HasBerries => timeOfDay.TotalHours - pickedAtHour >= regrowHours;

        void Awake() => timeOfDay = FindAnyObjectByType<TimeOfDay>();

        void Update()
        {
            bool hasBerries = HasBerries;
            if (berriesVisual.activeSelf != hasBerries)
                berriesVisual.SetActive(hasBerries);
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            float regrowsIn = regrowHours - (timeOfDay.TotalHours - pickedAtHour);
            options.Add(new InteractionOption("Pick berries", () =>
                interactor.Activity.Begin("Picking berries", pickMinutes, () =>
                {
                    interactor.Backpack.AddFood(FoodKind.Berries, Random.Range(minBerries, maxBerries + 1));
                    pickedAtHour = timeOfDay.TotalHours;
                }), HasBerries ? null : $"Already picked. More in about {Mathf.CeilToInt(regrowsIn)} h"));
        }
    }
}
