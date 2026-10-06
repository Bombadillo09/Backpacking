using System.Collections.Generic;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.Trip
{
    /// <summary>The weatherproof box on the summit with a notebook inside. Signing it finishes the thru-hike.</summary>
    public class SummitRegister : MonoBehaviour, IInteractable
    {
        public string DisplayName => "Summit register";

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            TripLog log = TripLog.Current;
            if (log == null)
                return;
            if (!log.IsFinished)
                options.Add(new InteractionOption("Sign the register and finish your thru-hike", log.Finish));
            else
                options.Add(new InteractionOption("Read your entry in the register", UI.JournalView.ShowSummary));
        }
    }
}
