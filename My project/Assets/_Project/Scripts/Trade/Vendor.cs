using System;
using System.Collections.Generic;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.Trade
{
    [Serializable]
    public class StockEntry
    {
        public ShopItemId item;
        [Tooltip("How many are for sale. -1 means they never run out.")]
        public int quantity = -1;
    }

    /// <summary>
    /// A trading post: sells supplies and gear from its stock, and buys food and pelts.
    /// Remote posts charge more, and some pay better for what you bring in.
    /// </summary>
    public class Vendor : MonoBehaviour, IInteractable
    {
        [SerializeField] string vendorName = "Trading Post";
        [Tooltip("Multiplies catalog prices. Remote posts charge more.")]
        [SerializeField, Min(0.1f)] float priceMultiplier = 1f;
        [Tooltip("Fraction of an item's value paid when you sell it.")]
        [SerializeField, Range(0f, 2f)] float sellRate = 0.7f;
        [SerializeField] List<StockEntry> stock = new();

        public string DisplayName => vendorName;
        public IReadOnlyList<StockEntry> Stock => stock;

        public int PriceOf(ShopItemId item) => Mathf.CeilToInt(ShopCatalog.Get(item).BasePrice * priceMultiplier);

        /// <summary>What this post pays for something worth <paramref name="value"/>. Always at least $1.</summary>
        public int OfferFor(int value) => value <= 0 ? 0 : Mathf.Max(1, Mathf.RoundToInt(value * sellRate));

        public void TakeOneFromStock(StockEntry entry)
        {
            if (entry.quantity > 0)
                entry.quantity--;
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            options.Add(new InteractionOption("Trade", () => interactor.Shop.Open(this)));
        }
    }
}
