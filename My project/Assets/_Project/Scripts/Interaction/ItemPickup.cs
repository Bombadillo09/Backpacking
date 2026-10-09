using System.Collections.Generic;
using Backpacking.Survival;
using Backpacking.UI;
using UnityEngine;

namespace Backpacking.Interaction
{
    /// <summary>What an <see cref="ItemPickup"/> gives you. Scenes store this by number: new kinds go at the end.</summary>
    public enum PickupKind
    {
        Daypack,
        BottledWater,
        TrailMix,
    }

    /// <summary>
    /// Something lying about at home to take with you: the old daypack in the bedroom, a bottle of water in the
    /// fridge, trail mix in the cupboard. Gone once taken (saved, and the same for everyone in co-op).
    /// </summary>
    public class ItemPickup : MonoBehaviour, IInteractable
    {
        [SerializeField] PickupKind kind;
        [SerializeField] string itemName = "Daypack";
        [Tooltip("For the daypack: the pack's look, built small when it appears.")]
        [SerializeField] Camp.GearLibrary gear;
        [SerializeField] Color colour = new(0.22f, 0.36f, 0.55f);

        public string DisplayName => itemName;
        public PickupKind Kind => kind;

        void Awake()
        {
            if (kind != PickupKind.Daypack || gear == null)
                return;
            // The same pack as one set down on the ground, at a daypack's size, empty and faded.
            var body = new GameObject("Pack").transform;
            body.SetParent(transform, false);
            body.localScale = Vector3.one * PackModels.Get(PackModel.Daypack25).VisualScale;
            Camp.PackVisual visual = Camp.PackDesign.Build(body, gear.pack, harness: true);
            visual.Show(false, false, false, false, false);
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", colour);
            foreach (Renderer part in GetComponentsInChildren<Renderer>())
                if (part.name.StartsWith("Bag"))
                    part.SetPropertyBlock(tint);
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            Backpack backpack = interactor.Backpack;
            string what = itemName.ToLowerInvariant();
            switch (kind)
            {
                case PickupKind.Daypack:
                    options.Add(new InteractionOption($"Put on the {what}", () =>
                    {
                        backpack.SetPack(PackModel.Daypack25);
                        backpack.IsWorn = true;
                        Notifications.Post($"You shoulder your old {what}. It's small, but it's a start: the outdoor store sells bigger packs.", 5f);
                        Destroy(gameObject);
                    }, backpack.HasPack ? "You already have a pack" : null));
                    break;
                case PickupKind.BottledWater:
                    options.Add(new InteractionOption($"Take the {what}", () =>
                    {
                        backpack.AddBottledWater(0.5f);
                        Notifications.Post($"You take the {what}: half a litre of clean water. It's on your hotbar.", 3f);
                        Destroy(gameObject);
                    }));
                    break;
                case PickupKind.TrailMix:
                    options.Add(new InteractionOption($"Take the {what}", () =>
                    {
                        backpack.AddFood(FoodKind.TrailMix);
                        backpack.AssignHotbar(new HotbarSlot(HotbarKind.Food, FoodKind.TrailMix));
                        Notifications.Post($"You take the {what}. It's on your hotbar.", 3f);
                        Destroy(gameObject);
                    }));
                    break;
            }
        }
    }
}
