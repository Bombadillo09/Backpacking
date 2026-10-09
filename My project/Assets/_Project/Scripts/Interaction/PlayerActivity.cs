using System;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.UI;
using Backpacking.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.Interaction
{
    /// <summary>
    /// Runs tasks that take game time, like pitching a tent or boiling water. The player stands still,
    /// a progress bar shows, and the clock is sped up so the task's game minutes pass in a few real seconds.
    /// Also handles sleeping through the night.
    /// </summary>
    public class PlayerActivity : MonoBehaviour
    {
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] Vitals vitals;
        [Tooltip("Real seconds a timed task takes on screen, however long it is in game time.")]
        [SerializeField] float taskRealSeconds = 2.5f;

        [Header("Sleep")]
        [SerializeField, Range(0f, 24f)] float wakeHour = 6.5f;
        [Tooltip("Nap length in hours when sleeping during the day.")]
        [SerializeField] float napHours = 2f;
        [Tooltip("Game hours that pass per real second while asleep.")]
        [SerializeField] float sleepHoursPerSecond = 1.2f;
        [Tooltip("Warmth level that wakes you up shivering.")]
        [SerializeField] float wakeWhenWarmthBelow = 20f;
        [Tooltip("Hours spent unconscious after collapsing from exhaustion.")]
        [SerializeField] float passOutHours = 3f;

        bool unconscious;
        string label;
        float durationHours;
        float elapsedHours;
        Action onComplete;
        bool sleeping;
        VisualElement progressPanel, progressFill, sleepOverlay;
        Label taskLabel, sleepLabel;

        bool sleepingInTent;

        public bool IsBusy => label != null;
        public bool IsSleeping => sleeping;

        /// <summary>Raised on waking up, whether rested or woken by the cold. The argument says if it was in a tent.</summary>
        public event Action<bool> WokeUp;

        /// <summary>Starts a task lasting <paramref name="gameMinutes"/> of game time. Ignored if already busy.</summary>
        public bool Begin(string taskLabel, float gameMinutes, Action completed)
        {
            if (IsBusy)
                return false;

            label = taskLabel;
            durationHours = gameMinutes / 60f;
            elapsedHours = 0f;
            onComplete = completed;
            sleeping = false;

            float multiplier = durationHours / taskRealSeconds / timeOfDay.BaseHoursPerSecond;
            timeOfDay.RequestSpeed(this, Mathf.Max(1f, multiplier));
            PlayerControlLock.Lock(this, needsCursor: false);
            return true;
        }

        /// <summary>
        /// Sleeps until morning, or naps during the day. Wakes early if too cold. In the sleeping bag and on the mat
        /// only if they're laid out.
        /// </summary>
        public bool Sleep(bool inTent, bool inBag = true, bool onMat = true)
        {
            float hour = timeOfDay.Hour;
            bool night = hour >= 18f || hour < wakeHour;
            if (!Begin("Sleeping", 0f, null))
                return false;

            StartSleeping(night ? Mathf.Repeat(wakeHour - hour, 24f) : napHours, inTent, inBag, onMat);
            return true;
        }

        /// <summary>
        /// Collapses from exhaustion where the player stands: no tent, no sleeping bag, and the cold
        /// doesn't wake you. Cancels whatever the player was doing.
        /// </summary>
        public void PassOut()
        {
            Interrupt();
            Begin("Passed out", 0f, null);
            StartSleeping(passOutHours, inTent: false, inBag: false, onMat: false);
            unconscious = true;
            Notifications.Post("You collapse from exhaustion.");
        }

        /// <summary>Stops the current task or sleep without finishing it.</summary>
        public void Interrupt()
        {
            if (IsBusy)
                Finish();
        }

        void StartSleeping(float hours, bool inTent, bool inBag, bool onMat)
        {
            vitals.OnMat = onMat;
            sleeping = true;
            durationHours = hours;
            vitals.IsSleeping = true;
            vitals.IsSheltered = inTent;
            vitals.InSleepingBag = inBag;
            sleepingInTent = inTent;
            timeOfDay.RequestSpeed(this, sleepHoursPerSecond / timeOfDay.BaseHoursPerSecond);
        }

        void Update()
        {
            if (!IsBusy)
                return;

            elapsedHours += Time.deltaTime * timeOfDay.HoursPerSecond;

            if (sleeping && !unconscious && vitals.Warmth < wakeWhenWarmthBelow)
            {
                Finish();
                Notifications.Post("You wake up shivering. It's too cold to sleep. Warm up or add layers.");
                WokeUp?.Invoke(sleepingInTent);
                return;
            }

            if (elapsedHours >= durationHours)
            {
                bool wasSleeping = sleeping, wasUnconscious = unconscious;
                Action completed = onComplete;
                Finish();
                completed?.Invoke();
                if (wasSleeping)
                {
                    Notifications.Post(wasUnconscious
                        ? $"You come to on the cold ground. It's {timeOfDay.ClockText}."
                        : $"You wake up. It's {timeOfDay.ClockText}.");
                    WokeUp?.Invoke(sleepingInTent);
                }
            }
        }

        void Finish()
        {
            label = null;
            onComplete = null;
            if (sleeping)
            {
                vitals.IsSleeping = false;
                vitals.IsSheltered = false;
                vitals.InSleepingBag = false;
                vitals.OnMat = false;
                sleeping = false;
                unconscious = false;
            }
            timeOfDay.ClearSpeed(this);
            PlayerControlLock.Unlock(this);
        }

        void OnDisable()
        {
            if (IsBusy)
                Finish();
        }

        void Start()
        {
            taskLabel = UIBuild.Text("", "progress-label", "shadowed");
            progressPanel = UIBuild.Box("progress").With(taskLabel, UIBuild.Bar(out progressFill));
            sleepLabel = UIBuild.Text("", "fade-label");
            sleepOverlay = UIBuild.Layer("fade", "centred").With(sleepLabel);
            progressPanel.SetVisible(false);
            sleepOverlay.SetVisible(false);
            GameUI.Current.Overlay.With(progressPanel.IgnoreMouse(), sleepOverlay.IgnoreMouse());
        }

        void LateUpdate()
        {
            if (progressPanel == null)
                return;
            progressPanel.SetVisible(IsBusy && !sleeping);
            sleepOverlay.SetVisible(IsBusy && sleeping);
            if (!IsBusy)
                return;

            if (sleeping)
            {
                // Fade to near-black while asleep.
                sleepOverlay.style.opacity = Mathf.Clamp01(elapsedHours * 4f) * 0.92f;
                sleepLabel.SetText($"{(unconscious ? "Unconscious..." : "Sleeping...")}   {timeOfDay.ClockText}");
            }
            else
            {
                taskLabel.SetText(label);
                progressFill.SetFill(durationHours > 0f ? elapsedHours / durationHours : 1f);
            }
        }
    }
}
