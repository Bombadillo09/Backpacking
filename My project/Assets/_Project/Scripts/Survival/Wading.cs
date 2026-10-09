using Backpacking.Camp;
using Backpacking.Player;
using Backpacking.UI;
using UnityEngine;

namespace Backpacking.Survival
{
    /// <summary>
    /// Walking into a lake, river or stream: the deeper it is, the slower you go and the wetter you get (over the
    /// boot tops, even waterproof boots fill). Waist-deep in a river, the sleeping bag in your pack starts to get wet too.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    public class Wading : MonoBehaviour
    {
        [SerializeField] Vitals vitals;
        [SerializeField] Backpack backpack;
        [Tooltip("Speed at waist depth (1 m), compared with dry ground.")]
        [SerializeField, Range(0.1f, 1f)] float waistDeepSpeed = 0.45f;

        static readonly Collider[] touching = new Collider[16];

        FirstPersonController player;
        bool warnedBoots, warnedDeep;

        /// <summary>How deep the water is where you stand, in metres (0 on dry land).</summary>
        public float Depth { get; private set; }

        void Awake()
        {
            player = GetComponent<FirstPersonController>();
            if (vitals == null)
                vitals = GetComponent<Vitals>();
            if (backpack == null)
                backpack = GetComponent<Backpack>();
        }

        void Update()
        {
            Depth = player.Mounted ? 0f : DepthAt(transform.position);
            player.WaterSpeedMultiplier = Mathf.Lerp(1f, waistDeepSpeed, Mathf.Clamp01(Depth / 1f));
            if (Depth <= 0.03f || vitals == null)
                return;

            vitals.Wade(Depth, Time.deltaTime);
            if (!warnedBoots && vitals.BootsFlooded > 0.3f)
            {
                warnedBoots = true;
                Notifications.Post("Cold water floods your boots. Wet feet blister and get infected: dry them by a fire, or sit with your boots off.", 6f);
            }
            if (Depth > 0.85f && backpack != null && backpack.IsWorn && backpack.HasSleepingBag && !backpack.BagLaidOut)
            {
                backpack.SoakBag(Time.deltaTime * 0.05f);
                if (!warnedDeep)
                {
                    warnedDeep = true;
                    Notifications.Post("You're waist-deep: the bottom of your pack is in the water, and your sleeping bag with it.", 5f);
                }
            }
        }

        /// <summary>How deep the water is over the ground at <paramref name="feet"/>, or 0 if there's none.</summary>
        public static float DepthAt(Vector3 feet)
        {
            int count = Physics.OverlapSphereNonAlloc(feet + Vector3.up * 0.6f, 0.75f, touching, ~0, QueryTriggerInteraction.Collide);
            float deepest = 0f;
            for (int i = 0; i < count; i++)
                if (touching[i].TryGetComponent(out WaterSource water))
                    deepest = Mathf.Max(deepest, water.SurfaceAt(feet) - feet.y);
            return deepest;
        }
    }
}
