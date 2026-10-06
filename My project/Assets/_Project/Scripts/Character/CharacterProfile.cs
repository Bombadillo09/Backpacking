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
        public bool female;
        [Range(0f, 1f)] public float skinTone = 0.25f;
        public int hairStyle = 1;
        public Color hairColour = new(0.3f, 0.2f, 0.12f);
        public bool beard;
        public Color jacketColour = new(0.75f, 0.32f, 0.12f);
        public Color pantsColour = new(0.3f, 0.32f, 0.26f);
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

        /// <summary>Skin from fair to deep brown, as a tint over the light skin texture.</summary>
        public static Color SkinTint(float tone)
        {
            Color[] stops =
            {
                new(1f, 0.97f, 0.95f),
                new(0.93f, 0.8f, 0.68f),
                new(0.68f, 0.5f, 0.37f),
                new(0.4f, 0.27f, 0.19f),
            };
            float scaled = Mathf.Clamp01(tone) * (stops.Length - 1);
            int i = Mathf.Min(stops.Length - 2, Mathf.FloorToInt(scaled));
            return Color.Lerp(stops[i], stops[i + 1], scaled - i);
        }

        public static readonly Color[] HairColours =
        {
            new(0.08f, 0.06f, 0.05f),
            new(0.3f, 0.2f, 0.12f),
            new(0.55f, 0.38f, 0.22f),
            new(0.85f, 0.7f, 0.45f),
            new(0.62f, 0.25f, 0.1f),
            new(0.75f, 0.75f, 0.75f),
        };

        /// <summary>Outdoor-gear colours for the jacket, pants and pack.</summary>
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
