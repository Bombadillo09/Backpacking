using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Trip;
using Backpacking.UI;
using Backpacking.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Backpacking.Gathering
{
    /// <summary>
    /// Fishing from the shore. Wait for a bite, then press Interact before the fish lets go.
    /// Game time runs faster while fishing, and fish bite sooner around dawn and dusk.
    /// </summary>
    public class FishingSession : MonoBehaviour
    {
        enum State
        {
            Idle,
            Waiting,
            Bite,
        }

        [SerializeField] InputActionAsset inputActions;
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] Backpack backpack;

        [Tooltip("How much faster game time runs while fishing.")]
        [SerializeField] float timeMultiplier = 10f;
        [Tooltip("Real seconds between bites, min and max.")]
        [SerializeField] Vector2 biteWaitSeconds = new(3f, 9f);
        [Tooltip("Waits are this much shorter around dawn and dusk.")]
        [SerializeField, Range(0.1f, 1f)] float goldenHourWaitFactor = 0.5f;
        [Tooltip("Real seconds you have to react once a fish bites.")]
        [SerializeField] float biteWindow = 0.9f;
        [Tooltip("Chance a hooked fish stays on the line.")]
        [SerializeField, Range(0f, 1f)] float landChance = 0.8f;

        [Header("With a Good Rod")]
        [SerializeField] float goodRodBiteWindow = 1.4f;
        [SerializeField, Range(0f, 1f)] float goodRodLandChance = 0.95f;

        InputAction interactAction;
        State state;
        float timer;
        int caughtThisSession;
        string lastResult;
        float lastResultTime;
        VisualElement panel;
        Label bite, result, status;

        public bool IsFishing => state != State.Idle;

        void Awake() => interactAction = inputActions.FindActionMap("Player", true).FindAction("Interact", true);

        public void Begin()
        {
            if (IsFishing || PlayerControlLock.MovementLocked)
                return;

            caughtThisSession = 0;
            lastResult = null;
            PlayerControlLock.Lock(this, needsCursor: false);
            GameUI.ClaimEscape(this, Stop);
            timeOfDay.RequestSpeed(this, timeMultiplier);
            StartWaiting();
        }

        public void Stop()
        {
            if (!IsFishing)
                return;
            state = State.Idle;
            PlayerControlLock.Unlock(this);
            GameUI.ReleaseEscape(this);
            timeOfDay.ClearSpeed(this);
            Notifications.Post(caughtThisSession > 0
                ? $"You reel in your line with {caughtThisSession} trout."
                : "You reel in your line. Nothing today.");
        }

        void OnDisable() => Stop();

        void StartWaiting()
        {
            float hour = timeOfDay.Hour;
            bool goldenHour = hour is >= 5f and < 9f or >= 17f and < 21f;
            timer = Random.Range(biteWaitSeconds.x, biteWaitSeconds.y) * (goldenHour ? goldenHourWaitFactor : 1f);
            state = State.Waiting;
        }

        void Update()
        {
            if (!IsFishing)
                return;

            bool hook = interactAction.WasPressedThisFrame();
            timer -= Time.deltaTime;

            if (state == State.Waiting)
            {
                if (hook)
                {
                    ShowResult("Too early. You pulled the bait away.");
                    StartWaiting();
                }
                else if (timer <= 0f)
                {
                    state = State.Bite;
                    timer = (backpack.HasGoodRod ? goodRodBiteWindow : biteWindow) * HikerTraits.BiteWindowFactor;
                }
            }
            else if (state == State.Bite)
            {
                if (hook)
                {
                    if (Random.value < (backpack.HasGoodRod ? goodRodLandChance : landChance) + HikerTraits.LandChanceBonus)
                    {
                        backpack.AddFood(FoodKind.RawFish);
                        TripLog.Tally(TripStat.Fish);
                        caughtThisSession++;
                        ShowResult("You land a trout!");
                    }
                    else
                        ShowResult("It shook itself off the hook.");
                    StartWaiting();
                }
                else if (timer <= 0f)
                {
                    ShowResult("Missed it.");
                    StartWaiting();
                }
            }
        }

        void ShowResult(string result)
        {
            lastResult = result;
            lastResultTime = Time.time;
        }

        void Start()
        {
            bite = UIBuild.Text("BITE!  Press E", "bite", "shadowed");
            result = UIBuild.Text("", "progress-label", "shadowed");
            status = UIBuild.Text("", "progress-label", "shadowed");
            panel = UIBuild.Box("progress").With(bite, result, status);
            panel.style.top = Length.Percent(52f);
            panel.SetVisible(false);
            GameUI.Current.Overlay.Add(panel.IgnoreMouse());
        }

        void LateUpdate()
        {
            if (panel == null)
                return;
            panel.SetVisible(IsFishing);
            if (!IsFishing)
                return;

            bite.SetVisible(state == State.Bite);
            bool showResult = state != State.Bite && lastResult != null && Time.time - lastResultTime < 2.5f;
            result.SetVisible(showResult);
            if (showResult)
                result.SetText(lastResult);
            status.SetText($"Fishing...  {timeOfDay.ClockText}   Caught: {caughtThisSession}     ·     E / Y to hook  ·  Right-click, B or Esc to stop");
        }
    }
}
