using System.Collections.Generic;
using System.Linq;
using Backpacking.Player;
using Backpacking.Survival;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.UI
{
    /// <summary>
    /// Packing your backpack. At the truck, the truck bed is on the left and your pack on the right: pack things
    /// one at a time (or all of a kind) and they go to their usual place if there's room; select something in the
    /// pack to move it to another part of it, or back to the truck. Anywhere else it's repacking: moving things
    /// around inside the pack. The top line shows how full it is, the load, and how well it's balanced; the tip
    /// under it says what's packed worst.
    /// </summary>
    public class PackingView : MonoBehaviour
    {
        [SerializeField] Backpack backpack;
        [SerializeField] ItemIconLibrary icons;

        static readonly (PackZone zone, string label)[] ZoneOrder =
        {
            (PackZone.Lid, "LID"), (PackZone.Top, "TOP"), (PackZone.Core, "CORE  ·  against your back"),
            (PackZone.Bottom, "BOTTOM"), (PackZone.Pockets, "SIDE POCKETS"), (PackZone.Straps, "STRAPPED OUTSIDE"),
        };

        static readonly (PackZone zone, string label)[] ZoneButtons =
        {
            (PackZone.Bottom, "Bottom"), (PackZone.Core, "Core"), (PackZone.Top, "Top"),
            (PackZone.Lid, "Lid"), (PackZone.Pockets, "Side pocket"), (PackZone.Straps, "Strap outside"),
        };

        readonly Bindings bindings = new();
        readonly Bindings listBindings = new();
        VisualElement screen, truckColumn, selection;
        ScrollView truckList, packList;
        Label title, selectedLabel;
        Backpack truckBed;
        string listKey, selected;
        float nextRefresh;

        public static PackingView Current { get; private set; }
        public bool IsOpen { get; private set; }

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
            title = UIBuild.Text("", "title");
            truckList = new ScrollView();
            packList = new ScrollView();
            truckList.style.height = packList.style.height = 470f;
            truckColumn = UIBuild.Box("column").With(UIBuild.Text("TRUCK BED", "heading"), truckList);

            selectedLabel = UIBuild.Text("", "text");
            VisualElement zoneButtons = UIBuild.Box("row");
            foreach ((PackZone zone, string label) in ZoneButtons)
            {
                PackZone target = zone;
                zoneButtons.Add(bindings.Enabled(UIBuild.Button(label, () => MoveSelected(target)), () => ZoneProblem(target) == null));
            }
            selection = UIBuild.Box("row").With(
                UIBuild.Box("grow").With(selectedLabel, bindings.Text(SelectionReason, "reason")),
                zoneButtons,
                bindings.Visible(bindings.Enabled(UIBuild.Button("To the truck", () => ToTruck(all: false)), () => SelectedItem() != null), () => truckBed != null),
                bindings.Visible(bindings.Enabled(UIBuild.Button("All to the truck", () => ToTruck(all: true)), () => SelectedItem()?.Count > 1), () => truckBed != null));

            VisualElement panel = UIBuild.Box("panel").With(
                UIBuild.Box("panel-header").With(title, bindings.Text(() => $"${backpack.Money}", "money")),
                bindings.Text(Summary, "small"),
                bindings.Text(Tip, "reason"),
                UIBuild.Box("columns").With(
                    truckColumn,
                    UIBuild.Box("column", "next").With(UIBuild.Text("YOUR PACK", "heading"), packList)),
                selection,
                UIBuild.Box("footer").With(UIBuild.Button("Done  (Tab)", Close)));
            panel.style.width = 1300f;

            screen = UIBuild.Layer("centred").With(panel);
            screen.SetVisible(false);
            GameUI.Current.Screens.Add(screen);
        }

        /// <summary>Opens the screen: with a truck bed to pack from, or with null to rearrange the pack on the trail.</summary>
        public void Open(Backpack from)
        {
            if (IsOpen || screen == null)
                return;
            truckBed = from;
            title.text = from != null ? "Packing at the truck" : "Repacking";
            truckColumn.SetVisible(from != null);
            selected = null;
            listKey = null;
            nextRefresh = 0f;
            IsOpen = true;
            screen.SetVisible(true);
            PlayerControlLock.Lock(this, needsCursor: true);
            GameUI.ClaimEscape(this, Close);
            Rebuild();
            screen.FocusFirstButton();
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            screen.SetVisible(false);
            PlayerControlLock.Unlock(this);
            GameUI.ReleaseEscape(this);
        }

        void OnDisable() => Close();

        void Update()
        {
            if (!IsOpen)
                return;
            if (GameInput.BackpackPressed && !PlayerControlLock.JustReleased)
            {
                Close();
                return;
            }
            // Working out what fits lists everything, so a few times a second is plenty.
            if (Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + 0.12f;
            Rebuild();
            bindings.Refresh();
            listBindings.Refresh();
        }

        // ---------- Lists ----------

        string Key(Backpack container) => container == null ? "" :
            string.Join(",", container.Contents().Select(item => $"{item.Key}:{item.Count}:{(container == backpack ? backpack.ZoneOf(item) : default)}"));

        void Rebuild()
        {
            string key = Key(backpack) + "|" + Key(truckBed) + "|" + selected + "|" + backpack.PackModel;
            if (key == listKey)
                return;
            listKey = key;
            listBindings.Clear();

            Vector2 truckScroll = truckList.scrollOffset, packScroll = packList.scrollOffset;
            truckList.Clear();
            if (truckBed != null)
            {
                List<PackItem> inTruck = truckBed.Contents();
                foreach (PackItem item in inTruck)
                    truckList.Add(TruckRow(item));
                if (inTruck.Count == 0)
                    truckList.Add(UIBuild.Text("Nothing in the truck bed. What you buy at the outdoor store is carried out here.", "small"));
            }

            packList.Clear();
            if (!backpack.HasPack)
                packList.Add(UIBuild.Text("You don't have a backpack yet. The outdoor store sells three: buy one and you'll wear it out of the shop.", "text"));
            else
            {
                List<PackItem> packed = backpack.Contents();
                foreach ((PackZone zone, string label) in ZoneOrder)
                {
                    if (zone == PackZone.Lid && backpack.Pack.LidLitres <= 0f)
                        continue;
                    PackZone section = zone;
                    packList.Add(listBindings.Text(() => $"{label}   {ZoneFullness(section)}", "heading"));
                    List<PackItem> here = packed.Where(item => backpack.ZoneOf(item) == zone).ToList();
                    foreach (PackItem item in here)
                        packList.Add(PackRow(item));
                    if (here.Count == 0)
                        packList.Add(UIBuild.Text("—", "small"));
                }
            }
            truckList.scrollOffset = truckScroll;
            packList.scrollOffset = packScroll;

            PackItem chosen = SelectedItem();
            if (chosen == null)
                selected = null;
            selection.SetVisible(backpack.HasPack);
            selectedLabel.text = chosen == null ? "Select something in your pack to move it."
                : $"{chosen.Name}{(chosen.Count > 1 ? $" ×{chosen.Count}" : "")}: in the {ZoneName(backpack.ZoneOf(chosen))}";
        }

        VisualElement Icon(string key)
        {
            VisualElement picture = UIBuild.Box("item-icon");
            picture.style.width = picture.style.height = 44f;
            picture.style.marginRight = 10f;
            Texture2D icon = icons != null ? icons.Get(key) : null;
            if (icon != null)
                picture.style.backgroundImage = icon;
            return picture;
        }

        static string Size(PackItem item) =>
            $"{item.Litres:0.#} L, {item.Weight:0.##} kg{(item.Count > 1 ? " each" : "")}{(item.StrapOnly ? "  ·  straps on outside" : item.Strappable ? "  ·  can strap on" : "")}";

        VisualElement TruckRow(PackItem item)
        {
            string key = item.Key;
            VisualElement buttons = UIBuild.Box("row");
            buttons.Add(listBindings.Enabled(UIBuild.Button("Pack", () => PackFromTruck(key, all: false), "primary"), () => PackProblem(key) == null));
            if (item.Count > 1)
                buttons.Add(listBindings.Enabled(UIBuild.Button("Pack all", () => PackFromTruck(key, all: true)), () => PackProblem(key) == null));
            if (key.StartsWith("garment-"))
                buttons.Add(UIBuild.Button("Wear", () => truckBed.Wear(key.Substring(8), backpack)));
            return UIBuild.Box("list-row").With(
                Icon(item.Icon),
                UIBuild.Box("grow").With(
                    UIBuild.Text(item.Count > 1 ? $"{item.Name}  ×{item.Count}" : item.Name, "text"),
                    listBindings.Text(() => PackProblem(key) ?? Size(item), "reason")),
                buttons);
        }

        VisualElement PackRow(PackItem item)
        {
            string key = item.Key;
            Button row = UIBuild.Button("", () =>
            {
                selected = selected == key ? null : key;
                listKey = null;
                nextRefresh = 0f;
            }, "list-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            if (selected == key)
                row.AddToClassList("selected");
            row.Add(Icon(item.Icon));
            row.Add(UIBuild.Box("grow").With(
                UIBuild.Text(item.Count > 1 ? $"{item.Name}  ×{item.Count}" : item.Name, "text"),
                UIBuild.Text($"{item.TotalLitres:0.#} L, {item.TotalWeight:0.##} kg{(item.Heavy ? "  ·  heavy" : "")}", "reason")));
            return row;
        }

        string ZoneFullness(PackZone zone)
        {
            float used = backpack.UsedIn(zone), capacity = backpack.CapacityOf(zone);
            if (zone == PackZone.Straps)
                return $"{used:0} / {capacity:0} straps";
            string shared = zone is PackZone.Bottom or PackZone.Core or PackZone.Top ? "  (main compartment)" : "";
            return $"{used:0.#} / {capacity:0} L{shared}{(used > capacity + 0.01f ? "  ·  OVER-FULL" : "")}";
        }

        static string ZoneName(PackZone zone) => zone switch
        {
            PackZone.Bottom => "bottom",
            PackZone.Core => "core",
            PackZone.Top => "top",
            PackZone.Lid => "lid",
            PackZone.Pockets => "side pockets",
            _ => "straps outside",
        };

        // ---------- Moving things ----------

        PackItem SelectedItem() => selected == null ? null : backpack.Contents().Find(item => item.Key == selected);

        string PackProblem(string key)
        {
            if (!backpack.HasPack)
                return "You need a backpack";
            PackItem item = truckBed?.Contents().Find(entry => entry.Key == key);
            if (item == null)
                return "";
            return backpack.BestZoneFor(item) == null ? "No room in your pack" : null;
        }

        void PackFromTruck(string key, bool all)
        {
            do
            {
                PackItem item = truckBed.Contents().Find(entry => entry.Key == key);
                if (item == null)
                    return;
                PackZone? zone = backpack.BestZoneFor(item);
                if (zone == null)
                {
                    Notifications.Post("There's no room for that in your pack.", 3f);
                    return;
                }
                string problem = truckBed.MoveOne(key, backpack);
                if (problem != null)
                {
                    Notifications.Post(problem, 3f);
                    return;
                }
                backpack.SetZone(key, zone.Value);
            } while (all);
        }

        void ToTruck(bool all)
        {
            if (truckBed == null || selected == null)
                return;
            do
            {
                if (backpack.MoveOne(selected, truckBed) != null)
                    break;
            } while (all && SelectedItem() != null);
        }

        string ZoneProblem(PackZone zone)
        {
            PackItem item = SelectedItem();
            if (item == null)
                return "";
            if (backpack.ZoneOf(item) == zone)
                return "It's there";
            return backpack.FitProblem(item, zone, oneMore: false);
        }

        void MoveSelected(PackZone zone)
        {
            if (ZoneProblem(zone) == null)
                backpack.SetZone(selected, zone);
        }

        string SelectionReason()
        {
            if (SelectedItem() == null)
                return "";
            foreach ((PackZone zone, string label) in ZoneButtons)
            {
                string problem = ZoneProblem(zone);
                if (problem != null && problem != "It's there" && problem != "")
                    return $"{label}: {problem}";
            }
            return "";
        }

        // ---------- How it's packed ----------

        string Summary()
        {
            if (!backpack.HasPack)
                return "No backpack.";
            PackInfo pack = backpack.Pack;
            float balance = backpack.Balance;
            return $"{pack.Name}   ·   {backpack.TotalWeight:0.0} kg (comfortable up to {backpack.ComfortableLoad:0}, at most {backpack.MaxLoad:0})"
                   + $"   ·   {Backpack.BalanceWord(balance)} ({balance * 100f:0}%)";
        }

        /// <summary>Advice on the worst-packed thing, or praise.</summary>
        string Tip()
        {
            if (!backpack.HasPack)
                return "";
            if (backpack.Overfull > 0f)
                return "It's over-full: things are crammed in and hanging off anyhow, which carries badly. Leave something in the truck, or strap it on.";
            PackItem worst = null;
            float worstCost = 0.05f;
            foreach (PackItem item in backpack.Contents())
            {
                float cost = item.TotalWeight * (1f - backpack.PlacementScore(item));
                if (cost > worstCost)
                {
                    worstCost = cost;
                    worst = item;
                }
            }
            if (worst == null)
                return backpack.Contents().Count == 0 ? "Empty. Heavy things carry best in the core, against your back." : "Nicely packed.";
            PackZone zone = backpack.ZoneOf(worst);
            string name = worst.Name.ToLowerInvariant();
            if (zone == PackZone.Straps)
                return worst.Key == "sleepingbag"
                    ? $"The {name} strapped outside swings about, snags in brush, and soaks up the rain. A wet bag is a cold night."
                    : $"The {name} strapped outside swings about and snags in brush.";
            if (worst.Heavy)
                return $"The {name} {(worst.Count > 1 ? "are" : "is")} heavy: carried best in the core, against your back, not in the {ZoneName(zone)}.";
            return $"The {name} would sit better somewhere else: light, bulky things go at the bottom, small things in the lid.";
        }
    }
}
