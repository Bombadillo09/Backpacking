using System.Collections.Generic;
using Backpacking.Audio;
using Backpacking.Player;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Wildlife
{
    /// <summary>
    /// Keeps wildlife living around the player: rabbits in grassy clearings, deer in the woods and meadows,
    /// and flocks of birds on the ground by day. Animals appear out of sight a little way off, and are
    /// removed once far behind. Rabbits and deer are most active around dawn and dusk.
    /// Leave a prefab slot empty to use a placeholder animal.
    /// </summary>
    public class WildlifeSpawner : MonoBehaviour
    {
        [SerializeField] FirstPersonController player;
        [SerializeField] TimeOfDay timeOfDay;

        [Header("Models (empty = placeholder)")]
        [Tooltip("Needs a child named Body; a Head pivot under it dips to graze.")]
        [SerializeField] GameObject rabbitPrefab;
        [SerializeField] GameObject deerPrefab;
        [Tooltip("Needs a child named Body with Head, Wing L and Wing R pivots under it.")]
        [SerializeField] GameObject birdPrefab;

        [Header("Population")]
        [SerializeField] int maxRabbits = 10;
        [SerializeField] int maxDeer = 5;
        [SerializeField] int maxBirdFlocks = 4;
        [SerializeField] Vector2Int birdsPerFlock = new(3, 7);
        [Tooltip("How far from the player new animals appear, in metres.")]
        [SerializeField] Vector2 spawnDistance = new(55f, 140f);
        [SerializeField] float despawnDistance = 210f;
        [Tooltip("Steepest ground (degrees) an animal will be placed on.")]
        [SerializeField] float maxSpawnSlope = 25f;

        [Header("Behaviour")]
        [SerializeField] AnimalProfile rabbit = new()
        {
            walkSpeed = 0.9f, runSpeed = 6.5f, alertDistance = 11f, calmDistance = 35f,
            hops = true, gaitHeight = 0.14f, strideLength = 0.45f, maxSlope = 38f,
            grazeSeconds = new Vector2(3f, 9f), wanderSeconds = new Vector2(1f, 3f), grazeHeadAngle = 35f,
        };
        [SerializeField] AnimalProfile deer = new()
        {
            walkSpeed = 1.2f, runSpeed = 9f, alertDistance = 28f, calmDistance = 85f,
            hops = false, gaitHeight = 0.05f, strideLength = 1.4f, maxSlope = 38f,
            grazeSeconds = new Vector2(6f, 16f), wanderSeconds = new Vector2(3f, 8f), grazeHeadAngle = 95f,
        };

        enum Species { Rabbit, Deer, Birds }

        readonly List<GameObject> rabbits = new();
        readonly List<GameObject> deerHerd = new();
        readonly List<GameObject> flocks = new();
        Transform container;
        float nextTickTime;

        void Start()
        {
            container = new GameObject("Wildlife").transform;
            // Fill up straight away, so the world isn't empty at the start. Nobody is watching yet.
            for (int i = 0; i < 30; i++)
                Tick(initial: true);
        }

        void Update()
        {
            if (Time.time < nextTickTime)
                return;
            nextTickTime = Time.time + 1f;
            Tick(initial: false);
        }

        void Tick(bool initial)
        {
            float hour = timeOfDay.Hour;
            // Most active around dawn and dusk.
            float twilight = Mathf.Max(Bell(hour, 6.5f, 1.5f), Bell(hour, 19f, 1.5f));
            float night = 1f - timeOfDay.Daylight;

            Maintain(rabbits, Mathf.RoundToInt(maxRabbits * Mathf.Lerp(0.4f, 1f, Mathf.Max(twilight, night * 0.8f))), Species.Rabbit, initial);
            Maintain(deerHerd, Mathf.RoundToInt(maxDeer * Mathf.Lerp(0.4f, 1f, twilight)), Species.Deer, initial);
            Maintain(flocks, Mathf.RoundToInt(maxBirdFlocks * Mathf.Clamp01(timeOfDay.Daylight * 1.5f - 0.3f)), Species.Birds, initial);
        }

        static float Bell(float hour, float centre, float width)
        {
            float d = Mathf.DeltaAngle(hour * 15f, centre * 15f) / 15f / width;
            return Mathf.Exp(-d * d);
        }

        /// <summary>Removes far-off animals, then adds one if there are fewer than wanted.</summary>
        void Maintain(List<GameObject> group, int wanted, Species species, bool initial)
        {
            Vector3 here = player.transform.position;
            for (int i = group.Count - 1; i >= 0; i--)
            {
                GameObject animal = group[i];
                if (animal == null)
                {
                    group.RemoveAt(i);
                    continue;
                }
                Vector3 offset = animal.transform.position - here;
                offset.y = 0f;
                if (offset.magnitude > despawnDistance)
                {
                    Destroy(animal);
                    group.RemoveAt(i);
                }
            }

            if (group.Count < wanted && TryFindSpot(species, initial, out Vector3 spot))
                group.Add(Spawn(species, spot));
        }

        bool TryFindSpot(Species species, bool initial, out Vector3 spot)
        {
            Terrain terrain = Terrain.activeTerrain;
            spot = default;
            if (terrain == null)
                return false;
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Transform view = player.CameraPivot;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 direction = Random.insideUnitCircle.normalized;
                float distance = Random.Range(initial ? 25f : spawnDistance.x, spawnDistance.y);
                var candidate = new Vector3(player.transform.position.x + direction.x * distance, 0f, player.transform.position.z + direction.y * distance);

                // Out of view, unless filling the world at the start.
                Vector3 look = view.forward;
                look.y = 0f;
                if (!initial && Vector3.Dot(look.normalized, new Vector3(direction.x, 0f, direction.y)) > 0.3f)
                    continue;

                float u = (candidate.x - origin.x) / data.size.x, v = (candidate.z - origin.z) / data.size.z;
                if (u < 0.02f || u > 0.98f || v < 0.02f || v > 0.98f)
                    continue;
                if (data.GetSteepness(u, v) > maxSpawnSlope)
                    continue;
                candidate.y = GroundCover.HeightAt(candidate);
                if (Animal.IsWater(candidate))
                    continue;
                if (Random.value > HabitatPreference(species, GroundCover.At(candidate)))
                    continue;

                spot = candidate;
                return true;
            }
            return false;
        }

        /// <summary>How happy a species is on this ground, 0 (never) to 1.</summary>
        static float HabitatPreference(Species species, Surface ground) => species switch
        {
            Species.Rabbit => ground switch { Surface.Soft => 1f, Surface.Leaves => 0.4f, Surface.Hard => 0.1f, _ => 0f },
            Species.Deer => ground switch { Surface.Leaves => 1f, Surface.Soft => 0.8f, Surface.Hard => 0.15f, _ => 0.05f },
            _ => ground switch { Surface.Soft => 1f, Surface.Leaves => 0.6f, Surface.Hard => 0.3f, _ => 0.1f },
        };

        GameObject Spawn(Species species, Vector3 position)
        {
            if (species == Species.Birds)
                return SpawnFlock(position);

            bool isRabbit = species == Species.Rabbit;
            GameObject animal = isRabbit
                ? rabbitPrefab != null ? Instantiate(rabbitPrefab) : PlaceholderAnimals.Rabbit()
                : deerPrefab != null ? Instantiate(deerPrefab) : PlaceholderAnimals.Deer();
            animal.transform.SetParent(container, false);
            animal.transform.position = position;
            // A little size variety.
            animal.transform.localScale *= Random.Range(0.85f, 1.15f);
            animal.AddComponent<Animal>().Initialise(isRabbit ? rabbit : deer, player, isRabbit ? null : SoundSynth.Snort());
            return animal;
        }

        GameObject SpawnFlock(Vector3 position)
        {
            var flock = new GameObject("Bird Flock");
            flock.transform.SetParent(container, false);
            flock.transform.position = position;

            var members = new List<GameObject>();
            int count = Random.Range(birdsPerFlock.x, birdsPerFlock.y + 1);
            for (int i = 0; i < count; i++)
            {
                GameObject bird = birdPrefab != null ? Instantiate(birdPrefab) : PlaceholderAnimals.Bird();
                bird.transform.SetParent(flock.transform, false);
                Vector2 scatter = Random.insideUnitCircle * 1.8f;
                Vector3 at = position + new Vector3(scatter.x, 0f, scatter.y);
                at.y = GroundCover.HeightAt(at);
                bird.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                members.Add(bird);
            }
            flock.AddComponent<BirdFlock>().Initialise(player, members, SoundSynth.Flutter());
            return flock;
        }
    }
}
