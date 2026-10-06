using Backpacking.Interaction;
using Backpacking.Navigation;
using Backpacking.Player;
using Backpacking.Saving;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Survival
{
    /// <summary>
    /// What happens when the body gives out. Running out of energy makes you pass out where you stand.
    /// Running out of health means a search party carries you to the nearest trading post you've reached.
    /// You lose time and pay for the rescue, then the game saves so it sticks.
    /// </summary>
    public class Rescue : MonoBehaviour
    {
        [SerializeField] Vitals vitals;
        [SerializeField] PlayerActivity activity;
        [SerializeField] FirstPersonController player;
        [SerializeField] Backpack backpack;
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] SaveSystem saves;

        [Header("Rescue")]
        [Tooltip("Fraction of your money the rescue costs.")]
        [SerializeField, Range(0f, 1f)] float feeFraction = 0.5f;
        [Tooltip("The least a rescue costs, if you have it.")]
        [SerializeField, Min(0)] int minimumFee = 40;
        [Tooltip("Minimum game hours between collapsing and waking up at the post.")]
        [SerializeField] float minimumHoursLost = 10f;
        [Tooltip("Hour of the morning you're back on your feet.")]
        [SerializeField, Range(0f, 24f)] float wakeHour = 8f;

        [Header("Condition After Rescue")]
        [SerializeField] float healthAfter = 40f;
        [SerializeField] float satietyAfter = 55f;
        [SerializeField] float hydrationAfter = 65f;
        [SerializeField] float warmthAfter = 75f;
        [SerializeField] float energyAfter = 70f;

        const float FadeOutSeconds = 2.5f;
        const float FadeInSeconds = 1.5f;

        enum Stage { None, FadingOut, Report, FadingIn }

        Stage stage;
        float stageStartTime;
        string cause;
        string report;
        GUIStyle titleStyle, textStyle;

        public bool InProgress => stage != Stage.None;

        void OnEnable()
        {
            vitals.Collapsed += OnCollapsed;
            vitals.Incapacitated += OnIncapacitated;
        }

        void OnDisable()
        {
            vitals.Collapsed -= OnCollapsed;
            vitals.Incapacitated -= OnIncapacitated;
            if (stage != Stage.None)
            {
                Time.timeScale = 1f;
                PlayerControlLock.Unlock(this);
            }
        }

        void OnCollapsed()
        {
            if (stage == Stage.None)
                activity.PassOut();
        }

        void OnIncapacitated()
        {
            // Work out the cause now, while the vitals still show it.
            cause = vitals.IsHypothermic ? "hypothermic and barely conscious"
                : vitals.IsDehydrated ? "severely dehydrated"
                : vitals.IsStarving ? "weak from starvation"
                : "too sick to stand";

            activity.Interrupt();
            PlayerControlLock.Lock(this, needsCursor: false);
            SetStage(Stage.FadingOut);
        }

        void Update()
        {
            float elapsed = Time.unscaledTime - stageStartTime;
            if (stage == Stage.FadingOut && elapsed >= FadeOutSeconds)
            {
                CarryToSafety();
                // Pause while the player reads what happened.
                Time.timeScale = 0f;
                PlayerControlLock.Lock(this, needsCursor: true);
                SetStage(Stage.Report);
            }
            else if (stage == Stage.FadingIn && elapsed >= FadeInSeconds)
                SetStage(Stage.None);
        }

        void SetStage(Stage next)
        {
            stage = next;
            stageStartTime = Time.unscaledTime;
        }

        void CarryToSafety()
        {
            NavigationPoint post = NearestTradingPost();
            float hoursLost = AdvanceClock();

            int fee = Mathf.Min(backpack.Money, Mathf.Max(minimumFee, Mathf.RoundToInt(backpack.Money * feeFraction)));
            backpack.TrySpendMoney(fee);
            vitals.Recover(healthAfter, satietyAfter, hydrationAfter, warmthAfter, energyAfter);

            string postName = post != null ? post.DisplayName : "the trailhead";
            if (post != null)
                PlaceNear(post);

            string bill = fee > 0
                ? $"The search and rescue bill came to ${fee}."
                : "You had no money, so the rescue team waived their fee. This time.";
            report =
                $"A search party found you {cause} and carried you to {postName}.\n\n" +
                $"You lost {hoursLost:0} hours recovering. {bill}\n\n" +
                "Anything you left set up out there is still where you left it.";

            saves.Save();
        }

        NavigationPoint NearestTradingPost()
        {
            NavigationPoint best = null, fallback = null;
            float bestDistance = float.MaxValue, fallbackDistance = float.MaxValue;
            Vector3 here = player.transform.position;
            foreach (NavigationPoint point in NavigationPoint.All)
            {
                if (point.Kind != NavigationPointKind.TradingPost)
                    continue;
                float distance = Vector3.Distance(here, point.transform.position);
                if (point.Visited && distance < bestDistance)
                {
                    best = point;
                    bestDistance = distance;
                }
                if (distance < fallbackDistance)
                {
                    fallback = point;
                    fallbackDistance = distance;
                }
            }
            return best != null ? best : fallback;
        }

        /// <summary>Moves the clock to the next morning at least <see cref="minimumHoursLost"/> away. Returns hours passed.</summary>
        float AdvanceClock()
        {
            float now = timeOfDay.TotalHours;
            float wake = Mathf.Floor(now / 24f) * 24f + wakeHour;
            while (wake < now + minimumHoursLost)
                wake += 24f;
            timeOfDay.SetDayAndTime(Mathf.FloorToInt(wake / 24f) + 1, wake % 24f);
            return wake - now;
        }

        void PlaceNear(NavigationPoint post)
        {
            // Trading posts stand just west of their cairn, so put the player east of it, facing the counter.
            Vector3 position = post.transform.position + new Vector3(6f, 0f, 0f);
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
                position.y = terrain.SampleHeight(position) + terrain.transform.position.y + 0.1f;

            // A CharacterController overrides direct moves, so switch it off while teleporting.
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, -90f, 0f));
            controller.enabled = true;
        }

        void CarryOn()
        {
            Time.timeScale = 1f;
            PlayerControlLock.Unlock(this);
            SetStage(Stage.FadingIn);
        }

        void OnGUI()
        {
            // Draw over every other screen.
            GUI.depth = -100;
            if (stage == Stage.None)
                return;

            float elapsed = Time.unscaledTime - stageStartTime;
            float black = stage switch
            {
                Stage.FadingOut => Mathf.Clamp01(elapsed / FadeOutSeconds),
                Stage.FadingIn => 1f - Mathf.Clamp01(elapsed / FadeInSeconds),
                _ => 1f,
            };
            GUI.color = new Color(0f, 0f, 0f, black);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (stage != Stage.Report)
                return;

            titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            textStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.UpperCenter, wordWrap = true };

            const float width = 560f, height = 300f;
            var area = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            GUI.Label(new Rect(area.x, area.y, width, 40f), "You were rescued", titleStyle);
            GUI.Label(new Rect(area.x + 10f, area.y + 60f, width - 20f, 170f), report, textStyle);
            if (GUI.Button(new Rect(area.x + 150f, area.y + 245f, width - 300f, 38f), "Carry on"))
                CarryOn();
        }
    }
}
