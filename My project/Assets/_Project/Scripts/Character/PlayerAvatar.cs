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
        [SerializeField] float firstPersonSetBack = 0.22f;
        [Tooltip("Seconds off the ground before the jump pose plays, so walking down bumps doesn't trigger it.")]
        [SerializeField] float airborneAfter = 0.25f;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int CrouchId = Animator.StringToHash("Crouch");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int BusyId = Animator.StringToHash("Busy");
        static readonly int SwingId = Animator.StringToHash("Swing");
        static readonly int SeatedId = Animator.StringToHash("Seated");

        CharacterProfile profile;
        bool shownFirstPerson;
        float airTime;

        /// <summary>The current hiker. Saved with the trip.</summary>
        public CharacterProfile Profile => profile ?? (profile = CharacterProfile.LoadLast());

        void OnEnable() => Undergrowth.Swung += OnSwung;
        void OnDisable() => Undergrowth.Swung -= OnSwung;

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
