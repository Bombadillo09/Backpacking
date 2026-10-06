using System;
using Backpacking.Camp;
using Backpacking.Player;
using Backpacking.World;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Backpacking.Wildlife
{
    /// <summary>How one kind of animal moves and how skittish it is.</summary>
    [Serializable]
    public class AnimalProfile
    {
        public float walkSpeed = 1f;
        public float runSpeed = 7f;
        [Tooltip("Flees when the player comes this close (further if sprinting, much closer if crouching).")]
        public float alertDistance = 15f;
        [Tooltip("Stops fleeing once this far from the player.")]
        public float calmDistance = 45f;
        [Tooltip("Rabbits hop; deer just bob.")]
        public bool hops;
        [Tooltip("Height of each hop, or of the bob, in metres.")]
        public float gaitHeight = 0.05f;
        [Tooltip("Metres covered per hop or stride.")]
        public float strideLength = 1f;
        [Tooltip("Steepest ground it will climb, in degrees.")]
        public float maxSlope = 35f;
        [Tooltip("Seconds spent grazing, min and max.")]
        public Vector2 grazeSeconds = new(4f, 10f);
        [Tooltip("Seconds spent wandering between grazes, min and max.")]
        public Vector2 wanderSeconds = new(2f, 6f);
        [Tooltip("Degrees the head dips to graze.")]
        public float grazeHeadAngle = 70f;

        [Header("Animated models")]
        [Tooltip("Animator float: 0 standing, 1 moving. Leave empty if the model has no such parameter.")]
        public string moveParameter = "Vert";
        [Tooltip("Animator float: 0 walking, 1 running.")]
        public string runParameter = "State";
        [Tooltip("Ground speed (m/s) the walk animation was made for, so the legs keep pace.")]
        public float animationWalkSpeed = 1.2f;
        [Tooltip("Ground speed (m/s) the run animation was made for.")]
        public float animationRunSpeed = 7f;
    }

    /// <summary>
    /// A ground animal that grazes and wanders, and bolts when the player gets too close. Walk softly
    /// (crouch) to get near; sprinting scares it from further away. Follows the terrain and avoids
    /// cliffs and lakes.
    /// </summary>
    public class Animal : MonoBehaviour
    {
        enum State { Grazing, Wandering, Fleeing }

        AnimalProfile profile;
        FirstPersonController player;
        Transform body, head;
        Animator animator;
        int moveHash, runHash;
        AudioSource voice;
        AudioClip alarm;

        State state;
        float stateTimer;
        float heading;
        float speed;
        float gaitPhase;
        float headAngle;
        float noiseSeed;
        float nextProbeTime;
        bool pathBlocked;

        /// <summary>Called by the spawner straight after creating the animal.</summary>
        public void Initialise(AnimalProfile animalProfile, FirstPersonController watcher, AudioClip alarmSound)
        {
            profile = animalProfile;
            player = watcher;
            animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                moveHash = HasFloat(animator, profile.moveParameter) ? Animator.StringToHash(profile.moveParameter) : 0;
                runHash = HasFloat(animator, profile.runParameter) ? Animator.StringToHash(profile.runParameter) : 0;
                // Different animals shouldn't step in time.
                animator.Update(Random.Range(0f, 2f));
            }
            else
            {
                body = transform.Find("Body");
                head = body != null ? body.Find("Head") : null;
            }
            alarm = alarmSound;
            if (alarm != null)
            {
                voice = gameObject.AddComponent<AudioSource>();
                voice.spatialBlend = 1f;
                voice.minDistance = 6f;
                voice.maxDistance = 70f;
                voice.dopplerLevel = 0f;
                voice.playOnAwake = false;
            }
            heading = Random.Range(0f, 360f);
            noiseSeed = Random.Range(0f, 100f);
            transform.rotation = Quaternion.Euler(0f, heading, 0f);
            Enter(Random.value < 0.6f ? State.Grazing : State.Wandering);
        }

        void Update()
        {
            if (profile == null || Time.deltaTime <= 0f)
                return;

            Vector3 away = transform.position - player.transform.position;
            away.y = 0f;
            float distance = away.magnitude;

            float alert = profile.alertDistance
                          * (player.IsSprinting ? 1.6f : player.IsCrouching ? 0.45f : 1f)
                          * (player.HorizontalSpeed < 0.3f ? 0.7f : 1f);
            if (state != State.Fleeing && distance < alert)
            {
                Enter(State.Fleeing);
                if (voice != null)
                    voice.PlayOneShot(alarm, Random.Range(0.6f, 1f));
            }
            else if (state == State.Fleeing && distance > profile.calmDistance)
                Enter(State.Wandering);

            stateTimer -= Time.deltaTime;
            float targetSpeed = 0f;
            switch (state)
            {
                case State.Grazing:
                    if (stateTimer <= 0f)
                        Enter(State.Wandering);
                    break;
                case State.Wandering:
                    targetSpeed = profile.walkSpeed;
                    heading += (Mathf.PerlinNoise(Time.time * 0.3f, noiseSeed) - 0.5f) * 60f * Time.deltaTime;
                    if (stateTimer <= 0f)
                        Enter(State.Grazing);
                    break;
                case State.Fleeing:
                    targetSpeed = profile.runSpeed;
                    // Run directly away, with a little weave.
                    float awayHeading = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg + Mathf.Sin(Time.time * 2.3f) * 25f;
                    heading = Mathf.MoveTowardsAngle(heading, awayHeading, 360f * Time.deltaTime);
                    break;
            }

            speed = Mathf.MoveTowards(speed, targetSpeed, (state == State.Fleeing ? 25f : 3f) * Time.deltaTime);
            Move();
            Animate();
        }

        void Enter(State next)
        {
            state = next;
            stateTimer = next == State.Grazing
                ? Random.Range(profile.grazeSeconds.x, profile.grazeSeconds.y)
                : Random.Range(profile.wanderSeconds.x, profile.wanderSeconds.y);
            if (next == State.Wandering)
                heading += Random.Range(-90f, 90f);
        }

        void Move()
        {
            if (speed <= 0.01f)
                return;

            Vector3 direction = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            // Look a little way ahead now and then for lakes and cliffs, and turn away from them.
            if (Time.time >= nextProbeTime)
            {
                nextProbeTime = Time.time + 0.3f;
                pathBlocked = Blocked(transform.position + direction * Mathf.Max(2f, speed * 0.5f));
            }
            if (pathBlocked)
            {
                heading += (state == State.Fleeing ? 60f : 120f) * (Random.value < 0.5f ? -1f : 1f);
                nextProbeTime = 0f;
                return;
            }

            Vector3 next = transform.position + direction * (speed * Time.deltaTime);
            next.y = GroundCover.HeightAt(next);
            transform.position = next;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, heading, 0f), 240f * Time.deltaTime);
        }

        bool Blocked(Vector3 ahead)
        {
            float here = GroundCover.HeightAt(transform.position);
            float there = GroundCover.HeightAt(ahead);
            Vector3 flat = ahead - transform.position;
            flat.y = 0f;
            if (Mathf.Abs(there - here) / Mathf.Max(0.01f, flat.magnitude) > Mathf.Tan(profile.maxSlope * Mathf.Deg2Rad))
                return true;
            return IsWater(ahead);
        }

        /// <summary>Lakes are thin triggers at the water's surface.</summary>
        public static bool IsWater(Vector3 position)
        {
            Vector3 from = new(position.x, GroundCover.HeightAt(position) + 3f, position.z);
            return Physics.Raycast(from, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Collide)
                   && hit.collider.GetComponent<WaterSource>() != null;
        }

        static bool HasFloat(Animator animator, string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return false;
            foreach (AnimatorControllerParameter candidate in animator.parameters)
                if (candidate.name == parameter && candidate.type == AnimatorControllerParameterType.Float)
                    return true;
            return false;
        }

        void Animate()
        {
            if (animator != null)
            {
                AnimateModel();
                return;
            }
            if (body != null)
            {
                gaitPhase += speed * Time.deltaTime / Mathf.Max(0.05f, profile.strideLength) * Mathf.PI;
                float lift = profile.hops ? Mathf.Abs(Mathf.Sin(gaitPhase)) : Mathf.Sin(gaitPhase * 2f) * 0.5f + 0.5f;
                // Lift fades in with speed, so a standing animal rests on the ground.
                body.localPosition = new Vector3(0f, lift * profile.gaitHeight * Mathf.Clamp01(speed / Mathf.Max(0.1f, profile.walkSpeed)), 0f);
            }
            if (head != null)
            {
                float target = state == State.Grazing ? profile.grazeHeadAngle : state == State.Fleeing ? -10f : 0f;
                headAngle = Mathf.MoveTowards(headAngle, target, 120f * Time.deltaTime);
                head.localRotation = Quaternion.Euler(headAngle, 0f, 0f);
            }
        }

        /// <summary>Blends the model's idle, walk and run animations to match how fast it's going.</summary>
        void AnimateModel()
        {
            float moving = Mathf.Clamp01(speed / Mathf.Max(0.05f, profile.walkSpeed));
            float running = Mathf.InverseLerp(profile.walkSpeed, profile.runSpeed, speed);
            if (moveHash != 0)
                animator.SetFloat(moveHash, moving, 0.15f, Time.deltaTime);
            if (runHash != 0)
                animator.SetFloat(runHash, running, 0.15f, Time.deltaTime);

            // Speed the clip up or down so the hooves roughly match the ground.
            float animationSpeed = Mathf.Lerp(profile.animationWalkSpeed, profile.animationRunSpeed, running);
            animator.speed = speed < 0.05f ? 1f : Mathf.Clamp(speed / Mathf.Max(0.1f, animationSpeed), 0.5f, 1.8f);
        }
    }
}
