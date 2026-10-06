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

        GUIStyle headingStyle, textStyle, reasonStyle;

        public bool IsOpen { get; private set; }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[toggleKey].wasPressedThisFrame)
                return;

            if (IsOpen)
                Close();
            else if (!PlayerControlLock.MovementLocked)
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

            const float width = 760f, height = 560f;
            var area = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);

            GUILayout.BeginArea(new Rect(area.x + 20f, area.y + 14f, width - 40f, height - 28f));
            GUILayout.Label("Backpack", new GUIStyle(headingStyle) { fontSize = 22 });
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(350f));
            DrawWater();
            GUILayout.Space(12f);
            DrawFood();
            GUILayout.Space(12f);
            DrawFuel();
            GUILayout.EndVertical();

            GUILayout.Space(20f);

            GUILayout.BeginVertical();
            DrawCampGear();
            GUILayout.Space(12f);
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
            GUILayout.Label("Untreated water may make you sick. Boil it first.", reasonStyle);
        }

        void DrawFood()
        {
            GUILayout.Label("Food", headingStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Snacks: {backpack.Snacks}", textStyle, GUILayout.Width(170f));
            ActionButton("Eat snack", backpack.EatSnack, backpack.Snacks <= 0 ? "None left" : null);
            GUILayout.EndHorizontal();
            GUILayout.Label($"Trail meals: {backpack.TrailMeals}   (cook on a stove or fire, uses {CampCooking.MealWaterLitres:0.0} L water)", textStyle);
        }

        void DrawFuel()
        {
            GUILayout.Label("Fire & Fuel", headingStyle);
            GUILayout.Label($"Stove gas: {backpack.GasGrams:0} g", textStyle);
            GUILayout.Label($"Matches: {backpack.Matches}", textStyle);
            GUILayout.Label($"Firewood: {backpack.Firewood}   (look for fallen branches)", textStyle);
        }

        void DrawCampGear()
        {
            GUILayout.Label("Set Up Camp", headingStyle);
            PlaceButton("Pitch tent", CampItem.Tent);
            PlaceButton("Build fire ring", CampItem.FireRing);
            PlaceButton("Set up stove", CampItem.Stove);
            GUILayout.Label($"Sleeping bag: {backpack.SleepingBagName} (comfort {backpack.SleepingBagComfort:0} °C)", textStyle);
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
            GUILayout.Label($"Comfortable down to about {vitals.ComfortTemperature:0} °C at rest. It's {vitals.FeltTemperature:0} °C now.", reasonStyle);
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
