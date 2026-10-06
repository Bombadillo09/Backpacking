using System;
using System.Linq;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Trade;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.UI
{
    /// <summary>The trading screen: buy from the vendor's stock on the left, sell food and pelts on the right.</summary>
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

        public bool IsOpen => vendor != null;

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
                UIBuild.Box("columns").With(
                    UIBuild.Box("column").With(UIBuild.Text("BUY", "heading"), buyList),
                    UIBuild.Box("column", "next").With(UIBuild.Text("SELL", "heading"), sellList)),
                UIBuild.Box("footer").With(UIBuild.Button("Leave  (Tab)", Close)));
            panel.style.width = 1040f;
            buyList.parent.style.flexGrow = 1.4f;

            screen = UIBuild.Layer("centred").With(panel);
            screen.SetVisible(false);
            GameUI.Current.Screens.Add(screen);
        }

        public void Open(Vendor trader)
        {
            if (IsOpen)
                return;
            vendor = trader;
            title.text = vendor.DisplayName;
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
            foreach (bool gear in new[] { false, true })
            {
                bool headed = false;
                foreach (StockEntry entry in vendor.Stock)
                {
                    ShopItem item = ShopCatalog.Get(entry.item);
                    if (item.IsGear != gear)
                        continue;
                    if (!headed)
                    {
                        buyList.Add(UIBuild.Text(gear ? "Gear" : "Supplies", "small"));
                        headed = true;
                    }
                    buyList.Add(BuyRow(entry, item));
                }
            }
            buyList.scrollOffset = Vector2.zero;
        }

        VisualElement BuyRow(StockEntry entry, ShopItem item)
        {
            Vendor seller = vendor;
            int price = seller.PriceOf(entry.item);
            string Problem() => entry.quantity == 0 ? "Sold out"
                : item.Problem(backpack) ?? (backpack.Money < price ? "Not enough money" : null);

            Button buy = UIBuild.Button($"Buy  ${price}", () =>
            {
                if (Problem() != null || !backpack.TrySpendMoney(price))
                    return;
                item.ApplyTo(backpack);
                seller.TakeOneFromStock(entry);
                if (item.IsGear)
                    Notifications.Post($"Bought: {item.Name}.");
            }, "primary");
            buy.style.width = 120f;
            buyBindings.Enabled(buy, () => Problem() == null);

            return UIBuild.Box("list-row").With(
                UIBuild.Box("grow").With(
                    buyBindings.Text(() => entry.quantity < 0 ? item.Name : $"{item.Name}   ({entry.quantity} left)"),
                    buyBindings.Text(() => Problem() ?? item.Description, "reason")),
                buy);
        }

        // ---------- Selling ----------

        string SellKey() =>
            string.Join(",", FoodCatalog.AllKinds.Where(kind => backpack.CountFood(kind) > 0 && FoodCatalog.Get(kind).Value > 0))
            + (backpack.Pelts > 0 ? ",pelts" : "");

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
