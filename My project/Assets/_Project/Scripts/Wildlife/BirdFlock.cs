using System.Collections.Generic;
using Backpacking.Player;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Wildlife
{
    /// <summary>
    /// A few birds of one kind going about their day: hopping and pecking on the ground, then flying up together
    /// to perch in a nearby tree, where they preen and sing (most at dawn), and back down again later. Come too
    /// close and birds on the ground flush up into the trees; perched birds only scatter if you're right under them.
    /// </summary>
    public class BirdFlock : MonoBehaviour
    {
        [Tooltip("Birds on the ground flush when the player comes this close (further if sprinting, closer if crouching).")]
        [SerializeField] float alertDistance = 12f;
        [Tooltip("Perched birds only scatter when the player is this close.")]
        [SerializeField] float perchedAlertDistance = 5f;
        [SerializeField] float flightSpeed = 8f;
        [Tooltip("Seconds the flock stays in one place before moving on, min and max.")]
        [SerializeField] Vector2 staySeconds = new(20f, 70f);

        enum State { Ground, Flying, Perched }

        static readonly int FlyingHash = Animator.StringToHash("flying");
        static readonly int LandingHash = Animator.StringToHash("landing");
        static readonly int DirectionHash = Animator.StringToHash("flyingDirectionX");
        static readonly int HopHash = Animator.StringToHash("hop");
        static readonly int AgitatedHash = Animator.StringToHash("IdleAgitated");
        static readonly int[] Gestures = { Animator.StringToHash("peck"), Animator.StringToHash("ruffle"), Animator.StringToHash("preen") };
        static readonly int SingHash = Animator.StringToHash("sing");

        class Bird
        {
            public Transform root, head, leftWing, rightWing;
            public Songbird songbird;
            public Animator animator;
            public State state;
            public Vector3 from, control, to;
            public float flightTime, flightDuration, landingTime;
            public float nextGesture, phase;
        }

        readonly List<Bird> birds = new();
        FirstPersonController player;
        TimeOfDay timeOfDay;
        AudioSource voice;
        AudioClip flutter;
        float moveOnTime;
        bool perched;

        public bool IsFlying { get; private set; }

        /// <summary>Called by the spawner with the birds it made, already placed on the ground around the flock's position.</summary>
        public void Initialise(FirstPersonController watcher, TimeOfDay clock, IEnumerable<GameObject> members, AudioClip takeOffSound)
        {
            player = watcher;
            timeOfDay = clock;
            flutter = takeOffSound;
            foreach (GameObject member in members)
            {
                Transform body = member.transform.Find("Body");
                var bird = new Bird
                {
                    root = member.transform,
                    songbird = member.GetComponent<Songbird>(),
                    animator = member.GetComponent<Animator>(),
                    head = body != null ? body.Find("Head") : null,
                    leftWing = body != null ? body.Find("Wing L") : null,
                    rightWing = body != null ? body.Find("Wing R") : null,
                    state = State.Ground,
                    phase = Random.Range(0f, 10f),
                    nextGesture = Time.time + Random.Range(0.5f, 3f),
                };
                if (bird.animator != null)
                {
                    bird.animator.applyRootMotion = true;
                    bird.animator.SetFloat(AgitatedHash, Random.value);
                    // Not all in step.
                    bird.animator.Update(Random.Range(0f, 1.5f));
                }
                birds.Add(bird);
            }
            voice = gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 1f;
            voice.minDistance = 5f;
            voice.maxDistance = 60f;
            voice.dopplerLevel = 0f;
            voice.playOnAwake = false;
            moveOnTime = Time.time + Random.Range(staySeconds.x, staySeconds.y);
        }

        void Update()
        {
            if (player == null || Time.deltaTime <= 0f)
                return;

            Vector3 offset = Centre() - player.transform.position;
            offset.y = 0f;
            float caution = player.IsSprinting ? 1.8f : player.IsCrouching ? 0.5f : 1f;
            if (!IsFlying && offset.magnitude < (perched ? perchedAlertDistance : alertDistance) * caution)
                // Flushed: up into the trees away from the player (or, from a tree, off to another).
                FlyTo(Perch(offset.normalized), offset.normalized, startled: true);
            else if (!IsFlying && Time.time > moveOnTime)
                MoveOn();

            bool anyFlying = false;
            foreach (Bird bird in birds)
            {
                switch (bird.state)
                {
                    case State.Flying:
                        anyFlying = true;
                        Fly(bird);
                        break;
                    default:
                        Idle(bird);
                        break;
                }
            }
            IsFlying = anyFlying;
        }

        void LateUpdate()
        {
            // Hops move birds on the ground by their animation; keep their feet on it.
            foreach (Bird bird in birds)
                if (bird.state == State.Ground && bird.animator != null)
                {
                    Vector3 position = bird.root.position;
                    position.y = GroundCover.HeightAt(position);
                    bird.root.position = position;
                }
        }

        Vector3 Centre()
        {
            Vector3 sum = Vector3.zero;
            foreach (Bird bird in birds)
                sum += bird.root.position;
            return birds.Count > 0 ? sum / birds.Count : transform.position;
        }

        // ---------- Deciding where to go ----------

        /// <summary>Time to move: from the ground up into a tree, or from a tree down to feed (or to another tree).</summary>
        void MoveOn()
        {
            Vector3 wander = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            if (!perched || Random.value < 0.3f)
            {
                FlyTo(Perch(wander), wander, startled: false);
                return;
            }
            // Down to the ground somewhere nearby that isn't water or too close to the player.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector3 spot = Centre() + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(6f, 20f);
                spot.y = GroundCover.HeightAt(spot);
                if (Animal.IsWater(spot) || (spot - player.transform.position).sqrMagnitude < alertDistance * alertDistance * 2f)
                    continue;
                FlyTo(new Target { position = spot, ground = true }, wander, startled: false);
                return;
            }
            moveOnTime = Time.time + 10f;
        }

        struct Target
        {
            public Vector3 position;
            public float spread;
            public TreeIndex.Tree tree;
            public bool ground;
        }

        /// <summary>A tree to perch in, preferably in <paramref name="direction"/>; or, with no trees about, far away.</summary>
        Target Perch(Vector3 direction)
        {
            Vector3 centre = Centre();
            Vector3 ahead = centre + direction * 18f;
            if (TreeIndex.Random(ahead, 0f, 16f, out TreeIndex.Tree tree) || TreeIndex.Random(centre, 4f, 35f, out tree))
                return new Target { position = tree.position, tree = tree, spread = 1f };
            // Open country: off over the horizon.
            Vector3 away = centre + direction * 120f;
            away.y = GroundCover.HeightAt(away) + 25f;
            return new Target { position = away };
        }

        void FlyTo(Target target, Vector3 direction, bool startled)
        {
            perched = target.spread > 0f;
            moveOnTime = Time.time + Random.Range(staySeconds.x, staySeconds.y);
            if (startled && flutter != null)
                voice.PlayOneShot(flutter, 0.9f);
            foreach (Bird bird in birds)
            {
                Vector3 to;
                if (target.spread > 0f)
                {
                    // Out along the branches, in the lower half of the crown.
                    Vector3 around = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                    to = target.tree.position + around * (target.tree.trunkRadius + Random.Range(0.4f, 1.8f))
                         + Vector3.up * target.tree.height * Random.Range(0.35f, 0.6f);
                }
                else if (target.ground)
                {
                    Vector2 scatter = Random.insideUnitCircle * 1.8f;
                    to = target.position + new Vector3(scatter.x, 0f, scatter.y);
                    to.y = GroundCover.HeightAt(to);
                }
                else
                    to = target.position + Random.insideUnitSphere * 4f;

                bird.from = bird.root.position;
                bird.to = to;
                float distance = Vector3.Distance(bird.from, to);
                // A climbing arc: steeply up off the ground, and swooping in to land.
                bird.control = Vector3.Lerp(bird.from, to, 0.5f) + Vector3.up * (distance * 0.2f + 2f) + direction * Random.Range(0f, 2f);
                bird.flightDuration = Mathf.Max(1f, distance / (flightSpeed * Random.Range(0.85f, 1.15f)));
                // The flock doesn't leave as one: a ripple of take-offs.
                bird.flightTime = -Random.Range(0f, startled ? 0.25f : 1.2f);
                bird.state = State.Flying;
                if (bird.animator != null)
                {
                    bird.animator.applyRootMotion = false;
                    bird.animator.SetInteger(HopHash, 0);
                    bird.animator.SetBool(LandingHash, false);
                }
            }
            IsFlying = true;
        }

        // ---------- Moving ----------

        void Fly(Bird bird)
        {
            bool justLeft = bird.flightTime < 0f && bird.flightTime + Time.deltaTime >= 0f;
            bird.flightTime += Time.deltaTime;
            if (bird.flightTime < 0f)
                return;
            if (justLeft)
            {
                bird.songbird?.PlayTakeOff();
                bird.animator?.SetBool(FlyingHash, true);
            }

            float t = Mathf.Clamp01(bird.flightTime / bird.flightDuration);
            // Quick off the mark, slowing into the landing.
            float eased = Mathf.Lerp(t, 1f - (1f - t) * (1f - t), 0.5f);
            Vector3 position = Bezier(bird, eased);
            Vector3 tangent = Bezier(bird, Mathf.Min(1f, eased + 0.02f)) - position;
            if (tangent.sqrMagnitude > 1e-6f)
            {
                Vector3 flat = new(tangent.x, 0f, tangent.z);
                Quaternion facing = flat.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(flat) : bird.root.rotation;
                float turn = Vector3.SignedAngle(bird.root.forward, flat, Vector3.up);
                bird.root.SetPositionAndRotation(position, Quaternion.RotateTowards(bird.root.rotation, facing, 360f * Time.deltaTime));
                if (bird.animator != null)
                    bird.animator.SetFloat(DirectionHash, Mathf.Clamp(turn / 20f, -1f, 1f), 0.2f, Time.deltaTime);
            }
            else
                bird.root.position = position;
            FlapPlaceholder(bird, 14f);

            if (t >= 0.9f && bird.animator != null)
            {
                bird.animator.SetBool(FlyingHash, false);
                bird.animator.SetBool(LandingHash, true);
            }
            if (t >= 1f)
                Land(bird);
        }

        static Vector3 Bezier(Bird bird, float t) =>
            Vector3.Lerp(Vector3.Lerp(bird.from, bird.control, t), Vector3.Lerp(bird.control, bird.to, t), t);

        void Land(Bird bird)
        {
            bool onGround = Mathf.Abs(bird.to.y - GroundCover.HeightAt(bird.to)) < 0.2f;
            // Flown off over the horizon: gone.
            if (!onGround && !perched)
            {
                bird.root.gameObject.SetActive(false);
                if (birds.TrueForAll(other => !other.root.gameObject.activeSelf))
                    Destroy(gameObject);
                return;
            }
            bird.state = onGround ? State.Ground : State.Perched;
            bird.root.position = bird.to;
            bird.root.rotation = Quaternion.Euler(0f, bird.root.eulerAngles.y, 0f);
            bird.landingTime = Time.time;
            bird.nextGesture = Time.time + Random.Range(1f, 3f);
            if (bird.animator != null)
            {
                bird.animator.SetFloat(DirectionHash, 0f);
                // Only birds on the ground hop about (by their animation); perched ones stay put.
                bird.animator.applyRootMotion = onGround;
            }
        }

        /// <summary>On the ground or perched: pecking, preening, ruffling, hopping about and, up in the trees, singing.</summary>
        void Idle(Bird bird)
        {
            if (bird.animator != null && Time.time - bird.landingTime > 0.5f)
                bird.animator.SetBool(LandingHash, false);
            if (bird.animator == null)
            {
                IdlePlaceholder(bird);
                return;
            }
            if (Time.time < bird.nextGesture)
                return;
            bird.nextGesture = Time.time + Random.Range(1.5f, 4.5f);
            Animator animator = bird.animator;
            float roll = Random.value;
            // Birds sing most in the dawn chorus, and from perches.
            float hour = timeOfDay != null ? timeOfDay.Hour : 12f;
            float singing = (bird.state == State.Perched ? 0.35f : 0.08f) * (hour > 4.5f && hour < 9f ? 2f : hour > 20.5f || hour < 4.5f ? 0f : 1f);
            if (roll < singing)
                animator.SetTrigger(SingHash);
            else if (bird.state == State.Ground && roll < 0.75f)
                animator.SetInteger(HopHash, Random.value < 0.6f ? 1 : Random.value < 0.5f ? 2 : -2);
            else
                // Perched birds peck and preen but don't ruffle (the animation crouches them off the branch).
                animator.SetTrigger(bird.state == State.Ground ? Gestures[Random.Range(0, 3)] : Gestures[Random.value < 0.5f ? 0 : 2]);
            animator.SetFloat(AgitatedHash, Random.value);
        }

        // ---------- Placeholder birds (no model) ----------

        void IdlePlaceholder(Bird bird)
        {
            bird.phase += Time.deltaTime;
            FlapPlaceholder(bird, 0f);
            if (bird.head != null)
                bird.head.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(bird.phase * 5f)) * 55f, 0f, 0f);
            if (bird.state != State.Ground || Time.time < bird.nextGesture)
                return;
            bird.nextGesture = Time.time + Random.Range(1f, 4f);
            bird.root.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            Vector3 hop = bird.root.position + bird.root.forward * Random.Range(0.1f, 0.35f);
            hop.y = GroundCover.HeightAt(hop);
            bird.root.position = hop;
        }

        static void FlapPlaceholder(Bird bird, float beatsPerSecond)
        {
            if (bird.animator != null || bird.leftWing == null)
                return;
            bird.phase += Time.deltaTime * beatsPerSecond * Mathf.PI * 2f;
            float flap = beatsPerSecond > 0f ? Mathf.Sin(bird.phase) * 60f : 0f;
            bird.leftWing.localRotation = Quaternion.Euler(0f, 0f, flap);
            bird.rightWing.localRotation = Quaternion.Euler(0f, 0f, -flap);
        }
    }
}
