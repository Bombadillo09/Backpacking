using System;
using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Survival
{
    /// <summary>Where something sits in or on the pack. Saves store this by number: new zones go at the end.</summary>
    public enum PackZone
    {
        /// <summary>The bottom of the main compartment: light, bulky things you need only at camp.</summary>
        Bottom,
        /// <summary>The middle, against your back: where heavy things carry best.</summary>
        Core,
        /// <summary>The top of the main compartment: what you want during the day.</summary>
        Top,
        /// <summary>The lid pocket: small things to hand.</summary>
        Lid,
        /// <summary>The side pockets: bottles.</summary>
        Pockets,
        /// <summary>Strapped on the outside: bulky things, but they swing, snag on brush and get rained on.</summary>
        Straps,
    }

    [Serializable]
    public struct ZoneEntry
    {
        public string key;
        public PackZone zone;
    }

    /// <summary>One kind of thing that can be packed, or moved between the pack and the truck bed.</summary>
    public sealed class PackItem
    {
        public string Key, Name, Icon;
        public int Count;
        /// <summary>Litres and kilograms of one.</summary>
        public float Litres, Weight;
        public PackZone DefaultZone;
        public bool Strappable;
        /// <summary>Too big or awkward to go inside: a foam mat, a bow, a deer hide.</summary>
        public bool StrapOnly;

        public float TotalLitres => Litres * Count;
        public float TotalWeight => Weight * Count;
        /// <summary>Dense and heavy: carries best in the middle, against your back.</summary>
        public bool Heavy => TotalWeight >= 0.35f && Weight / Mathf.Max(0.05f, Litres) >= 0.35f;
    }

    /// <summary>
    /// Packing: the pack model you carry, everything in a container as a list of things that can be moved (to and
    /// from the truck bed, which is a Backpack too), and where each is packed. Packing well matters: heavy things
    /// in the middle against your back carry far better than in the lid or hanging off the straps, the main
    /// compartment only holds so much, and whatever is strapped outside snags in brush and gets rained on.
    /// </summary>
    public partial class Backpack
    {
        [Header("Pack")]
        [SerializeField] PackModel packModel = PackModel.Trekking55;
        [Tooltip("Where things are packed, by item key. Anything not listed is in its usual place.")]
        [SerializeField] List<ZoneEntry> zones = new();
        [Tooltip("Owns a stove at all. HasStove is whether it's packed (false while it's set up at camp).")]
        [SerializeField] bool ownsStove = true;
        [Tooltip("The mat and sleeping bag are laid out in the tent, not in the pack.")]
        [SerializeField] bool matOut, bagOut;

        /// <summary>How wet the sleeping bag is, 0 dry to 1 soaked. A wet bag is far less warm.</summary>
        float bagWetness;
        // A summary of how the pack is packed, refreshed twice a second (working it out lists everything).
        float balance = 1f;
        float balanceCheckedAt = -1f;
        int strapped;
        bool padOutside, tentOutside, chairOutside, bottleOutside, macheteOutside, bagOutside;

        const float CanisterGrams = 230f;
        const int MatchesPerBox = 12;
        const int BandagesPerPack = 3;

        public PackModel PackModel => packModel;
        public PackInfo Pack => PackModels.Get(packModel);
        public bool HasPack => packModel != PackModel.None;
        public bool OwnsStove => ownsStove;
        public bool OwnsTent => !string.IsNullOrEmpty(tentName);
        public bool HasSleepingBag => !string.IsNullOrEmpty(sleepingBagName);
        public float BagWetness => bagWetness;
        public bool MatLaidOut => HasMat && matOut;
        public bool BagLaidOut => HasSleepingBag && bagOut;

        /// <summary>Takes the mat out of the pack to lay it out in the tent, or packs it again.</summary>
        public void SetMatLaidOut(bool laidOut)
        {
            matOut = laidOut && HasMat;
            balanceCheckedAt = -1f;
        }

        public void SetBagLaidOut(bool laidOut)
        {
            bagOut = laidOut && HasSleepingBag;
            balanceCheckedAt = -1f;
        }

        /// <summary>Puts on a different backpack. Everything that was in the old one goes into the new one.</summary>
        public void SetPack(PackModel model)
        {
            packModel = model;
            packWeight = PackModels.Get(model).Weight;
        }

        public void AddStove()
        {
            ownsStove = true;
            hasStove = true;
        }

        public void AddMachete()
        {
            hasMachete = true;
            AssignHotbar(new HotbarSlot(HotbarKind.Machete));
        }

        public void AddFishingKit()
        {
            hasFishingKit = true;
            if (!IsTruckBed)
                AssignHotbarIfFree(new HotbarSlot(HotbarKind.FishingRod));
        }

        /// <summary>A water bottle or bladder of this size, if you have none bigger.</summary>
        public void AddBottle(float litres) => waterCapacity = Mathf.Max(waterCapacity, litres);

        /// <summary>
        /// Nothing at all but, optionally, the clothes you stand in, and some money: the start of a trip, or an
        /// empty truck bed.
        /// </summary>
        public void EmptyKit(int startingMoney, bool streetClothes)
        {
            money = startingMoney;
            pelts = hides = 0;
            tentName = "";
            tentModel = Camp.TentModel.OnePerson;
            tentShelter = tentWeight = 0f;
            hasTent = false;
            ownsStove = hasStove = false;
            gasGrams = 0f;
            matches = firewood = snares = arrows = antibiotics = bandages = 0;
            hasFishingKit = hasGoodRod = hasBow = hasWaterFilter = hasMachete = hasFlashlight = false;
            hasChair = chairInPack = false;
            hasLantern = lanternInPack = false;
            hotbar = new List<HotbarSlot>();
            NormaliseHotbar();
            food.Clear();
            waterCapacity = safeWater = untreatedWater = 0f;
            sleepingBagName = "";
            sleepingBagComfort = sleepingBagWeight = 0f;
            bagWetness = 0f;
            matName = "";
            matWarmth = matWeight = 0f;
            matRecovery = 1f;
            matOut = bagOut = false;
            clothing = streetClothes
                ? new List<Garment> { new("Cotton T-shirt", 1f, 0.2f, true), new("Jeans", 2f, 0.7f, true) }
                : new List<Garment>();
            if (streetClothes)
                SetBoots("Old sneakers", 1.35f, false, 0f);
            packModel = PackModel.None;
            packWeight = 0f;
            zones.Clear();
            balanceCheckedAt = -1f;
        }

        // ---------- What's in here ----------

        /// <summary>Everything in here that can be packed or moved, as the packing screen lists it.</summary>
        public List<PackItem> Contents()
        {
            var list = new List<PackItem>();
            void Add(string key, string name, string icon, int count, float litres, float weight, PackZone zone,
                bool strappable = false, bool strapOnly = false)
            {
                if (count > 0)
                    list.Add(new PackItem
                    {
                        Key = key, Name = name, Icon = icon, Count = count, Litres = litres, Weight = weight,
                        DefaultZone = strapOnly ? PackZone.Straps : zone, Strappable = strappable || strapOnly, StrapOnly = strapOnly,
                    });
            }

            if (hasTent && OwnsTent)
                Add("tent", tentName, "tent", 1, TentLitres(tentModel), tentWeight, PackZone.Core, strappable: true);
            if (HasSleepingBag && !bagOut)
                Add("sleepingbag", sleepingBagName, "sleepingbag", 1, sleepingBagComfort <= -8f ? 14f : sleepingBagComfort <= 2f ? 9f : 6f,
                    sleepingBagWeight, PackZone.Bottom, strappable: true);
            if (HasMat && !matOut)
            {
                bool foam = matRecovery < 1.4f;
                Add("mat", matName, foam ? "mat" : "airmat", 1, foam ? 12f : 1.5f, matWeight, PackZone.Core, strappable: true, strapOnly: foam);
            }
            if (hasStove)
                Add("stove", "Canister stove & pot", "stove", 1, 1.5f, stoveWeight, PackZone.Top);
            int canisters = Mathf.CeilToInt(gasGrams / CanisterGrams - 0.01f);
            if (canisters > 0)
                Add("gas", "Gas canister", "gas", canisters, 1f, gasGrams / canisters / 1000f + canisterWeight, PackZone.Core);
            int boxes = Mathf.CeilToInt(matches / (float)MatchesPerBox);
            Add("matches", "Waterproof matches", "matches", boxes, 0.05f, 0.03f, PackZone.Lid);
            if (hasFishingKit)
                Add("fishing", hasGoodRod ? "Telescopic rod" : "Hand line", hasGoodRod ? "rod" : "fishing", 1,
                    hasGoodRod ? 1.2f : 0.3f, hasGoodRod ? fishingRodWeight : fishingKitWeight, PackZone.Top, strappable: hasGoodRod);
            Add("snares", "Wire snare", "snare", snares, 0.15f, snareWeight, PackZone.Top);
            if (hasBow)
                Add("bow", "Recurve bow", "bow", 1, 3f, bowWeight, PackZone.Straps, strapOnly: true);
            if (arrows > 0)
                Add("arrows", $"Arrows ({arrows})", "bow", 1, 1f, arrows * arrowWeight, PackZone.Straps, strappable: true);
            if (hasWaterFilter)
                Add("filter", "Squeeze filter", "filter", 1, 0.3f, filterWeight, PackZone.Lid);
            if (hasFlashlight)
                Add("flashlight", "Flashlight", "flashlight", 1, 0.2f, flashlightWeight, PackZone.Lid);
            if (waterCapacity > 0f)
                Add("bottle", waterCapacity >= 3f ? $"{waterCapacity:0.#} L water bladder" : $"{waterCapacity:0.#} L water bottle", "water", 1,
                    waterCapacity, 0.1f + TotalWater, waterCapacity >= 3f ? PackZone.Core : PackZone.Pockets);
            if (hasMachete)
                Add("machete", "Machete", "machete", 1, 1f, macheteWeight, PackZone.Straps, strappable: true);
            Add("bandages", "Bandages", "bandage", Mathf.CeilToInt(bandages / (float)BandagesPerPack), 0.1f, 0.03f, PackZone.Lid);
            Add("antibiotics", "Antibiotics", "antibiotics", antibiotics, 0.05f, 0.03f, PackZone.Lid);
            if (hasChair && chairInPack)
                Add("chair", "Camp chair", "chair", 1, 2.5f, chairWeight, PackZone.Top, strappable: true);
            if (hasLantern && lanternInPack)
                Add("lantern", "Camp lantern", "lantern", 1, 1.5f, lanternWeight, PackZone.Top, strappable: true);
            Add("firewood", "Firewood", "firewood", firewood, 2f, firewoodWeight, PackZone.Straps, strappable: true);
            Add("pelts", "Rabbit pelt", "pelt", pelts, 1.5f, peltWeight, PackZone.Bottom, strappable: true);
            Add("hides", "Deer hide", "hide", hides, 10f, hideWeight, PackZone.Straps, strapOnly: true);

            foreach (FoodKind kind in FoodCatalog.AllKinds)
            {
                int count = CountFood(kind);
                if (count == 0)
                    continue;
                FoodInfo info = FoodCatalog.Get(kind);
                Add($"food-{kind}", info.Name, $"food-{kind}", count, Mathf.Max(0.15f, info.Weight * 1.3f), info.Weight,
                    info.Weight >= 0.5f ? PackZone.Core : PackZone.Top);
            }
            foreach (Garment garment in clothing)
                if (!garment.worn)
                    Add($"garment-{garment.name}", garment.name, UI.ItemIconLibrary.GarmentKey(garment.name), 1,
                        Mathf.Clamp(garment.insulation * 0.4f, 0.5f, 4f), garment.weight, garment.waterproof ? PackZone.Top : PackZone.Bottom);
            return list;
        }

        static float TentLitres(Camp.TentModel model) => model switch
        {
            Camp.TentModel.TwoPerson => 5f,
            Camp.TentModel.FourSeason => 7f,
            _ => 3.5f,
        };

        // ---------- Moving things ----------

        /// <summary>
        /// Moves one of something into <paramref name="to"/>: the tent, a gas canister, a box of matches, a meal...
        /// Returns why it can't be moved, or null if it was.
        /// </summary>
        public string MoveOne(string key, Backpack to)
        {
            if (to == null || to == this)
                return "Nowhere to put it";
            switch (key)
            {
                // There's room for one tent, bag, mat, bottle and fishing kit in each place: moving one onto
                // another swaps them.
                case "tent":
                {
                    if (!hasTent || !OwnsTent)
                        return "No tent here";
                    if (to.OwnsTent && !to.hasTent)
                        return "Your other tent is out: pack it away first";
                    (string name, Camp.TentModel model, float shelter, float weight) theirs = (to.tentName, to.tentModel, to.tentShelter, to.tentWeight);
                    bool swap = to.OwnsTent;
                    to.AddTent(tentName, tentModel, tentShelter, tentWeight);
                    if (swap)
                        AddTent(theirs.name, theirs.model, theirs.shelter, theirs.weight);
                    else
                    {
                        tentName = "";
                        hasTent = false;
                    }
                    break;
                }
                case "sleepingbag":
                {
                    if (!HasSleepingBag || bagOut)
                        return bagOut ? "It's laid out in your tent" : "No sleeping bag here";
                    if (to.bagOut)
                        return "The one there is laid out in the tent";
                    (string name, float comfort, float weight, float wet) theirs = (to.sleepingBagName, to.sleepingBagComfort, to.sleepingBagWeight, to.bagWetness);
                    to.SetSleepingBag(sleepingBagName, sleepingBagComfort, sleepingBagWeight);
                    to.bagWetness = bagWetness;
                    SetSleepingBag(theirs.name ?? "", theirs.comfort, theirs.weight);
                    bagWetness = theirs.wet;
                    break;
                }
                case "mat":
                {
                    if (!HasMat || matOut)
                        return matOut ? "It's laid out in your tent" : "No mat here";
                    if (to.matOut)
                        return "The one there is laid out in the tent";
                    (string name, float warmth, float weight, float recovery) theirs = (to.matName, to.matWarmth, to.matWeight, to.matRecovery);
                    to.SetMat(matName, matWarmth, matWeight, matRecovery);
                    SetMat(theirs.name ?? "", theirs.warmth, theirs.weight, theirs.recovery > 0f ? theirs.recovery : 1f);
                    break;
                }
                case "stove":
                    if (!hasStove)
                        return "No stove here";
                    if (to.ownsStove)
                        return "There's already a stove there";
                    to.AddStove();
                    hasStove = ownsStove = false;
                    break;
                case "gas":
                {
                    if (gasGrams <= 0f)
                        return "No gas here";
                    // The part-used canister goes first.
                    float part = gasGrams % CanisterGrams;
                    float grams = part > 0.5f ? part : Mathf.Min(CanisterGrams, gasGrams);
                    gasGrams -= grams;
                    to.gasGrams += grams;
                    break;
                }
                case "matches":
                {
                    if (matches <= 0)
                        return "No matches here";
                    int part = matches % MatchesPerBox;
                    int count = part > 0 ? part : Mathf.Min(MatchesPerBox, matches);
                    matches -= count;
                    to.matches += count;
                    break;
                }
                case "fishing":
                {
                    if (!hasFishingKit)
                        return "No fishing kit here";
                    (bool kit, bool rod) theirs = (to.hasFishingKit, to.hasGoodRod);
                    to.hasFishingKit = true;
                    to.hasGoodRod = hasGoodRod;
                    if (!to.IsTruckBed)
                        to.AssignHotbarIfFree(new HotbarSlot(HotbarKind.FishingRod));
                    hasFishingKit = theirs.kit;
                    hasGoodRod = theirs.rod;
                    if (!hasFishingKit)
                        RemoveFromHotbar(HotbarKind.FishingRod);
                    break;
                }
                case "snares":
                    if (!TrySpend(ref snares, 1))
                        return "No snares here";
                    to.snares++;
                    break;
                case "bow":
                    if (!hasBow)
                        return "No bow here";
                    if (to.hasBow)
                        return "There's already a bow there";
                    to.AddBow();
                    hasBow = false;
                    RemoveFromHotbar(HotbarKind.Bow);
                    break;
                case "arrows":
                    if (arrows <= 0)
                        return "No arrows here";
                    to.arrows += arrows;
                    arrows = 0;
                    break;
                case "filter":
                    if (!hasWaterFilter)
                        return "No filter here";
                    if (to.hasWaterFilter)
                        return "There's already a filter there";
                    to.hasWaterFilter = true;
                    hasWaterFilter = false;
                    break;
                case "flashlight":
                    if (!hasFlashlight)
                        return "No flashlight here";
                    if (to.hasFlashlight)
                        return "There's already a flashlight there";
                    to.AddFlashlight();
                    hasFlashlight = false;
                    RemoveFromHotbar(HotbarKind.Flashlight);
                    break;
                case "bottle":
                {
                    if (waterCapacity <= 0f)
                        return "No bottle here";
                    (float capacity, float safe, float untreated) theirs = (to.waterCapacity, to.safeWater, to.untreatedWater);
                    to.waterCapacity = waterCapacity;
                    to.safeWater = safeWater;
                    to.untreatedWater = untreatedWater;
                    if (!to.IsTruckBed)
                        to.AssignHotbarIfFree(new HotbarSlot(HotbarKind.Water));
                    waterCapacity = theirs.capacity;
                    safeWater = theirs.safe;
                    untreatedWater = theirs.untreated;
                    if (waterCapacity <= 0f)
                        RemoveFromHotbar(HotbarKind.Water);
                    break;
                }
                case "machete":
                    if (!hasMachete)
                        return "No machete here";
                    if (to.hasMachete)
                        return "There's already a machete there";
                    to.AddMachete();
                    hasMachete = false;
                    RemoveFromHotbar(HotbarKind.Machete);
                    break;
                case "bandages":
                {
                    if (bandages <= 0)
                        return "No bandages here";
                    int part = bandages % BandagesPerPack;
                    int count = part > 0 ? part : Mathf.Min(BandagesPerPack, bandages);
                    bandages -= count;
                    to.bandages += count;
                    break;
                }
                case "antibiotics":
                    if (!TrySpend(ref antibiotics, 1))
                        return "No antibiotics here";
                    to.antibiotics++;
                    break;
                case "chair":
                    if (!hasChair || !chairInPack)
                        return "No chair here";
                    if (to.hasChair)
                        return "There's already a chair there";
                    to.AddChair();
                    hasChair = chairInPack = false;
                    break;
                case "lantern":
                    if (!hasLantern || !lanternInPack)
                        return "No lantern here";
                    if (to.hasLantern)
                        return "There's already a lantern there";
                    to.AddLantern();
                    hasLantern = lanternInPack = false;
                    break;
                case "firewood":
                    if (!TrySpend(ref firewood, 1))
                        return "No firewood here";
                    to.firewood++;
                    break;
                case "pelts":
                    if (!TrySpend(ref pelts, 1))
                        return "No pelts here";
                    to.pelts++;
                    break;
                case "hides":
                    if (!TrySpend(ref hides, 1))
                        return "No hides here";
                    to.hides++;
                    break;
                default:
                    if (key.StartsWith("food-") && Enum.TryParse(key.Substring(5), out FoodKind kind))
                    {
                        // The freshest goes first: what you take on the trail should keep longest.
                        int best = -1;
                        for (int i = 0; i < food.Count; i++)
                            if (food[i].kind == kind && (best < 0 || food[i].hoursLeft > food[best].hoursLeft))
                                best = i;
                        if (best < 0)
                            return "None here";
                        to.food.Add(food[best]);
                        food.RemoveAt(best);
                        if (to.vitals != null)
                            to.AssignHotbarIfFree(new HotbarSlot(HotbarKind.Food, kind));
                        break;
                    }
                    if (key.StartsWith("garment-"))
                    {
                        string name = key.Substring(8);
                        int index = clothing.FindIndex(garment => garment.name == name && !garment.worn);
                        if (index < 0)
                            return "Not here (or you're wearing it)";
                        Garment garment = clothing[index];
                        clothing.RemoveAt(index);
                        garment.worn = false;
                        to.clothing.Add(garment);
                        break;
                    }
                    return "That can't be moved";
            }
            balanceCheckedAt = to.balanceCheckedAt = -1f;
            return null;
        }

        /// <summary>Takes a garment out of here and puts it on someone wearing <paramref name="wearer"/>'s clothes.</summary>
        public string Wear(string garmentName, Backpack wearer)
        {
            int index = clothing.FindIndex(garment => garment.name == garmentName && !garment.worn);
            if (index < 0)
                return "Not here";
            Garment garment = clothing[index];
            clothing.RemoveAt(index);
            garment.worn = false;
            wearer.clothing.Add(garment);
            wearer.Wear(garment);
            balanceCheckedAt = wearer.balanceCheckedAt = -1f;
            return null;
        }

        void RemoveFromHotbar(HotbarKind kind)
        {
            for (int i = 0; i < hotbar.Count; i++)
                if (hotbar[i].kind == kind)
                    hotbar[i] = new HotbarSlot(HotbarKind.Empty);
        }

        void AssignHotbarIfFree(HotbarSlot item)
        {
            if (!OnHotbar(item))
                AssignHotbar(item);
        }

        // ---------- Where things are packed ----------

        static bool IsMain(PackZone zone) => zone <= PackZone.Top;

        /// <summary>Where this is packed now.</summary>
        public PackZone ZoneOf(PackItem item)
        {
            if (item.StrapOnly)
                return PackZone.Straps;
            int index = zones.FindIndex(entry => entry.key == item.Key);
            PackZone zone = index >= 0 ? zones[index].zone : item.DefaultZone;
            if (zone == PackZone.Straps && !item.Strappable)
                zone = PackZone.Top;
            if (zone == PackZone.Lid && Pack.LidLitres <= 0f)
                zone = PackZone.Top;
            return zone;
        }

        public void SetZone(string key, PackZone zone)
        {
            zones.RemoveAll(entry => entry.key == key);
            zones.Add(new ZoneEntry { key = key, zone = zone });
            balanceCheckedAt = -1f;
        }

        /// <summary>
        /// How much a zone holds: litres for the main compartment (bottom, core and top share it), the lid and the
        /// side pockets; the number of things for the straps.
        /// </summary>
        public float CapacityOf(PackZone zone) => zone switch
        {
            PackZone.Lid => Pack.LidLitres,
            PackZone.Pockets => Pack.PocketLitres,
            PackZone.Straps => Pack.Straps,
            _ => Pack.MainLitres,
        };

        /// <summary>How full a zone is, in the same units as <see cref="CapacityOf"/>.</summary>
        public float UsedIn(PackZone zone, List<PackItem> contents = null)
        {
            float used = 0f;
            foreach (PackItem item in contents ?? Contents())
            {
                PackZone at = ZoneOf(item);
                if (zone == PackZone.Straps)
                    used += at == PackZone.Straps ? 1f : 0f;
                else if (IsMain(zone) ? IsMain(at) : at == zone)
                    used += item.TotalLitres;
            }
            return used;
        }

        /// <summary>Why <paramref name="item"/> (or one more of it) can't go in <paramref name="zone"/>, or null if it fits.</summary>
        public string FitProblem(PackItem item, PackZone zone, bool oneMore)
        {
            if (!HasPack)
                return "You need a pack";
            if (zone == PackZone.Straps && !item.Strappable)
                return "That won't strap on the outside";
            if (zone != PackZone.Straps && item.StrapOnly)
                return "Too big to go inside: strap it on";
            if (zone == PackZone.Lid && Pack.LidLitres <= 0f)
                return "This pack has no lid";
            List<PackItem> contents = Contents();
            PackItem existing = contents.Find(entry => entry.Key == item.Key);
            bool alreadyThere = existing != null && ZoneOf(existing) == zone;
            if (zone == PackZone.Straps)
                return alreadyThere || UsedIn(zone, contents) < CapacityOf(zone) ? null : "No free strap";
            float adding = oneMore ? item.Litres : alreadyThere ? 0f : existing?.TotalLitres ?? item.TotalLitres;
            return UsedIn(zone, contents) + adding <= CapacityOf(zone) + 0.01f ? null
                : IsMain(zone) ? "The main compartment is full" : zone == PackZone.Lid ? "The lid is full" : "The side pockets are full";
        }

        /// <summary>Where one more of this would go if packed now: its usual place, else anywhere with room.</summary>
        public PackZone? BestZoneFor(PackItem item)
        {
            PackItem existing = Contents().Find(entry => entry.Key == item.Key);
            if (existing != null && FitProblem(item, ZoneOf(existing), oneMore: true) == null)
                return ZoneOf(existing);
            PackZone usual = ZoneOf(item);
            if (FitProblem(item, usual, oneMore: true) == null)
                return usual;
            foreach (PackZone zone in new[] { PackZone.Core, PackZone.Top, PackZone.Bottom, PackZone.Pockets, PackZone.Lid, PackZone.Straps })
                if (FitProblem(item, zone, oneMore: true) == null)
                    return zone;
            return null;
        }

        /// <summary>Litres past what the pack holds (things crammed in or hung off it anyhow), or 0.</summary>
        public float Overfull
        {
            get
            {
                if (!HasPack)
                    return 0f;
                List<PackItem> contents = Contents();
                float over = 0f;
                foreach (PackZone zone in new[] { PackZone.Core, PackZone.Lid, PackZone.Pockets })
                    over += Mathf.Max(0f, UsedIn(zone, contents) - CapacityOf(zone));
                over += Mathf.Max(0f, UsedIn(PackZone.Straps, contents) - CapacityOf(PackZone.Straps)) * 3f;
                return over;
            }
        }

        /// <summary>
        /// How well the load is packed, 0 (badly) to 1 (well): heavy things in the middle against your back, light
        /// bulky things at the bottom, small things in the lid, little hanging off the outside, and nothing crammed in.
        /// </summary>
        public float Balance
        {
            get
            {
                RefreshPacking();
                return balance;
            }
        }

        void RefreshPacking()
        {
            if (balanceCheckedAt >= 0f && Time.time - balanceCheckedAt < 0.5f && Time.time >= balanceCheckedAt)
                return;
            balanceCheckedAt = Time.time;
            float total = 0f, score = 0f;
            strapped = 0;
            padOutside = tentOutside = chairOutside = bottleOutside = macheteOutside = bagOutside = false;
            foreach (PackItem item in Contents())
            {
                PackZone zone = ZoneOf(item);
                if (zone == PackZone.Straps)
                {
                    strapped++;
                    padOutside |= item.Key == "mat";
                    tentOutside |= item.Key == "tent";
                    chairOutside |= item.Key == "chair";
                    macheteOutside |= item.Key == "machete";
                    bagOutside |= item.Key == "sleepingbag";
                }
                bottleOutside |= item.Key == "bottle" && zone == PackZone.Pockets;
                float weight = item.TotalWeight;
                if (weight <= 0f)
                    continue;
                total += weight;
                score += weight * ZoneScore(zone, item);
            }
            balance = total > 0f ? score / total : 1f;
            balance *= Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(Overfull / 10f));
        }

        /// <summary>How well this is placed where it is, 0 to 1 (see <see cref="Balance"/>).</summary>
        public float PlacementScore(PackItem item) => ZoneScore(ZoneOf(item), item);

        /// <summary>
        /// How well a zone suits a thing, 0 to 1, by how dense it is: the denser, the more it wants the core against
        /// your back, and the more it pulls you about anywhere else.
        /// </summary>
        static float ZoneScore(PackZone zone, PackItem item)
        {
            float dense = Mathf.Clamp01((item.Weight / Mathf.Max(0.05f, item.Litres) - 0.1f) / 0.4f);
            return zone switch
            {
                PackZone.Core => Mathf.Lerp(0.85f, 1f, dense),
                PackZone.Bottom => Mathf.Lerp(1f, 0.5f, dense),
                PackZone.Top => Mathf.Lerp(0.95f, 0.55f, dense),
                PackZone.Lid => Mathf.Lerp(1f, 0.25f, dense),
                PackZone.Pockets => item.Key == "bottle" ? 1f : Mathf.Lerp(0.9f, 0.45f, dense),
                _ => Mathf.Lerp(0.6f, 0.25f, dense),
            };
        }

        public static string BalanceWord(float balance) =>
            balance >= 0.9f ? "well balanced" : balance >= 0.75f ? "fairly balanced" : balance >= 0.6f ? "badly balanced" : "a mess";

        /// <summary>Extra effort from a badly packed load: up to 30% more, mattering more the heavier it is.</summary>
        float BalanceExertion => IsWorn && HasPack
            ? 1f + (1f - Balance) * 0.3f * Mathf.Clamp01(CarriedWeight / Mathf.Max(1f, ComfortableLoad))
            : 1f;

        /// <summary>How much things strapped outside slow you in brush: 0 with nothing outside.</summary>
        public float StrapSnag
        {
            get
            {
                if (!IsWorn)
                    return 0f;
                RefreshPacking();
                return Mathf.Min(0.3f, strapped * 0.07f);
            }
        }

        /// <summary>What shows on the outside of the pack: strapped gear, and a bottle in a side pocket.</summary>
        public void OutsideGear(out bool pad, out bool tent, out bool chair, out bool bottle, out bool machete)
        {
            RefreshPacking();
            pad = padOutside;
            tent = tentOutside;
            chair = chairOutside;
            bottle = bottleOutside;
            machete = macheteOutside;
        }

        /// <summary>
        /// Rain soaks a sleeping bag strapped outside the pack; it dries slowly when it stops, faster by a fire.
        /// Called by the vitals with how hard it's raining where you are (0 under cover).
        /// </summary>
        /// <summary>Wets the sleeping bag by <paramref name="amount"/> (0–1): the pack dragged through a river.</summary>
        public void SoakBag(float amount)
        {
            if (HasSleepingBag && !bagOut)
                bagWetness = Mathf.Min(1f, bagWetness + amount);
        }

        public void WeatherTheBag(float rain, float hours, float fireWarmth)
        {
            if (!HasSleepingBag || hours <= 0f)
                return;
            bool raining = rain > 0.05f;
            RefreshPacking();
            if (raining && bagOutside)
                bagWetness = Mathf.Min(1f, bagWetness + rain * 0.6f * hours);
            else
                bagWetness = Mathf.Max(0f, bagWetness - ((raining ? 0.02f : 0.06f) + fireWarmth * 0.03f) * hours);
        }
    }
}
