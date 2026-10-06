using System;
using UnityEngine;

namespace Backpacking.Character
{
    public enum Background
    {
        WeekendHiker,
        Ranger,
        Angler,
        Ultralight,
        Forager,
    }

    /// <summary>Who the hiker is and how they look. Chosen when starting a trip and saved with it.</summary>
    [Serializable]
    public class CharacterProfile
    {
        public string name = "Sam";
        [Tooltip("Which person from the roster (CharacterLibrary.Hiker.id).")]
        public string hiker = "Male_Adult_04"; // the Weekend Hiker look, matching the default background
        public Color packColour = new(0.18f, 0.32f, 0.42f);
        public Background background;

        public CharacterProfile Clone() => JsonUtility.FromJson<CharacterProfile>(JsonUtility.ToJson(this));

        const string PrefsKey = "character.lastProfile";

        /// <summary>The hiker last created on this machine, as a starting point for the next.</summary>
        public static CharacterProfile LoadLast()
        {
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json))
                return new CharacterProfile();
            try
            {
                return JsonUtility.FromJson<CharacterProfile>(json) ?? new CharacterProfile();
            }
            catch (ArgumentException)
            {
                return new CharacterProfile();
            }
        }

        public void SaveAsLast()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        /// <summary>Outdoor-gear colours for the pack.</summary>
        public static readonly Color[] GearColours =
        {
            new(0.75f, 0.32f, 0.12f),
            new(0.72f, 0.15f, 0.12f),
            new(0.85f, 0.65f, 0.15f),
            new(0.3f, 0.45f, 0.22f),
            new(0.3f, 0.32f, 0.26f),
            new(0.18f, 0.32f, 0.42f),
            new(0.2f, 0.22f, 0.3f),
            new(0.45f, 0.36f, 0.26f),
            new(0.12f, 0.12f, 0.13f),
        };
    }
}
