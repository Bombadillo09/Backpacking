using System;
using System.Linq;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Trade;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.UI
{
    /// <summary>
    /// The trading screen: buy from the vendor's stock on the left, in tabs (gear, food, clothing), and sell food,
    /// pelts and hides on the right. Limited stock is counted for each hiker, so friends can each buy one.
    /// </summary>
    public class ShopView : MonoBehaviour
    {
        [SerializeField] Backpack backpack;

        readonly Bindings bindings = new();
        readonly Bindings buyBindings = new();
        readonly Bindings sellBindings = new();
        Vendor vendor;
        VisualElement screen;
        Label title;
        ScrollView buyList, sellList;
        string sellKey;
        static readonly string[] Tabs = { "Gear", "Food", "Clothing" };
        string tab = Tabs[0];
        readonly System.Collections.Generic.Dictionary<string, Button> tabButtons = new();

        public bool IsOpen => vendor != null;

        /// <summary>Where bought supplies and gear go: the truck bed at a roadside store with the truck parked close, else your pack.</summary>
        Backpack Delivery =>
            vendor != null && vendor.DeliversToTruck && Vehicles.Pickup.Current != null
            && Vector3.Distance(Vehicles.Pickup.Current.transform.position, vendor.transform.position) < TruckReach
                ? Vehicles.Pickup.Current.Bed : backpack;

        /// <summary>How close the truck has to be parked for the store to carry purchases out to it, in metres.</summary>
        const float TruckReach = 80f;

        void Start()
        {
            title = UIBuild.Text("", "title");
            buyList = new ScrollView();
            sellList = new ScrollView();
            buyList.style.height = sellList.style.height = 480f;

            VisualElement panel = UIBuild.Box("panel").With(
                UIBuild.Box("panel-header").With(
                    title,
                    bindings.Text(() => $"${backpack.Money}", "money")),
                bindings.Text(PackLine, "small"),
                UIBuild.Box("columns").With(
                    UIBuild.Box("column").With(UIBuild.Text("BUY", "heading"), TabRow(), buyList),
                    UIBuild.Box("column", "next").With(UIBuild.Text("SELL", "heading"), sellList)),
                UIBuild.Box("footer").With(UIBuild.Button("Leave  (Tab)", Close)));
            panel.style.width = 1040f;
            buyList.parent.style.flexGrow = 1.4f;

            screen = UIBuild.Layer("centred").With(panel);
            screen.SetVisible(false);
            GameUI.Current.Screens.Add(screen);
        }

        /// <summary>The gear, food and clothing tabs over the buy list, each with how many things it has.</summary>
        VisualElement TabRow()
        {
            VisualElement row = UIBuild.Box("row", "shop-tabs");
            foreach (string name in Tabs)
            {
                string shown = name;
                Button button = UIBuild.Button(name, () =>
                {
                    tab = shown;
                    BuildBuyList();
                }, "shop-tab");
                bindings.Add(() =>
                {
                    int count = vendor == null ? 0 : vendor.Stock.Count(entry => TabOf(entry.item) == shown);
                    button.SetText($"{shown}   {count}");
                    button.SetEnabled(count > 0);
                });
                tabButtons[name] = button;
                row.Add(button);
            }
            return row;
        }

        /// <summary>Which tab something's in: food, clothing and boots, or gear (everything else).</summary>
        static string TabOf(ShopItemId item) => ShopCatalog.CategoryOf(item) switch
        {
            "Food" => "Food",
            "Clothing & boots" => "Clothing",
            _ => "Gear",
        };

        /// <summary>How full the pack is, so you can tell what will fit before you buy.</summary>
        string PackLine()
        {
            if (!backpack.HasPack)
                return "No backpack yet: buy one first (you wear it out of the shop).";
            string truck = Delivery != backpack ? "  ·  Purchases go to your truck bed." : "";
            return $"Your {backpack.Pack.Name}: {backpack.UsedIn(PackZone.Core):0} of {backpack.CapacityOf(PackZone.Core):0} L inside, "
                   + $"{backpack.TotalWeight:0.0} kg (comfortable up to {backpack.ComfortableLoad:0}).{truck}";
        }

        public void Open(Vendor trader)
        {
            if (IsOpen)
                return;
            vendor = trader;
            title.text = vendor.DisplayName;
            // Start on the first tab with anything in it.
            tab = Tabs.FirstOrDefault(name => vendor.Stock.Any(entry => TabOf(entry.item) == name)) ?? Tabs[0];
            BuildBuyList();
            sellKey = null;
            screen.SetVisible(true);
            screen.FocusFirstButton();
            PlayerControlLock.Lock(this, needsCursor: true);
            GameUI.ClaimEscape(this, Close);
        }

        public void Close()
        {
            vendor = null;
            screen.SetVisible(false);
            PlayerControlLock.Unlock(this);
            GameUI.ReleaseEscape(this);
        }

        void Update()
        {
            if (!IsOpen)
                return;
            if (GameInput.BackpackPressed)
            {
                Close();
                return;
            }

            string key = SellKey();
            if (key != sellKey)
            {
                sellKey = key;
                BuildSellList();
            }
            bindings.Refresh();
            buyBindings.Refresh();
            sellBindings.Refresh();
        }

        void OnDisable()
        {
            if (IsOpen)
                Close();
        }

        // ---------- Buying ----------

        void BuildBuyList()
        {
            buyBindings.Clear();
            buyList.Clear();
            foreach ((string name, Button button) in tabButtons)
                if (name == tab)
                    button.AddToClassList("selected");
                else
                    button.RemoveFromClassList("selected");
            // Grouped by kind of thing: packs, shelter and sleeping, cooking, water, food, clothing, tools, first aid.
            foreach (string category in ShopCatalog.Categories)
            {
                bool headed = false;
                foreach (StockEntry entry in vendor.Stock)
                {
                    if (ShopCatalog.CategoryOf(entry.item) != category || TabOf(entry.item) != tab)
                        continue;
                    if (!headed)
                    {
                        Label heading = UIBuild.Text(category.ToUpperInvariant(), "heading");
                        heading.style.marginTop = buyList.childCount > 0 ? 14f : 0f;
                        buyList.Add(heading);
                        headed = true;
                    }
                    buyList.Add(BuyRow(entry, ShopCatalog.Get(entry.item)));
                }
            }
            buyList.scrollOffset = Vector2.zero;
        }

        VisualElement BuyRow(StockEntry entry, ShopItem item)
        {
            Vendor seller = vendor;
            int price = seller.PriceOf(entry.item);
            string Problem() => entry.quantity == 0 ? "Sold out"
                : item.Problem(backpack, Delivery) ?? (backpack.Money < price ? "Not enough money" : null);

            Button buy = UIBuild.Button($"Buy  ${price}", () =>
            {
                if (Problem() != null || !backpack.TrySpendMoney(price))
                    return;
                Backpack into = item.Worn ? backpack : Delivery;
                item.ApplyTo(into);
                seller.TakeOneFromStock(entry);
                if (into != backpack)
                    Notifications.Post(item.IsGear ? $"Bought: {item.Name}. It's carried out to your truck bed." : $"Bought: {item.Name}. In the truck bed.", 3f);
                else if (item.IsGear)
                    Notifications.Post(item.Worn ? $"Bought: {item.Name}. You put it on." : $"Bought: {item.Name}.");
                if (into == backpack && backpack.HasPack && backpack.Overfull > 0f)
                    Notifications.Post("Your pack is over-full: the extra's crammed in and hung off it anyhow. Repack it (Backpack > Repack).", 6f);
            }, "primary");
            buy.style.width = 120f;
            buyBindings.Enabled(buy, () => Problem() == null);

            return UIBuild.Box("list-row").With(
                UIBuild.Box("grow").With(
                    buyBindings.Text(() => entry.quantity < 0 ? item.Name : $"{item.Name}   ({entry.quantity} left)"),
                    buyBindings.Text(() => Problem() ?? item.Description, "reason"),
                    buyBindings.Visible(buyBindings.Text(() => item.CompareWith(backpack, Delivery) ?? "", "compare"), () => item.CompareWith(backpack, Delivery) != null)),
                buy);
        }

        // ---------- Selling ----------

        string SellKey() =>
            string.Join(",", FoodCatalog.AllKinds.Where(kind => backpack.CountFood(kind) > 0 && FoodCatalog.Get(kind).Value > 0))
            + (backpack.Pelts > 0 ? ",pelts" : "") + (backpack.Hides > 0 ? ",hides" : "");

        void BuildSellList()
        {
            Vector2 scroll = sellList.scrollOffset;
            sellList.Clear();
            sellBindings.Clear();
            foreach (FoodKind kind in FoodCatalog.AllKinds)
            {
                FoodInfo info = FoodCatalog.Get(kind);
                if (backpack.CountFood(kind) == 0 || info.Value <= 0)
                    continue;
                sellList.Add(SellRow(() => $"{info.Name}  ×{backpack.CountFood(kind)}", vendor.OfferFor(info.Value),
                    () => backpack.CountFood(kind), () => backpack.TryTakeFood(kind)));
            }

            if (backpack.Pelts > 0)
                sellList.Add(SellRow(() => $"Rabbit pelt  ×{backpack.Pelts}", vendor.OfferFor(ShopCatalog.PeltValue),
                    () => backpack.Pelts, backpack.TryTakePelt));
            if (backpack.Hides > 0)
                sellList.Add(SellRow(() => $"Deer hide  ×{backpack.Hides}", vendor.OfferFor(ShopCatalog.HideValue),
                    () => backpack.Hides, backpack.TryTakeHide));

            if (sellList.childCount == 0)
                sellList.Add(UIBuild.Text("Nothing to sell. Smoked fish, jerky and pelts fetch the best prices.", "small"));
            sellList.scrollOffset = scroll;
        }

        VisualElement SellRow(Func<string> label, int offer, Func<int> count, Func<bool> takeOne)
        {
            Button sellAll = UIBuild.Button("", () =>
            {
                while (takeOne())
                    backpack.AddMoney(offer);
            });
            sellBindings.Add(() => sellAll.SetText($"All ${offer * count()}"));
            return UIBuild.Box("list-row").With(
                sellBindings.Text(label, "grow"),
                UIBuild.Button($"Sell ${offer}", () =>
                {
                    if (takeOne())
                        backpack.AddMoney(offer);
                }),
                sellAll);
        }
    }
}
