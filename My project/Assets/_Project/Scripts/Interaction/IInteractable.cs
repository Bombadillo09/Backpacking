using System;
using System.Collections.Generic;

namespace Backpacking.Interaction
{
    /// <summary>One thing the player can do with an object, such as "Add firewood".</summary>
    public readonly struct InteractionOption
    {
        public readonly string Label;
        public readonly Action Execute;
        /// <summary>Why the option can't be used right now, or null if it can.</summary>
        public readonly string DisabledReason;

        public bool Enabled => DisabledReason == null;

        public InteractionOption(string label, Action execute, string disabledReason = null)
        {
            Label = label;
            Execute = execute;
            DisabledReason = disabledReason;
        }
    }

    /// <summary>
    /// Anything the player can look at and press Interact on. Put it on the object with the collider
    /// or on one of its parents.
    /// </summary>
    public interface IInteractable
    {
        string DisplayName { get; }

        /// <summary>Adds the options available right now. Disabled options are shown with their reason.</summary>
        void GetOptions(Interactor interactor, List<InteractionOption> options);
    }
}
