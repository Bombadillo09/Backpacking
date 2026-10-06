using System;
using System.Linq;
using Backpacking.Camp;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using UnityEngine;
using UnityEngine.UIElements;

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
        [SerializeField] float cleanRabbitMinutes = 15f;
        [SerializeField] int meatPerRabbit = 2;

        readonly Bindings bindings = new();
        readonly Bindings listBindings = new();
        VisualElement screen;
        ScrollView foodList;
        VisualElement clothingList;
        string foodKey, clothingKey;

        public bool IsOpen { get; private set; }

        void Start()
        {
            foodList = new ScrollView();
            foodList.style.height = 330f;
            clothingList = UIBuild.Box();

            VisualElement left = UIBuild.Box("column").With(
                UIBuild.Text("WATER", "heading"),
                bindings.Text(() => $"Bottle: {backpack.TotalWater:0.00} / {backpack.WaterCapacity:0.0} L"),
                bindings.Text(() => $"Safe: {backpack.SafeWater:0.00} L     Untreated: {backpack.UntreatedWater:0.00} L", "small"),
                UIBuild.Box("row").With(
                    bindings.ActionButton(() => $"Drink safe ({backpack.SipLitres:0.00} L)", backpack.DrinkSafeWater,
                        () => backpack.SafeWater <= 0f ? "No safe water" : null, Busy),
                    bindings.ActionButton("Drink untreated", backpack.DrinkUntreatedWater,
                        () => backpack.UntreatedWater <= 0f ? "No untreated water" : null, Busy),
                    bindings.ActionButton("Pour out untreated", backpack.PourOutUntreatedWater,
                        () => backpack.UntreatedWater <= 0f ? "" : null, Busy)),
                bindings.Text(() => backpack.HasWaterFilter
                    ? "Your filter makes lake and stream water safe as you fill up."
                    : "Untreated water may make you sick. Boil it first.", "reason"),
                UIBuild.Text("FOOD", "heading"),
                foodList);

            VisualElement right = UIBuild.Box("column", "next").With(
                UIBuild.Text("SET UP CAMP", "heading"),
                UIBuild.Box("row").With(
                    PlaceButton("Pitch tent", CampItem.Tent),
                    PlaceButton("Build fire ring", CampItem.FireRing)),
                UIBuild.Box("row").With(
                    PlaceButton("Set up stove", CampItem.Stove),
                    PlaceButton(() => $"Set a snare ({backpack.Snares} left)", CampItem.Snare)),
                UIBuild.Box("row").With(PlaceButton("Clear campsite (machete)", CampItem.Clearing)),
                UIBuild.Text("In the woods, clear the brush before pitching the tent or building a fire.", "reason"),
                bindings.Text(() => $"Tent: {backpack.TentName} (+{backpack.TentShelter:0} °C when sleeping)", "small"),
                bindings.Text(() => $"Sleeping bag: {backpack.SleepingBagName} (comfort {backpack.SleepingBagComfort:0} °C)", "small"),
                bindings.Text(() => $"Fishing: {(backpack.HasGoodRod ? "telescopic rod" : backpack.HasFishingKit ? "basic hand line" : "none")}", "small"),
                bindings.Text(() => $"Tools: {(backpack.HasMachete ? "machete" : "none")}", "small"),
                UIBuild.Text("FIRE & FUEL", "heading"),
                UIBuild.Box("row", "spread").With(
                    bindings.Text(() => $"Stove gas: {backpack.GasGrams:0} g    Matches: {backpack.Matches}    Firewood: {backpack.Firewood}"),
                    bindings.Enabled(UIBuild.Button("Drop wood", () => backpack.TryUseFirewood(1)), () => backpack.Firewood > 0)),
                UIBuild.Text("CLOTHING", "heading"),
                clothingList,
                bindings.Text(() => $"Comfortable down to about {vitals.ComfortTemperature:0} °C. It feels like {vitals.FeltTemperature:0} °C now.", "reason"));

            VisualElement panel = UIBuild.Box("panel").With(
                UIBuild.Box("panel-header").With(
                    UIBuild.Text("Backpack", "title"),
                    bindings.Text(() => backpack.Pelts > 0 ? $"${backpack.Money}    Rabbit pelts: {backpack.Pelts}" : $"${backpack.Money}", "money")),
                bindings.Text(LoadDescription, "small"),
                UIBuild.Box("columns").With(left, right),
                UIBuild.Box("footer").With(
                    UIBuild.Button("Journal  (J)", () =>
                    {
                        Close();
                        JournalView.ShowJournal();
                    }),
                    UIBuild.Button("Close  (Tab)", Close)));
            panel.style.width = 1000f;

            screen = UIBuild.Layer("centred").With(panel);
            screen.SetVisible(false);
            GameUI.Current.Screens.Add(screen);
        }

        void Update()
        {
            if (GameInput.BackpackPressed)
            {
                if (IsOpen)
                    Close();
                else if (!PlayerControlLock.MovementLocked && !PlayerControlLock.JustReleased)
                    Open();
            }

            if (!IsOpen)
                return;
            RebuildListsIfChanged();
            bindings.Refresh();
            listBindings.Refresh();
        }

        void Open()
        {
            placer.CancelPlacement();
            IsOpen = true;
            foodKey = clothingKey = null;
            screen.SetVisible(true);
            screen.FocusFirstButton();
            PlayerControlLock.Lock(this, needsCursor: true);
            GameUI.ClaimEscape(this, Close);
        }

        void Close()
        {
            IsOpen = false;
            screen.SetVisible(false);
            PlayerControlLock.Unlock(this);
            GameUI.ReleaseEscape(this);
        }

        void OnDisable()
        {
            if (IsOpen)
                Close();
        }

        bool Busy() => activity.IsBusy;

        // ---------- Lists ----------

        void RebuildListsIfChanged()
        {
            string food = string.Join(",", FoodCatalog.AllKinds.Where(kind => backpack.CountFood(kind) > 0));
            string clothing = string.Join(",", backpack.Clothing.Select(garment => garment.name));
            if (food == foodKey && clothing == clothingKey)
                return;

            foodKey = food;
            clothingKey = clothing;
            listBindings.Clear();
            BuildFoodList();
            BuildClothingList();
        }

        void BuildFoodList()
        {
            Vector2 scroll = foodList.scrollOffset;
            foodList.Clear();
            bool any = false;
            foreach (FoodKind kind in FoodCatalog.AllKinds)
            {
                if (backpack.CountFood(kind) == 0)
                    continue;
                any = true;
                FoodInfo info = FoodCatalog.Get(kind);

                VisualElement action = kind == FoodKind.RabbitCarcass
                    ? listBindings.ActionButton("Clean", () =>
                    {
                        Close();
                        activity.Begin("Cleaning the rabbit", cleanRabbitMinutes, () => backpack.CleanCarcass(meatPerRabbit));
                    }, null, Busy)
                    : listBindings.ActionButton(info.SicknessChance > 0f ? "Eat (risky)" : "Eat", () => backpack.Eat(kind),
                        () => info.NotEdibleReason, Busy);

                foodList.Add(UIBuild.Box("list-row").With(
                    UIBuild.Box("grow").With(
                        listBindings.Text(() => $"{info.Name}  ×{backpack.CountFood(kind)}"),
                        listBindings.Text(() => Describe(kind, info), "reason")),
                    action,
                    UIBuild.Button("Drop", () => backpack.TryTakeFood(kind), "quiet")));
            }
            if (!any)
                foodList.Add(UIBuild.Text("No food. Forage, fish or set snares.", "small"));
            foodList.scrollOffset = scroll;
        }

        void BuildClothingList()
        {
            clothingList.Clear();
            foreach (Garment garment in backpack.Clothing)
            {
                Button toggle = UIBuild.Button("", () => backpack.ToggleGarment(garment));
                toggle.style.width = 110f;
                listBindings.Add(() => toggle.SetText(garment.worn ? "Take off" : "Put on"));
                clothingList.Add(UIBuild.Box("list-row").With(
                    UIBuild.Text($"{garment.name}  (+{garment.insulation:0} °C)", "grow"),
                    listBindings.Text(() => garment.worn ? "worn" : "", "reason"),
                    toggle));
            }
        }

        // ---------- Text ----------

        string Describe(FoodKind kind, FoodInfo info)
        {
            string keeps = !info.Spoils ? "keeps" : $"next spoils in {FormatHours(backpack.SoonestSpoilHours(kind))}";
            string prep = info.SmokesInto != null ? info.CooksInto != null ? " · cook or smoke" : " · can smoke" : "";
            string weight = $"{info.Weight:0.##} kg each";
            return info.Satiety > 0f ? $"+{info.Satiety:0} food · {weight} · {keeps}{prep}" : $"{weight} · {keeps}{prep}";
        }

        string LoadDescription()
        {
            float weight = backpack.TotalWeight;
            string feel = backpack.IsOverloaded ? "Overloaded: very slow, no sprinting. Drop something."
                : weight > backpack.ComfortableLoad ? "Heavy: slower, and tiring."
                : "Comfortable.";
            return $"Pack weight: {weight:0.0} kg  (comfortable up to {backpack.ComfortableLoad:0} kg, max {backpack.MaxLoad:0} kg).  {feel}";
        }

        static string FormatHours(float hours) => hours >= 48f ? $"{hours / 24f:0} days" : $"{Mathf.CeilToInt(hours)} h";

        VisualElement PlaceButton(string label, CampItem item) => PlaceButton(() => label, item);

        VisualElement PlaceButton(Func<string> label, CampItem item) =>
            bindings.ActionButton(label, () =>
            {
                Close();
                placer.BeginPlacement(item);
            }, () => placer.RequirementProblem(item), Busy);
    }
}
