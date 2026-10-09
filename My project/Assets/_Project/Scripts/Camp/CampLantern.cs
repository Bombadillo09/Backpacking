using System.Collections.Generic;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// A camp lantern set down on the ground (or a rock, a table): a warm pool of light round camp. Look at it to
    /// turn it off and on, or to pick it up and hang it back on your pack. The glass glows while it's lit. A friend's
    /// lantern (co-op) can be borrowed: it goes on your pack, and they're told.
    /// </summary>
    public class CampLantern : MonoBehaviour, IInteractable
    {
        [SerializeField] Light lamp;
        [Tooltip("The glass, lit up while the lantern's on.")]
        [SerializeField] Renderer globe;
        [SerializeField] Color glow = new(1f, 0.78f, 0.45f);

        bool on = true;
        float baseIntensity;
        MaterialPropertyBlock block;

        public string DisplayName => "Camp lantern";

        /// <summary>A friend's lantern was borrowed in this game: (its owner's key). Co-op tells them.</summary>
        public static event System.Action<string> Borrowed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Borrowed = null;

        public bool IsOn => on;

        void Awake()
        {
            baseIntensity = lamp != null ? lamp.intensity : 1f;
            block = new MaterialPropertyBlock();
            SetOn(on);
        }

        public void SetOn(bool lit)
        {
            on = lit;
            if (lamp != null)
                lamp.enabled = lit;
            if (globe != null)
            {
                block.SetColor("_EmissionColor", lit ? glow * 3f : Color.black);
                block.SetColor("_BaseColor", lit ? new Color(1f, 0.92f, 0.75f, 1f) : new Color(0.75f, 0.78f, 0.78f, 1f));
                globe.SetPropertyBlock(block);
            }
        }

        void Update()
        {
            // A faint flutter, like a mantle burning.
            if (on && lamp != null)
                lamp.intensity = baseIntensity * (0.96f + 0.04f * Mathf.PerlinNoise(Time.time * 5f, transform.position.x));
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            options.Add(new InteractionOption(on ? "Turn the lantern off" : "Turn the lantern on", () => SetOn(!on)));
            if (CampOwner.IsOthers(gameObject, out string ownerName))
            {
                string ownerKey = GetComponent<CampOwner>().key;
                options.Add(new InteractionOption($"Borrow {ownerName}'s lantern", () =>
                {
                    interactor.Backpack.AddLantern();
                    Borrowed?.Invoke(ownerKey);
                    Destroy(gameObject);
                }, interactor.Backpack.HasLantern ? "You have a lantern of your own" : null));
                return;
            }
            options.Add(new InteractionOption("Pick up the lantern", () =>
            {
                interactor.Backpack.LanternInPack = true;
                Destroy(gameObject);
            }));
        }
    }
}
