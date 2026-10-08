using System;
using System.Collections.Generic;
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
    /// The backpack screen (Tab), laid out like an inventory: everything you carry as tiles with a picture of it,
    /// grouped into food, water and first aid, camp gear, tools and fuel, and clothing. Select a tile to see the
    /// item larger with everything you can do with it (eat, drink, put on, set up, put on the hotbar...). The
    /// hotbar and the pack itself are along the bottom.
    /// </summary>
    public class BackpackView : MonoBehaviour
    {
        [SerializeField] Backpack backpack;
        [SerializeField] Vitals vitals;
        [SerializeField] CampPlacer placer;
        [SerializeField] PlayerActivity activity;
        [SerializeField] PackHandling packHandling;
        [SerializeField] ItemIconLibrary icons;
        [SerializeField] float cleanRabbitMinutes = 15f;
        [SerializeField] int meatPerRabbit = 2;

        /// <summary>One thing in the pack, as the screen shows it.</summary>
        sealed class Item
        {
            public string Key, Icon, Section;
            public Func<string> Name, Count, Description;
            public readonly List<(Func<string> label, Action action, Func<string> problem)> Actions = new();
            public HotbarSlot? Hotbar;
        }

        static readonly string[] Sections = { "FOOD", "WATER & FIRST AID", "CAMP GEAR", "TOOLS & FUEL", "CLOTHING" };

        readonly Bindings bindings = new();
        readonly Bindings gridBindings = new();
        readonly Bindings detailBindings = new();
        VisualElement screen, grid, detailIcon, detailActions;
        Label detailName, detailCount, detailText;
        ScrollView gridScroll;
        readonly Dictionary<string, VisualElement> tiles = new();
        List<Item> items = new();
        string itemsKey, selected;

        public bool IsOpen { get; private set; }

        void Start()
        {
            gridScroll = new ScrollView();
            gridScroll.AddToClassList("inventory-scroll");
            grid = UIBuild.Box("inventory-grid");
            gridScroll.Add(grid);

            detailIcon = UIBuild.Box("detail-icon");
            detailName = UIBuild.Text("", "title", "detail-name");
            detailCount = UIBuild.Text("", "money");
            detailText = UIBuild.Text("", "text", "detail-text");
            detailActions = UIBuild.Box("detail-actions");
            VisualElement detail = UIBuild.Box("inventory-detail").With(detailIcon, detailName, detailCount, detailText, detailActions);

            VisualElement panel = UIBuild.Box("panel", "inventory").With(
                UIBuild.Box("panel-header").With(
                    UIBuild.Text("Backpack", "title"),
                    bindings.Text(() => backpack.Pelts > 0 ? $"${backpack.Money}    Rabbit pelts: {backpack.Pelts}" : $"${backpack.Money}", "money")),
                bindings.Text(LoadDescription, "small"),
                UIBuild.Box("columns").With(gridScroll, detail),
                UIBuild.Box("inventory-bottom").With(
                    UIBuild.Box().With(UIBuild.Text("HOTBAR  ·  keys 1–5  ·  click a slot to empty it", "heading"), HotbarRow()),
                    UIBuild.Box("grow").With(
                        UIBuild.Text("YOUR PACK", "heading"),
                        UIBuild.Box("row").With(
                            bindings.ActionButton(() => backpack.IsWorn ? "Take off pack" : "Put pack on", TogglePack, null, Busy),
                            PlaceButton("Clear campsite (machete)", CampItem.Clearing),
                            PlaceButton("Build fire ring", CampItem.FireRing)),
                        bindings.Text(() => backpack.IsWorn
                            ? "Camp gear (tent, stove, chair, snares, fishing kit) is packed inside: take the pack off to get it out."
                            : "Your pack is on the ground. Camp gear comes out of it while you're beside it.", "reason"))),
                UIBuild.Box("footer").With(
                    UIBuild.Button("Journal  (J)", () =>
                    {
                        Close();
                        JournalView.ShowJournal();
                    }),
                    UIBuild.Button("Close  (Tab)", Close)));
            panel.style.width = 1180f;

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
            RebuildIfChanged();
            bindings.Refresh();
            gridBindings.Refresh();
            detailBindings.Refresh();
        }

        /// <summary>Opens the screen from code (the editor's screenshot tool).</summary>
        public void Show() => Open();

        void Open()
        {
            // Off your back, the pack has to be within reach to get into it.
            if (packHandling != null && !packHandling.CanReachPack)
            {
                Notifications.Post($"Your pack is on the ground {packHandling.PackDistance:0} m away. Go back to it.", 3f);
                return;
            }
            placer.CancelPlacement();
            IsOpen = true;
            itemsKey = null;
            screen.SetVisible(true);
            PlayerControlLock.Lock(this, needsCursor: true);
            GameUI.ClaimEscape(this, Close);
            RebuildIfChanged();
            screen.FocusFirstButton();
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

        // ---------- What's in the pack ----------

        List<Item> Gather()
        {
            var list = new List<Item>();
            Item Add(string section, string key, string icon, Func<string> name, Func<string> count, Func<string> description, HotbarSlot? hotbar = null)
            {
                var item = new Item { Section = section, Key = key, Icon = icon, Name = name, Count = count, Description = description, Hotbar = hotbar };
                list.Add(item);
                return item;
            }

            // Food.
            foreach (FoodKind kind in FoodCatalog.AllKinds)
            {
                if (backpack.CountFood(kind) == 0)
                    continue;
                FoodKind food = kind;
                FoodInfo info = FoodCatalog.Get(food);
                Item item = Add("FOOD", $"food-{food}", $"food-{food}", () => info.Name, () => $"×{backpack.CountFood(food)}",
                    () => DescribeFood(food, info), new HotbarSlot(HotbarKind.Food, food));
                if (food == FoodKind.RabbitCarcass)
                    item.Actions.Add((() => "Clean it", () =>
                    {
                        Close();
                        activity.Begin("Cleaning the rabbit", cleanRabbitMinutes, () => backpack.CleanCarcass(meatPerRabbit));
                    }, null));
                else
                    item.Actions.Add((() => info.SicknessChance > 0f ? "Eat (risky)" : "Eat", () => backpack.Eat(food), () => info.NotEdibleReason));
                item.Actions.Add((() => "Drop one", () => backpack.TryTakeFood(food), null));
            }

            // Water and first aid.
            Item water = Add("WATER & FIRST AID", "water", "water", () => "Water bottle", () => $"{backpack.TotalWater:0.0} / {backpack.WaterCapacity:0.0} L",
                () => $"Safe to drink: {backpack.SafeWater:0.00} L. Untreated: {backpack.UntreatedWater:0.00} L.\n" +
                      (backpack.HasWaterFilter ? "Your filter makes lake and stream water safe as you fill up." : "Untreated water may make you sick. Boil it first."),
                new HotbarSlot(HotbarKind.Water));
            water.Actions.Add((() => $"Drink safe water ({backpack.SipLitres:0.00} L)", backpack.DrinkSafeWater, () => backpack.SafeWater <= 0f ? "No safe water" : null));
            water.Actions.Add((() => "Drink untreated water", backpack.DrinkUntreatedWater, () => backpack.UntreatedWater <= 0f ? "No untreated water" : null));
            water.Actions.Add((() => "Pour out untreated water", backpack.PourOutUntreatedWater, () => backpack.UntreatedWater <= 0f ? "" : null));
            if (backpack.HasWaterFilter)
                Add("WATER & FIRST AID", "filter", "filter", () => "Squeeze filter", () => "",
                    () => "Filters lake and stream water as you fill your bottle, so it's safe straight away.");
            Item antibiotics = Add("WATER & FIRST AID", "antibiotics", "antibiotics", () => "Antibiotics", () => $"×{backpack.Antibiotics}",
                () => "One course clears an infection in a few hours. Trading posts sell them.", new HotbarSlot(HotbarKind.Antibiotics));
            antibiotics.Actions.Add((() => "Take a course", TakeAntibiotics,
                () => backpack.Antibiotics <= 0 ? "None left. Trading posts sell them." : !vitals.IsInfected ? "No infection to treat" : vitals.OnAntibiotics ? "Already taking a course" : null));
            Item bandages = Add("WATER & FIRST AID", "bandage", "bandage", () => "Bandages", () => $"×{backpack.Bandages}",
                () => "For cuts: a bandaged cut stops bleeding. Sore feet need rest instead: sit (Z) and take your boots off (E).",
                new HotbarSlot(HotbarKind.Bandage));
            bandages.Actions.Add((() => "Bandage a cut", BandageCut,
                () => backpack.Bandages <= 0 ? "None left. Trading posts sell them." : !vitals.IsBleeding ? "No cut to bandage" : null));

            // Camp gear.
            Item tent = Add("CAMP GEAR", "tent", "tent", () => backpack.TentName, () => backpack.HasTent ? "packed" : "out",
                () => $"+{backpack.TentShelter:0} °C when you sleep in it.\n{TentStatus()}");
            tent.Actions.Add((() => "Take out the tent bag", () =>
            {
                Close();
                packHandling.TakeOutTent();
            }, TentBagButtonProblem));
            Add("CAMP GEAR", "sleepingbag", "sleepingbag", () => backpack.SleepingBagName, () => $"{backpack.SleepingBagComfort:0} °C",
                () => $"Keeps you warm asleep down to about {backpack.SleepingBagComfort:0} °C (lower with a sleeping mat).");
            if (backpack.HasMat)
                Add("CAMP GEAR", "mat", backpack.MatRecovery >= 1.4f ? "airmat" : "mat", () => backpack.MatName, () => "",
                    () => $"+{backpack.MatWarmth:0} °C asleep, and {(backpack.MatRecovery - 1f) * 100f:0}% more energy back from sleep.");
            Item stove = Add("CAMP GEAR", "stove", "stove", () => "Canister stove & pot", () => $"{backpack.GasGrams:0} g gas",
                () => $"Quick, reliable cooking and boiling. {backpack.GasGrams:0} g of gas left; trading posts sell canisters." +
                      (backpack.HasStove ? "" : "\nIt's set up at camp: look at it to cook, or to pack it away."));
            stove.Actions.Add((() => "Set up the stove", () => Place(CampItem.Stove), () => placer.RequirementProblem(CampItem.Stove)));
            if (backpack.HasChair)
            {
                Item chair = Add("CAMP GEAR", "chair", "chair", () => "Camp chair", () => backpack.ChairInPack ? "packed" : "out",
                    () => "Poles first, then the seat. Sitting in it your feet rest faster than on the ground, and your boots still come off.");
                chair.Actions.Add((() => "Take out the chair", () => Place(CampItem.Chair), () => placer.RequirementProblem(CampItem.Chair)));
            }
            if (backpack.Snares > 0)
            {
                Item snares = Add("CAMP GEAR", "snare", "snare", () => "Wire snares", () => $"×{backpack.Snares}",
                    () => "Set one on a game trail and leave it a while; check it later for a rabbit.");
                snares.Actions.Add((() => "Set a snare", () => Place(CampItem.Snare), () => placer.RequirementProblem(CampItem.Snare)));
            }
            if (backpack.HasFishingKit)
                Add("CAMP GEAR", "fishing", backpack.HasGoodRod ? "rod" : "fishing", () => backpack.HasGoodRod ? "Telescopic rod" : "Hand line",
                    () => "", () => "Look at a lake or stream to go fishing (with your pack off: the kit's inside it).");

            // Tools and fuel.
            if (backpack.HasMachete)
            {
                Item machete = Add("TOOLS & FUEL", "machete", "machete", () => "Machete", () => "",
                    () => "Hold it (hotbar) and click to hack through brush. Also clears a campsite. Careful: a tired swing can cut you.",
                    new HotbarSlot(HotbarKind.Machete));
                machete.Actions.Add((() => "Clear a campsite here", () => Place(CampItem.Clearing), () => placer.RequirementProblem(CampItem.Clearing)));
            }
            Add("TOOLS & FUEL", "matches", "matches", () => "Waterproof matches", () => $"×{backpack.Matches}",
                () => "For lighting fires. Rain can beat a match.");
            if (backpack.Firewood > 0)
            {
                Item wood = Add("TOOLS & FUEL", "firewood", "firewood", () => "Firewood", () => $"×{backpack.Firewood}",
                    () => "Dry wood for a fire. Heavy: drop what you don't need.");
                wood.Actions.Add((() => "Build a fire ring", () => Place(CampItem.FireRing), () => placer.RequirementProblem(CampItem.FireRing)));
                wood.Actions.Add((() => "Drop one", () => backpack.TryUseFirewood(1), null));
            }
            if (backpack.HasBow)
                Add("TOOLS & FUEL", "bow", "bow", () => "Recurve bow", () => $"{backpack.Arrows} arrows",
                    () => "Hold it (hotbar), then hold the mouse button (or X) to draw and let go to shoot. Arrows drop over distance: " +
                          "aim a little high when the animal's far. Crouch to get close. Shoot just behind the shoulder; a poor hit " +
                          "leaves a wounded animal you'll have to track by its blood. Pick your arrows up again after.",
                    new HotbarSlot(HotbarKind.Bow));
            if (backpack.Pelts > 0)
                Add("TOOLS & FUEL", "pelts", "pelt", () => "Rabbit pelts", () => $"×{backpack.Pelts}", () => "Trading posts buy them.");
            if (backpack.Hides > 0)
            {
                Item hides = Add("TOOLS & FUEL", "hides", "hide", () => "Deer hides", () => $"×{backpack.Hides}",
                    () => $"Trading posts pay well for them, but each weighs {backpack.HideWeight:0.#} kg.");
                hides.Actions.Add((() => "Drop one", () => backpack.TryTakeHide(), null));
            }

            // Clothing.
            foreach (Garment garment in backpack.Clothing)
            {
                Garment worn = garment;
                Item item = Add("CLOTHING", $"garment-{garment.name}", ItemIconLibrary.GarmentKey(garment.name), () => worn.name,
                    () => worn.worn ? "worn" : "",
                    () => $"+{worn.insulation:0} °C, {worn.weight:0.##} kg{(worn.waterproof ? ", keeps the rain off" : "")}.\n" +
                          $"You're comfortable down to about {vitals.ComfortTemperature:0} °C; it feels like {vitals.FeltTemperature:0} °C.");
                item.Actions.Add((() => worn.worn ? "Take off" : "Put on", () => backpack.ToggleGarment(worn), null));
            }
            Add("CLOTHING", "boots", "boots", () => backpack.BootsName, () => backpack.BootsWaterproof ? "waterproof" : "",
                () => $"Your feet: {FeetDescription()}. Better boots tire your feet less and blister less.");
            return list;
        }

        // ---------- Building the screen ----------

        void RebuildIfChanged()
        {
            List<Item> gathered = Gather();
            string key = string.Join(",", gathered.Select(item => item.Key + ":" + item.Icon));
            if (key == itemsKey)
            {
                items = gathered;
                return;
            }
            itemsKey = key;
            items = gathered;
            Vector2 scroll = gridScroll.scrollOffset;
            grid.Clear();
            tiles.Clear();
            gridBindings.Clear();
            foreach (string section in Sections)
            {
                List<Item> inSection = items.Where(item => item.Section == section).ToList();
                if (inSection.Count == 0)
                    continue;
                grid.Add(UIBuild.Text(section, "heading", "inventory-section"));
                VisualElement row = UIBuild.Box("inventory-row");
                foreach (Item item in inSection)
                    row.Add(Tile(item));
                grid.Add(row);
            }
            gridScroll.scrollOffset = scroll;
            if (selected == null || !items.Any(item => item.Key == selected))
                selected = items.Count > 0 ? items[0].Key : null;
            ShowDetail();
        }

        VisualElement Tile(Item item)
        {
            string key = item.Key;
            Button tile = UIBuild.Button("", () =>
            {
                selected = key;
                ShowDetail();
            }, "item-tile");
            VisualElement picture = UIBuild.Box("item-icon");
            Texture2D icon = icons != null ? icons.Get(item.Icon) : null;
            if (icon != null)
                picture.style.backgroundImage = Background.FromTexture2D(icon);
            tile.Add(picture);
            tile.Add(gridBindings.Text(() => Find(key)?.Count() ?? "", "item-count"));
            tile.Add(gridBindings.Text(() => Find(key)?.Name() ?? "", "item-name"));
            if (item.Hotbar.HasValue)
            {
                HotbarSlot slot = item.Hotbar.Value;
                Label badge = UIBuild.Text("", "item-badge");
                gridBindings.Add(() =>
                {
                    int index = HotbarIndex(slot);
                    badge.SetText(index >= 0 ? (index + 1).ToString() : "");
                    badge.SetVisible(index >= 0);
                });
                tile.Add(badge);
            }
            tiles[key] = tile;
            return tile;
        }

        Item Find(string key) => items.FirstOrDefault(item => item.Key == key);

        int HotbarIndex(HotbarSlot slot)
        {
            for (int i = 0; i < backpack.Hotbar.Count; i++)
                if (backpack.Hotbar[i].Same(slot))
                    return i;
            return -1;
        }

        /// <summary>The selected item, large, with everything you can do with it.</summary>
        void ShowDetail()
        {
            foreach ((string tileKey, VisualElement tile) in tiles)
                if (tileKey == selected)
                    tile.AddToClassList("selected");
                else
                    tile.RemoveFromClassList("selected");

            detailBindings.Clear();
            detailActions.Clear();
            Item item = Find(selected);
            if (item == null)
            {
                detailName.SetText("");
                detailCount.SetText("");
                detailText.SetText("Your pack is empty.");
                detailIcon.style.backgroundImage = StyleKeyword.None;
                return;
            }
            string key = item.Key;
            Texture2D icon = icons != null ? icons.Get(item.Icon) : null;
            detailIcon.style.backgroundImage = icon != null ? Background.FromTexture2D(icon) : StyleKeyword.None;
            detailBindings.Add(() =>
            {
                Item current = Find(key);
                if (current == null)
                    return;
                detailName.SetText(current.Name());
                detailCount.SetText(current.Count());
                detailText.SetText(current.Description());
            });
            foreach ((Func<string> label, Action action, Func<string> problem) in item.Actions)
                detailActions.Add(detailBindings.ActionButton(label, action, problem, Busy));
            if (item.Hotbar.HasValue)
            {
                HotbarSlot slot = item.Hotbar.Value;
                detailActions.Add(detailBindings.ActionButton(() => HotbarIndex(slot) >= 0 ? $"On the hotbar (key {HotbarIndex(slot) + 1})" : "Put on the hotbar", () =>
                {
                    if (!backpack.AssignHotbar(slot))
                        Notifications.Post("Your hotbar's full. Click a slot below to empty it.", 3f);
                }, () => HotbarIndex(slot) >= 0 ? "" : null));
            }
            detailBindings.Refresh();
        }

        /// <summary>The five hotbar slots, with pictures; clicking one empties it.</summary>
        VisualElement HotbarRow()
        {
            VisualElement row = UIBuild.Box("row");
            for (int i = 0; i < Backpack.HotbarSize; i++)
            {
                int slot = i;
                Button button = UIBuild.Button("", () => backpack.ClearHotbar(slot), "hotbar-tile");
                VisualElement picture = UIBuild.Box("hotbar-tile-icon");
                Label number = UIBuild.Text((slot + 1).ToString(), "hotbar-key");
                button.Add(picture);
                button.Add(number);
                string shown = null;
                bindings.Add(() =>
                {
                    HotbarSlot held = slot < backpack.Hotbar.Count ? backpack.Hotbar[slot] : default;
                    string key = IconKey(held);
                    if (key == shown)
                        return;
                    shown = key;
                    Texture2D icon = icons != null ? icons.Get(key) : null;
                    picture.style.backgroundImage = icon != null ? Background.FromTexture2D(icon) : StyleKeyword.None;
                    button.tooltip = Player.Hotbar.Describe(held);
                });
                row.Add(button);
            }
            return row;
        }

        /// <summary>The picture for what a hotbar slot holds.</summary>
        public static string IconKey(HotbarSlot slot) => slot.kind switch
        {
            HotbarKind.Machete => "machete",
            HotbarKind.Water => "water",
            HotbarKind.Food => $"food-{slot.food}",
            HotbarKind.Antibiotics => "antibiotics",
            HotbarKind.Bandage => "bandage",
            HotbarKind.Bow => "bow",
            _ => null,
        };

        // ---------- Actions ----------

        void Place(CampItem item)
        {
            Close();
            placer.BeginPlacement(item);
        }

        VisualElement PlaceButton(string label, CampItem item) =>
            bindings.ActionButton(label, () => Place(item), () => placer.RequirementProblem(item), Busy);

        void TogglePack()
        {
            Close();
            if (backpack.IsWorn)
                packHandling.SetDown();
            else
                packHandling.PickUp();
        }

        string TentBagButtonProblem() =>
            !backpack.HasTent ? "" :
            backpack.IsWorn ? "Take your pack off first" :
            null;

        string TentStatus()
        {
            if (backpack.HasTent)
                return backpack.IsWorn
                    ? "To pitch it: take your pack off, take out the tent bag, then look at the bag to unpack it where you want it."
                    : "Take out the tent bag, then look at it to unpack the tent where you want to pitch.";
            if (packHandling != null && packHandling.TentBag != null)
                return "It's out in its bag. Look at the bag to unpack it, or to put it back in your pack.";
            return "It's out. Look at it to carry on pitching it, sleep in it, or take it down.";
        }

        void TakeAntibiotics()
        {
            if (!backpack.TryUseAntibiotics())
                return;
            vitals.TakeAntibiotics();
            Notifications.Post("You take a course of antibiotics. The infection should clear in a few hours.");
            Trip.TripLog.Note("Started a course of antibiotics for my feet.");
        }

        void BandageCut()
        {
            Close();
            activity.Begin("Cleaning and bandaging the cut", 3f, () =>
            {
                if (backpack.TryUseBandage() && vitals.Bandage())
                    Notifications.Post("Cut cleaned and bandaged.", 2.5f);
            });
        }

        // ---------- Text ----------

        string FeetDescription() =>
            vitals.IsInfected ? "infected" :
            vitals.Feet < 40f ? "raw" : vitals.HasBlisters ? "blistered" : vitals.FootStrain > 60f ? "aching" : vitals.FootStrain > 35f ? "tired" : "fine";

        string DescribeFood(FoodKind kind, FoodInfo info)
        {
            string keeps = !info.Spoils ? "Keeps indefinitely." : $"The next one spoils in {FormatHours(backpack.SoonestSpoilHours(kind))}.";
            string prep = info.SmokesInto != null ? info.CooksInto != null ? " Cook it at a fire or stove, or smoke it to keep." : " Smoke it at a fire to keep." : "";
            string value = info.Satiety > 0f ? $"+{info.Satiety:0} food. " : "";
            return $"{value}{info.Weight:0.##} kg each. {keeps}{prep}";
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
    }
}
