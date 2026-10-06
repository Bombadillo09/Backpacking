using System;
using Backpacking.Camp;
using Backpacking.Player;
using Backpacking.World;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Backpacking.Audio
{
    /// <summary>
    /// Footsteps that match the ground: grass and dirt, leaf litter, rock, snow, or splashing through water.
    /// Steps come with the head bob's stride, louder when sprinting or carrying a heavy pack and softer when crouched.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController), typeof(HeadBob))]
    public class Footsteps : MonoBehaviour
    {
        [Serializable]
        public class SurfaceClips
        {
            public Surface surface;
            [Tooltip("Leave empty to use generated placeholder steps.")]
            public AudioClip[] clips;
        }

        [SerializeField] SurfaceClips[] clipOverrides;

        [Header("Levels")]
        [SerializeField, Range(0f, 1f)] float walkVolume = 0.4f;
        [SerializeField, Range(0f, 1f)] float sprintVolume = 0.65f;
        [SerializeField, Range(0f, 1f)] float crouchVolume = 0.15f;
        [Tooltip("Extra loudness with a full pack: heavier footfalls.")]
        [SerializeField, Range(0f, 1f)] float loadedExtra = 0.4f;

        FirstPersonController player;
        HeadBob bob;
        AudioSource source;
        int lastVariant;

        void Awake()
        {
            player = GetComponent<FirstPersonController>();
            bob = GetComponent<HeadBob>();
            var go = new GameObject("Footsteps");
            go.transform.SetParent(transform, false);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        // Steps follow the head bob's stride, so each footfall sounds as the view dips.
        void OnEnable()
        {
            if (bob == null)
                return;
            bob.Stepped += OnStepped;
            bob.Landed += OnLanded;
        }

        void OnDisable()
        {
            if (bob == null)
                return;
            bob.Stepped -= OnStepped;
            bob.Landed -= OnLanded;
        }

        void OnStepped(float hardness)
        {
            float volume = player.IsCrouching ? crouchVolume : player.IsSprinting ? sprintVolume : walkVolume;
            Step(volume * (1f + loadedExtra * player.LoadFactor));
        }

        void OnLanded(float fallSpeed) => Step(Mathf.Clamp01(0.5f + fallSpeed * 0.08f));

        void Step(float volume)
        {
            Surface surface = SurfaceUnderfoot();
            // Pushing through brush: crunching leaves and snapping twigs, louder the thicker it is.
            float brush = Camp.Undergrowth.Density;
            if (brush > 0.3f && surface != Surface.Water)
            {
                surface = Surface.Leaves;
                volume *= 1f + brush;
            }
            AudioClip clip = PickClip(surface);
            source.pitch = Random.Range(0.92f, 1.08f);
            source.PlayOneShot(clip, volume * Random.Range(0.85f, 1f));
        }

        AudioClip PickClip(Surface surface)
        {
            if (clipOverrides != null)
                foreach (SurfaceClips entry in clipOverrides)
                    if (entry.surface == surface && entry.clips != null && entry.clips.Length > 0)
                        return entry.clips[Random.Range(0, entry.clips.Length)];

            // Never the same variant twice in a row.
            int variant = Random.Range(0, SoundSynth.FootstepVariants - 1);
            if (variant >= lastVariant)
                variant++;
            lastVariant = variant;
            return SoundSynth.Footstep(surface, variant);
        }

        Surface SurfaceUnderfoot()
        {
            Vector3 feet = transform.position;
            // Wading: lakes are thin triggers at the water surface.
            foreach (Collider hit in Physics.OverlapSphere(feet + Vector3.up * 0.3f, 0.4f, ~0, QueryTriggerInteraction.Collide))
                if (hit.GetComponent<WaterSource>() != null)
                    return Surface.Water;

            // Standing on a log, boulder or building rather than the ground.
            if (Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out RaycastHit ground, 0.8f, ~0, QueryTriggerInteraction.Ignore)
                && ground.collider is not TerrainCollider)
                return Surface.Hard;

            return GroundCover.At(feet);
        }
    }
}
