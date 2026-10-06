using System.Collections.Generic;
using Backpacking.Player;
using Backpacking.Saving;
using Backpacking.Survival;
using Backpacking.Trip;
using Backpacking.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.UI
{
    /// <summary>
    /// The trip journal (J, or from the backpack): the hike so far, day by day, with its statistics.
    /// Also the end screen shown when the summit register is signed, which saves the trip and pauses
    /// the world until you choose to keep exploring or head back to the title.
    /// </summary>
    public class JournalView : MonoBehaviour
    {
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] SaveSystem saves;
        [SerializeField] Vitals vitals;
        [SerializeField] Backpack backpack;

        static JournalView current;

        VisualElement journalScreen, summaryScreen;
        ScrollView entryList;
        Label journalStats, summaryTitle, summaryRating;
        VisualElement summaryStats;
        bool summaryOpen;
        VisualElement journalPage;
        BodyStatusPanel status;
        Button journalTab, statusTab;
        bool showingStatus;
        float nextStatusRefresh;

        /// <summary>True once the Status page has been opened this session (the tutorial asks you to).</summary>
        public static bool StatusViewed { get; private set; }

        public bool IsOpen { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            StatusViewed = false;
        }

        /// <summary>Shows the end-of-trip screen (from the summit register).</summary>
        public static void ShowSummary()
        {
            if (current != null)
                current.OpenSummary();
        }

        /// <summary>Opens the journal, e.g. from the backpack.</summary>
        public static void ShowJournal()
        {
            if (current != null && !current.IsOpen)
                current.OpenJournal();
        }

        /// <summary>Opens the journal on its Status page.</summary>
        public static void ShowStatus()
        {
            if (current == null)
                return;
            if (!current.IsOpen)
                current.OpenJournal();
            current.ShowTab(status: true);
        }

        void Awake() => current = this;

        void OnEnable() => TripLog.Finished += OnFinished;
        void OnDisable() => TripLog.Finished -= OnFinished;

        void Start()
        {
            entryList = new ScrollView();
            entryList.style.height = 560f;
            journalStats = UIBuild.Text("", "small");
            journalPage = UIBuild.Box().With(
                UIBuild.Text($"Destination: {TripLog.Destination}", "small"),
                journalStats,
                entryList);
            status = new BodyStatusPanel(vitals, backpack);
            journalTab = UIBuild.Button("Journal", () => ShowTab(status: false));
            statusTab = UIBuild.Button("Status", () => ShowTab(status: true));
            VisualElement journalPanel = UIBuild.Box("panel").With(
                UIBuild.Box("panel-header").With(
                    UIBuild.Text("Trip Journal", "title"),
                    UIBuild.Box("row").With(journalTab, statusTab)),
                journalPage,
                status.Root,
                UIBuild.Box("footer").With(UIBuild.Button("Close  (J)", CloseJournal)));
            journalPanel.style.width = 900f;
            journalScreen = UIBuild.Layer("centred").With(journalPanel);
            journalScreen.SetVisible(false);
            GameUI.Current.Screens.Add(journalScreen);

            summaryTitle = UIBuild.Text("", "game-title", "shadowed");
            summaryRating = UIBuild.Text("", "game-subtitle", "shadowed");
            summaryStats = UIBuild.Box();
            VisualElement summaryPanel = UIBuild.Box("panel", "menu-panel").With(
                summaryStats,
                UIBuild.Box("footer").With(
                    UIBuild.Button("Read the journal", () =>
                    {
                        CloseSummary();
                        OpenJournal();
                    }),
                    UIBuild.Button("Return to title", saves.ReturnToTitle),
                    UIBuild.Button("Keep exploring", CloseSummary, "primary")));
            summaryPanel.style.width = 640f;
            summaryScreen = UIBuild.Layer("screen-dim", "centred").With(summaryTitle, summaryRating, summaryPanel);
            summaryScreen.pickingMode = PickingMode.Position;
            summaryScreen.SetVisible(false);
            GameUI.Current.Menus.Add(summaryScreen);
        }

        void Update()
        {
            if (IsOpen && showingStatus && Time.unscaledTime >= nextStatusRefresh)
            {
                nextStatusRefresh = Time.unscaledTime + 0.5f;
                status.Refresh();
            }
            if (!GameInput.JournalPressed || summaryOpen)
                return;
            if (IsOpen)
                CloseJournal();
            else if (!PlayerControlLock.MovementLocked && !PlayerControlLock.JustReleased)
                OpenJournal();
        }

        // ---------- Journal ----------

        void OpenJournal()
        {
            TripLog log = TripLog.Current;
            if (log == null)
                return;
            IsOpen = true;
            journalStats.text = $"Day {timeOfDay.Day} on the trail  ·  {log.DistanceKm:0.0} km walked  ·  highest point {log.HighestAltitude:0} m"
                                + (log.IsFinished ? "  ·  thru-hike complete" : "");
            FillEntries(log);
            ShowTab(showingStatus);
            journalScreen.SetVisible(true);
            journalScreen.FocusFirstButton();
            PlayerControlLock.Lock(this, needsCursor: true);
            GameUI.ClaimEscape(this, CloseJournal);
        }

        void ShowTab(bool status)
        {
            showingStatus = status;
            journalPage.SetVisible(!status);
            this.status.Root.SetVisible(status);
            SetSelected(journalTab, !status);
            SetSelected(statusTab, status);
            if (status)
            {
                StatusViewed = true;
                this.status.Refresh();
            }
        }

        static void SetSelected(VisualElement button, bool selected)
        {
            if (selected)
                button.AddToClassList("selected");
            else
                button.RemoveFromClassList("selected");
        }

        void CloseJournal()
        {
            IsOpen = false;
            journalScreen.SetVisible(false);
            PlayerControlLock.Unlock(this);
            GameUI.ReleaseEscape(this);
        }

        void FillEntries(TripLog log)
        {
            entryList.Clear();
            int shownDay = -1;
            foreach (JournalEntry entry in log.Entries)
            {
                if (entry.day != shownDay)
                {
                    shownDay = entry.day;
                    entryList.Add(UIBuild.Text($"DAY {entry.day}", "heading"));
                }
                string time = entry.hour >= 23.98f ? "" : TimeOfDay.FormatClock(entry.hour);
                Label clock = UIBuild.Text(time, "reason");
                clock.style.width = 60f;
                entryList.Add(UIBuild.Box("list-row").With(clock, UIBuild.Text(entry.text, "text", "grow")));
            }
            // Newest at the bottom, so start scrolled there.
            entryList.schedule.Execute(() => entryList.scrollOffset = new Vector2(0f, float.MaxValue));
        }

        // ---------- End of the trip ----------

        void OnFinished()
        {
            saves.Save();
            OpenSummary();
        }

        void OpenSummary()
        {
            TripLog log = TripLog.Current;
            if (log == null || summaryOpen)
                return;
            if (IsOpen)
                CloseJournal();
            GameUI.CloseAllScreens();

            summaryTitle.text = log.IsFinished ? "THRU-HIKE COMPLETE" : TripLog.Destination.ToUpperInvariant();
            summaryRating.text = string.IsNullOrEmpty(TripLog.HikerName) ? log.TrailTitle() : $"{TripLog.HikerName}  ·  {log.TrailTitle()}";
            summaryStats.Clear();
            foreach ((string label, string value) in Stats(log))
                summaryStats.Add(UIBuild.Box("row", "spread").With(UIBuild.Text(label), UIBuild.Text(value, "money")));

            summaryOpen = true;
            Time.timeScale = 0f;
            GameUI.Current.Hud.SetVisible(false);
            summaryScreen.SetVisible(true);
            summaryScreen.FocusFirstButton();
            PlayerControlLock.Lock(this, needsCursor: true);
        }

        void CloseSummary()
        {
            summaryOpen = false;
            Time.timeScale = 1f;
            GameUI.Current.Hud.SetVisible(true);
            summaryScreen.SetVisible(false);
            PlayerControlLock.Unlock(this);
        }

        static IEnumerable<(string, string)> Stats(TripLog log)
        {
            yield return ("Days on the trail", log.DaysOnTrail.ToString());
            yield return ("Distance walked", $"{log.DistanceKm:0.0} km");
            yield return ("Highest point", $"{log.HighestAltitude:0} m");
            yield return ("Coldest it felt", log.ColdestFelt < 1000f ? $"{log.ColdestFelt:0} °C" : "-");
            yield return ("Nights in the tent", log.Stat(TripStat.NightsInTent).ToString());
            yield return ("Nights out in the open", log.Stat(TripStat.NightsOutside).ToString());
            yield return ("Trout caught", log.Stat(TripStat.Fish).ToString());
            yield return ("Rabbits snared", log.Stat(TripStat.Rabbits).ToString());
            yield return ("Times collapsed", log.Stat(TripStat.Collapses).ToString());
            yield return ("Rescues", log.Stat(TripStat.Rescues).ToString());
        }
    }
}
