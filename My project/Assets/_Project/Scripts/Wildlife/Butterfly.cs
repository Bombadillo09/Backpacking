using Backpacking.Player;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Wildlife
{
    /// <summary>
    /// A butterfly drifting about a meadow in erratic loops near the ground, settling now and then to rest with
    /// slowly opening wings. Startled by the player passing close, it lifts away. Out on warm, dry, bright days only:
    /// the spawner sends them off when it turns cold, wet or dark.
    /// </summary>
    public class Butterfly : MonoBehaviour
    {
        [SerializeField] float flightSpeed = 1.6f;
        [Tooltip("How far it wanders from where it appeared, in metres.")]
        [SerializeField] float range = 10f;
        [SerializeField] float startleDistance = 2.5f;

        FirstPersonController player;
        Animation flapping;
        Vector3 home, target;
        float seed, restTimer, flyTimer, height;
        bool resting, leaving;

        /// <summary>Called by the spawner straight after creating it.</summary>
        public void Initialise(FirstPersonController watcher)
        {
            player = watcher;
            flapping = GetComponentInChildren<Animation>();
            if (flapping != null && flapping.clip != null)
            {
                flapping.wrapMode = WrapMode.Loop;
                flapping.Play();
                // Not all in step.
                flapping[flapping.clip.name].time = Random.Range(0f, flapping.clip.length);
            }
            home = transform.position;
            seed = Random.Range(0f, 100f);
            height = Random.Range(0.4f, 1.5f);
            PickTarget();
            flyTimer = Random.Range(6f, 20f);
            transform.position += Vector3.up * height;
        }

        /// <summary>Flies up and away, and is gone (cold, rain or dusk).</summary>
        public void Leave()
        {
            leaving = true;
            resting = false;
        }

        void Update()
        {
            if (player == null || Time.deltaTime <= 0f)
                return;
            float near = (player.transform.position - transform.position).sqrMagnitude;
            if (resting)
            {
                restTimer -= Time.deltaTime;
                if (restTimer <= 0f || near < startleDistance * startleDistance)
                {
                    resting = false;
                    SetFlapSpeed(1f);
                    PickTarget();
                    flyTimer = Random.Range(8f, 25f);
                }
                return;
            }

            if (leaving)
            {
                transform.position += (transform.forward * flightSpeed + Vector3.up * 1.2f) * Time.deltaTime;
                Wobble();
                if (transform.position.y - GroundCover.HeightAt(transform.position) > 25f)
                    Destroy(gameObject);
                return;
            }

            Vector3 offset = target - transform.position;
            offset.y = 0f;
            if (offset.magnitude < 0.5f)
                PickTarget();

            // Erratic: the heading wanders around the way to its target, and it bobs up and down as it flaps.
            float wander = (Mathf.PerlinNoise(Time.time * 0.9f, seed) - 0.5f) * 140f;
            Vector3 direction = Quaternion.Euler(0f, wander, 0f) * offset.normalized;
            Vector3 next = transform.position + direction * (flightSpeed * Time.deltaTime);
            float ground = GroundCover.HeightAt(next);
            float bob = Mathf.Sin(Time.time * 7f + seed) * 0.12f + (Mathf.PerlinNoise(seed, Time.time * 0.4f) - 0.5f) * 0.8f;
            next.y = Mathf.Lerp(transform.position.y, ground + Mathf.Max(0.15f, height + bob), Time.deltaTime * 3f);
            transform.SetPositionAndRotation(next,
                Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 6f));
            Wobble();

            flyTimer -= Time.deltaTime;
            if (flyTimer <= 0f)
                Settle();
        }

        /// <summary>A little roll and pitch with each wingbeat.</summary>
        void Wobble()
        {
            Vector3 angles = transform.eulerAngles;
            transform.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 9f + seed) * 12f, angles.y, Mathf.Sin(Time.time * 5f + seed) * 10f);
        }

        void Settle()
        {
            resting = true;
            restTimer = Random.Range(3f, 9f);
            Vector3 down = transform.position;
            down.y = GroundCover.HeightAt(down) + 0.05f;
            transform.SetPositionAndRotation(down, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            SetFlapSpeed(0.25f);
        }

        void PickTarget()
        {
            Vector2 spot = Random.insideUnitCircle * range;
            target = home + new Vector3(spot.x, 0f, spot.y);
            height = Random.Range(0.4f, 1.6f);
        }

        void SetFlapSpeed(float speed)
        {
            if (flapping != null && flapping.clip != null)
                flapping[flapping.clip.name].speed = speed;
        }
    }
}
