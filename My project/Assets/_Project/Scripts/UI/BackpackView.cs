using Backpacking.Camp;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backpacking.UI
{
    /// <summary>
    /// The backpack screen (Tab): water, food, fuel, clothing layers, and buttons to set up camp gear.
    /// </summary>
    public class BackpackView : MonoBehaviour
    {
        [SerializeField] Backpack backpack;
        [SerializeField] Vitals vitals;
        [SerializeField] CampPlacer placer;
        [SerializeField] PlayerActivity activity;
        [SerializeField] Key toggleKey = Key.Tab;
        [SerializeField] float cleanRabbitMinutes = 15f;
        [SerializeField] int meatPerRabbit = 2;

        GUIStyle headingStyle, textStyle, reasonStyle;
        Vector2 foodScroll;

        public bool IsOpen { get; private set; }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[toggleKey].wasPressedThisFrame)
                return;

            if (IsOpen)
                Close();
            else if (!PlayerControlLock.MovementLocked && !PlayerControlLock.JustReleased)
                Open();
        }

        void Open()
        {
            placer.CancelPlacement();
            IsOpen = true;
            PlayerControlLock.Lock(this, needsCursor: true);
        }

        void Close()
        {
            IsOpen = false;
            PlayerControlLock.Unlock(this);
        }

        void OnDisable()
        {
            if (IsOpen)
                Close();
        }

        void OnGUI()
        {
            if (!IsOpen)
                return;

            if (headingStyle == null)
            {
                headingStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
                textStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
                reasonStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Italic };
            }

            const float width = 820f, height = 640f;
            var area = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);

            GUILayout.BeginArea(new Rect(area.x + 20f, area.y + 14f, width - 40f, height - 28f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Backpack", new GUIStyle(headingStyle) { fontSize = 22 });
            GUILayout.FlexibleSpace();
            string pelts = backpack.Pelts > 0 ? $"    Rabbit pelts: {backpack.Pelts}" : "";
            GUILayout.Label($"Money: ${backpack.Money}{pelts}", new GUIStyle(headingStyle) { fontSize = 20 });
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(400f));
            DrawWater();
            GUILayout.Space(12f);
            DrawFood();
            GUILayout.EndVertical();

            GUILayout.Space(20f);

            GUILayout.BeginVertical();
            DrawCampGear();
            GUILayout.Space(10f);
            DrawFuel();
            GUILayout.Space(10f);
            DrawClothing();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close  (Tab)", GUILayout.Height(28f)))
                Close();
            GUILayout.EndArea();
        }

        void DrawWater()
        {
            GUILayout.Label("Water", headingStyle);
            GUILayout.Label($"Bottle: {backpack.TotalWater:0.00} / {backpack.WaterCapacity:0.0} L", textStyle);
            GUILayout.Label($"   Safe: {backpack.SafeWater:0.00} L    Untreated: {backpack.UntreatedWater:0.00} L", textStyle);
            GUILayout.BeginHorizontal();
            ActionButton($"Drink safe ({backpack.SipLitres:0.00} L)", backpack.DrinkSafeWater,
                backpack.SafeWater <= 0f ? "No safe water" : null);
            ActionButton("Drink untreated", backpack.DrinkUntreatedWater,
                backpack.UntreatedWater <= 0f ? "No untreated water" : null);
            GUILayout.EndHorizontal();
            GUILayout.Label(backpack.HasWaterFilter
                ? "Your filter makes lake and stream water safe as you fill up."
                : "Untreated water may make you sick. Boil it first.", reasonStyle);
        }

        void DrawFood()
        {
            GUILayout.Label("Food", headingStyle);
            foodScroll = GUILayout.BeginScrollView(foodScroll, GUILayout.Height(300f));
            bool any = false;
            foreach (FoodKind kind in FoodCatalog.AllKinds)
            {
                int count = backpack.CountFood(kind);
                if (count == 0)
                    continue;
                any = true;
                FoodInfo info = FoodCatalog.Get(kind);

                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(230f));
                GUILayout.Label($"{info.Name}  ×{count}", textStyle);
                GUILayout.Label(Describe(kind, info), reasonStyle);
                GUILayout.EndVertical();

                if (kind == FoodKind.RabbitCarcass)
                    ActionButton("Clean", () =>
                    {
                        Close();
                        activity.Begin("Cleaning the rabbit", cleanRabbitMinutes, () => backpack.CleanCarcass(meatPerRabbit));
                    }, null);
                else
                    ActionButton(info.SicknessChance > 0f ? "Eat (risky)" : "Eat", () => backpack.Eat(kind), info.NotEdibleReason);
                GUILayout.EndHorizontal();
            }
            if (!any)
                GUILayout.Label("No food. Forage, fish or set snares.", textStyle);
            GUILayout.EndScrollView();
        }

        string Describe(FoodKind kind, FoodInfo info)
        {
            string keeps = !info.Spoils ? "keeps" : $"next spoils in {FormatHours(backpack.SoonestSpoilHours(kind))}";
            string prep = info.SmokesInto != null ? info.CooksInto != null ? " · cook or smoke" : " · can smoke" : "";
            return info.Satiety > 0f ? $"+{info.Satiety:0} food · {keeps}{prep}" : $"{keeps}{prep}";
        }

        static string FormatHours(float hours) => hours >= 48f ? $"{hours / 24f:0} days" : $"{Mathf.CeilToInt(hours)} h";

        void DrawCampGear()
        {
            GUILayout.Label("Set Up Camp", headingStyle);
            PlaceButton("Pitch tent", CampItem.Tent);
            PlaceButton("Build fire ring", CampItem.FireRing);
            PlaceButton("Set up stove", CampItem.Stove);
            PlaceButton($"Set a snare ({backpack.Snares} left)", CampItem.Snare);
            GUILayout.Label($"Tent: {backpack.TentName} (+{backpack.TentShelter:0} °C when sleeping)", textStyle);
            GUILayout.Label($"Sleeping bag: {backpack.SleepingBagName} (comfort {backpack.SleepingBagComfort:0} °C)", textStyle);
            string fishing = backpack.HasGoodRod ? "telescopic rod" : backpack.HasFishingKit ? "basic hand line" : "none";
            GUILayout.Label($"Fishing: {fishing}", textStyle);
        }

        void DrawFuel()
        {
            GUILayout.Label("Fire & Fuel", headingStyle);
            GUILayout.Label($"Stove gas: {backpack.GasGrams:0} g    Matches: {backpack.Matches}    Firewood: {backpack.Firewood}", textStyle);
        }

        void DrawClothing()
        {
            GUILayout.Label("Clothing", headingStyle);
            foreach (Garment garment in backpack.Clothing)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{garment.name}  (+{garment.insulation:0} °C)", textStyle, GUILayout.Width(220f));
                if (GUILayout.Button(garment.worn ? "Take off" : "Put on", GUILayout.Width(110f)))
                    backpack.ToggleGarment(garment);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label($"Comfortable down to about {vitals.ComfortTemperature:0} °C. It feels like {vitals.FeltTemperature:0} °C now.", reasonStyle);
        }

        void PlaceButton(string label, CampItem item)
        {
            ActionButton(label, () =>
            {
                Close();
                placer.BeginPlacement(item);
            }, placer.RequirementProblem(item));
        }

        void ActionButton(string label, System.Action action, string problem)
        {
            GUILayout.BeginVertical();
            GUI.enabled = problem == null && !activity.IsBusy;
            if (GUILayout.Button(label, GUILayout.Height(28f)))
                action();
            GUI.enabled = true;
            if (problem != null)
                GUILayout.Label(problem, reasonStyle);
            GUILayout.EndVertical();
        }
    }
}
