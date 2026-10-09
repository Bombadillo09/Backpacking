using System.Collections.Generic;
using Backpacking.Interaction;
using Backpacking.Survival;
using Backpacking.UI;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// A backpack set down on the ground. Your own: look at it to put it back on or take the tent out of it; while it's
    /// down, the backpack screen only opens when you're beside it. Made by <see cref="PackHandling"/>. A friend's (in
    /// co-op): look at it to go through it, moving things between it and your own pack; its contents are a copy of
    /// theirs, kept in step over the network (see Net.CoopWorld).
    /// </summary>
    public class GroundPack : MonoBehaviour, IInteractable
    {
        [SerializeField] GearLibrary gear;

        PackVisual visual;

        /// <summary>A friend's pack: its contents, as their game last sent them. Null for your own.</summary>
        public Backpack Contents { get; private set; }
        public string OwnerName { get; private set; }
        public bool IsFriends => Contents != null;

        public string DisplayName => IsFriends ? $"{OwnerName}'s backpack" : "Your backpack";

        void Awake()
        {
            if (gear != null)
            {
                var body = new GameObject("Pack").transform;
                body.SetParent(transform, false);
                // Standing on its base, harness side down the back.
                visual = PackDesign.Build(body, gear.pack, harness: true, gear.bottle, gear.machete);
            }
        }

        /// <summary>Makes this a friend's pack, holding a copy of what's in it.</summary>
        public void MakeFriends(string ownerName, Backpack contents)
        {
            OwnerName = ownerName;
            Contents = contents;
        }

        /// <summary>Shows the gear strapped on outside, as it is in the backpack.</summary>
        public void ShowGear(Backpack backpack, bool bottleInHand, bool macheteInHand)
        {
            if (visual == null)
                return;
            backpack.OutsideGear(out bool pad, out bool tent, out bool chair, out bool bottle, out bool machete);
            visual.Show(pad, tent, chair, bottle && !bottleInHand, machete && !macheteInHand);
            visual.Tint(TentDesign.Of(backpack.TentModel).Fly);
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            if (IsFriends)
            {
                PackHandling mine = PackHandling.Current;
                options.Add(new InteractionOption($"Go through {OwnerName}'s pack", () => PackingView.Current?.Open(Contents, OwnerName),
                    PackingView.Current == null ? ""
                    : !interactor.Backpack.HasPack ? "You need a pack of your own to put things in"
                    : mine != null && !mine.CanReachPack ? "Your own pack is too far away to put things in"
                    : null));
                return;
            }
            PackHandling handling = PackHandling.Current;
            if (handling == null)
                return;
            options.Add(new InteractionOption("Put your pack on", handling.PickUp));
            if (interactor.Backpack.HasStove)
                options.Add(new InteractionOption("Take out the stove and set it up", () => interactor.Placer.BeginPlacement(CampItem.Stove)));
            if (interactor.Backpack.HasChair && interactor.Backpack.ChairInPack)
                options.Add(new InteractionOption("Take out the camp chair", () => interactor.Placer.BeginPlacement(CampItem.Chair)));
            if (interactor.Backpack.HasTent)
                options.Add(new InteractionOption("Take out the tent bag", () => handling.TakeOutTent(),
                    handling.TentBag != null ? "The tent bag is already out" : null));
            else if (handling.TentBag != null)
                options.Add(new InteractionOption("Pack the tent bag away", handling.PackTentBag, handling.TentBagProblem()));
        }

        /// <summary>Tints the bag panels (children named "Bag...") to the hiker's pack colour.</summary>
        public void SetColour(Color colour)
        {
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", colour);
            foreach (Renderer part in GetComponentsInChildren<Renderer>())
                if (part.name.StartsWith("Bag"))
                    part.SetPropertyBlock(tint);
        }
    }
}
