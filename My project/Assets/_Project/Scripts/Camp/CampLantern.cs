using System.Collections.Generic;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// A camp lantern set down on the ground (or a rock, a table): a warm pool of light round camp. Look at it to
    /// turn it off and on, or to pick it up and hang it back on your pack. The glass glows while it's lit.
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
            options.Add(new InteractionOption("Pick up the lantern", () =>
            {
                interactor.Backpack.LanternInPack = true;
                Destroy(gameObject);
            }, CampOwner.PackProblem(gameObject)));
        }
    }
}
