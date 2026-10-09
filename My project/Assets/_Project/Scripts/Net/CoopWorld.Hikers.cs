using System.Collections.Generic;
using Backpacking.Camp;
using Backpacking.Saving;
using Backpacking.Survival;
using Backpacking.Trade;
using Backpacking.Trip;
using Backpacking.UI;
using Unity.Netcode;
using UnityEngine;

namespace Backpacking.Net
{
    /// <summary>
    /// Guests' hikers kept by the host: a guest's game sends its hiker (how they are, what they carry, where they are)
    /// now and then and as it leaves, and the host keeps it in its save under the guest's key. Rejoining, they come
    /// back as they left. Someone new joining a trip already under way starts beside the host; if the host is past
    /// the store, with a basic kit bought from their starting money, so they can set off straight away.
    /// </summary>
    public partial class CoopWorld
    {
        const float HikerSaveEvery = 20f;
        /// <summary>A returning hiker more than this far from everyone (the host loaded an older save, say) starts beside the host instead.</summary>
        const float RejoinRange = 250f;

        /// <summary>The basic kit a late joiner gets, bought in this order while the money lasts.</summary>
        static readonly ShopItemId[] StarterKit =
        {
            ShopItemId.TrekkingPack, ShopItemId.OnePersonTent, ShopItemId.SummerBag, ShopItemId.FoamMat, ShopItemId.Stove, ShopItemId.GasCanister,
            ShopItemId.WaterBottle, ShopItemId.Matches, ShopItemId.RainShell, ShopItemId.HikingBoots, ShopItemId.DehydratedMeal,
            ShopItemId.DehydratedMeal, ShopItemId.TrailMix, ShopItemId.TrailMix, ShopItemId.Bandages, ShopItemId.Machete,
        };

        // The host's record of each guest's key.
        readonly Dictionary<ulong, string> guestKeys = new();
        float nextHikerSave;
        ArrivalGuide arrival;

        void SpawnHikers()
        {
            arrival = FindAnyObjectByType<ArrivalGuide>();
            if (!IsServer)
            {
                nextHikerSave = Time.unscaledTime + HikerSaveEvery;
                CoopSession.Leaving += SendHiker;
            }
        }

        void DespawnHikers() => CoopSession.Leaving -= SendHiker;

        void UpdateHikers()
        {
            if (IsServer || !synced || Time.unscaledTime < nextHikerSave)
                return;
            nextHikerSave = Time.unscaledTime + HikerSaveEvery;
            SendHiker();
        }

        /// <summary>A guest's hiker to the host, to keep.</summary>
        void SendHiker()
        {
            if (IsServer || !synced || saves == null || !IsSpawned)
                return;
            HikerSaveRpc(JsonUtility.ToJson(saves.CaptureHiker(CampOwner.LocalKey)));
        }

        [Rpc(SendTo.Server)]
        void HikerSaveRpc(string json, RpcParams rpcParams = default)
        {
            if (saves == null || !guestKeys.TryGetValue(rpcParams.Receive.SenderClientId, out string key))
                return;
            HikerSave hiker = JsonUtility.FromJson<HikerSave>(json);
            if (hiker == null)
                return;
            hiker.key = key;
            saves.Guests[key] = hiker;
        }

        /// <summary>What the host has of a joining guest's hiker ("" if they're new to this trip).</summary>
        string StoredHiker(ulong guest, string key)
        {
            if (string.IsNullOrEmpty(key))
                return "";
            guestKeys[guest] = key;
            return saves != null && saves.Guests.TryGetValue(key, out HikerSave hiker) ? JsonUtility.ToJson(hiker) : "";
        }

        /// <summary>
        /// A guest's start on the trip: their hiker as they left it, if the host kept one; or, new, beside the host,
        /// at the host's stage of getting to the trail (with a basic kit if the store's behind them).
        /// </summary>
        void StartGuest(string hikerJson, int hostPhase, Vector3 spot, float facing)
        {
            HikerSave hiker = string.IsNullOrEmpty(hikerJson) ? null : JsonUtility.FromJson<HikerSave>(hikerJson);
            if (hiker != null && saves != null)
            {
                bool near = (hiker.position - spot).sqrMagnitude < RejoinRange * RejoinRange;
                saves.ApplyHiker(hiker, near ? null : spot, facing);
                string name = hiker.character != null && !string.IsNullOrEmpty(hiker.character.name) ? $", {hiker.character.name}" : "";
                Notifications.Post(near ? $"Welcome back{name}. You're as you left the trip." : $"Welcome back{name}. You've caught up with the others.", 6f);
                return;
            }

            PlaceBesideHost(spot, facing);
            var phase = (ArrivalPhase)hostPhase;
            if (arrival == null || phase <= ArrivalPhase.Shopping)
            {
                // Still getting to the store: shop with your own money like everyone else.
                if (arrival != null)
                    arrival.Restore(hostPhase, arrival.TutorialPending);
                return;
            }
            int spent = BuyStarterKit();
            arrival.Restore((int)(phase == ArrivalPhase.OnTheTrail ? ArrivalPhase.OnTheTrail : ArrivalPhase.Driving), false);
            Notifications.Post($"Your friends are already on their way, so you're kitted out: a {backpack.Pack.Name.ToLowerInvariant()} with a tent, "
                               + $"sleeping bag, mat, stove and food, for ${spent} of your money (${backpack.Money} left).", 10f);
        }

        void PlaceBesideHost(Vector3 spot, float facing)
        {
            if (player == null || player.Mounted)
                return;
            var controller = player.GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = false;
            player.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, facing, 0f));
            if (controller != null)
                controller.enabled = true;
        }

        /// <summary>The basic kit, straight into the pack (worn things put on), at the store's usual prices. Returns what it cost.</summary>
        int BuyStarterKit()
        {
            if (backpack == null)
                return 0;
            int spent = 0;
            foreach (ShopItemId id in StarterKit)
            {
                ShopItem item = ShopCatalog.Get(id);
                if (item.Problem(backpack) != null || !backpack.TrySpendMoney(item.BasePrice))
                    continue;
                item.ApplyTo(backpack);
                spent += item.BasePrice;
            }
            backpack.FillWithTapWater();
            return spent;
        }
    }
}
