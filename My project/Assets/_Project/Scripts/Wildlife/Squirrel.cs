using Backpacking.Audio;
using Backpacking.Player;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Wildlife
{
    /// <summary>
    /// A squirrel foraging around the foot of a tree in quick dashes and pauses. Come close and it races to the
    /// nearest trunk and up it, then clings head-down a few metres up, watching you and chattering, until you've
    /// gone. Then it comes back down to carry on feeding.
    /// </summary>
    public class Squirrel : MonoBehaviour
    {
        [SerializeField] float alertDistance = 9f;
        [Tooltip("Comes back down once the player is this far away (and has been for a while).")]
        [SerializeField] float calmDistance = 22f;
        [SerializeField] float runSpeed = 4.5f;
        [SerializeField] float climbSpeed = 2.2f;
        [Tooltip("Beyond this distance its fur layers aren't drawn (they're expensive and too fine to see).")]
        [SerializeField] float furDistance = 18f;

        enum State { Foraging, Dashing, Fleeing, Climbing, Watching, Descending }

        const string RunState = "Squirrel_Armature|Running";
        const string IdleState = "Squirrel_Armature|Idle";
        const string LookState = "Squirrel_Armature|Idle2";

        FirstPersonController player;
        Animator animator;
        Renderer[] fur;
        bool furShown = true;
        AudioSource voice;
        string playing;

        State state;
        float stateTimer, calmTimer, nextChatter;
        Vector3 dashTarget;
        TreeIndex.Tree tree;
        bool hasTree;
        Vector3 outward;
        float climbHeight, height;

        /// <summary>Called by the spawner straight after creating it.</summary>
        public void Initialise(FirstPersonController watcher)
        {
            player = watcher;
            animator = GetComponentInChildren<Animator>();
            if (animator != null)
                animator.applyRootMotion = false;
            fur = System.Array.FindAll(GetComponentsInChildren<Renderer>(true), r => r.name.Contains("Fur"));
            voice = gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 1f;
            voice.minDistance = 4f;
            voice.maxDistance = 50f;
            voice.dopplerLevel = 0f;
            voice.playOnAwake = false;
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            hasTree = TreeIndex.Nearest(transform.position, 25f, out tree);
            Enter(State.Foraging);
        }

        void Update()
        {
            if (player == null || Time.deltaTime <= 0f)
                return;
            Vector3 fromPlayer = transform.position - player.transform.position;
            fromPlayer.y = 0f;
            float distance = fromPlayer.magnitude;
            ShowFur(distance < furDistance);

            float caution = player.IsSprinting ? 1.6f : player.IsCrouching ? 0.5f : 1f;
            bool threatened = distance < alertDistance * caution;
            stateTimer -= Time.deltaTime;

            switch (state)
            {
                case State.Foraging:
                    Play(stateTimer > 1.2f ? IdleState : LookState);
                    if (threatened)
                        Flee();
                    else if (stateTimer <= 0f)
                        StartDash();
                    break;
                case State.Dashing:
                    Play(RunState);
                    if (threatened)
                        Flee();
                    else if (RunTowards(dashTarget, runSpeed * 0.7f) || stateTimer <= 0f)
                        Enter(State.Foraging);
                    break;
                case State.Fleeing:
                    Play(RunState);
                    if (!hasTree)
                    {
                        // No tree to climb: just bolt, and the spawner tidies it away once it's far off.
                        Vector3 away = transform.position + fromPlayer.normalized * 5f;
                        RunTowards(away, runSpeed);
                        if (distance > calmDistance)
                            Enter(State.Foraging);
                    }
                    else if (RunTowards(TrunkFoot(), runSpeed))
                        StartClimb();
                    break;
                case State.Climbing:
                    Play(RunState);
                    height = Mathf.MoveTowards(height, climbHeight, climbSpeed * Time.deltaTime);
                    Cling(up: true);
                    if (height >= climbHeight)
                        Enter(State.Watching);
                    break;
                case State.Watching:
                    Play(IdleState);
                    // Turned head-down on the trunk, watching, scolding now and then.
                    Cling(up: false);
                    if (distance < calmDistance * 0.8f && Time.time > nextChatter)
                    {
                        nextChatter = Time.time + Random.Range(3f, 8f);
                        voice.PlayOneShot(SoundSynth.Chatter(), Random.Range(0.5f, 0.8f));
                    }
                    calmTimer = distance > calmDistance ? calmTimer + Time.deltaTime : 0f;
                    if (calmTimer > 6f)
                        Enter(State.Descending);
                    break;
                case State.Descending:
                    Play(RunState);
                    if (threatened)
                    {
                        Enter(State.Climbing);
                        break;
                    }
                    height = Mathf.MoveTowards(height, 0f, climbSpeed * 0.6f * Time.deltaTime);
                    Cling(up: false);
                    if (height <= 0f)
                    {
                        transform.rotation = Quaternion.LookRotation(outward);
                        Enter(State.Foraging);
                    }
                    break;
            }
        }

        void Enter(State next)
        {
            state = next;
            stateTimer = next == State.Foraging ? Random.Range(1.5f, 5f) : 2.5f;
            calmTimer = 0f;
        }

        void Flee()
        {
            hasTree = TreeIndex.Nearest(transform.position, 25f, out tree);
            Enter(State.Fleeing);
            if (Random.value < 0.5f)
                voice.PlayOneShot(SoundSynth.Chatter(), 0.4f);
        }

        /// <summary>A short scamper to a new spot nearby, staying around its tree.</summary>
        void StartDash()
        {
            Vector3 centre = hasTree ? tree.position : transform.position;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Vector3 spot = centre + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(1f, 7f);
                spot.y = GroundCover.HeightAt(spot);
                if (Animal.IsWater(spot))
                    continue;
                dashTarget = spot;
                Enter(State.Dashing);
                return;
            }
            Enter(State.Foraging);
        }

        Vector3 TrunkFoot()
        {
            Vector3 side = transform.position - tree.position;
            side.y = 0f;
            outward = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.forward;
            Vector3 foot = tree.position + outward * tree.trunkRadius;
            foot.y = GroundCover.HeightAt(foot);
            return foot;
        }

        void StartClimb()
        {
            climbHeight = Mathf.Min(tree.height * 0.45f, Random.Range(3.5f, 6.5f));
            height = 0f;
            Enter(State.Climbing);
        }

        /// <summary>On the trunk at the current height, facing up it or down it, belly to the bark.</summary>
        void Cling(bool up)
        {
            Vector3 foot = tree.position + outward * tree.trunkRadius;
            foot.y = GroundCover.HeightAt(foot) + height;
            Quaternion target = Quaternion.LookRotation(up ? Vector3.up : Vector3.down, outward);
            transform.SetPositionAndRotation(foot, Quaternion.RotateTowards(transform.rotation, target, 540f * Time.deltaTime));
        }

        /// <summary>Runs along the ground towards <paramref name="target"/>; true once there.</summary>
        bool RunTowards(Vector3 target, float speed)
        {
            Vector3 offset = target - transform.position;
            offset.y = 0f;
            float step = speed * Time.deltaTime;
            if (offset.magnitude <= step)
            {
                transform.position = new Vector3(target.x, GroundCover.HeightAt(target), target.z);
                return true;
            }
            Vector3 next = transform.position + offset.normalized * step;
            next.y = GroundCover.HeightAt(next);
            transform.SetPositionAndRotation(next,
                Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(offset), 720f * Time.deltaTime));
            return false;
        }

        void Play(string animation)
        {
            if (animator == null || playing == animation)
                return;
            playing = animation;
            animator.CrossFadeInFixedTime(animation, 0.15f);
            // The run is played faster when racing.
            animator.speed = animation == RunState && (state == State.Fleeing || state == State.Climbing) ? 1.4f : 1f;
        }

        void ShowFur(bool show)
        {
            if (show == furShown)
                return;
            furShown = show;
            foreach (Renderer layer in fur)
                layer.enabled = show;
        }
    }
}
