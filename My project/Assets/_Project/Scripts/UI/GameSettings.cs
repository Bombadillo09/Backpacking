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

        /// <summary>The world's background sounds (wind, rain, birds, crickets, water), 0 to 1, under the master volume.</summary>
        public static float AmbienceVolume
        {
            get => PlayerPrefs.GetFloat(Prefix + "ambience", 0.5f);
            set => Set("ambience", Mathf.Clamp01(value));
        }

        /// <summary>Waits for the monitor's refresh before each frame: no tearing, frame rate capped at the refresh rate.</summary>
        public static bool VSync
        {
            get => PlayerPrefs.GetInt(Prefix + "vsync", 1) == 1;
            set => Set("vsync", value ? 1 : 0);
        }

        /// <summary>Puts the display settings (V-sync) into effect.</summary>
        public static void ApplyDisplay()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            // Without V-sync, no cap: as fast as it'll go.
            Application.targetFrameRate = -1;
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

        /// <summary>How much the view bobs and sways when walking, 0 (none) to 1 (full).</summary>
        public static float HeadBob
        {
            get => PlayerPrefs.GetFloat(Prefix + "headBob", 1f);
            set => Set("headBob", Mathf.Clamp01(value));
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
