using System;
using System.Collections.Generic;
using Backpacking.Camp;
using Backpacking.Hunting;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Trip;
using Backpacking.UI;
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

    public enum AnimalKind
    {
        Rabbit,
        Deer,
    }

    /// <summary>Something a player did to a carcass, for the game that runs the animal to know (see <see cref="Animal.RemoteAction"/>).</summary>
    public enum CarcassAction
    {
        Taken,
        FieldDressed,
        Meat,
        Hide,
    }

    /// <summary>All of an animal another game needs to show it: where it is and what it's doing, and what's left of its carcass.</summary>
    public struct AnimalSnapshot
    {
        public Vector3 position;
        public float heading;
        public float speed;
        public byte state;
        public byte wound;
        public bool watching;
        public bool fieldDressed;
        public bool hideTaken;
        public byte meatLeft;
        public byte arrowsIn;
        public float deadAtHour;
    }

    /// <summary>
    /// A ground animal that grazes and wanders, and bolts when the player gets too close. A little further off it
    /// freezes and watches (a rabbit sits up, ears pricked). Walk softly (crouch) to get near; sprinting scares it
    /// from further away. Follows the terrain and avoids cliffs and lakes.
    /// <para>
    /// It can be hunted with the bow. A rabbit hit anywhere drops. A deer shot through the heart and lungs (just behind
    /// the shoulder) runs a short way and falls; hit in the head or neck it drops on the spot. Hit too far back, or in
    /// a leg, it runs off wounded, leaving a trail of blood, lies down once it feels safe and dies there a while later;
    /// walk up on it too soon and it gets up and runs again. A dead animal can be butchered (see GetOptions).
    /// </para>
    /// </summary>
    public class Animal : MonoBehaviour, IInteractable
    {
        enum State : byte { Grazing, Wandering, Fleeing, Bedded, Dead }

        enum Wound : byte { None, Vitals, Gut, Leg }

        // Game hours a carcass lies before it's gone (scavengers, rot), and before its meat has spoiled.
        const float CarcassHours = 48f;
        const float MeatHours = 20f;
        const float FieldDressMinutes = 40f;
        const int DeerMeatKilograms = 14;

        static readonly List<Animal> all = new();
        // While a friend's shot is judged here, what it says goes back to them instead of onto this screen.
        static bool reporting;
        static string report;

        AnimalProfile profile;
        AnimalKind kind;
        FirstPersonController player;
        TimeOfDay timeOfDay;
        Transform body, head;
        SmallAnimalRig rig;
        Animator animator;
        int moveHash, runHash;
        AudioSource voice;
        AudioClip alarm;
        BoxCollider hitbox;

        State state;
        float stateTimer;
        float heading;
        float speed;
        float gaitPhase;
        float headAngle;
        float noiseSeed;
        float nextProbeTime;
        bool pathBlocked;
        bool watching;

        // Hunting.
        Wound wound;
        float woundedAtHour;
        float bleedOutHours;
        float runLeft;
        float nextBloodAt;
        Vector3 lastPosition;
        float deadAtHour;
        float fall;
        Quaternion standing;
        int arrowsIn;
        bool fieldDressed;
        int meatLeft;
        bool hideTaken;

        public static IReadOnlyList<Animal> All => all;

        /// <summary>A new animal living in this game (not a copy of one in another game).</summary>
        public static event Action<Animal> Spawned;
        /// <summary>An animal living in this game is gone (left behind, taken, rotted away).</summary>
        public static event Action<Animal> Removed;
        /// <summary>Something startled the animals at a point (see <see cref="StartleNear"/>), for co-op to pass on.</summary>
        public static Action<Vector3, float> Startled;
        /// <summary>An arrow hit a copy of another game's animal: where on it (in its own space) and which way, to be judged there.</summary>
        public static Action<Animal, Vector3, Vector3> RemoteHit;
        /// <summary>A player did something to a copy of another game's carcass.</summary>
        public static Action<Animal, CarcassAction, int> RemoteAction;

        /// <summary>A copy of an animal another game runs (co-op): it goes where that game says and does nothing by itself.</summary>
        public bool Puppet { get; private set; }
        /// <summary>Co-op's number for it, the same in every game.</summary>
        public int NetId { get; set; }
        /// <summary>How much bigger or smaller than usual this one is.</summary>
        public float Size { get; set; } = 1f;
        public AnimalKind Kind => kind;
        public bool IsDead => state == State.Dead;
        /// <summary>Wounded or dead: kept in the world even when the player's far off, so it can be tracked and found.</summary>
        public bool KeepAround => state == State.Dead || wound != Wound.None;

        public string DisplayName => kind == AnimalKind.Deer ? fieldDressed ? "Deer carcass" : "Dead deer" : "Dead rabbit";

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        void OnDestroy()
        {
            if (!Puppet && profile != null)
                Removed?.Invoke(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            all.Clear();
            Spawned = Removed = null;
            Startled = null;
            RemoteHit = null;
            RemoteAction = null;
        }

        /// <summary>Called by the spawner straight after creating the animal.</summary>
        public void Initialise(AnimalProfile animalProfile, AnimalKind animalKind, FirstPersonController watcher, TimeOfDay clock, AudioClip alarmSound)
        {
            profile = animalProfile;
            kind = animalKind;
            player = watcher;
            timeOfDay = clock;
            animator = GetComponentInChildren<Animator>();
            rig = GetComponent<SmallAnimalRig>();
            if (rig != null)
                animator = null;
            else if (animator != null)
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
            // Measured before it's turned, while the model is square to its own axes.
            AddHitbox();
            heading = Random.Range(0f, 360f);
            noiseSeed = Random.Range(0f, 100f);
            transform.rotation = Quaternion.Euler(0f, heading, 0f);
            lastPosition = transform.position;
            Enter(Random.value < 0.6f ? State.Grazing : State.Wandering);
            if (!Puppet)
                Spawned?.Invoke(this);
        }

        /// <summary>Made by the spawner as a copy of an animal another game runs.</summary>
        public void InitialisePuppet(AnimalProfile animalProfile, AnimalKind animalKind, FirstPersonController watcher, TimeOfDay clock, AudioClip alarmSound,
            AnimalSnapshot snapshot)
        {
            Puppet = true;
            Initialise(animalProfile, animalKind, watcher, clock, alarmSound);
            transform.SetPositionAndRotation(snapshot.position, Quaternion.Euler(0f, snapshot.heading, 0f));
            heading = snapshot.heading;
            lastPosition = snapshot.position;
            ApplySnapshot(snapshot);
        }

        /// <summary>
        /// A box round the body that arrows can hit (and the interaction ray can find once it's dead). A trigger, so
        /// the player walks through animals as before.
        /// </summary>
        void AddHitbox()
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (Renderer part in GetComponentsInChildren<Renderer>())
            {
                Bounds world = part.bounds;
                Vector3 min = world.min, max = world.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = transform.InverseTransformPoint(new Vector3(
                        (corner & 1) == 0 ? min.x : max.x, (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z));
                    if (!any)
                        bounds = new Bounds(point, Vector3.zero);
                    else
                        bounds.Encapsulate(point);
                    any = true;
                }
            }
            if (!any)
                bounds = new Bounds(new Vector3(0f, 0.15f, 0f), new Vector3(0.2f, 0.3f, 0.4f));
            var box = new GameObject("Hitbox");
            box.transform.SetParent(transform, false);
            hitbox = box.AddComponent<BoxCollider>();
            hitbox.isTrigger = true;
            hitbox.center = bounds.center;
            // A rabbit is a small target; give it a little extra so a near-perfect shot isn't a miss. A skinned deer's
            // bounds are far wider than its body, so its box is slimmed to a deer's width.
            hitbox.size = kind == AnimalKind.Rabbit ? bounds.size + Vector3.one * 0.04f
                : new Vector3(Mathf.Min(bounds.size.x, bounds.size.z * 0.3f), bounds.size.y, bounds.size.z);
        }

        void Update()
        {
            if (profile == null || Time.deltaTime <= 0f)
                return;
            if (Puppet)
            {
                UpdatePuppet();
                return;
            }
            if (state == State.Dead)
            {
                UpdateDead();
                return;
            }

            NearestThreat(out Vector3 away, out float distance, out float alert);
            if (state == State.Bedded)
            {
                UpdateBedded(distance, alert);
                return;
            }

            if (wound != Wound.None)
                UpdateWounded();
            if (state == State.Dead)
                return;

            if (state != State.Fleeing && distance < alert)
                Bolt();
            else if (state == State.Fleeing && wound == Wound.None && distance > profile.calmDistance)
                Enter(State.Wandering);
            // Not quite close enough to bolt: it freezes and watches.
            watching = state != State.Fleeing && distance < alert * 1.8f;

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
                    targetSpeed = profile.runSpeed * (wound == Wound.Leg ? 0.65f : wound != Wound.None ? 0.85f : 1f);
                    // Run directly away, with a little weave.
                    float awayHeading = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg + Mathf.Sin(Time.time * 2.3f) * 25f;
                    heading = Mathf.MoveTowardsAngle(heading, awayHeading, 360f * Time.deltaTime);
                    break;
            }

            if (watching)
                targetSpeed = 0f;
            speed = Mathf.MoveTowards(speed, targetSpeed, (state == State.Fleeing ? 25f : 3f) * Time.deltaTime);
            Move();
            Animate();
        }

        /// <summary>
        /// The hiker it's most wary of: this player or, on a trip with friends, one of theirs. How far off they are and
        /// how close they can come before it bolts (further if they're sprinting, much closer if they're creeping).
        /// </summary>
        void NearestThreat(out Vector3 away, out float distance, out float alert)
        {
            away = transform.position - player.transform.position;
            away.y = 0f;
            distance = away.magnitude;
            alert = AlertDistance(player.IsSprinting, player.IsCrouching, player.HorizontalSpeed);
            foreach (OtherHiker other in OtherHikers.All)
            {
                Vector3 offset = transform.position - other.position;
                offset.y = 0f;
                float otherAlert = AlertDistance(other.sprinting, other.crouching, other.speed);
                // The one closest to scaring it, for how close each may come.
                if (offset.magnitude / otherAlert < distance / alert)
                {
                    away = offset;
                    distance = offset.magnitude;
                    alert = otherAlert;
                }
            }
        }

        float AlertDistance(bool sprinting, bool crouching, float speed) =>
            profile.alertDistance * (sprinting ? 1.6f : crouching ? 0.45f : 1f) * (speed < 0.3f ? 0.7f : 1f);

        void Enter(State next)
        {
            state = next;
            stateTimer = next == State.Grazing
                ? Random.Range(profile.grazeSeconds.x, profile.grazeSeconds.y)
                : Random.Range(profile.wanderSeconds.x, profile.wanderSeconds.y);
            if (next == State.Wandering)
                heading += Random.Range(-90f, 90f);
        }

        void Bolt()
        {
            Enter(State.Fleeing);
            if (voice != null)
                voice.PlayOneShot(alarm, Random.Range(0.6f, 1f));
        }

        /// <summary>Something startling happened at <paramref name="point"/> (an arrow thudding in, a bowstring): animals nearby bolt.</summary>
        public static void StartleNear(Vector3 point, float radius)
        {
            foreach (Animal animal in all)
                if (!animal.Puppet && animal.state is State.Grazing or State.Wandering && (animal.transform.position - point).sqrMagnitude < radius * radius)
                    animal.Bolt();
            Startled?.Invoke(point, radius);
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
            if (rig != null)
            {
                gaitPhase += speed * Time.deltaTime / Mathf.Max(0.05f, profile.strideLength) * Mathf.PI;
                float hop = Mathf.Abs(Mathf.Sin(gaitPhase)) * profile.gaitHeight * Mathf.Clamp01(speed / Mathf.Max(0.1f, profile.walkSpeed));
                rig.Pose(speed, gaitPhase, hop, state == State.Grazing, watching);
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

        // ---------- Hunting ----------

        /// <summary>
        /// An arrow flying along <paramref name="direction"/> reaching the hitbox at <paramref name="point"/>. Returns
        /// false if it only passed through the space under a deer's belly or between its legs, so the arrow flies on.
        /// </summary>
        public bool TakeArrow(Vector3 point, Vector3 direction)
        {
            if (Puppet)
            {
                // The game that runs it judges the shot; only an arrow passing under a deer flies on from here.
                if (state != State.Dead && kind == AnimalKind.Deer)
                {
                    ArrowEntry(point, direction, out float entryAlong, out float entryHeight);
                    if (entryHeight < BellyLine && Mathf.Abs(entryAlong) < 0.3f)
                        return false;
                }
                RemoteHit?.Invoke(this, transform.InverseTransformPoint(point), transform.InverseTransformDirection(direction));
                return true;
            }
            if (state == State.Dead)
            {
                arrowsIn++;
                return true;
            }
            ArrowEntry(point, direction, out float along, out float height);
            if (Arrow.Trace)
                Debug.Log($"[Arrow] {kind} hit at along {along:F2}, height {height:F2}");

            if (kind == AnimalKind.Rabbit)
            {
                arrowsIn++;
                Report("Got it.", 2f);
                Die();
                return true;
            }

            // A deer's box takes in its long legs and its raised head: the body is the band above the legs.
            if (height < BellyLine && Mathf.Abs(along) < 0.3f)
                return false;
            arrowsIn++;

            if (wound != Wound.None)
            {
                Report("A second arrow finishes it.", 3f);
                Die();
                return true;
            }
            if (along > 0.55f && height > 0.5f)
            {
                Report("Head shot. It dropped where it stood.", 3f);
                Die();
                return true;
            }
            if (height < BellyLine)
            {
                Wounded(Wound.Leg, Random.Range(3f, 4.5f), Random.Range(160f, 260f));
                Report("A leg hit. It's run off limping, bleeding a little. Give it time, then follow the blood.", 5f);
            }
            else if (along > 0.02f)
            {
                // Heart and lungs: it runs on adrenaline for a few seconds and falls.
                Wounded(Wound.Vitals, 0.25f, Random.Range(25f, 50f));
                Report("Clean hit, just behind the shoulder. It won't go far.", 4f);
            }
            else
            {
                Wounded(Wound.Gut, Random.Range(1.5f, 2.5f), Random.Range(120f, 220f));
                Report("Hit too far back. It's run off wounded. Wait a while before you follow the blood, or you'll push it on.", 6f);
            }
            return true;
        }

        /// <summary>A deer's box takes in its long legs: below this share of its height is the space under its belly.</summary>
        const float BellyLine = 0.42f;

        /// <summary>
        /// A friend's arrow, judged here: where it hit (in the animal's own space) and which way it was flying. Returns
        /// what the shooter should be told, or null.
        /// </summary>
        public string TakeArrowFromFriend(Vector3 localPoint, Vector3 localDirection)
        {
            reporting = true;
            report = null;
            try
            {
                TakeArrow(transform.TransformPoint(localPoint), transform.TransformDirection(localDirection));
            }
            finally
            {
                reporting = false;
            }
            return report;
        }

        static void Report(string text, float seconds)
        {
            if (reporting)
                report = text;
            else
                Notifications.Post(text, seconds);
        }

        /// <summary>Where an arrow goes in: how far along, -1 tail to 1 nose, and how high, 0 hooves to 1 top.</summary>
        void ArrowEntry(Vector3 point, Vector3 direction, out float along, out float height)
        {
            // Where it goes in is judged where the arrow crosses the middle of the body, not where it meets the box:
            // a shot from an angle enters the box well forward or back of where it strikes.
            Vector3 half = hitbox.size * 0.5f;
            Vector3 local = hitbox.transform.InverseTransformPoint(point) - hitbox.center;
            Vector3 heading = hitbox.transform.InverseTransformDirection(direction);
            if (Mathf.Abs(heading.x) > 0.2f)
                local += heading * (-local.x / heading.x);
            local = Vector3.Max(-half, Vector3.Min(half, local));
            along = Mathf.Clamp(local.z / Mathf.Max(0.01f, half.z), -1f, 1f);
            height = Mathf.Clamp01((local.y + half.y) / Mathf.Max(0.01f, hitbox.size.y));
        }

        void Wounded(Wound how, float hoursToDie, float runMetres)
        {
            wound = how;
            woundedAtHour = timeOfDay.TotalHours;
            bleedOutHours = hoursToDie;
            runLeft = runMetres;
            nextBloodAt = 0f;
            lastPosition = transform.position;
            watching = false;
            Bolt();
            // Blood where it stood when it was hit.
            BloodTrail.Splash(transform.position, how == Wound.Vitals ? 0.35f : 0.2f);
        }

        /// <summary>A wounded animal: it bleeds as it goes, beds down once it's run far enough, and dies in time.</summary>
        void UpdateWounded()
        {
            Vector3 step = transform.position - lastPosition;
            step.y = 0f;
            float moved = step.magnitude;
            lastPosition = transform.position;
            runLeft -= moved;
            nextBloodAt -= moved;
            if (nextBloodAt <= 0f)
            {
                // Hit through the lungs it sprays; a leg wound only drips.
                float spacing = wound == Wound.Vitals ? 0.6f : wound == Wound.Gut ? 1.5f : 2.4f;
                nextBloodAt = spacing * Random.Range(0.6f, 1.4f);
                BloodTrail.Drop(transform.position + transform.right * Random.Range(-0.2f, 0.2f), wound == Wound.Vitals ? 1.4f : 1f);
            }

            if (timeOfDay.TotalHours - woundedAtHour >= bleedOutHours || (wound == Wound.Vitals && runLeft <= 0f))
            {
                Die();
                return;
            }
            if (runLeft <= 0f && state == State.Fleeing)
                BedDown();
        }

        void BedDown()
        {
            state = State.Bedded;
            speed = 0f;
            standing = transform.rotation;
            BloodTrail.Splash(transform.position, 0.3f);
            if (animator != null)
                animator.speed = 1f;
            Animate();
        }

        /// <summary>Lying low and weak. Comes too close and it struggles up and runs on, bleeding again.</summary>
        void UpdateBedded(float distance, float alert)
        {
            speed = 0f;
            if (moveHash != 0)
                animator.SetFloat(moveHash, 0f, 0.15f, Time.deltaTime);
            // Lying down: sunk low on its folded legs.
            float lie = Mathf.Clamp01(fall += Time.deltaTime * 1.5f);
            transform.position = new Vector3(transform.position.x,
                GroundCover.HeightAt(transform.position) - hitbox.size.y * 0.3f * lie * transform.lossyScale.y, transform.position.z);

            if (timeOfDay.TotalHours - woundedAtHour >= bleedOutHours)
            {
                Die();
                return;
            }
            if (distance < alert * 0.75f)
            {
                fall = 0f;
                // Pushed on, it lives a little longer on adrenaline, but goes on bleeding.
                bleedOutHours += 0.25f;
                runLeft = Random.Range(80f, 150f);
                Bolt();
            }
        }

        void Die()
        {
            state = State.Dead;
            speed = 0f;
            fall = 0f;
            standing = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            deadAtHour = timeOfDay.TotalHours;
            meatLeft = Mathf.Max(6, Mathf.RoundToInt(DeerMeatKilograms * transform.localScale.x));
            if (animator != null)
            {
                if (moveHash != 0)
                    animator.SetFloat(moveHash, 0f);
                if (runHash != 0)
                    animator.SetFloat(runHash, 0f);
                animator.speed = 1f;
            }
            if (rig != null)
                rig.Pose(0f, 0f, 0f, false, false);
            BloodTrail.Splash(transform.position, kind == AnimalKind.Deer ? 0.4f : 0.15f);
        }

        /// <summary>Topples onto its side, then lies there until it's taken, butchered or has rotted away.</summary>
        void UpdateDead()
        {
            if (fall < 1f)
            {
                fall = Mathf.MoveTowards(fall, 1f, Time.deltaTime / 0.7f);
                float t = 1f - (1f - fall) * (1f - fall);
                transform.rotation = standing * Quaternion.Euler(0f, 0f, 84f * t);
                // Lying on its side, its flank rests on the ground rather than through it.
                float halfWidth = hitbox.size.x * 0.5f * transform.lossyScale.x;
                transform.position = new Vector3(transform.position.x, GroundCover.HeightAt(transform.position) + halfWidth * t, transform.position.z);
                if (fall >= 1f && animator != null)
                    animator.enabled = false;
            }
            if (!Puppet && timeOfDay.TotalHours - deadAtHour > CarcassHours)
                Destroy(gameObject);
        }

        // ---------- A copy of another game's animal ----------

        Vector3 netPosition;
        float netHeading;
        bool hasNet;

        /// <summary>How it is now, for the other games.</summary>
        public AnimalSnapshot Snapshot() => new()
        {
            position = transform.position,
            heading = transform.eulerAngles.y,
            speed = speed,
            state = (byte)state,
            wound = (byte)wound,
            watching = watching,
            fieldDressed = fieldDressed,
            hideTaken = hideTaken,
            meatLeft = (byte)Mathf.Clamp(meatLeft, 0, 255),
            arrowsIn = (byte)Mathf.Clamp(arrowsIn, 0, 255),
            deadAtHour = deadAtHour,
        };

        /// <summary>The game running it says how it is now.</summary>
        public void ApplySnapshot(AnimalSnapshot snapshot)
        {
            var next = (State)snapshot.state;
            var nextWound = (Wound)snapshot.wound;
            if (nextWound != Wound.None && wound == Wound.None)
                BloodTrail.Splash(snapshot.position, nextWound == Wound.Vitals ? 0.35f : 0.2f);
            if (next != state)
            {
                if (next == State.Fleeing && state is State.Grazing or State.Wandering && voice != null)
                    voice.PlayOneShot(alarm, Random.Range(0.6f, 1f));
                if (next == State.Bedded)
                {
                    fall = 0f;
                    BloodTrail.Splash(snapshot.position, 0.3f);
                }
                if (next == State.Dead)
                    ShowDeath();
            }
            state = next;
            wound = nextWound;
            watching = snapshot.watching;
            fieldDressed = snapshot.fieldDressed;
            hideTaken = snapshot.hideTaken;
            meatLeft = snapshot.meatLeft;
            arrowsIn = snapshot.arrowsIn;
            deadAtHour = snapshot.deadAtHour;
            netPosition = snapshot.position;
            netHeading = snapshot.heading;
            if (state != State.Dead)
                speed = snapshot.speed;
            hasNet = true;
        }

        /// <summary>Follows where the game running it says it is, smoothly, moving its legs to match; bleeds as it goes, if wounded.</summary>
        void UpdatePuppet()
        {
            if (!hasNet)
                return;
            if (state == State.Dead)
            {
                if (fall < 1f)
                {
                    Vector3 flat = Vector3.Lerp(transform.position, netPosition, 1f - Mathf.Exp(-10f * Time.deltaTime));
                    transform.position = new Vector3(flat.x, transform.position.y, flat.z);
                }
                UpdateDead();
                return;
            }
            float ease = 1f - Mathf.Exp(-10f * Time.deltaTime);
            // Run on at its speed between updates, then settle on where it was said to be.
            Vector3 ahead = netPosition + Quaternion.Euler(0f, netHeading, 0f) * Vector3.forward * (speed * 0.05f);
            transform.position = (transform.position - netPosition).sqrMagnitude > 64f ? netPosition : Vector3.Lerp(transform.position, ahead, ease);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, netHeading, 0f), ease);
            if (state == State.Bedded)
            {
                speed = 0f;
                if (moveHash != 0)
                    animator.SetFloat(moveHash, 0f, 0.15f, Time.deltaTime);
                return;
            }
            if (wound != Wound.None)
            {
                Vector3 step = transform.position - lastPosition;
                step.y = 0f;
                nextBloodAt -= step.magnitude;
                if (nextBloodAt <= 0f)
                {
                    float spacing = wound == Wound.Vitals ? 0.6f : wound == Wound.Gut ? 1.5f : 2.4f;
                    nextBloodAt = spacing * Random.Range(0.6f, 1.4f);
                    BloodTrail.Drop(transform.position + transform.right * Random.Range(-0.2f, 0.2f), wound == Wound.Vitals ? 1.4f : 1f);
                }
            }
            lastPosition = transform.position;
            Animate();
        }

        /// <summary>The death as this game shows it: it stops and topples over.</summary>
        void ShowDeath()
        {
            speed = 0f;
            fall = 0f;
            standing = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            if (animator != null)
            {
                if (moveHash != 0)
                    animator.SetFloat(moveHash, 0f);
                if (runHash != 0)
                    animator.SetFloat(runHash, 0f);
                animator.speed = 1f;
            }
            if (rig != null)
                rig.Pose(0f, 0f, 0f, false, false);
            BloodTrail.Splash(transform.position, kind == AnimalKind.Deer ? 0.4f : 0.15f);
        }

        /// <summary>A friend did something to this carcass, in their game.</summary>
        public void ApplyFriendAction(CarcassAction action, int amount)
        {
            switch (action)
            {
                case CarcassAction.Taken:
                    Destroy(gameObject);
                    break;
                case CarcassAction.FieldDressed:
                    fieldDressed = true;
                    DropArrows();
                    break;
                case CarcassAction.Meat:
                    meatLeft = Mathf.Max(0, meatLeft - amount);
                    break;
                case CarcassAction.Hide:
                    hideTaken = true;
                    break;
            }
        }

        /// <summary>The arrows in it were taken out (by whoever took or butchered it).</summary>
        void DropArrows()
        {
            arrowsIn = 0;
            foreach (Arrow stuck in GetComponentsInChildren<Arrow>())
                Destroy(stuck.gameObject);
        }

        void Tell(CarcassAction action, int amount = 0)
        {
            if (Puppet)
                RemoteAction?.Invoke(this, action, amount);
        }

        bool MeatSpoiled => timeOfDay.TotalHours - deadAtHour > MeatHours;

        /// <summary>Arrows still in the animal come back as it's taken or butchered; some break.</summary>
        void RecoverArrows(Backpack backpack)
        {
            int whole = 0;
            for (int i = 0; i < arrowsIn; i++)
                if (Random.value < 0.75f)
                    whole++;
            if (arrowsIn > 0)
                Notifications.Post(whole == arrowsIn ? $"You get your arrow{(whole > 1 ? "s" : "")} back."
                    : whole == 0 ? "The arrow broke inside." : $"You get {whole} of {arrowsIn} arrows back.", 3f);
            backpack.AddArrows(whole);
            DropArrows();
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            if (state != State.Dead)
                return;
            Backpack backpack = interactor.Backpack;

            if (kind == AnimalKind.Rabbit)
            {
                options.Add(new InteractionOption("Take the rabbit", () =>
                {
                    backpack.AddFood(FoodKind.RabbitCarcass);
                    RecoverArrows(backpack);
                    TripLog.Tally(TripStat.Rabbits);
                    Tell(CarcassAction.Taken);
                    Destroy(gameObject);
                }));
                return;
            }

            if (!fieldDressed)
            {
                options.Add(new InteractionOption($"Field dress the deer ({FieldDressMinutes:0} min)", () =>
                    interactor.Activity.Begin("Field dressing the deer", FieldDressMinutes, () =>
                    {
                        fieldDressed = true;
                        RecoverArrows(backpack);
                        Tell(CarcassAction.FieldDressed);
                        TripLog.Tally(TripStat.Deer);
                        TripLog.Note(MeatSpoiled ? "Found the deer too late: the meat had turned. Took the hide." : "Field dressed a deer.");
                        Notifications.Post(MeatSpoiled ? "The meat has spoiled, but the hide's still good."
                            : $"About {meatLeft} kg of venison. Take what you can carry.", 4f);
                    }),
                    interactor.Activity.IsBusy ? "" : null));
                options.Add(new InteractionOption("Gut it, skin it and cut the meat into kilogram pieces you can carry.", () => { }, ""));
                return;
            }

            float spare = backpack.MaxLoad - backpack.TotalWeight;
            string meatProblem = MeatSpoiled ? "The meat has spoiled" : meatLeft <= 0 ? "No meat left" : null;
            options.Add(new InteractionOption("Take 1 kg of venison", () => TakeMeat(backpack, 1), meatProblem));
            options.Add(new InteractionOption($"Take {Mathf.Min(4, meatLeft)} kg of venison", () => TakeMeat(backpack, 4),
                meatProblem ?? (meatLeft <= 1 ? "" : null)));
            options.Add(new InteractionOption($"Take the hide ({backpack.HideWeight:0.#} kg)", () =>
            {
                hideTaken = true;
                backpack.AddHide();
                Tell(CarcassAction.Hide);
            }, hideTaken ? "Already taken" : null));
            options.Add(new InteractionOption(
                $"{(MeatSpoiled ? "Spoiled meat" : $"{meatLeft} kg of meat left")}. You carry {backpack.TotalWeight:0.#} kg; over {backpack.MaxLoad:0} kg you can barely walk ({Mathf.Max(0f, spare):0.#} kg spare).",
                () => { }, ""));
        }

        void TakeMeat(Backpack backpack, int kilograms)
        {
            int taken = Mathf.Min(kilograms, meatLeft);
            backpack.AddFood(FoodKind.RawVenison, taken);
            meatLeft -= taken;
            Tell(CarcassAction.Meat, taken);
        }
    }
}
