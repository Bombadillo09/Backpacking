using System.Collections.Generic;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// A lake, river or stream: a thin trigger at the water's surface (a stretch of a river is tilted down its
    /// slope). Water from here is untreated until boiled. Wade in and it gets you wet (see Survival.Wading).
    /// </summary>
    public class WaterSource : MonoBehaviour, IInteractable
    {
        [SerializeField] string displayName = "Lake";
        [Tooltip("A river or stream: the water runs (it sounds of rushing water, not lapping).")]
        [SerializeField] bool flowing;
        [SerializeField] float fillMinutes = 2f;
        [SerializeField] float drinkLitres = 0.25f;

        static readonly List<WaterSource> all = new();

        public string DisplayName => displayName;
        public bool Flowing => flowing;
        public static IReadOnlyList<WaterSource> All => all;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        /// <summary>Height of the water's surface over a point (on the plane of this stretch).</summary>
        public float SurfaceAt(Vector3 point)
        {
            Vector3 up = transform.up;
            Vector3 origin = transform.position;
            if (up.y < 0.05f)
                return origin.y;
            // On the plane through the trigger's centre, square to its up.
            return origin.y - ((point.x - origin.x) * up.x + (point.z - origin.z) * up.z) / up.y;
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            var backpack = interactor.Backpack;
            float space = backpack.FreeWaterSpace;
            string treatment = backpack.HasWaterFilter ? "filtered" : "untreated";
            options.Add(new InteractionOption($"Fill water bottle (+{space:0.0} L {treatment})", () =>
                interactor.Activity.Begin("Filling water bottle", fillMinutes, () => backpack.FillFromSource()),
                backpack.WaterCapacity <= 0f ? "You have no water bottle" : space <= 0.01f ? "Bottle is full" : null));
            options.Add(new InteractionOption($"Drink straight from the water ({treatment})", () =>
                backpack.DrinkFromSource(drinkLitres)));
            // The rod lives in the pack, like the rest of the camp gear, unless it's in your hand (on the hotbar).
            PackHandling pack = PackHandling.Current;
            Vector3 spot = interactor.AimPoint;
            options.Add(new InteractionOption("Go fishing", () => interactor.Fishing.Begin(spot),
                !backpack.HasFishingKit ? "You need a fishing kit"
                : Player.Hotbar.HoldingRod ? null
                : pack != null && backpack.IsWorn ? "Hold your fishing kit (hotbar), or take your pack off to get it out"
                : pack != null && !pack.CanReachPack ? "Your fishing kit is in your pack, back there"
                : null));
        }
    }
}
