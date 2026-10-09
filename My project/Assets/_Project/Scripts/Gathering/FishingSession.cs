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
    /// Game time runs faster while fishing, and fish bite sooner around dawn and dusk. Cast at a spot on the water,
    /// a float bobs there on the end of the line; it twitches and is dragged under when a fish bites.
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
        Vector3 spot;
        Transform bobber;
        LineRenderer line;
        Camera view;

        public bool IsFishing => state != State.Idle;

        void Awake() => interactAction = inputActions.FindActionMap("Player", true).FindAction("Interact", true);

        /// <summary>Casts at <paramref name="at"/> on the water (a few metres out from you if it's not given).</summary>
        public void Begin(Vector3? at = null)
        {
            if (IsFishing || PlayerControlLock.MovementLocked)
                return;

            view = Camera.main;
            Transform eyes = view != null ? view.transform : transform;
            Vector3 ahead = Vector3.ProjectOnPlane(eyes.forward, Vector3.up).normalized;
            spot = at ?? eyes.position + ahead * 6f;
            ShowTackle(true);

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
            ShowTackle(false);
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

        void ShowTackle(bool shown)
        {
            if (shown && bobber == null)
            {
                // A red-and-white float, and the line up to the rod tip.
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                Material Plain(Color colour)
                {
                    var material = new Material(shader);
                    material.SetColor("_BaseColor", colour);
                    material.SetColor("_Color", colour);
                    return material;
                }
                bobber = new GameObject("Fishing Float").transform;
                foreach ((Vector3 at, Vector3 size, Color colour) in new[]
                         {
                             (new Vector3(0f, 0.015f, 0f), new Vector3(0.05f, 0.03f, 0.05f), new Color(0.85f, 0.1f, 0.08f)),
                             (new Vector3(0f, -0.012f, 0f), new Vector3(0.048f, 0.026f, 0.048f), new Color(0.95f, 0.95f, 0.92f)),
                         })
                {
                    GameObject part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(part.GetComponent<Collider>());
                    part.transform.SetParent(bobber, false);
                    part.transform.localPosition = at;
                    part.transform.localScale = size;
                    part.GetComponent<Renderer>().sharedMaterial = Plain(colour);
                }
                line = bobber.gameObject.AddComponent<LineRenderer>();
                line.sharedMaterial = Plain(new Color(0.85f, 0.85f, 0.8f));
                line.widthMultiplier = 0.0035f;
                line.positionCount = 12;
                line.numCapVertices = 0;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (bobber != null)
                bobber.gameObject.SetActive(shown);
        }

        void UpdateTackle()
        {
            if (bobber == null || !bobber.gameObject.activeSelf)
                return;
            // Bobbing on the ripples; twitching while a fish is at it, then dragged under.
            float bob = Mathf.Sin(Time.time * 2.1f) * 0.008f + Mathf.Sin(Time.time * 3.7f) * 0.004f;
            if (state == State.Bite)
                bob = -0.05f + Mathf.Sin(Time.time * 40f) * 0.012f;
            else if (state == State.Waiting && timer < 0.6f)
                bob += Mathf.Sin(Time.time * 30f) * 0.01f * (0.6f - timer);
            bobber.position = spot + Vector3.up * bob;

            // The line sags from just up and out from the view (the rod tip) down to the float.
            Transform eyes = view != null ? view.transform : transform;
            Vector3 tip = eyes.position + eyes.forward * 1.2f + eyes.up * 0.55f + eyes.right * 0.35f;
            for (int i = 0; i < line.positionCount; i++)
            {
                float t = i / (line.positionCount - 1f);
                float sag = Mathf.Sin(t * Mathf.PI) * Mathf.Min(0.6f, Vector3.Distance(tip, bobber.position) * 0.05f);
                line.SetPosition(i, Vector3.Lerp(tip, bobber.position + Vector3.up * 0.03f, t) + Vector3.down * sag);
            }
        }

        void OnDestroy()
        {
            if (bobber != null)
                Destroy(bobber.gameObject);
        }

        void LateUpdate()
        {
            UpdateTackle();
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
