using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.UI;
using Backpacking.World;
using UnityEngine;
using UnityEngine.InputSystem;

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
        GUIStyle statusStyle, biteStyle;

        public bool IsFishing => state != State.Idle;

        void Awake() => interactAction = inputActions.FindActionMap("Player", true).FindAction("Interact", true);

        public void Begin()
        {
            if (IsFishing || PlayerControlLock.MovementLocked)
                return;

            caughtThisSession = 0;
            lastResult = null;
            PlayerControlLock.Lock(this, needsCursor: false);
            timeOfDay.RequestSpeed(this, timeMultiplier);
            StartWaiting();
        }

        public void Stop()
        {
            if (!IsFishing)
                return;
            state = State.Idle;
            PlayerControlLock.Unlock(this);
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

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                Stop();
                return;
            }

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
                    timer = backpack.HasGoodRod ? goodRodBiteWindow : biteWindow;
                }
            }
            else if (state == State.Bite)
            {
                if (hook)
                {
                    if (Random.value < (backpack.HasGoodRod ? goodRodLandChance : landChance))
                    {
                        backpack.AddFood(FoodKind.RawFish);
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

        void OnGUI()
        {
            if (!IsFishing)
                return;

            statusStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            biteStyle ??= new GUIStyle(statusStyle) { fontSize = 44, fontStyle = FontStyle.Bold };

            float y = Screen.height * 0.62f;
            if (state == State.Bite)
            {
                biteStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
                GUI.Label(new Rect(0f, y - 70f, Screen.width, 60f), "BITE!  Press E", biteStyle);
            }
            else if (lastResult != null && Time.time - lastResultTime < 2.5f)
                GUI.Label(new Rect(0f, y - 50f, Screen.width, 30f), lastResult, statusStyle);

            string status = $"Fishing...  {timeOfDay.ClockText}   Caught: {caughtThisSession}     ·     E to hook  ·  Right-click to stop";
            GUI.Label(new Rect(0f, y, Screen.width, 30f), status, statusStyle);
        }
    }
}
