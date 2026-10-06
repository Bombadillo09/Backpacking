using System;
using Backpacking.Character;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Saving;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.UI
{
    /// <summary>
    /// The title menu shown on start (continue the saved trip or start a new one), the pause menu on Esc,
    /// and the settings and controls pages both of them open. The world stays paused while either is up.
    /// </summary>
    public class GameMenus : MonoBehaviour
    {
        [SerializeField] SaveSystem saves;
        [SerializeField] CharacterCreator creator;
        [SerializeField] PlayerAvatar avatar;
        [SerializeField] Backpack backpack;
        [SerializeField] Tutorial tutorial;

        // Keyboard and mouse | gamepad | what it does.
        const string ControlsText =
            "WASD|Left stick|Walk\nMouse|Right stick|Look around\nShift|Left stick press|Sprint\n" +
            "C|Right stick press|Crouch\nSpace|A|Jump\nE|Y|Interact, place gear, hook a fish\n" +
            "Tab|View|Backpack, leave a shop\nJ|Backpack > Journal|Trip journal\nV|RB|First or third person\nM|D-pad up|Map\nQ|D-pad down|Compass\nHold T|Hold LB|Fast-forward time\n" +
            "Right-click|B|Back, cancel placing, stop fishing\nEsc|Start|Close screen, pause\nF5 / F9|-|Quick-save / quick-load";

        enum Page { None, Title, Pause, Settings, Controls, Confirm, Creator }

        Page page;
        Page returnPage;
        VisualElement screen;
        VisualElement titlePage, pausePage, settingsPage, controlsPage, confirmPage;
        Button continueButton;
        Label continueSummary, confirmText;
        Button loadButton;
        Button skipTutorialButton;
        Action confirmAction;
        float timeScaleBeforePause = 1f;
        bool onTitle;

        public bool IsOpen => page != Page.None;

        void OnEnable()
        {
            GameUI.EscapeUnhandled += OnEscape;
            GameUI.CancelUnhandled += OnCancel;
            GameSettings.Changed += ApplyAudio;
        }

        void OnDisable()
        {
            GameUI.EscapeUnhandled -= OnEscape;
            GameUI.CancelUnhandled -= OnCancel;
            GameSettings.Changed -= ApplyAudio;
            AudioListener.pause = false;
        }

        void Start()
        {
            ApplyAudio();
            BuildPages();
            if (!SaveSystem.LoadingOnSceneStart)
                ShowTitle();
        }

        static void ApplyAudio() => AudioListener.volume = GameSettings.MasterVolume;

        // ---------- Pages ----------

        void BuildPages()
        {
            continueButton = UIBuild.Button("Continue trip", ContinueTrip, "menu", "primary");
            continueSummary = UIBuild.Text("", "save-summary");
            titlePage = UIBuild.Box("centred").With(
                UIBuild.Text("BACKPACKING", "game-title", "shadowed"),
                UIBuild.Text("hike  ·  camp  ·  survive", "game-subtitle", "shadowed"),
                UIBuild.Box("panel", "menu-panel").With(
                    continueButton,
                    continueSummary,
                    UIBuild.Button("New trip", NewTrip, "menu"),
                    UIBuild.Button("Settings", () => ShowPage(Page.Settings), "menu"),
                    UIBuild.Button("Controls", () => ShowPage(Page.Controls), "menu"),
                    UIBuild.Button("Quit", QuitGame, "menu", "quiet")));

            loadButton = UIBuild.Button("Load last save", () => Confirm("Load your last save? Progress since then will be lost.", saves.QuickLoad), "menu");
            pausePage = UIBuild.Box("panel", "menu-panel").With(
                UIBuild.Text("Paused", "title"),
                UIBuild.Button("Resume", Resume, "menu", "primary"),
                skipTutorialButton = UIBuild.Button("Skip tutorial", () =>
                {
                    if (tutorial != null)
                        tutorial.Stop();
                    PlayerPrefs.SetInt("tutorial.done", 1);
                    Resume();
                }, "menu"),
                UIBuild.Button("Save trip", () =>
                {
                    saves.Save();
                    Resume();
                }, "menu"),
                loadButton,
                UIBuild.Button("Settings", () => ShowPage(Page.Settings), "menu"),
                UIBuild.Button("Controls", () => ShowPage(Page.Controls), "menu"),
                UIBuild.Button("Quit to title", () => Confirm("Quit to the title screen? Progress since your last save will be lost.", saves.ReturnToTitle), "menu"),
                UIBuild.Button("Quit game", () => Confirm("Quit the game? Progress since your last save will be lost.", QuitGame), "menu", "quiet"));

            settingsPage = BuildSettings();
            controlsPage = BuildControls();

            confirmText = UIBuild.Text("", "text");
            confirmPage = UIBuild.Box("panel", "menu-panel").With(
                confirmText,
                UIBuild.Box("footer").With(
                    UIBuild.Button("Cancel", Back),
                    UIBuild.Button("Yes", () =>
                    {
                        Action action = confirmAction;
                        HideAll();
                        action?.Invoke();
                    }, "primary")));
            confirmPage.style.maxWidth = 520f;

            screen = UIBuild.Layer("screen-dim", "centred").With(titlePage, pausePage, settingsPage, controlsPage, confirmPage);
            screen.pickingMode = PickingMode.Position;
            GameUI.Current.Menus.Add(screen);
            ShowPage(Page.None);
        }

        VisualElement BuildSettings()
        {
            var fullscreen = new Toggle { value = GameSettings.Fullscreen };
            fullscreen.RegisterValueChangedCallback(change => GameSettings.Fullscreen = change.newValue);

            VisualElement panel = UIBuild.Box("panel", "menu-panel").With(
                UIBuild.Text("Settings", "title"),
                SliderRow("Master volume", 0f, 1f, GameSettings.MasterVolume, value => GameSettings.MasterVolume = value, value => $"{value * 100f:0}%"),
                SliderRow("Mouse sensitivity", 0.02f, 0.4f, GameSettings.MouseSensitivity, value => GameSettings.MouseSensitivity = value, value => $"{value * 10f:0.0}"),
                SliderRow("Field of view", 55f, 95f, GameSettings.FieldOfView, value => GameSettings.FieldOfView = value, value => $"{value:0}°"),
                SliderRow("Head bob", 0f, 1f, GameSettings.HeadBob, value => GameSettings.HeadBob = value, value => $"{value * 100f:0}%"),
                ToggleRow("Invert mouse Y", GameSettings.InvertMouseY, value => GameSettings.InvertMouseY = value),
                ToggleRow("Show control hints", GameSettings.ShowControlHints, value => GameSettings.ShowControlHints = value),
                UIBuild.Box("setting").With(UIBuild.Text("Fullscreen", "setting-label"), fullscreen),
                UIBuild.Box("footer").With(UIBuild.Button("Back", Back, "primary")));
            panel.style.width = 600f;
            return panel;
        }

        static VisualElement SliderRow(string label, float min, float max, float value, Action<float> set, Func<float, string> format)
        {
            var slider = new Slider(min, max) { value = value };
            Label shown = UIBuild.Text(format(value), "setting-value");
            slider.RegisterValueChangedCallback(change =>
            {
                set(change.newValue);
                shown.text = format(change.newValue);
            });
            return UIBuild.Box("setting").With(UIBuild.Text(label, "setting-label"), slider, shown);
        }

        static VisualElement ToggleRow(string label, bool value, Action<bool> set)
        {
            var toggle = new Toggle { value = value };
            toggle.RegisterValueChangedCallback(change => set(change.newValue));
            return UIBuild.Box("setting").With(UIBuild.Text(label, "setting-label"), toggle);
        }

        VisualElement BuildControls()
        {
            VisualElement panel = UIBuild.Box("panel", "menu-panel").With(
                UIBuild.Text("Controls", "title"),
                UIBuild.Box("row").With(
                    UIBuild.Text("Keyboard & mouse", "controls-key", "small"),
                    UIBuild.Text("Gamepad", "controls-key", "small")));
            foreach (string line in ControlsText.Split('\n'))
            {
                string[] parts = line.Split('|');
                panel.Add(UIBuild.Box("row").With(
                    UIBuild.Text(parts[0], "controls-key"),
                    UIBuild.Text(parts[1], "controls-key"),
                    UIBuild.Text(parts[2])));
            }
            panel.Add(UIBuild.Box("footer").With(UIBuild.Button("Back", Back, "primary")));
            panel.style.width = 760f;
            return panel;
        }

        // ---------- Navigation ----------

        void ShowPage(Page next)
        {
            if ((next is Page.Settings or Page.Controls or Page.Confirm) && (page is Page.Title or Page.Pause))
                returnPage = page;
            page = next;

            // The character creator draws its own screen.
            screen.SetVisible(next is not Page.None and not Page.Creator);
            titlePage.SetVisible(next == Page.Title);
            pausePage.SetVisible(next == Page.Pause);
            settingsPage.SetVisible(next == Page.Settings);
            controlsPage.SetVisible(next == Page.Controls);
            confirmPage.SetVisible(next == Page.Confirm);
            // The HUD would only clutter the title screen.
            GameUI.Current.Hud.SetVisible(!onTitle);

            if (next == Page.Pause)
            {
                loadButton.SetEnabled(saves.HasSave);
                skipTutorialButton.SetVisible(tutorial != null && tutorial.IsRunning);
            }
            if (next != Page.None)
                screen.FocusFirstButton();
        }

        void Back()
        {
            if (page is Page.Settings or Page.Controls or Page.Confirm)
                ShowPage(returnPage);
            else if (page == Page.Pause)
                Resume();
        }

        void Confirm(string question, Action action)
        {
            confirmText.text = question;
            confirmAction = action;
            ShowPage(Page.Confirm);
        }

        /// <summary>
        /// Esc reaches here only when no other screen is open: it pauses, steps back out of a sub-page,
        /// or resumes. It does nothing on the title page itself.
        /// </summary>
        void OnEscape()
        {
            if (page == Page.None)
            {
                if (!PlayerControlLock.CursorNeeded)
                    Pause();
            }
            else if (page == Page.Creator)
                creator.Back();
            else if (page != Page.Title)
                Back();
        }

        /// <summary>Right-click / B steps back out of a menu page, but never pauses or leaves the title.</summary>
        void OnCancel()
        {
            if (page == Page.Creator)
                creator.Back();
            else if (page is not Page.None and not Page.Title)
                Back();
        }

        void Freeze()
        {
            timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            PlayerControlLock.Lock(this, needsCursor: true);
        }

        void Unfreeze()
        {
            Time.timeScale = timeScaleBeforePause;
            AudioListener.pause = false;
            PlayerControlLock.Unlock(this);
        }

        void HideAll()
        {
            Unfreeze();
            onTitle = false;
            returnPage = Page.None;
            ShowPage(Page.None);
        }

        // ---------- Title ----------

        void ShowTitle()
        {
            Freeze();
            onTitle = true;
            string summary = saves.SavedSummary();
            continueButton.SetVisible(summary != null);
            continueSummary.SetVisible(summary != null);
            continueSummary.text = summary ?? "";
            returnPage = Page.Title;
            ShowPage(Page.Title);
        }

        void ContinueTrip()
        {
            HideAll();
            saves.ContinueSavedTrip();
        }

        /// <summary>Make a hiker first; the trip starts when they're ready.</summary>
        void NewTrip()
        {
            if (creator == null)
            {
                BeginTrip(avatar != null ? avatar.Profile : new CharacterProfile());
                return;
            }
            ShowPage(Page.Creator);
            creator.Open(BeginTrip, () => ShowPage(Page.Title));
        }

        void BeginTrip(CharacterProfile hiker)
        {
            HideAll();
            if (avatar != null)
                avatar.Apply(hiker);
            if (backpack != null)
                Backgrounds.ApplyStartingKit(hiker.background, backpack);
            if (Trip.TripLog.Current != null)
                Trip.TripLog.Current.BeginTrip($"{hiker.name} set out from Trailhead Outfitter as a {Backgrounds.Name(hiker.background)}, "
                                               + $"heading north along the route for {Trip.TripLog.Destination}.");
            if (tutorial != null)
            {
                if (creator == null || creator.TutorialWanted)
                {
                    tutorial.Begin(hiker.name);
                    PlayerPrefs.SetInt("tutorial.done", 1);
                    return;
                }
                tutorial.Stop();
            }
            string replaces = saves.HasSave ? " Your next save replaces the old trip." : "";
            Notifications.Post($"Your goal, {hiker.name}: hike north along the route to {Trip.TripLog.Destination} and sign the summit register. "
                               + $"Check your map (M).{replaces}", 12f);
        }

        // ---------- Pause ----------

        void Pause()
        {
            Freeze();
            AudioListener.pause = true;
            ShowPage(Page.Pause);
        }

        void Resume() => HideAll();

        static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
