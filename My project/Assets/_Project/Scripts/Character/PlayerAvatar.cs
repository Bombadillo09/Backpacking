using Backpacking.Camp;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Trip;
using UnityEngine;

namespace Backpacking.Character
{
    /// <summary>
    /// The player's own hiker: built from their profile, animated by how they move (idle, walk, jog,
    /// sprint, crouch, jump, kneeling at camp tasks, swinging the machete), seen from the eyes in first person
    /// (body below, head hidden but still casting its shadow) or from behind in third person.
    /// </summary>
    public class PlayerAvatar : MonoBehaviour
    {
        [SerializeField] CharacterAppearance appearance;
        [SerializeField] FirstPersonController player;
        [SerializeField] PlayerActivity activity;
        [Tooltip("In first person the body sits this far behind the eyes, so looking down shows the legs, not the inside of the chest.")]
        [SerializeField] float firstPersonSetBack = 0.12f;
        [Tooltip("Seated in first person, the body moves this far forward so the sitting head is under the eyes.")]
        [SerializeField] float seatedHeadForward = 0.2f;
        [Tooltip("Seconds off the ground before the jump pose plays, so walking down bumps doesn't trigger it.")]
        [SerializeField] float airborneAfter = 0.25f;
        [Tooltip("Where the boots go when you take them off: right of and slightly ahead of where you sit (metres).")]
        [SerializeField] Vector2 bootsAside = new(0.5f, 0.15f);

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int CrouchId = Animator.StringToHash("Crouch");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int BusyId = Animator.StringToHash("Busy");
        static readonly int SwingId = Animator.StringToHash("Swing");
        static readonly int SeatedId = Animator.StringToHash("Seated");

        CharacterProfile profile;
        Survival.Backpack backpack;
        bool shownFirstPerson;
        float airTime;
        float seated;
        GameObject bootsPile;

        /// <summary>The current hiker. Saved with the trip.</summary>
        public CharacterProfile Profile => profile ?? (profile = CharacterProfile.LoadLast());

        void OnEnable() => Undergrowth.Swung += OnSwung;
        void OnDisable()
        {
            Undergrowth.Swung -= OnSwung;
            PutBootsOn();
        }

        void Start()
        {
            // Until a trip is started or loaded, show the last hiker made on this machine.
            if (!appearance.IsBuilt)
                Apply(Profile);
        }

        /// <summary>Becomes this hiker: their looks, name and background traits.</summary>
        public void Apply(CharacterProfile newProfile)
        {
            profile = newProfile.Clone();
            PutBootsOn();
            appearance.Build(profile);
            Backgrounds.ApplyTraits(profile.background);
            TripLog.HikerName = profile.name;
            // Scale the model so its eyes are where the camera is.
            float eyes = appearance.EyeHeight;
            appearance.transform.localScale = Vector3.one * (eyes > 0.5f ? 1.68f / eyes : 1f);
            shownFirstPerson = !player.ThirdPerson;
            ApplyView();
        }

        void Update()
        {
            if (!appearance.IsBuilt)
                return;
            if (shownFirstPerson == player.ThirdPerson)
            {
                shownFirstPerson = !player.ThirdPerson;
                ApplyView();
            }

            Animator animator = appearance.Animator;
            if (animator == null)
                return;
            airTime = player.IsGrounded ? 0f : airTime + Time.deltaTime;
            animator.SetFloat(SpeedId, player.HorizontalSpeed, 0.1f, Time.deltaTime);
            animator.SetBool(CrouchId, player.IsCrouching);
            animator.SetBool(GroundedId, airTime < airborneAfter);
            animator.SetBool(BusyId, activity.IsBusy && !activity.IsSleeping);
            animator.SetBool(SeatedId, player.Seated);
            UpdatePose();
            UpdateBoots();
            if (backpack == null)
                backpack = GetComponent<Survival.Backpack>();
            if (backpack != null)
                appearance.SetPackWorn(backpack.IsWorn);
        }

        void UpdatePose()
        {
            HikerPose pose = appearance.Pose;
            if (pose == null)
                return;
            // Sink to the ground at the same pace as the camera lowers.
            seated = Mathf.MoveTowards(seated, player.Seated ? 1f : 0f, Time.deltaTime * 2.5f);
            pose.Seated = seated;
            if (shownFirstPerson)
                appearance.transform.localPosition = new Vector3(0f, 0f, Mathf.Lerp(-firstPersonSetBack, seatedHeadForward, Mathf.SmoothStep(0f, 1f, seated)));
            pose.GroundFeet = player.IsGrounded && !activity.IsBusy;
            // Seen from behind, the head and shoulders follow the view, except while busy with a task (the clip
            // knows where to look). In first person the head is hidden and turning the chest would only crowd the view.
            Camera view = Camera.main;
            pose.LookTarget = view != null && player.ThirdPerson && !activity.IsBusy
                ? view.transform.position + view.transform.forward * 20f : null;
        }

        /// <summary>Bare feet while the boots are off, and the boots and socks on the ground beside you.</summary>
        void UpdateBoots()
        {
            bool off = RestMode.Current != null && RestMode.Current.IsSeated && RestMode.Current.BootsOff;
            if (off == (bootsPile != null))
                return;
            if (!off)
            {
                PutBootsOn();
                return;
            }
            appearance.SetBootsOn(false);
            bootsPile = appearance.BuildBootsAndSocks();
            Transform body = appearance.transform;
            Vector3 spot = body.position + body.right * bootsAside.x + body.forward * bootsAside.y;
            Quaternion facing = Quaternion.LookRotation(body.forward, Vector3.up) * Quaternion.Euler(0f, 15f, 0f);
            // Set them on whatever is under that spot: ground, a rock, the tent floor.
            if (Physics.Raycast(spot + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 2.4f, ~0, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(transform))
            {
                spot = hit.point;
                facing = Quaternion.FromToRotation(Vector3.up, hit.normal) * facing;
            }
            bootsPile.transform.SetPositionAndRotation(spot, facing);
        }

        void PutBootsOn()
        {
            if (bootsPile != null)
                Destroy(bootsPile);
            bootsPile = null;
            if (appearance != null)
                appearance.SetBootsOn(true);
        }

        void ApplyView()
        {
            appearance.SetFirstPerson(shownFirstPerson);
            appearance.transform.localPosition = shownFirstPerson ? new Vector3(0f, 0f, -firstPersonSetBack) : Vector3.zero;
        }

        void OnSwung()
        {
            if (appearance.Animator != null)
                appearance.Animator.SetTrigger(SwingId);
        }
    }
}
