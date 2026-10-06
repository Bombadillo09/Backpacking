using System;
using UnityEngine;

namespace Backpacking.UI
{
    /// <summary>Player preferences from the settings screen, kept between sessions in PlayerPrefs.</summary>
    public static class GameSettings
    {
        const string Prefix = "settings.";

        /// <summary>Raised whenever a setting changes, so systems can apply it.</summary>
        public static event Action Changed;

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(Prefix + "volume", 0.8f);
            set => Set("volume", Mathf.Clamp01(value));
        }

        /// <summary>Degrees per pixel of mouse movement.</summary>
        public static float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat(Prefix + "mouse", 0.1f);
            set => Set("mouse", Mathf.Clamp(value, 0.02f, 0.4f));
        }

        public static bool InvertMouseY
        {
            get => PlayerPrefs.GetInt(Prefix + "invertY", 0) == 1;
            set => Set("invertY", value ? 1 : 0);
        }

        public static float FieldOfView
        {
            get => PlayerPrefs.GetFloat(Prefix + "fov", 70f);
            set => Set("fov", Mathf.Clamp(value, 55f, 95f));
        }

        /// <summary>Shows the list of keys in the corner of the HUD.</summary>
        public static bool ShowControlHints
        {
            get => PlayerPrefs.GetInt(Prefix + "hints", 1) == 1;
            set => Set("hints", value ? 1 : 0);
        }

        public static bool Fullscreen
        {
            get => Screen.fullScreen;
            set => Screen.fullScreen = value;
        }

        static void Set(string key, float value)
        {
            PlayerPrefs.SetFloat(Prefix + key, value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        static void Set(string key, int value)
        {
            PlayerPrefs.SetInt(Prefix + key, value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Changed = null;
    }
}
