using System.Collections.Generic;
using Backpacking.Camp;
using Backpacking.Interaction;
using Backpacking.Survival;
using Backpacking.UI;
using Backpacking.World;
using Unity.Netcode;
using UnityEngine;

namespace Backpacking.Net
{
    /// <summary>
    /// Looking after each other: handing a friend things or money, giving them a drink, bandaging their cut,
    /// giving them antibiotics, helping them up when they're down, sharing a cooked meal, and borrowing their
    /// lantern. Whoever helps spends their own supplies; the friend's game does the rest.
    /// </summary>
    public partial class CoopWorld
    {
        Vitals vitals;
        PlayerActivity activity;
        GameObject giftHolder;

        void SpawnHelp()
        {
            vitals = player != null ? player.GetComponent<Vitals>() : FindAnyObjectByType<Vitals>();
            activity = player != null ? player.GetComponent<PlayerActivity>() : FindAnyObjectByType<PlayerActivity>();
            OtherHikers.Feed = (id, food, drink) => FeedRpc(food, drink, LocalName(), RpcTarget.Single(id, RpcTargetUse.Temp));
            CampLantern.Borrowed += OnLanternBorrowed;
        }

        void DespawnHelp()
        {
            OtherHikers.Feed = null;
            CampLantern.Borrowed -= OnLanternBorrowed;
            if (giftHolder != null)
                Destroy(giftHolder);
        }

        RpcParams To(ulong client) => RpcTarget.Single(client, RpcTargetUse.Temp);

        // ---------- Giving things ----------

        /// <summary>Opens the packing screen with a box for <paramref name="friend"/>: what goes in it is handed over when it closes.</summary>
        public void OpenGift(ulong friend, string friendName)
        {
            if (PackingView.Current == null || backpack == null)
                return;
            if (giftHolder != null)
                Destroy(giftHolder);
            giftHolder = new GameObject($"Gift for {friendName}");
            var box = giftHolder.AddComponent<Backpack>();
            box.enabled = false;
            PackingView.Current.Open(box, friendName, gift: true, onClose: () =>
            {
                if (box == null)
                    return;
                if (box.Contents().Count > 0)
                {
                    GiftRpc(JsonUtility.ToJson(box.CaptureState()), LocalName(), To(friend));
                    Notifications.Post($"You hand {friendName} {Describe(box)}.", 3f);
                }
                Destroy(giftHolder);
            });
        }

        static string Describe(Backpack box)
        {
            List<PackItem> items = box.Contents();
            if (items.Count == 0)
                return "nothing";
            string first = items[0].Count > 1 ? $"{items[0].Count} × {items[0].Name.ToLowerInvariant()}" : items[0].Name.ToLowerInvariant();
            return items.Count == 1 ? first : $"{first} and {items.Count - 1} more thing{(items.Count > 2 ? "s" : "")}";
        }

        /// <summary>Things handed over: into this player's pack. What won't go in (a second tent, say) goes back.</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        void GiftRpc(string json, string from, RpcParams rpcParams)
        {
            BackpackState state = JsonUtility.FromJson<BackpackState>(json);
            if (state == null || backpack == null)
                return;
            var holder = new GameObject("Gift");
            var box = holder.AddComponent<Backpack>();
            box.enabled = false;
            box.RestoreState(state);
            string what = Describe(box);
            TakeEverything(box, backpack);
            Notifications.Post($"{from} hands you {what}.", 4f);
            if (box.Contents().Count > 0)
                ReturnRpc(JsonUtility.ToJson(box.CaptureState()), LocalName(), To(rpcParams.Receive.SenderClientId));
            Destroy(holder);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ReturnRpc(string json, string from, RpcParams rpcParams)
        {
            BackpackState state = JsonUtility.FromJson<BackpackState>(json);
            if (state == null || backpack == null)
                return;
            var holder = new GameObject("Returned");
            var box = holder.AddComponent<Backpack>();
            box.enabled = false;
            box.RestoreState(state);
            Notifications.Post($"{from} had no use for {Describe(box)}: you take it back.", 4f);
            TakeEverything(box, backpack);
            Destroy(holder);
        }

        /// <summary>Moves all it can from <paramref name="from"/> into <paramref name="into"/>, stopping on anything that only swaps.</summary>
        static void TakeEverything(Backpack from, Backpack into)
        {
            foreach (PackItem item in from.Contents())
                for (int i = 0; i < item.Count; i++)
                {
                    int before = from.Contents().Find(entry => entry.Key == item.Key)?.Count ?? 0;
                    if (before == 0 || from.MoveOne(item.Key, into) != null)
                        break;
                    PackItem left = from.Contents().Find(entry => entry.Key == item.Key);
                    if (left != null && left.Count >= before)
                        break;
                }
        }

        public void GiveMoney(ulong friend, int amount)
        {
            if (backpack == null || !backpack.TrySpendMoney(amount))
                return;
            MoneyRpc(amount, LocalName(), To(friend));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void MoneyRpc(int amount, string from, RpcParams rpcParams)
        {
            backpack?.AddMoney(amount);
            Notifications.Post($"{from} gives you ${amount}.", 3f);
        }

        // ---------- Looking after them ----------

        public void GiveDrink(ulong friend)
        {
            if (backpack == null || backpack.SafeWater < backpack.SipLitres)
                return;
            float litres = backpack.SipLitres;
            // Out of your bottle, into them.
            backpack.PourSafeWater(litres);
            DrinkRpc(litres, LocalName(), To(friend));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void DrinkRpc(float litres, string from, RpcParams rpcParams)
        {
            backpack?.DrinkTapWater(litres);
            Notifications.Post($"{from} gives you a drink of water.", 3f);
        }

        public void Bandage(ulong friend, string friendName)
        {
            if (backpack == null || activity == null || backpack.Bandages <= 0)
                return;
            activity.Begin($"Bandaging {friendName}'s cut", 3f, () =>
            {
                if (backpack.TryUseBandage())
                    BandageRpc(LocalName(), To(friend));
            });
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void BandageRpc(string from, RpcParams rpcParams)
        {
            if (vitals != null && vitals.Bandage())
                Notifications.Post($"{from} cleans and bandages your cut.", 3.5f);
        }

        public void GiveAntibiotics(ulong friend)
        {
            if (backpack == null || !backpack.TryUseAntibiotics())
                return;
            AntibioticsRpc(LocalName(), To(friend));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void AntibioticsRpc(string from, RpcParams rpcParams)
        {
            vitals?.TakeAntibiotics();
            Notifications.Post($"{from} gives you a course of antibiotics. The infection should clear in a few hours.", 4f);
        }

        public void HelpUp(ulong friend, string friendName)
        {
            if (activity == null)
                return;
            activity.Begin($"Helping {friendName} up", 2f, () => HelpUpRpc(LocalName(), To(friend)));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void HelpUpRpc(string from, RpcParams rpcParams) => Rescue.Current?.HelpUp(from);

        [Rpc(SendTo.SpecifiedInParams)]
        void FeedRpc(float food, float drink, string from, RpcParams rpcParams)
        {
            if (vitals == null)
                return;
            vitals.Eat(food);
            vitals.Drink(drink);
            Notifications.Post($"{from} shares a hot meal with you.", 3.5f);
        }

        // ---------- Borrowing a lantern ----------

        void OnLanternBorrowed(string ownerKey) => LanternBorrowedRpc(ownerKey, LocalName());

        [Rpc(SendTo.NotMe)]
        void LanternBorrowedRpc(string ownerKey, string by)
        {
            if (ownerKey != CampOwner.LocalKey || backpack == null)
                return;
            backpack.LoseLantern();
            Notifications.Post($"{by} borrowed your lantern. Ask for it back: they can hand it over (look at them, E).", 5f);
        }
    }
}
