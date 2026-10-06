using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Trade;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backpacking.UI
{
    /// <summary>The trading screen: buy from the vendor's stock on the left, sell food and pelts on the right.</summary>
    public class ShopView : MonoBehaviour
    {
        [SerializeField] Backpack backpack;

        Vendor vendor;
        Vector2 buyScroll, sellScroll;
        GUIStyle titleStyle, headingStyle, textStyle, smallStyle;

        public bool IsOpen => vendor != null;

        public void Open(Vendor trader)
        {
            if (IsOpen)
                return;
            vendor = trader;
            PlayerControlLock.Lock(this, needsCursor: true);
        }

        public void Close()
        {
            vendor = null;
            PlayerControlLock.Unlock(this);
        }

        void Update()
        {
            if (!IsOpen)
                return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
                Close();
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

            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
                headingStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
                textStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
                smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Italic, wordWrap = true };
            }

            const float width = 900f, height = 620f;
            var area = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);

            GUILayout.BeginArea(new Rect(area.x + 20f, area.y + 14f, width - 40f, height - 28f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(vendor.DisplayName, titleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Money: ${backpack.Money}", titleStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(520f));
            GUILayout.Label("Buy", headingStyle);
            buyScroll = GUILayout.BeginScrollView(buyScroll, GUILayout.Height(470f));
            DrawBuyList();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(16f);

            GUILayout.BeginVertical();
            GUILayout.Label("Sell", headingStyle);
            sellScroll = GUILayout.BeginScrollView(sellScroll, GUILayout.Height(470f));
            DrawSellList();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Leave  (Tab)", GUILayout.Height(28f)))
                Close();
            GUILayout.EndArea();
        }

        void DrawBuyList()
        {
            bool drewGearHeading = false;
            foreach (bool gear in new[] { false, true })
            {
                foreach (StockEntry entry in vendor.Stock)
                {
                    ShopItem item = ShopCatalog.Get(entry.item);
                    if (item.IsGear != gear)
                        continue;
                    if (gear && !drewGearHeading)
                    {
                        GUILayout.Space(8f);
                        GUILayout.Label("Gear", headingStyle);
                        drewGearHeading = true;
                    }
                    DrawBuyRow(entry, item);
                }
            }
        }

        void DrawBuyRow(StockEntry entry, ShopItem item)
        {
            int price = vendor.PriceOf(entry.item);
            string problem = entry.quantity == 0 ? "Sold out"
                : item.Problem(backpack) ?? (backpack.Money < price ? "Not enough money" : null);

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(360f));
            string stockText = entry.quantity < 0 ? "" : $"   ({entry.quantity} left)";
            GUILayout.Label($"{item.Name}{stockText}", textStyle);
            GUILayout.Label(problem ?? item.Description, smallStyle);
            GUILayout.EndVertical();

            GUI.enabled = problem == null;
            if (GUILayout.Button($"Buy  ${price}", GUILayout.Width(110f), GUILayout.Height(30f)) && backpack.TrySpendMoney(price))
            {
                item.ApplyTo(backpack);
                vendor.TakeOneFromStock(entry);
                if (item.IsGear)
                    Notifications.Post($"Bought: {item.Name}.");
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        void DrawSellList()
        {
            bool any = false;
            foreach (FoodKind kind in FoodCatalog.AllKinds)
            {
                int count = backpack.CountFood(kind);
                FoodInfo info = FoodCatalog.Get(kind);
                if (count == 0 || info.Value <= 0)
                    continue;
                any = true;
                int offer = vendor.OfferFor(info.Value);
                DrawSellRow($"{info.Name}  ×{count}", offer, count,
                    () => backpack.TryTakeFood(kind));
            }

            if (backpack.Pelts > 0)
            {
                any = true;
                DrawSellRow($"Rabbit pelt  ×{backpack.Pelts}", vendor.OfferFor(ShopCatalog.PeltValue), backpack.Pelts,
                    backpack.TryTakePelt);
            }

            if (!any)
                GUILayout.Label("Nothing to sell. Smoked fish, jerky and pelts fetch the best prices.", smallStyle);
        }

        void DrawSellRow(string label, int offer, int count, System.Func<bool> takeOne)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, textStyle, GUILayout.Width(170f));
            if (GUILayout.Button($"Sell ${offer}", GUILayout.Width(75f)) && takeOne())
                backpack.AddMoney(offer);
            if (GUILayout.Button($"All ${offer * count}", GUILayout.Width(75f)))
            {
                while (takeOne())
                    backpack.AddMoney(offer);
            }
            GUILayout.EndHorizontal();
        }
    }
}
