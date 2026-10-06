using System.Collections.Generic;
using Backpacking.Player;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Wildlife
{
    /// <summary>
    /// A few birds pecking about on the ground. Get too close and they burst into the air together,
    /// fly off and are gone.
    /// </summary>
    public class BirdFlock : MonoBehaviour
    {
        [Tooltip("Takes flight when the player comes this close (further if sprinting, closer if crouching).")]
        [SerializeField] float alertDistance = 14f;
        [SerializeField] float flightSeconds = 14f;

        class Bird
        {
            public Transform root, body, head, leftWing, rightWing;
            public Vector3 velocity;
            public float phase, nextHopTime;
        }

        readonly List<Bird> birds = new();
        FirstPersonController player;
        AudioSource voice;
        AudioClip flutter;
        bool flying;
        float flightTime;

        public bool IsFlying => flying;

        /// <summary>Called by the spawner with the birds it made, already placed around the flock's position.</summary>
        public void Initialise(FirstPersonController watcher, IEnumerable<GameObject> members, AudioClip takeOffSound)
        {
            player = watcher;
            flutter = takeOffSound;
            foreach (GameObject member in members)
            {
                Transform body = member.transform.Find("Body");
                birds.Add(new Bird
                {
                    root = member.transform,
                    body = body,
                    head = body != null ? body.Find("Head") : null,
                    leftWing = body != null ? body.Find("Wing L") : null,
                    rightWing = body != null ? body.Find("Wing R") : null,
                    phase = Random.Range(0f, 10f),
                    nextHopTime = Time.time + Random.Range(0.5f, 3f),
                });
            }
            voice = gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 1f;
            voice.minDistance = 5f;
            voice.maxDistance = 60f;
            voice.dopplerLevel = 0f;
            voice.playOnAwake = false;
        }

        void Update()
        {
            if (player == null || Time.deltaTime <= 0f)
                return;

            if (!flying)
            {
                Vector3 offset = transform.position - player.transform.position;
                offset.y = 0f;
                float alert = alertDistance * (player.IsSprinting ? 1.8f : player.IsCrouching ? 0.5f : 1f);
                if (offset.magnitude < alert)
                    TakeOff(offset.normalized);
                else
                    Peck();
            }
            else
                Fly();
        }

        void Peck()
        {
            foreach (Bird bird in birds)
            {
                bird.phase += Time.deltaTime;
                // Peck: a quick dip of the head now and then.
                if (bird.head != null)
                    bird.head.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(bird.phase * 5f)) * 55f, 0f, 0f);
                if (Time.time < bird.nextHopTime)
                    continue;

                // A little hop to a new spot nearby.
                bird.nextHopTime = Time.time + Random.Range(1f, 4f);
                bird.root.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                Vector3 hop = bird.root.position + bird.root.forward * Random.Range(0.1f, 0.35f);
                hop.y = GroundCover.HeightAt(hop);
                bird.root.position = hop;
            }
        }

        void TakeOff(Vector3 away)
        {
            flying = true;
            flightTime = 0f;
            if (flutter != null)
                voice.PlayOneShot(flutter, 0.9f);
            foreach (Bird bird in birds)
            {
                // Away from the player, spreading out a little, climbing steeply at first.
                Vector3 direction = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * away;
                bird.velocity = direction * Random.Range(6f, 9f) + Vector3.up * Random.Range(4f, 6.5f);
                if (bird.head != null)
                    bird.head.localRotation = Quaternion.identity;
            }
        }

        void Fly()
        {
            flightTime += Time.deltaTime;
            foreach (Bird bird in birds)
            {
                // Level out after the first burst of climbing.
                bird.velocity.y = Mathf.MoveTowards(bird.velocity.y, 0.8f, 2f * Time.deltaTime);
                bird.root.position += bird.velocity * Time.deltaTime;
                bird.root.rotation = Quaternion.LookRotation(bird.velocity);

                bird.phase += Time.deltaTime * 14f * Mathf.PI * 2f;
                float flap = Mathf.Sin(bird.phase) * 60f;
                if (bird.leftWing != null)
                    bird.leftWing.localRotation = Quaternion.Euler(0f, 0f, flap);
                if (bird.rightWing != null)
                    bird.rightWing.localRotation = Quaternion.Euler(0f, 0f, -flap);
            }
            if (flightTime > flightSeconds)
                Destroy(gameObject);
        }
    }
}
