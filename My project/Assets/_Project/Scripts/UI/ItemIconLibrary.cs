using System;
using UnityEngine;

namespace Backpacking.UI
{
    /// <summary>
    /// Pictures of every item, rendered from their models by the editor's item-icon tool, for the backpack screen
    /// and the hotbar. Looked up by key: "water", "machete", "food-TrailMix", "garment-Fleece" and so on.
    /// </summary>
    [CreateAssetMenu(menuName = "Backpacking/Item Icon Library", fileName = "ItemIconLibrary")]
    public class ItemIconLibrary : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string key;
            public Texture2D icon;
        }

        public Entry[] entries;

        public Texture2D Get(string key)
        {
            if (entries == null || string.IsNullOrEmpty(key))
                return null;
            foreach (Entry entry in entries)
                if (entry.key == key)
                    return entry.icon;
            return null;
        }

        /// <summary>The picture for a garment, by what kind of garment its name says it is.</summary>
        public static string GarmentKey(string garment)
        {
            string name = garment.ToLowerInvariant();
            return name.Contains("hat") ? "garment-hat"
                : name.Contains("pants") ? "garment-pants"
                : name.Contains("shell") || name.Contains("rain") ? "garment-shell"
                : name.Contains("down") ? "garment-down"
                : name.Contains("fleece") ? "garment-fleece"
                : "garment-base";
        }
    }
}
