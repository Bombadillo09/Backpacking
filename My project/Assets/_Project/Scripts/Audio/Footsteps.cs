using System;
using Backpacking.Camp;
using Backpacking.Player;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Backpacking.Audio
{
    /// <summary>
    /// Footsteps that match the ground: grass and dirt, leaf litter, rock, snow, or splashing through water.
    /// Steps come further apart as you speed up, louder when sprinting and softer when crouched.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
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
        [Tooltip("Seconds in the air before landing makes a sound.")]
        [SerializeField] float landingAirTime = 0.3f;

        FirstPersonController player;
        AudioSource source;
        Terrain terrain;
        Surface[] layerSurfaces;
        float distanceSinceStep;
        float airTime;
        int lastVariant;

        void Awake()
        {
            player = GetComponent<FirstPersonController>();
            var go = new GameObject("Footsteps");
            go.transform.SetParent(transform, false);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        void Start()
        {
            terrain = Terrain.activeTerrain;
            if (terrain == null)
                return;
            TerrainLayer[] layers = terrain.terrainData.terrainLayers;
            layerSurfaces = new Surface[layers.Length];
            for (int i = 0; i < layers.Length; i++)
                layerSurfaces[i] = Classify(layers[i] != null ? layers[i].name : "");
        }

        /// <summary>Guesses how a ground layer sounds underfoot from its name.</summary>
        static Surface Classify(string layerName)
        {
            string lower = layerName.ToLowerInvariant();
            if (lower.Contains("rock") || lower.Contains("stone") || lower.Contains("gravel"))
                return Surface.Hard;
            if (lower.Contains("snow"))
                return Surface.Snow;
            if (lower.Contains("grass") || lower.Contains("meadow") || lower.Contains("dirt"))
                return Surface.Soft;
            if (lower.Contains("leaf") || lower.Contains("leaves") || lower.Contains("litter") || lower.Contains("forest") || lower.Contains("forrest"))
                return Surface.Leaves;
            return Surface.Soft;
        }

        void Update()
        {
            if (!player.IsGrounded)
            {
                airTime += Time.deltaTime;
                return;
            }

            if (airTime > landingAirTime)
            {
                Step(1f);
                distanceSinceStep = 0f;
            }
            airTime = 0f;

            float speed = player.HorizontalSpeed;
            if (speed < 0.3f)
            {
                // Half a step's credit, so the first step after stopping comes quickly.
                distanceSinceStep = Mathf.Min(distanceSinceStep, 0.5f);
                return;
            }

            distanceSinceStep += speed * Time.deltaTime;
            float stride = Mathf.Clamp(0.45f + 0.23f * speed, 0.6f, 1.5f);
            if (distanceSinceStep >= stride)
            {
                distanceSinceStep -= stride;
                Step(player.IsCrouching ? crouchVolume : player.IsSprinting ? sprintVolume : walkVolume);
            }
        }

        void Step(float volume)
        {
            Surface surface = SurfaceUnderfoot();
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

            return terrain != null && layerSurfaces != null ? TerrainSurface(feet) : Surface.Soft;
        }

        Surface TerrainSurface(Vector3 position)
        {
            TerrainData data = terrain.terrainData;
            Vector3 local = position - terrain.transform.position;
            int x = Mathf.Clamp((int)(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp((int)(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            float[,,] weights = data.GetAlphamaps(x, z, 1, 1);

            int strongest = 0;
            for (int layer = 1; layer < weights.GetLength(2); layer++)
                if (weights[0, 0, layer] > weights[0, 0, strongest])
                    strongest = layer;
            return strongest < layerSurfaces.Length ? layerSurfaces[strongest] : Surface.Soft;
        }
    }
}
