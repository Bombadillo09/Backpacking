using System;
using System.Collections.Generic;
using Backpacking.Audio;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.World
{
    /// <summary>One hinged leaf of a door: the pivot it swings on, and how far it turns to open.</summary>
    [Serializable]
    public struct DoorLeaf
    {
        public Transform hinge;
        [Tooltip("Degrees about the hinge (local Y) when fully open. Closed is 0.")]
        public float openAngle;
    }

    /// <summary>
    /// A door you open and close: look at it and press E. Its leaves (one, or two for a double door) swing on their
    /// hinges over a moment, solid all the way, with a creak and a thud.
    /// </summary>
    public class Door : MonoBehaviour, IInteractable
    {
        [SerializeField] string doorName = "Door";
        [SerializeField] DoorLeaf[] leaves = Array.Empty<DoorLeaf>();
        [SerializeField] bool startOpen;
        [Tooltip("Degrees per second the leaves swing.")]
        [SerializeField] float swingSpeed = 180f;

        bool open;
        float amount;

        public string DisplayName => doorName;
        public bool IsOpen => open;

        /// <summary>Raised when the player opens or closes a door (not when <see cref="SetOpen"/> does), for co-op.</summary>
        public static event Action<Door> Toggled;

        void Awake()
        {
            open = startOpen;
            amount = open ? 1f : 0f;
            Apply();
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options) =>
            options.Add(new InteractionOption(open ? "Close the door" : "Open the door", Toggle));

        public void Toggle()
        {
            SetOpen(!open);
            Toggled?.Invoke(this);
        }

        /// <summary>Swings the door open or shut (someone else's hand, in co-op).</summary>
        public void SetOpen(bool shouldOpen)
        {
            if (open == shouldOpen)
                return;
            open = shouldOpen;
            if (!open)
                Invoke(nameof(Thud), 0.45f);
        }

        void Thud() => AudioSource.PlayClipAtPoint(Sounds.DoorShut(), transform.position, 0.5f);

        void Update()
        {
            float target = open ? 1f : 0f;
            if (Mathf.Approximately(amount, target))
                return;
            float widest = 1f;
            foreach (DoorLeaf leaf in leaves)
                widest = Mathf.Max(widest, Mathf.Abs(leaf.openAngle));
            amount = Mathf.MoveTowards(amount, target, swingSpeed / widest * Time.deltaTime);
            Apply();
        }

        void Apply()
        {
            // Eased, so the door slows as it reaches the end of its swing.
            float eased = Mathf.SmoothStep(0f, 1f, amount);
            foreach (DoorLeaf leaf in leaves)
                if (leaf.hinge != null)
                    leaf.hinge.localRotation = Quaternion.Euler(0f, leaf.openAngle * eased, 0f);
        }
    }
}
