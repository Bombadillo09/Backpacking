using System;
using Backpacking.Survival;
using UnityEngine;

namespace Backpacking.Player
{
    /// <summary>
    /// The models shown in the hiker's hand for hotbar items. Each prefab's origin is where the hand grips it, with
    /// +Y running along the item (handle to tip) and +Z its front (a blade's edge). Built by the editor's held-item
    /// setup from the Poly Haven models in Art/Held; foods without a model are made from simple shapes.
    /// </summary>
    [CreateAssetMenu(menuName = "Backpacking/Held Item Library", fileName = "HeldItemLibrary")]
    public class HeldItemLibrary : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public HotbarKind kind;
            [Tooltip("For food: which food. Ignored otherwise.")]
            public FoodKind food;
            public GameObject prefab;
        }

        public Entry[] entries;
        [Tooltip("A plain URP Lit material, tinted per part, for foods made from simple shapes.")]
        public Material plain;

        public GameObject PrefabFor(HotbarSlot slot)
        {
            if (entries == null)
                return null;
            foreach (Entry entry in entries)
                if (entry.prefab != null && entry.kind == slot.kind && (slot.kind != HotbarKind.Food || entry.food == slot.food))
                    return entry.prefab;
            return null;
        }
    }
}
