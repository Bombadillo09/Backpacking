using System.Collections.Generic;
using Backpacking.Audio;
using Backpacking.Camp;
using Backpacking.Interaction;
using Backpacking.UI;
using Backpacking.Wildlife;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Hunting
{
    /// <summary>
    /// An arrow in flight: it falls as it goes, and either sticks in an animal, sticks in the ground or a tree where
    /// it can be picked up again, breaks on rock, or is lost in a lake. Animals near where it lands are startled.
    /// On a co-op trip friends see it fly as a ghost (it passes through animals and people; the shooter's game
    /// decides where it lands), and whoever picks it up gets it.
    /// </summary>
    public class Arrow : MonoBehaviour, IInteractable
    {
        const float GravityScale = 0.75f;
        const float MaxFlightSeconds = 6f;
        const int MaxLying = 40;

        static readonly Queue<Arrow> lying = new();
        static readonly RaycastHit[] hits = new RaycastHit[16];

        Vector3 velocity;
        Transform shooter;
        TrailRenderer trail;

        public Vector3 Velocity => velocity;

        /// <summary>Its number on a co-op trip (0 alone).</summary>
        public int NetId { get; set; }
        /// <summary>A friend's arrow as seen here: it hurts nothing, and lands where their game says.</summary>
        public bool Ghost { get; private set; }

        /// <summary>This player loosed an arrow (co-op tells the others).</summary>
        public static event System.Action<Arrow> Loosed;
        /// <summary>This player's arrow stopped: lying where it can be picked up (true), or gone (broken, lost, in an animal).</summary>
        public static event System.Action<Arrow, bool> Settled;
        /// <summary>An arrow was picked up here.</summary>
        public static event System.Action<Arrow> PickedUp;
        float flightTime;
        bool flying;
        readonly List<Collider> passed = new();

        public string DisplayName => "Arrow";

        /// <summary>Logs everything an arrow touches (for the editor's hunting test).</summary>
        public static bool Trace { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lying.Clear();
            Loosed = null;
            Settled = null;
            PickedUp = null;
        }

        /// <summary>Looses an arrow from <paramref name="from"/>. The shooter's own colliders are ignored.</summary>
        public static Arrow Shoot(Vector3 from, Vector3 velocity, Transform shooter)
        {
            GameObject model = BowDesign.Arrow();
            model.transform.SetPositionAndRotation(from, Quaternion.LookRotation(velocity));
            var arrow = model.AddComponent<Arrow>();
            arrow.velocity = velocity;
            arrow.shooter = shooter;
            arrow.flying = true;
            arrow.trail = BowDesign.AddFlightTrail(model);
            Loosed?.Invoke(arrow);
            return arrow;
        }

        /// <summary>A friend's arrow, flying the same way theirs did.</summary>
        public static Arrow ShootGhost(Vector3 from, Vector3 velocity, int netId)
        {
            GameObject model = BowDesign.Arrow();
            model.transform.SetPositionAndRotation(from, Quaternion.LookRotation(velocity));
            var arrow = model.AddComponent<Arrow>();
            arrow.velocity = velocity;
            arrow.flying = true;
            arrow.Ghost = true;
            arrow.NetId = netId;
            arrow.trail = BowDesign.AddFlightTrail(model);
            return arrow;
        }

        /// <summary>Where a friend's arrow ended up in their game: lying there, ready to pick up.</summary>
        public void SettleAt(Vector3 position, Quaternion rotation)
        {
            if (flying)
                StopTrail();
            flying = false;
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(position, rotation);
            if (GetComponent<BoxCollider>() == null)
                LieAbout();
        }

        /// <summary>Gone in a friend's game (broken, lost, in an animal, picked up).</summary>
        public void Vanish() => Destroy(gameObject);

        void Gone()
        {
            if (!Ghost)
                Settled?.Invoke(this, false);
            Destroy(gameObject);
        }

        void Update()
        {
            if (!flying)
                return;
            flightTime += Time.deltaTime;
            if (flightTime > MaxFlightSeconds)
            {
                Gone();
                return;
            }

            // Small steps, so a fast arrow can't skip through a rabbit between frames.
            float remaining = Time.deltaTime;
            while (remaining > 0f && flying)
            {
                float dt = Mathf.Min(remaining, 0.01f);
                remaining -= dt;
                Vector3 start = transform.position;
                velocity += Physics.gravity * (GravityScale * dt);
                Vector3 step = velocity * dt;
                if (Hit(start, step))
                    return;
                transform.SetPositionAndRotation(start + step, Quaternion.LookRotation(velocity));
            }
        }

        /// <summary>Checks the path of one step for anything the arrow would hit, nearest first.</summary>
        bool Hit(Vector3 start, Vector3 step)
        {
            float length = step.magnitude;
            if (length < 1e-5f)
                return false;
            Vector3 direction = step / length;
            int count = Physics.RaycastNonAlloc(start, direction, hits, length, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, 0, count, Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                Collider other = hit.collider;
                if (Trace)
                    Debug.Log($"[Arrow] touches {other.name} (trigger {other.isTrigger}, animal {other.GetComponentInParent<Animal>() != null}) at {hit.point:F2}");
                if (passed.Contains(other) || (shooter != null && other.transform.IsChildOf(shooter)))
                    continue;
                // Friends on a co-op trip aren't targets.
                if (other.GetComponentInParent<Net.RemoteHikerBody>() != null)
                {
                    passed.Add(other);
                    continue;
                }

                Animal animal = other.GetComponentInParent<Animal>();
                if (animal != null && Ghost)
                {
                    // The shooter's game says whether it hit.
                    passed.Add(other);
                    continue;
                }
                if (animal != null)
                {
                    if (!animal.TakeArrow(hit.point, direction))
                    {
                        passed.Add(other);
                        continue;
                    }
                    StickIn(hit.point, direction, animal.transform, 0.25f);
                    PlayAt(Sounds.ArrowHit(), hit.point, 0.6f);
                    Settled?.Invoke(this, false);
                    return true;
                }
                if (other.GetComponent<WaterSource>() != null)
                {
                    // Into the lake: gone.
                    if (!Ghost)
                        Animal.StartleNear(hit.point, 15f);
                    Gone();
                    return true;
                }
                // Other triggers (interaction zones, arrows lying about) don't stop it.
                if (other.isTrigger)
                    continue;

                PlayAt(Sounds.ArrowHit(), hit.point, 1f);
                if (Ghost)
                {
                    // Stuck where it hit, until the shooter's game says where it really lies.
                    StickIn(hit.point, direction, null, 0.15f);
                    return true;
                }
                Animal.StartleNear(hit.point, 18f);
                bool stone = other is not TerrainCollider || GroundCover.At(hit.point) == Surface.Hard;
                if (Random.value < (stone ? 0.35f : 0.08f))
                {
                    if (shooter != null && (hit.point - shooter.position).sqrMagnitude < 40f * 40f)
                        Notifications.Post(stone ? "The arrow shattered on the rock." : "The arrow snapped.", 2.5f);
                    Gone();
                    return true;
                }
                StickIn(hit.point, direction, null, stone ? 0.04f : 0.15f);
                LieAbout();
                Settled?.Invoke(this, true);
                return true;
            }
            return false;
        }

        void StopTrail()
        {
            // The streak fades out behind it, then goes.
            if (trail != null)
            {
                trail.emitting = false;
                Destroy(trail, trail.time + 0.1f);
            }
            trail = null;
        }

        void StickIn(Vector3 point, Vector3 direction, Transform parent, float depth)
        {
            flying = false;
            StopTrail();
            transform.SetPositionAndRotation(point + direction * depth, Quaternion.LookRotation(direction));
            if (parent != null)
                transform.SetParent(parent, true);
        }

        /// <summary>Stuck in the ground or a tree: it can be picked up. Only the newest few dozen are kept.</summary>
        void LieAbout()
        {
            // A trigger round the shaft, so it can be looked at and picked up without getting in the way.
            var grab = gameObject.AddComponent<BoxCollider>();
            grab.isTrigger = true;
            grab.center = new Vector3(0f, 0f, -BowDesign.ArrowLength * 0.5f);
            grab.size = new Vector3(0.15f, 0.15f, BowDesign.ArrowLength);
            lying.Enqueue(this);
            while (lying.Count > MaxLying)
            {
                Arrow oldest = lying.Dequeue();
                if (oldest != null)
                    Destroy(oldest.gameObject);
            }
        }

        static void PlayAt(AudioClip clip, Vector3 point, float volume)
        {
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, point, volume);
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            if (flying || transform.parent != null)
                return;
            options.Add(new InteractionOption("Pick up the arrow", () =>
            {
                interactor.Backpack.AddArrows(1);
                PickedUp?.Invoke(this);
                Destroy(gameObject);
            }));
        }
    }
}
