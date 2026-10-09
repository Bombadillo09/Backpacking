using System.Collections.Generic;
using Backpacking.Audio;
using Backpacking.Player;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Wildlife
{
    /// <summary>
    /// Keeps wildlife living around the player: rabbits in grassy clearings, deer in the woods and meadows,
    /// squirrels around the trees, small flocks of songbirds, and butterflies over meadows on warm, dry days.
    /// Animals appear out of sight a little way off, and are removed once far behind. Rabbits and deer are most
    /// active around dawn and dusk; squirrels, birds and butterflies by day.
    /// Leave a prefab slot empty to use a placeholder (or, for rabbits, the built-in model).
    /// <para>
    /// On a trip with friends, wildlife lives around each of them. The host's game runs the rabbits and deer (they're
    /// hunted, so everyone must see the same ones) and the guests' games show copies (<see cref="AnimalsFromHost"/>);
    /// squirrels, birds and butterflies are each game's own.
    /// </para>
    /// </summary>
    public class WildlifeSpawner : MonoBehaviour
    {
        [SerializeField] FirstPersonController player;
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] WeatherSystem weather;
        [SerializeField] AmbientTemperature temperature;

        [Header("Models (empty = built-in or placeholder)")]
        [Tooltip("Needs a child named Body; a Head pivot under it dips to graze. Empty builds a rabbit in code.")]
        [SerializeField] GameObject rabbitPrefab;
        [Tooltip("A plain lit material the built-in rabbit's fur is made from.")]
        [SerializeField] Material rabbitMaterial;
        [SerializeField] GameObject deerPrefab;
        [Tooltip("Animated songbirds (each with a Songbird component); a flock is all one kind.")]
        [SerializeField] GameObject[] songbirdPrefabs;
        [Tooltip("Used when there are no songbirds: needs a child named Body with Head, Wing L and Wing R pivots under it.")]
        [SerializeField] GameObject birdPrefab;
        [SerializeField] GameObject squirrelPrefab;
        [SerializeField] GameObject butterflyPrefab;

        [Header("Population")]
        [SerializeField] int maxRabbits = 18;
        [SerializeField] int maxDeer = 5;
        [SerializeField] int maxBirdFlocks = 7;
        [SerializeField] Vector2Int birdsPerFlock = new(2, 5);
        [Tooltip("Squirrels are detailed (furry) models, so only a few are about at once.")]
        [SerializeField] int maxSquirrels = 5;
        [SerializeField] int maxButterflies = 8;
        [Tooltip("Butterflies only fly at this temperature (°C) or warmer, by day, when it's dry.")]
        [SerializeField] float butterflyWarmth = 12f;
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

        enum Species { Rabbit, Deer, Birds, Squirrel, Butterfly }

        static WildlifeSpawner current;

        /// <summary>A co-op guest: the rabbits and deer come from the host's game, so this one makes none of its own.</summary>
        public static bool AnimalsFromHost { get; set; }

        public static WildlifeSpawner Current => current;

        readonly List<GameObject> rabbits = new();
        readonly List<GameObject> deerHerd = new();
        readonly List<GameObject> flocks = new();
        readonly List<GameObject> squirrels = new();
        readonly List<GameObject> butterflies = new();
        Transform container;
        float nextTickTime;
        // Whose surroundings get the next new animal: this player (0) or one of the others.
        int spawnTurn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            AnimalsFromHost = false;
        }

        void Awake() => current = this;

        void Start()
        {
            container = new GameObject("Wildlife").transform;
            // Fill up straight away, so the world isn't empty at the start. Nobody is watching yet.
            for (int i = 0; i < 40; i++)
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
            float day = Mathf.Clamp01(timeOfDay.Daylight * 1.5f - 0.3f);
            float rain = weather != null ? weather.RainIntensity : 0f;
            // Friends hiking apart each have wildlife around them.
            int groups = HikerGroups();

            Maintain(rabbits, AnimalsFromHost ? 0 : Mathf.RoundToInt(groups * maxRabbits * Mathf.Lerp(0.4f, 1f, Mathf.Max(twilight, night * 0.8f))), Species.Rabbit, initial);
            Maintain(deerHerd, AnimalsFromHost ? 0 : Mathf.RoundToInt(groups * maxDeer * Mathf.Lerp(0.4f, 1f, twilight)), Species.Deer, initial);
            // Birds and squirrels shelter in heavy rain.
            Maintain(flocks, Mathf.RoundToInt(maxBirdFlocks * day * Mathf.Lerp(1f, 0.3f, rain)), Species.Birds, initial);
            Maintain(squirrels, squirrelPrefab == null ? 0 : Mathf.RoundToInt(maxSquirrels * day * Mathf.Lerp(1f, 0.2f, rain)), Species.Squirrel, initial);

            bool butterflyWeather = timeOfDay.Daylight > 0.5f && rain < 0.05f
                                    && (temperature == null || temperature.GetTemperature(player.transform.position) >= butterflyWarmth);
            if (!butterflyWeather)
                foreach (GameObject butterfly in butterflies)
                    if (butterfly != null)
                        butterfly.GetComponent<Butterfly>().Leave();
            Maintain(butterflies, butterflyWeather && butterflyPrefab != null ? maxButterflies : 0, Species.Butterfly, initial);
        }

        /// <summary>How many separate groups the hikers are in (more than 150 m from each other), alone 1.</summary>
        int HikerGroups()
        {
            var leaders = new List<Vector3> { player.transform.position };
            foreach (OtherHiker other in OtherHikers.All)
            {
                bool alone = true;
                foreach (Vector3 leader in leaders)
                    if ((leader - other.position).sqrMagnitude < 150f * 150f)
                        alone = false;
                if (alone)
                    leaders.Add(other.position);
            }
            return leaders.Count;
        }

        /// <summary>The flat distance from the nearest hiker (this player or a friend).</summary>
        float DistanceToHikers(Vector3 position)
        {
            Vector3 offset = position - player.transform.position;
            offset.y = 0f;
            float nearest = offset.magnitude;
            foreach (OtherHiker other in OtherHikers.All)
            {
                offset = position - other.position;
                offset.y = 0f;
                nearest = Mathf.Min(nearest, offset.magnitude);
            }
            return nearest;
        }

        static float Bell(float hour, float centre, float width)
        {
            float d = Mathf.DeltaAngle(hour * 15f, centre * 15f) / 15f / width;
            return Mathf.Exp(-d * d);
        }

        /// <summary>How many live rabbits are within <paramref name="radius"/> metres (for snares).</summary>
        public static int RabbitsNear(Vector3 position, float radius)
        {
            int count = 0;
            foreach (Animal animal in Animal.All)
                if (animal.Kind == AnimalKind.Rabbit && !animal.IsDead && (animal.transform.position - position).sqrMagnitude < radius * radius)
                    count++;
            return count;
        }

        /// <summary>How good the ground here is for rabbits, 0 (none live here) to 1 (grassy meadow).</summary>
        public static float RabbitHabitat(Vector3 position) => HabitatPreference(Species.Rabbit, GroundCover.At(position));

        /// <summary>Removes far-off animals, then adds one if there are fewer than wanted.</summary>
        void Maintain(List<GameObject> group, int wanted, Species species, bool initial)
        {
            float limit = species switch { Species.Butterfly => 80f, Species.Squirrel => 120f, _ => despawnDistance };
            // The rabbits and deer near friends are the host's to keep; everything else lives around this player alone.
            bool shared = species is Species.Rabbit or Species.Deer;
            for (int i = group.Count - 1; i >= 0; i--)
            {
                GameObject animal = group[i];
                if (animal == null)
                {
                    group.RemoveAt(i);
                    continue;
                }
                Vector3 offset = animal.transform.position - player.transform.position;
                offset.y = 0f;
                float distance = shared ? DistanceToHikers(animal.transform.position) : offset.magnitude;
                // A wounded or dead animal stays put for tracking and butchering, unless the player's left the area.
                bool handedOver = shared && AnimalsFromHost;
                if (!handedOver && animal.TryGetComponent(out Animal hunted) && hunted.KeepAround && distance < 900f)
                    continue;
                if (distance > limit || handedOver)
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
            // Rabbits and deer take turns appearing around each hiker; the rest only around this player.
            bool shared = species is Species.Rabbit or Species.Deer;
            int turn = shared && OtherHikers.All.Count > 0 ? spawnTurn++ % (OtherHikers.All.Count + 1) : 0;
            Vector3 centre = turn == 0 ? player.transform.position : OtherHikers.All[turn - 1].position;
            Vector3 look = turn == 0 ? player.CameraPivot.forward : OtherHikers.All[turn - 1].forward;
            look.y = 0f;
            // Small animals appear closer: they'd be invisible at a deer's distance.
            Vector2 range = species switch
            {
                Species.Butterfly => new Vector2(15f, 45f),
                Species.Squirrel => new Vector2(25f, 80f),
                _ => spawnDistance,
            };

            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 direction = Random.insideUnitCircle.normalized;
                float distance = Random.Range(initial ? Mathf.Min(25f, range.x) : range.x, range.y);
                var candidate = new Vector3(centre.x + direction.x * distance, 0f, centre.z + direction.y * distance);

                // Out of view, unless filling the world at the start, and not right beside anyone else.
                if (!initial && Vector3.Dot(look.normalized, new Vector3(direction.x, 0f, direction.y)) > 0.3f)
                    continue;
                if (shared && !initial && DistanceToHikers(candidate) < range.x * 0.8f)
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
                // Squirrels live around the trees.
                if (species == Species.Squirrel)
                {
                    if (!TreeIndex.Nearest(candidate, 8f, out TreeIndex.Tree tree))
                        continue;
                    Vector2 near = Random.insideUnitCircle * 3f;
                    candidate = tree.position + new Vector3(near.x, 0f, near.y);
                    candidate.y = GroundCover.HeightAt(candidate);
                }

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
            Species.Squirrel => ground switch { Surface.Leaves => 1f, Surface.Soft => 0.6f, Surface.Hard => 0.3f, _ => 0f },
            Species.Butterfly => ground switch { Surface.Soft => 1f, Surface.Leaves => 0.15f, _ => 0f },
            _ => ground switch { Surface.Soft => 1f, Surface.Leaves => 0.6f, Surface.Hard => 0.3f, _ => 0.1f },
        };

        GameObject Spawn(Species species, Vector3 position)
        {
            switch (species)
            {
                case Species.Birds:
                    return SpawnFlock(position);
                case Species.Squirrel:
                    return SpawnSquirrel(position);
                case Species.Butterfly:
                    return SpawnButterfly(position);
            }

            bool isRabbit = species == Species.Rabbit;
            GameObject animal = isRabbit
                ? rabbitPrefab != null ? Instantiate(rabbitPrefab) : rabbitMaterial != null ? SmallAnimals.Rabbit(rabbitMaterial) : PlaceholderAnimals.Rabbit()
                : deerPrefab != null ? Instantiate(deerPrefab) : PlaceholderAnimals.Deer();
            animal.transform.SetParent(container, false);
            animal.transform.position = position;
            // A little size variety.
            float size = Random.Range(0.85f, 1.15f);
            animal.transform.localScale *= size;
            var created = animal.AddComponent<Animal>();
            created.Size = size;
            created.Initialise(isRabbit ? rabbit : deer, isRabbit ? AnimalKind.Rabbit : AnimalKind.Deer, player, timeOfDay,
                isRabbit ? null : Sounds.DeerAlarm());
            return animal;
        }

        /// <summary>A copy of a rabbit or deer the host's game runs, to show here.</summary>
        public Animal SpawnCopy(AnimalKind kind, float size, AnimalSnapshot snapshot)
        {
            bool isRabbit = kind == AnimalKind.Rabbit;
            GameObject animal = isRabbit
                ? rabbitPrefab != null ? Instantiate(rabbitPrefab) : rabbitMaterial != null ? SmallAnimals.Rabbit(rabbitMaterial) : PlaceholderAnimals.Rabbit()
                : deerPrefab != null ? Instantiate(deerPrefab) : PlaceholderAnimals.Deer();
            if (container == null)
                container = new GameObject("Wildlife").transform;
            animal.transform.SetParent(container, false);
            animal.transform.position = snapshot.position;
            animal.transform.localScale *= size;
            var copy = animal.AddComponent<Animal>();
            copy.Size = size;
            copy.InitialisePuppet(isRabbit ? rabbit : deer, kind, player, timeOfDay, isRabbit ? null : Sounds.DeerAlarm(), snapshot);
            return copy;
        }

        GameObject SpawnSquirrel(Vector3 position)
        {
            if (squirrelPrefab == null)
                return null;
            GameObject squirrel = Instantiate(squirrelPrefab, position, Quaternion.identity, container);
            squirrel.transform.localScale *= Random.Range(0.9f, 1.1f);
            squirrel.AddComponent<Squirrel>().Initialise(player);
            return squirrel;
        }

        GameObject SpawnButterfly(Vector3 position)
        {
            if (butterflyPrefab == null)
                return null;
            GameObject butterfly = Instantiate(butterflyPrefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), container);
            butterfly.transform.localScale *= Random.Range(0.8f, 1.2f);
            butterfly.AddComponent<Butterfly>().Initialise(player);
            return butterfly;
        }

        GameObject SpawnFlock(Vector3 position)
        {
            var flock = new GameObject("Bird Flock");
            flock.transform.SetParent(container, false);
            flock.transform.position = position;

            bool songbirds = songbirdPrefabs != null && songbirdPrefabs.Length > 0;
            GameObject kind = songbirds ? songbirdPrefabs[Random.Range(0, songbirdPrefabs.Length)] : birdPrefab;
            var members = new List<GameObject>();
            int count = Random.Range(birdsPerFlock.x, birdsPerFlock.y + 1);
            for (int i = 0; i < count; i++)
            {
                GameObject bird = kind != null ? Instantiate(kind) : PlaceholderAnimals.Bird();
                bird.transform.SetParent(flock.transform, false);
                Vector2 scatter = Random.insideUnitCircle * 1.8f;
                Vector3 at = position + new Vector3(scatter.x, 0f, scatter.y);
                at.y = GroundCover.HeightAt(at);
                bird.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                members.Add(bird);
            }
            flock.AddComponent<BirdFlock>().Initialise(player, timeOfDay, members, Sounds.Flutter());
            return flock;
        }
    }
}
