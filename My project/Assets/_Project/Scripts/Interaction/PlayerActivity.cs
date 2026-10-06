using System;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.UI;
using Backpacking.World;
using UnityEngine;

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
        GUIStyle labelStyle;

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

        /// <summary>Sleeps until morning, or naps during the day. Wakes early if too cold.</summary>
        public bool Sleep(bool inTent)
        {
            float hour = timeOfDay.Hour;
            bool night = hour >= 18f || hour < wakeHour;
            if (!Begin("Sleeping", 0f, null))
                return false;

            StartSleeping(night ? Mathf.Repeat(wakeHour - hour, 24f) : napHours, inTent, inBag: true);
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
            StartSleeping(passOutHours, inTent: false, inBag: false);
            unconscious = true;
            Notifications.Post("You collapse from exhaustion.");
        }

        /// <summary>Stops the current task or sleep without finishing it.</summary>
        public void Interrupt()
        {
            if (IsBusy)
                Finish();
        }

        void StartSleeping(float hours, bool inTent, bool inBag)
        {
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

        void OnGUI()
        {
            if (!IsBusy)
                return;

            labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
            float progress = durationHours > 0f ? Mathf.Clamp01(elapsedHours / durationHours) : 1f;

            if (sleeping)
            {
                // Fade to near-black while asleep.
                GUI.color = new Color(0f, 0f, 0.02f, Mathf.Clamp01(elapsedHours * 4f) * 0.92f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                string text = unconscious ? "Unconscious..." : "Sleeping...";
                GUI.Label(new Rect(0f, Screen.height * 0.45f, Screen.width, 30f), $"{text}   {timeOfDay.ClockText}", labelStyle);
                return;
            }

            const float width = 360f;
            var bar = new Rect((Screen.width - width) / 2f, Screen.height * 0.7f, width, 14f);
            GUI.Label(new Rect(0f, bar.y - 34f, Screen.width, 30f), label, labelStyle);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(0.95f, 0.8f, 0.4f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * progress, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
