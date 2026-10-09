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
    /// hotbar and the pack itself are along the bottom: drag any tile onto a hotbar slot to carry it to hand there,
    /// or drag between slots to swap them.
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
            /// <summary>Where it is: which part of the pack, worn, laid out in the tent...</summary>
            public Func<string> Where;
        }

        static readonly string[] Sections = { "FOOD", "WATER & FIRST AID", "CAMP GEAR", "TOOLS & FUEL", "CLOTHING" };
        const string AllSections = "EVERYTHING";
        const string ClothingSection = "CLOTHING";

        readonly Bindings bindings = new();
        readonly Bindings gridBindings = new();
        readonly Bindings detailBindings = new();
        VisualElement screen, grid, detailIcon, detailActions;
        Label detailName, detailCount, detailText, detailStats;
        string section = AllSections;
        readonly Dictionary<string, Button> tabs = new();
        List<PackItem> packed = new();
        float packedAt = -1f;
        ScrollView gridScroll;
        readonly Dictionary<string, VisualElement> tiles = new();
        List<Item> items = new();
        string itemsKey, selected;

        public bool IsOpen { get; private set; }
        public static BackpackView Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        void Awake() => Current = this;

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }

        void Start()
        {
            gridScroll = new ScrollView();
            gridScroll.AddToClassList("inventory-scroll");
            grid = UIBuild.Box("inventory-grid");
            gridScroll.Add(grid);

            detailIcon = UIBuild.Box("detail-icon");
            detailName = UIBuild.Text("", "title", "detail-name");
            detailCount = UIBuild.Text("", "money");
            detailStats = UIBuild.Text("", "small", "detail-stats");
            detailText = UIBuild.Text("", "text", "detail-text");
            detailActions = UIBuild.Box("detail-actions");
            VisualElement detail = UIBuild.Box("inventory-detail").With(detailIcon, detailName, detailCount, detailStats, detailText, detailActions);

            VisualElement panel = UIBuild.Box("panel", "inventory").With(
                UIBuild.Box("panel-header").With(
                    UIBuild.Text("Backpack", "title"),
                    bindings.Text(() => backpack.Pelts > 0 ? $"${backpack.Money}    Rabbit pelts: {backpack.Pelts}" : $"${backpack.Money}", "money")),
                PackMeter(),
                bindings.Text(LoadDescription, "small"),
                UIBuild.Box("columns").With(Tabs(), gridScroll, detail),
                UIBuild.Box("inventory-bottom").With(
                    UIBuild.Box().With(UIBuild.Text("HOTBAR  ·  keys 1–5  ·  drag anything here  ·  click a slot to empty it", "heading"), HotbarRow()),
                    UIBuild.Box("grow").With(
                        UIBuild.Text("YOUR PACK", "heading"),
                        UIBuild.Box("row").With(
                            bindings.ActionButton(() => backpack.IsWorn ? "Take off pack" : "Put pack on", TogglePack,
                                () => backpack.HasPack ? null : "You don't have a backpack yet", Busy),
                            bindings.ActionButton("Repack", Repack, () => backpack.HasPack ? null : "", Busy),
                            PlaceButton("Clear campsite (machete)", CampItem.Clearing),
                            PlaceButton("Build fire ring", CampItem.FireRing)),
                        bindings.Text(PackDescription, "reason"))),
                UIBuild.Box("footer").With(
                    UIBuild.Button("Journal  (J)", () =>
                    {
                        Close();
                        JournalView.ShowJournal();
                    }),
                    UIBuild.Button("Close  (Tab)", Close)));
            panel.style.width = 1380f;

            screen = UIBuild.Layer("centred").With(panel);
            screen.SetVisible(false);
            GameUI.Current.Screens.Add(screen);

            // The picture that follows the pointer while dragging something to the hotbar.
            dragGhost = UIBuild.Box("drag-ghost");
            dragGhost.style.position = Position.Absolute;
            dragGhost.style.width = dragGhost.style.height = 72f;
            dragGhost.style.opacity = 0.85f;
            dragGhost.pickingMode = PickingMode.Ignore;
            dragGhost.SetVisible(false);
            screen.Add(dragGhost);
            // In case the pointer isn't held by what it was pressed on.
            screen.RegisterCallback<PointerMoveEvent>(move => DragTo(move.pointerId, move.position));
            screen.RegisterCallback<PointerUpEvent>(up =>
            {
                if (up.pointerId == dragPointer)
                    EndDrag(drop: true, up.position);
            });
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
            EndDrag(drop: false);
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

        /// <summary>Rearranges the pack; beside the truck, with the truck bed to pack from as well.</summary>
        void Repack()
        {
            if (PackingView.Current == null)
                return;
            Vehicles.Pickup truck = Vehicles.Pickup.Current;
            bool atTruck = truck != null && truck.Bed != null && !truck.Driving
                           && Vector3.Distance(truck.transform.position, backpack.transform.position) < 6f;
            Close();
            PackingView.Current.Open(atTruck ? truck.Bed : null);
        }

        static readonly Color MeterGood = new(0.45f, 0.72f, 0.38f), MeterFull = new(0.92f, 0.68f, 0.2f), MeterOver = new(0.85f, 0.25f, 0.18f);

        /// <summary>
        /// How full the pack is at a glance: a bar for each part (inside, lid, side pockets, straps) and one for the
        /// load, green with room to spare, amber when nearly full (or heavy), red when over.
        /// </summary>
        VisualElement PackMeter()
        {
            VisualElement row = UIBuild.Box("row");
            row.style.marginTop = 6f;
            row.style.marginBottom = 6f;

            void Meter(string label, Func<(float used, float full, float limit, string text)> read)
            {
                VisualElement bar = UIBuild.Bar(out VisualElement fill);
                bar.style.width = 170f;
                bar.style.height = 12f;
                Label text = UIBuild.Text("", "small");
                VisualElement box = UIBuild.Box().With(text, bar);
                box.style.marginRight = 28f;
                bindings.Add(() =>
                {
                    (float used, float full, float limit, string value) = read();
                    float fraction = limit > 0f ? used / limit : 0f;
                    fill.SetFill(fraction);
                    fill.style.backgroundColor = used > limit + 0.01f ? MeterOver : used > full ? MeterFull : MeterGood;
                    text.SetText($"{label}   {value}");
                    box.SetVisible(limit > 0f);
                });
                row.Add(box);
            }

            float Litres(PackZone zone) => backpack.UsedIn(zone);
            Meter("INSIDE", () => (Litres(PackZone.Core), backpack.CapacityOf(PackZone.Core) * 0.85f, backpack.CapacityOf(PackZone.Core),
                $"{Litres(PackZone.Core):0.#} / {backpack.CapacityOf(PackZone.Core):0} L"));
            Meter("LID", () => (Litres(PackZone.Lid), backpack.CapacityOf(PackZone.Lid) * 0.85f, backpack.CapacityOf(PackZone.Lid),
                $"{Litres(PackZone.Lid):0.#} / {backpack.CapacityOf(PackZone.Lid):0} L"));
            Meter("SIDE POCKETS", () => (Litres(PackZone.Pockets), backpack.CapacityOf(PackZone.Pockets) * 0.85f, backpack.CapacityOf(PackZone.Pockets),
                $"{Litres(PackZone.Pockets):0.#} / {backpack.CapacityOf(PackZone.Pockets):0} L"));
            Meter("STRAPS", () => (Litres(PackZone.Straps), backpack.CapacityOf(PackZone.Straps) - 0.5f, backpack.CapacityOf(PackZone.Straps),
                $"{Litres(PackZone.Straps):0} / {backpack.CapacityOf(PackZone.Straps):0} used"));
            Meter("WEIGHT", () => (backpack.TotalWeight, backpack.ComfortableLoad, backpack.MaxLoad,
                $"{backpack.TotalWeight:0.0} kg  (easy to {backpack.ComfortableLoad:0}, max {backpack.MaxLoad:0})"));
            bindings.Visible(row, () => backpack.HasPack);
            return row;
        }

        string PackDescription()
        {
            if (!backpack.HasPack)
                return "No backpack yet: you can only carry what you wear. The outdoor store sells three.";
            float inside = backpack.UsedIn(PackZone.Core), room = backpack.CapacityOf(PackZone.Core);
            string where = backpack.IsWorn
                ? "Camp gear is packed inside: take the pack off to get it out."
                : "Your pack is on the ground. Camp gear comes out of it while you're beside it.";
            return $"{backpack.Pack.Name}: {inside:0} of {room:0} L inside, {Backpack.BalanceWord(backpack.Balance)}"
                   + $"{(backpack.Overfull > 0f ? ", over-full" : "")}. {where}";
        }

        // ---------- What's in the pack ----------

        List<Item> Gather()
        {
            var list = new List<Item>();
            Item Add(string section, string key, string icon, Func<string> name, Func<string> count, Func<string> description, HotbarSlot? hotbar = null)
            {
                // Anything can be carried to hand: what has no use of its own there does its first action from the hotbar.
                var item = new Item { Section = section, Key = key, Icon = icon, Name = name, Count = count, Description = description,
                    Hotbar = hotbar ?? HotbarSlot.Gear(key, name(), icon), Where = () => WhereIs(key) };
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
            // Only what you own: a new trip starts with nothing until you've been to the outdoor store.
            if (backpack.WaterCapacity > 0f)
            {
                Item water = Add("WATER & FIRST AID", "water", "water", () => "Water bottle", () => $"{backpack.TotalWater:0.0} / {backpack.WaterCapacity:0.0} L",
                    () => $"Safe to drink: {backpack.SafeWater:0.00} L. Untreated: {backpack.UntreatedWater:0.00} L.\n" +
                          (backpack.HasWaterFilter ? "Your filter makes lake and stream water safe as you fill up." : "Untreated water may make you sick. Boil it first."),
                    new HotbarSlot(HotbarKind.Water));
                water.Actions.Add((() => $"Drink safe water ({backpack.SipLitres:0.00} L)", backpack.DrinkSafeWater, () => backpack.SafeWater <= 0f ? "No safe water" : null));
                water.Actions.Add((() => "Drink untreated water", backpack.DrinkUntreatedWater, () => backpack.UntreatedWater <= 0f ? "No untreated water" : null));
                water.Actions.Add((() => "Pour out untreated water", backpack.PourOutUntreatedWater, () => backpack.UntreatedWater <= 0f ? "" : null));
            }
            if (backpack.HasWaterFilter)
                Add("WATER & FIRST AID", "filter", "filter", () => "Squeeze filter", () => "",
                    () => "Filters lake and stream water as you fill your bottle, so it's safe straight away.");
            if (backpack.Antibiotics > 0)
            {
                Item antibiotics = Add("WATER & FIRST AID", "antibiotics", "antibiotics", () => "Antibiotics", () => $"×{backpack.Antibiotics}",
                    () => "One course clears an infection in a few hours. Trading posts sell them.", new HotbarSlot(HotbarKind.Antibiotics));
                antibiotics.Actions.Add((() => "Take a course", TakeAntibiotics,
                    () => backpack.Antibiotics <= 0 ? "None left. Trading posts sell them." : !vitals.IsInfected ? "No infection to treat" : vitals.OnAntibiotics ? "Already taking a course" : null));
            }
            if (backpack.Bandages > 0)
            {
                Item bandages = Add("WATER & FIRST AID", "bandage", "bandage", () => "Bandages", () => $"×{backpack.Bandages}",
                    () => "For cuts: a bandaged cut stops bleeding. Sore feet need rest instead: sit (Z) and take your boots off (E).",
                    new HotbarSlot(HotbarKind.Bandage));
                bandages.Actions.Add((() => "Bandage a cut", BandageCut,
                    () => backpack.Bandages <= 0 ? "None left. Trading posts sell them." : !vitals.IsBleeding ? "No cut to bandage" : null));
            }

            // Camp gear.
            if (backpack.OwnsTent)
            {
                Item tent = Add("CAMP GEAR", "tent", "tent", () => backpack.TentName, () => backpack.HasTent ? "packed" : "out",
                    () => $"+{backpack.TentShelter:0} °C when you sleep in it.\n{TentStatus()}");
                tent.Actions.Add((() => "Take out the tent bag", () =>
                {
                    Close();
                    packHandling.TakeOutTent();
                }, TentBagButtonProblem));
            }
            if (backpack.HasSleepingBag)
                Add("CAMP GEAR", "sleepingbag", "sleepingbag", () => backpack.SleepingBagName, () => backpack.BagLaidOut ? "in the tent" : $"{backpack.SleepingBagComfort:0} °C",
                    () => $"Keeps you warm asleep down to about {backpack.SleepingBagComfort:0} °C (lower with a sleeping mat)."
                          + (backpack.BagWetness > 0.15f ? $"\nIt's {backpack.BagWetness * 100f:0}% wet, and far less warm until it dries. A fire helps." : ""));
            if (backpack.HasMat)
                Add("CAMP GEAR", "mat", backpack.MatRecovery >= 1.4f ? "airmat" : "mat", () => backpack.MatName, () => backpack.MatLaidOut ? "in the tent" : "",
                    () => $"+{backpack.MatWarmth:0} °C asleep, and {(backpack.MatRecovery - 1f) * 100f:0}% more energy back from sleep.");
            if (backpack.OwnsStove)
            {
                Item stove = Add("CAMP GEAR", "stove", "stove", () => "Canister stove & pot", () => $"{backpack.GasGrams:0} g gas",
                    () => $"Quick, reliable cooking and boiling. {backpack.GasGrams:0} g of gas left; trading posts sell canisters." +
                          (backpack.HasStove ? "" : "\nIt's set up at camp: look at it to cook, or to pack it away."));
                stove.Actions.Add((() => "Set up the stove", () => Place(CampItem.Stove), () => placer.RequirementProblem(CampItem.Stove)));
            }
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
                    () => "", () => "Hold it (hotbar), look out over a lake or stream and click to cast. Press E when a fish bites.",
                    new HotbarSlot(HotbarKind.FishingRod));
            if (backpack.HasFlashlight)
                Add("TOOLS & FUEL", "flashlight", "flashlight", () => "Flashlight", () => Player.Hotbar.Current != null && Player.Hotbar.Current.FlashlightOn ? "on" : "",
                    () => "Hold it (hotbar) and click to switch it on or off. It lights the way while it's in your hand.",
                    new HotbarSlot(HotbarKind.Flashlight));

            // Tools and fuel.
            if (backpack.HasMachete)
            {
                Item machete = Add("TOOLS & FUEL", "machete", "machete", () => "Machete", () => "",
                    () => "Hold it (hotbar) and click to hack through brush. Also clears a campsite. Careful: a tired swing can cut you.",
                    new HotbarSlot(HotbarKind.Machete));
                machete.Actions.Add((() => "Clear a campsite here", () => Place(CampItem.Clearing), () => placer.RequirementProblem(CampItem.Clearing)));
            }
            if (backpack.Matches > 0)
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

        // ---------- Where things are ----------

        /// <summary>The pack's contents (for where things are packed), listed a few times a second at most.</summary>
        PackItem Packed(string packKey)
        {
            if (packedAt < 0f || Time.unscaledTime - packedAt > 0.3f)
            {
                packed = backpack.Contents();
                packedAt = Time.unscaledTime;
            }
            return packed.Find(item => item.Key == packKey);
        }

        /// <summary>How many of something there are, for its hotbar slot ("×3", or nothing for one-offs).</summary>
        public string CountOf(string key)
        {
            PackItem item = Packed(PackKey(key));
            return item != null && item.Count > 1 ? $"×{item.Count}" : "";
        }

        /// <summary>
        /// Uses something carried to hand that has no use of its own there (the tent, a fleece, the stove): its first
        /// action on this screen, or why it can't be done. False if it isn't carried any more.
        /// </summary>
        public bool UseGear(string key)
        {
            Item item = Gather().FirstOrDefault(entry => entry.Key == key);
            if (item == null)
            {
                Notifications.Post("You don't have that any more.", 2.5f);
                return false;
            }
            if (Busy())
                return true;
            foreach ((Func<string> label, Action action, Func<string> problem) in item.Actions)
            {
                string why = problem?.Invoke();
                if (why == null)
                {
                    action();
                    return true;
                }
                if (why.Length > 0)
                {
                    Notifications.Post($"{label()}: {why}", 2.5f);
                    return true;
                }
            }
            Notifications.Post($"{item.Name()}: nothing to do with it in hand. Open your pack (Tab) to use it.", 2.5f);
            return true;
        }

        /// <summary>The pack's key for something on this screen (they mostly match).</summary>
        static string PackKey(string key) => key switch
        {
            "water" => "bottle",
            "bandage" => "bandages",
            "snare" => "snares",
            _ => key,
        };

        static string ZoneLabel(PackZone zone) => zone switch
        {
            PackZone.Bottom => "bottom of pack",
            PackZone.Core => "core of pack",
            PackZone.Top => "top of pack",
            PackZone.Lid => "lid",
            PackZone.Pockets => "side pocket",
            _ => "strapped on",
        };

        /// <summary>Where something is, in a few words for its tile: which part of the pack, worn, out at camp.</summary>
        string WhereIs(string key)
        {
            if (key == "boots")
                return "worn";
            if (key.StartsWith("garment-"))
            {
                Garment garment = backpack.Clothing.FirstOrDefault(entry => $"garment-{entry.name}" == key);
                if (garment != null && garment.worn)
                    return "worn";
            }
            switch (key)
            {
                case "tent" when !backpack.HasTent:
                    return "out at camp";
                case "sleepingbag" when backpack.BagLaidOut:
                case "mat" when backpack.MatLaidOut:
                    return "in the tent";
                case "stove" when !backpack.HasStove:
                case "chair" when !backpack.ChairInPack:
                    return "set up at camp";
            }
            if (!backpack.HasPack)
                return "";
            PackItem item = Packed(PackKey(key));
            return item == null ? "" : ZoneLabel(backpack.ZoneOf(item));
        }

        /// <summary>The numbers for the detail panel: weight, size, where packed; warmth for clothing.</summary>
        string Stats(string key)
        {
            if (key.StartsWith("garment-"))
            {
                Garment garment = backpack.Clothing.FirstOrDefault(entry => $"garment-{entry.name}" == key);
                if (garment == null)
                    return "";
                Garment warmest = backpack.WarmestFor(garment.Slot);
                string best = warmest == garment ? "  ·  your warmest" : $"  ·  your {warmest.name.ToLowerInvariant()} is warmer";
                return $"{GarmentSlots.Name(garment.Slot)}  ·  +{garment.insulation:0} °C  ·  {garment.weight:0.##} kg{(garment.waterproof ? "  ·  waterproof" : "")}{best}";
            }
            PackItem item = Packed(PackKey(key));
            if (item == null)
                return WhereIs(key);
            string where = WhereIs(key);
            return $"{item.TotalWeight:0.##} kg  ·  {item.TotalLitres:0.#} L{(where.Length > 0 ? $"  ·  {where}" : "")}{(item.Heavy ? "  ·  heavy" : "")}";
        }

        // ---------- Sections and the outfit ----------

        /// <summary>The section tabs down the left, each with how many kinds of thing are in it.</summary>
        VisualElement Tabs()
        {
            VisualElement column = UIBuild.Box("inventory-tabs");
            foreach (string name in new[] { AllSections }.Concat(Sections))
            {
                string tabSection = name;
                Button tab = UIBuild.Button("", () =>
                {
                    section = tabSection;
                    itemsKey = null;
                    RebuildIfChanged();
                }, "inventory-tab");
                bindings.Add(() =>
                {
                    int count = tabSection == AllSections ? items.Count : items.Count(item => item.Section == tabSection);
                    string title = tabSection == AllSections ? "Everything" : tabSection == ClothingSection ? "Clothing (outfit)" : Title(tabSection);
                    tab.SetText($"{title}   {count}");
                });
                tabs[name] = tab;
                column.Add(tab);
            }
            return column;
        }

        static string Title(string upper) =>
            System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(upper.ToLowerInvariant()).Replace(" And ", " and ");

        /// <summary>
        /// What you're wearing, head to feet: a row for each part of you with everything you own for it, the one
        /// you're wearing highlighted, the warmest marked, and how warm each is. Click one to wear it instead.
        /// </summary>
        VisualElement Outfit()
        {
            VisualElement outfit = UIBuild.Box("outfit");
            outfit.Add(gridBindings.Text(() =>
                $"Your clothes add +{backpack.ClothingInsulation:0} °C. You're comfortable down to about {vitals.ComfortTemperature:0} °C; "
                + $"it feels like {vitals.FeltTemperature:0} °C.{(backpack.WearingWaterproof ? " Your rain shell is on." : "")}", "text"));
            foreach (GarmentSlot slot in GarmentSlots.Order)
            {
                VisualElement row = UIBuild.Box("outfit-row").With(UIBuild.Text(GarmentSlots.Name(slot), "outfit-slot"));
                List<Garment> owned = backpack.Clothing.Where(garment => garment.Slot == slot).OrderByDescending(garment => garment.insulation).ToList();
                if (owned.Count == 0)
                    row.Add(UIBuild.Text($"Nothing yet: {GarmentSlots.Example(slot)}, from an outdoor store or trading post.", "reason"));
                foreach (Garment garment in owned)
                {
                    Garment wear = garment;
                    Button button = UIBuild.Button("", () =>
                    {
                        backpack.ToggleGarment(wear);
                        selected = $"garment-{wear.name}";
                        itemsKey = null;
                        RebuildIfChanged();
                    }, "outfit-item");
                    if (garment.worn)
                        button.AddToClassList("worn");
                    bool warmest = garment == owned[0] && owned.Count > 1;
                    string tags = (garment.worn ? "WORN" : "") + (warmest ? (garment.worn ? " · WARMEST" : "WARMEST") : "");
                    button.Add(UIBuild.Text(garment.name, "outfit-name"));
                    button.Add(UIBuild.Text($"+{garment.insulation:0} °C{(garment.waterproof ? " · waterproof" : "")}{(tags.Length > 0 ? "   " + tags : "")}", "small"));
                    VisualElement warmth = UIBuild.Box("outfit-warmth");
                    warmth.style.width = Mathf.Clamp(garment.insulation * 12f, 6f, 150f);
                    button.Add(warmth);
                    button.tooltip = garment.worn ? "Click to take it off" : "Click to wear it (instead of what you have on here)";
                    row.Add(button);
                }
                outfit.Add(row);
            }
            Button boots = UIBuild.Button("", () =>
            {
                selected = "boots";
                ShowDetail();
            }, "outfit-item", "worn");
            boots.Add(UIBuild.Text(backpack.BootsName, "outfit-name"));
            boots.Add(UIBuild.Text($"feet tire {(backpack.BootsStrain < 1f ? $"{(1f - backpack.BootsStrain) * 100f:0}% slower" : backpack.BootsStrain > 1f ? $"{(backpack.BootsStrain - 1f) * 100f:0}% faster" : "normally")}"
                                   + $"{(backpack.BootsWaterproof ? " · waterproof" : "")}{(backpack.BootsWarmth > 0f ? $" · +{backpack.BootsWarmth:0} °C" : "")}   WORN", "small"));
            outfit.Add(UIBuild.Box("outfit-row").With(UIBuild.Text("Feet", "outfit-slot"), boots));
            return outfit;
        }

        // ---------- Building the screen ----------

        void RebuildIfChanged()
        {
            List<Item> gathered = Gather();
            string key = section + "|" + string.Join(",", gathered.Select(item => item.Key + ":" + item.Icon))
                         + (section == ClothingSection ? "|" + string.Join(",", backpack.Clothing.Select(garment => garment.worn ? "1" : "0")) : "");
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
            foreach ((string name, Button tab) in tabs)
                if (name == section)
                    tab.AddToClassList("selected");
                else
                    tab.RemoveFromClassList("selected");
            if (section == ClothingSection)
                grid.Add(Outfit());
            else
                foreach (string heading in Sections)
                {
                    if (section != AllSections && section != heading)
                        continue;
                    List<Item> inSection = items.Where(item => item.Section == heading).ToList();
                    if (inSection.Count == 0)
                        continue;
                    grid.Add(UIBuild.Text(heading, "heading", "inventory-section"));
                    VisualElement row = UIBuild.Box("inventory-row");
                    foreach (Item item in inSection)
                        row.Add(Tile(item));
                    grid.Add(row);
                }
            if (grid.childCount == 0)
                grid.Add(UIBuild.Text("Nothing here.", "small"));
            gridScroll.scrollOffset = scroll;
            List<Item> shown = section == AllSections ? items : items.Where(item => item.Section == section).ToList();
            if (selected == null || !shown.Any(item => item.Key == selected))
                selected = shown.Count > 0 ? shown[0].Key : null;
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
            tile.Add(gridBindings.Text(() => Find(key)?.Where?.Invoke() ?? "", "item-where"));
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
            if (item.Hotbar.HasValue)
                MakeDraggable(tile, () => Find(key)?.Hotbar, () => item.Icon, -1);
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
                detailStats.SetText("");
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
                detailStats.SetText(Stats(key));
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
                Button button = UIBuild.Button("", () =>
                {
                    // A drag that ends on its own slot isn't a click.
                    if (Time.unscaledTime - droppedAt > 0.1f)
                        backpack.ClearHotbar(slot);
                }, "hotbar-tile");
                hotbarTiles[slot] = button;
                MakeDraggable(button, () => slot < backpack.Hotbar.Count && backpack.Hotbar[slot].kind != HotbarKind.Empty ? backpack.Hotbar[slot] : null,
                    () => IconKey(backpack.Hotbar[slot]), slot);
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
            HotbarKind.FishingRod => Current != null && Current.backpack.HasGoodRod ? "rod" : "fishing",
            HotbarKind.Flashlight => "flashlight",
            HotbarKind.Gear => slot.icon,
            _ => null,
        };

        // ---------- Dragging to the hotbar ----------

        readonly VisualElement[] hotbarTiles = new VisualElement[Backpack.HotbarSize];
        VisualElement dragGhost;
        HotbarSlot? dragging;
        int dragFrom = -1, dragPointer = -1;
        Vector2 dragStart;
        bool dragMoved;
        float droppedAt = -1f;

        /// <summary>
        /// Lets something be dragged from <paramref name="element"/> onto a hotbar slot: a tile (from −1), or another
        /// slot (<paramref name="fromSlot"/>), which swaps the two. A press that hardly moves is still a click.
        /// </summary>
        void MakeDraggable(VisualElement element, Func<HotbarSlot?> what, Func<string> icon, int fromSlot)
        {
            element.RegisterCallback<PointerDownEvent>(down =>
            {
                if (down.button != 0 || Busy())
                    return;
                HotbarSlot? slot = what();
                if (slot == null)
                    return;
                dragging = slot;
                dragFrom = fromSlot;
                dragPointer = down.pointerId;
                dragStart = down.position;
                dragMoved = false;
                Texture2D picture = icons != null ? icons.Get(icon()) : null;
                dragGhost.style.backgroundImage = picture != null ? Background.FromTexture2D(picture) : StyleKeyword.None;
            }, TrickleDown.TrickleDown);
            element.RegisterCallback<PointerMoveEvent>(move => DragTo(move.pointerId, move.position), TrickleDown.TrickleDown);
            element.RegisterCallback<PointerUpEvent>(up =>
            {
                if (up.pointerId == dragPointer)
                    EndDrag(drop: true, up.position);
            }, TrickleDown.TrickleDown);
        }

        void DragTo(int pointer, Vector2 position)
        {
            if (dragging == null || pointer != dragPointer)
                return;
            if (!dragMoved && (position - dragStart).sqrMagnitude < 64f)
                return;
            dragMoved = true;
            Vector2 local = screen.WorldToLocal(position);
            dragGhost.style.left = local.x - 36f;
            dragGhost.style.top = local.y - 36f;
            dragGhost.SetVisible(true);
            dragGhost.BringToFront();
            foreach (VisualElement tile in hotbarTiles)
                if (tile != null)
                {
                    if (tile.worldBound.Contains(position))
                        tile.AddToClassList("drop-target");
                    else
                        tile.RemoveFromClassList("drop-target");
                }
        }

        void EndDrag(bool drop, Vector2 position = default)
        {
            if (dragging == null)
                return;
            HotbarSlot item = dragging.Value;
            bool moved = dragMoved;
            dragging = null;
            dragPointer = -1;
            dragGhost?.SetVisible(false);
            int target = -1;
            for (int i = 0; i < hotbarTiles.Length; i++)
            {
                if (hotbarTiles[i] == null)
                    continue;
                if (drop && moved && hotbarTiles[i].worldBound.Contains(position))
                    target = i;
                hotbarTiles[i].RemoveFromClassList("drop-target");
            }
            if (!drop || !moved)
                return;
            droppedAt = Time.unscaledTime;
            if (target < 0)
            {
                // A slot dragged off the hotbar is emptied.
                if (dragFrom >= 0)
                    backpack.ClearHotbar(dragFrom);
                return;
            }
            if (dragFrom >= 0)
            {
                // Two slots swap.
                HotbarSlot there = backpack.Hotbar[target];
                backpack.ClearHotbar(dragFrom);
                backpack.AssignHotbar(item, target);
                if (there.kind != HotbarKind.Empty && dragFrom != target)
                    backpack.AssignHotbar(there, dragFrom);
                return;
            }
            backpack.AssignHotbar(item, target);
            Notifications.Post($"{Player.Hotbar.Describe(item)} is on key {target + 1}.", 2f);
        }

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
            // The numbers are on the weight bar above; this says how it feels.
            return backpack.HasPack ? feel : $"Carrying {weight:0.0} kg in your hands and pockets. {feel}";
        }

        static string FormatHours(float hours) => hours >= 48f ? $"{hours / 24f:0} days" : $"{Mathf.CeilToInt(hours)} h";
    }
}
