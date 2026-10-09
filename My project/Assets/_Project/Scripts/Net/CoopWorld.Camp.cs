using System;
using System.Collections.Generic;
using System.Globalization;
using Backpacking.Camp;
using Backpacking.Gathering;
using Backpacking.Player;
using Backpacking.Saving;
using Backpacking.Survival;
using Backpacking.Trade;
using Unity.Netcode;
using UnityEngine;

namespace Backpacking.Net
{
    /// <summary>A tent, fire, stove, snare or chair everyone sees, by its number in the session.</summary>
    [Serializable]
    public class SharedItem
    {
        public int id;
        public PlacedItemState state;
    }

    /// <summary>Everything set up or changed in the world so far, for someone joining.</summary>
    [Serializable]
    public class SharedWorld
    {
        public List<SharedItem> items = new();
        public List<Vector4> clearings = new();
        public List<string> keys = new();
        public List<string> values = new();
    }

    /// <summary>Some of the world's scene objects' state: a firewood pickup gone, a bush picked, a vendor's stock.</summary>
    [Serializable]
    public class WorldThings
    {
        public List<string> keys = new();
        public List<string> values = new();
    }

    [Serializable]
    class ClearedSpots
    {
        public List<Vector4> spots = new();
    }

    /// <summary>
    /// The shared camp: gear anyone sets up (tents, fire rings, stoves, snares, chairs) appears in every game, and what
    /// anyone does to it (pitching, lighting, adding wood, a snare's catch, packing it away) happens everywhere. So do
    /// campsites cleared and brush cut, firewood picked up, berries picked and what the vendors have left. Each game
    /// watches its own world for changes and sends them; gear is numbered by whoever set it up, and belongs to them
    /// (<see cref="CampOwner"/>).
    /// </summary>
    public partial class CoopWorld
    {
        const float CampCheckEvery = 0.3f;
        const float ThingsCheckEvery = 1f;

        CampPlacer placer;
        GroundClearing clearing;
        Backpack backpack;
        SaveSystem saves;
        readonly Dictionary<int, GameObject> items = new();
        readonly Dictionary<GameObject, int> itemIds = new();
        readonly Dictionary<int, PlacedItemState> itemsKnown = new();
        readonly Dictionary<string, string> thingsKnown = new();
        readonly Dictionary<string, BerryBush> bushes = new();
        readonly Dictionary<string, Vendor> vendors = new();
        int nextItem, clearingsKnown;
        float nextCampCheck, nextThingsCheck;

        void SpawnCamp()
        {
            placer = player != null ? player.GetComponent<CampPlacer>() : FindAnyObjectByType<CampPlacer>();
            clearing = FindAnyObjectByType<GroundClearing>();
            backpack = player != null ? player.GetComponent<Backpack>() : FindAnyObjectByType<Backpack>();
            saves = FindAnyObjectByType<SaveSystem>();
            foreach (SaveId saveId in FindObjectsByType<SaveId>(FindObjectsSortMode.None))
            {
                if (saveId.TryGetComponent(out BerryBush bush))
                    bushes[saveId.Id] = bush;
                if (saveId.TryGetComponent(out Vendor vendor))
                    vendors[saveId.Id] = vendor;
            }
            if (IsServer)
            {
                // What's already set up becomes the shared world as it stands.
                CheckCamp(send: false);
                clearingsKnown = clearing != null ? clearing.Cleared.Count : 0;
                foreach (KeyValuePair<string, string> thing in CaptureThings())
                    thingsKnown[thing.Key] = thing.Value;
            }
        }

        void UpdateCamp()
        {
            if (!synced)
                return;
            if (Time.unscaledTime >= nextCampCheck)
            {
                nextCampCheck = Time.unscaledTime + CampCheckEvery;
                CheckCamp(send: true);
                FlushClearings();
            }
            if (Time.unscaledTime >= nextThingsCheck)
            {
                nextThingsCheck = Time.unscaledTime + ThingsCheckEvery;
                CheckThings();
            }
        }

        // ---------- Gear ----------

        string LocalName() => Trip.TripLog.HikerName is { Length: > 0 } name ? name : "a friend";

        /// <summary>Looks over the gear standing in this game: new, changed or gone since last time, and tells the others.</summary>
        void CheckCamp(bool send)
        {
            if (placer == null)
                return;
            foreach ((CampItem kind, GameObject instance) in placer.PlacedItems)
            {
                if (!itemIds.TryGetValue(instance, out int id))
                {
                    // Set up here: number it and mark it as this player's (unless it's a friend's, from a save).
                    if (instance.GetComponent<CampOwner>() == null)
                        CampOwner.Set(instance, CampOwner.LocalKey, LocalName());
                    id = (int)((NetworkManager.LocalClientId + 1) << 20) | ++nextItem;
                    Register(id, instance);
                    PlacedItemState state = SaveSystem.CapturePlaced(kind, instance);
                    WithOwner(state, instance);
                    itemsKnown[id] = state;
                    if (send)
                        ItemRpc(id, JsonUtility.ToJson(state));
                    continue;
                }
                PlacedItemState now = WithOwner(SaveSystem.CapturePlaced(kind, instance), instance);
                if (itemsKnown.TryGetValue(id, out PlacedItemState known) && !Differs(now, known))
                    continue;
                itemsKnown[id] = now;
                if (send)
                    ItemRpc(id, JsonUtility.ToJson(now));
            }

            // Packed away (or burned down and dismantled) here.
            List<int> gone = null;
            foreach (KeyValuePair<int, GameObject> item in items)
                if (item.Value == null)
                    (gone ??= new List<int>()).Add(item.Key);
            if (gone == null)
                return;
            foreach (int id in gone)
            {
                items.Remove(id);
                itemsKnown.Remove(id);
                if (send)
                    ItemGoneRpc(id);
            }
            itemIds.Clear();
            foreach (KeyValuePair<int, GameObject> item in items)
                itemIds[item.Value] = item.Key;
        }

        /// <summary>Over the network the owner is always named, so this player's gear is theirs in the others' games too.</summary>
        static PlacedItemState WithOwner(PlacedItemState state, GameObject instance)
        {
            if (state.owner.Length == 0 && instance.TryGetComponent(out CampOwner owner) && !string.IsNullOrEmpty(owner.key))
            {
                state.owner = owner.key;
                state.ownerName = owner.ownerName;
            }
            else if (state.owner.Length == 0)
                state.owner = CampOwner.LocalKey;
            return state;
        }

        void Register(int id, GameObject instance)
        {
            items[id] = instance;
            itemIds[instance] = id;
        }

        /// <summary>Changed enough to tell the others. A fire burning down is the same everywhere (the clock is shared), so only wood added counts.</summary>
        static bool Differs(PlacedItemState a, PlacedItemState b) =>
            a.kind != b.kind || a.stage != b.stage || a.chairStage != b.chairStage || a.matLaidOut != b.matLaidOut || a.bagLaidOut != b.bagLaidOut
            || a.burning != b.burning || a.hasCatch != b.hasCatch || a.tentModel != b.tentModel || Tent.Signature(a.beds) != Tent.Signature(b.beds)
            || (a.position - b.position).sqrMagnitude > 0.0004f || Quaternion.Angle(a.rotation, b.rotation) > 1f
            || a.fuelHours > b.fuelHours + 0.05f || Mathf.Abs(a.fuelHours - b.fuelHours) > 0.5f;

        [Rpc(SendTo.NotMe)]
        void ItemRpc(int id, string json) => ApplyItem(id, json);

        void ApplyItem(int id, string json)
        {
            if (placer == null || string.IsNullOrEmpty(json))
                return;
            PlacedItemState state = JsonUtility.FromJson<PlacedItemState>(json);
            if (state == null)
                return;
            if (items.TryGetValue(id, out GameObject instance) && instance != null)
                SaveSystem.ApplyPlaced(instance, state, backpack, fresh: false);
            else
            {
                instance = placer.Spawn(state.kind, state.position, state.rotation);
                Register(id, instance);
                SaveSystem.ApplyPlaced(instance, state, backpack, fresh: true);
            }
            itemsKnown[id] = WithOwner(SaveSystem.CapturePlaced(state.kind, instance), instance);
        }

        [Rpc(SendTo.NotMe)]
        void ItemGoneRpc(int id)
        {
            if (!items.TryGetValue(id, out GameObject instance))
                return;
            items.Remove(id);
            itemsKnown.Remove(id);
            if (instance == null)
                return;
            itemIds.Remove(instance);
            // Packed away with you inside: out you come first.
            if (instance.TryGetComponent(out Tent tent) && tent.PlayerInside)
                tent.CrawlOut(player);
            if (instance.TryGetComponent(out CampChair chair) && chair.Occupied && RestMode.Current != null)
                RestMode.Current.StandUp();
            Destroy(instance);
        }

        // ---------- Clearings ----------

        /// <summary>Sends the campsites cleared and brush cut here since last time.</summary>
        void FlushClearings()
        {
            if (clearing == null || clearing.Cleared.Count <= clearingsKnown)
                return;
            var spots = new ClearedSpots();
            for (int i = clearingsKnown; i < clearing.Cleared.Count; i++)
                spots.spots.Add(clearing.Cleared[i]);
            clearingsKnown = clearing.Cleared.Count;
            ClearingsRpc(JsonUtility.ToJson(spots));
        }

        [Rpc(SendTo.NotMe)]
        void ClearingsRpc(string json)
        {
            if (clearing == null)
                return;
            // Anything cut here in the meantime goes first, so it isn't mistaken for what just arrived.
            FlushClearings();
            ApplyClearings(JsonUtility.FromJson<ClearedSpots>(json)?.spots);
        }

        void ApplyClearings(List<Vector4> spots)
        {
            if (clearing == null || spots == null)
                return;
            foreach (Vector4 spot in spots)
                clearing.Restore(spot);
            clearingsKnown = clearing.Cleared.Count;
        }

        // ---------- Pickups, bushes, vendors ----------

        Dictionary<string, string> CaptureThings()
        {
            var things = new Dictionary<string, string>();
            if (saves != null)
                foreach (KeyValuePair<string, GameObject> pickup in saves.Pickups)
                    if (pickup.Value == null)
                        things["p:" + pickup.Key] = "1";
            foreach (KeyValuePair<string, BerryBush> bush in bushes)
                if (bush.Value != null && !float.IsInfinity(bush.Value.PickedAtHour))
                    things["b:" + bush.Key] = bush.Value.PickedAtHour.ToString("R", CultureInfo.InvariantCulture);
            foreach (KeyValuePair<string, Vendor> vendor in vendors)
                if (vendor.Value != null)
                    things["v:" + vendor.Key] = string.Join(",", vendor.Value.CaptureStock());
            return things;
        }

        void CheckThings()
        {
            WorldThings changed = null;
            foreach (KeyValuePair<string, string> thing in CaptureThings())
            {
                if (thingsKnown.TryGetValue(thing.Key, out string known) && known == thing.Value)
                    continue;
                thingsKnown[thing.Key] = thing.Value;
                changed ??= new WorldThings();
                changed.keys.Add(thing.Key);
                changed.values.Add(thing.Value);
            }
            if (changed != null)
                ThingsRpc(JsonUtility.ToJson(changed));
        }

        [Rpc(SendTo.NotMe)]
        void ThingsRpc(string json)
        {
            WorldThings things = JsonUtility.FromJson<WorldThings>(json);
            if (things != null)
                ApplyThings(things.keys, things.values);
        }

        void ApplyThings(List<string> keys, List<string> values)
        {
            for (int i = 0; i < keys.Count && i < values.Count; i++)
            {
                string key = keys[i], value = values[i];
                if (key.Length < 3)
                    continue;
                string id = key.Substring(2);
                switch (key[0])
                {
                    case 'p':
                        if (saves != null && saves.Pickups.TryGetValue(id, out GameObject pickup) && pickup != null)
                            Destroy(pickup);
                        break;
                    case 'b':
                        if (bushes.TryGetValue(id, out BerryBush bush) && bush != null
                            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float hour))
                            bush.PickedAtHour = hour;
                        break;
                    case 'v':
                        if (vendors.TryGetValue(id, out Vendor vendor) && vendor != null)
                            vendor.RestoreStock(Array.ConvertAll(value.Split(','), part => int.TryParse(part, out int count) ? count : 0));
                        break;
                }
                thingsKnown[key] = value;
            }
        }

        // ---------- For someone joining ----------

        string SharedWorldJson()
        {
            CheckCamp(send: true);
            FlushClearings();
            CheckThings();
            var world = new SharedWorld();
            foreach (KeyValuePair<int, PlacedItemState> item in itemsKnown)
                world.items.Add(new SharedItem { id = item.Key, state = item.Value });
            if (clearing != null)
                world.clearings.AddRange(clearing.Cleared);
            foreach (KeyValuePair<string, string> thing in thingsKnown)
            {
                world.keys.Add(thing.Key);
                world.values.Add(thing.Value);
            }
            return JsonUtility.ToJson(world);
        }

        void ApplySharedWorld(string json)
        {
            SharedWorld world = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<SharedWorld>(json);
            if (world == null)
                return;
            ApplyClearings(world.clearings);
            foreach (SharedItem item in world.items)
                if (item.state != null)
                    ApplyItem(item.id, JsonUtility.ToJson(item.state));
            ApplyThings(world.keys, world.values);
            // What this game had before joining (picked bushes, its own vendors' stock) is the trip's now.
            foreach (KeyValuePair<string, string> thing in CaptureThings())
                thingsKnown[thing.Key] = thing.Value;
        }
    }
}
