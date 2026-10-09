using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.UI;
using Backpacking.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.Trip
{
    /// <summary>The run-up to the hike on a new trip. Saves store this by number: new stages go at the end.</summary>
    public enum ArrivalPhase
    {
        AtHome,
        Shopping,
        Packing,
        Driving,
        OnTheTrail,
    }

    /// <summary>
    /// The start of a new trip, before the trail: you wake at home with money and nothing else, drive the pickup to
    /// the outdoor store, buy a pack and your kit, pack it at the tailgate, and drive on to the trailhead. A line on
    /// the HUD says what to do next, the journal notes each stage, and the tutorial (if wanted) starts when you set
    /// off from the trailhead parking on foot.
    /// </summary>
    public class ArrivalGuide : MonoBehaviour
    {
        [SerializeField] FirstPersonController player;
        [SerializeField] Backpack backpack;
        [SerializeField] Pickup truck;
        [Tooltip("The outdoor store's front door.")]
        [SerializeField] Transform store;
        [Tooltip("The middle of the trailhead parking.")]
        [SerializeField] Transform trailhead;
        [SerializeField] UI.Tutorial tutorial;
        [Tooltip("How close counts as being at the store or the trailhead, in metres.")]
        [SerializeField] float arriveRadius = 30f;

        /// <summary>Money for gear at the start of a trip.</summary>
        public const int StartingMoney = 500;

        ArrivalPhase phase = ArrivalPhase.OnTheTrail;
        bool tutorialPending;
        int moneyBeforeShopping;
        float nextCheck;
        VisualElement panel;
        Label goal;

        UI.ShopView shop;

        public ArrivalPhase Phase => phase;
        public bool TutorialPending => tutorialPending;

        void Start()
        {
            shop = player.GetComponent<Interaction.Interactor>()?.Shop;
            goal = UIBuild.Text("", "text");
            goal.enableRichText = true;
            panel = UIBuild.Box("panel", "tutorial").With(UIBuild.Text("GETTING TO THE TRAIL", "reason"), goal);
            panel.style.maxWidth = 560f;
            panel.SetVisible(false);
            GameUI.Current.Hud.Add(panel.IgnoreMouse());
        }

        /// <summary>A new trip: at home with nothing but money. The tutorial waits until you're at the trailhead.</summary>
        public void BeginAtHome(bool tutorialWanted)
        {
            phase = ArrivalPhase.AtHome;
            tutorialPending = tutorialWanted;
            moneyBeforeShopping = backpack.Money;
            TripLog.Note($"Woke at home with ${backpack.Money} saved for gear, the pickup parked outside, and the trail a short drive away.");
        }

        /// <summary>Straight onto the trail (old saves, the benchmark): nothing to guide.</summary>
        public void Skip()
        {
            phase = ArrivalPhase.OnTheTrail;
            tutorialPending = false;
        }

        public void Restore(int savedPhase, bool savedTutorialPending)
        {
            phase = (ArrivalPhase)Mathf.Clamp(savedPhase, 0, (int)ArrivalPhase.OnTheTrail);
            tutorialPending = savedTutorialPending;
            moneyBeforeShopping = backpack.Money;
        }

        void Update()
        {
            if (panel == null)
                return;
            if (phase == ArrivalPhase.OnTheTrail)
            {
                panel.SetVisible(false);
                return;
            }
            // Checking lists the truck bed and the pack, so a few times a second is plenty.
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 0.25f;
                Advance();
            }
            panel.SetVisible(phase != ArrivalPhase.OnTheTrail && !PlayerControlLock.CursorNeeded);
            goal.SetText(GoalText());
        }

        float FlatDistance(Transform target)
        {
            if (target == null)
                return float.MaxValue;
            Vector3 offset = target.position - player.transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        void Advance()
        {
            int inTruckBed = truck != null && truck.Bed != null ? truck.Bed.Contents().Count : 0;
            // Setting off from the trailhead on foot with a pack: the hike begins, whatever came before.
            if (FlatDistance(trailhead) < arriveRadius && !player.Mounted && backpack.HasPack && backpack.IsWorn)
            {
                EnterTrail();
                return;
            }
            switch (phase)
            {
                case ArrivalPhase.AtHome:
                    if (FlatDistance(store) < arriveRadius)
                    {
                        phase = ArrivalPhase.Shopping;
                        moneyBeforeShopping = backpack.Money;
                        TripLog.Note($"Pulled in at {TripLog.Outfitter}.");
                    }
                    break;
                case ArrivalPhase.Shopping:
                    // Out of the shop screen with a pack and gear in the truck: time to pack (you can still go back in).
                    if (backpack.HasPack && inTruckBed > 0 && (shop == null || !shop.IsOpen))
                    {
                        phase = ArrivalPhase.Packing;
                        int spent = moneyBeforeShopping - backpack.Money;
                        TripLog.Note($"Spent ${spent} at {TripLog.Outfitter} and walked out wearing a new {backpack.Pack.Name}, "
                                     + $"with ${backpack.Money} left.");
                    }
                    break;
                case ArrivalPhase.Packing:
                    if (player.Mounted && backpack.Contents().Count >= 3)
                    {
                        phase = ArrivalPhase.Driving;
                        TripLog.Note($"Packed at the tailgate: {backpack.TotalWeight:0.0} kg, {Backpack.BalanceWord(backpack.Balance)}"
                                     + (inTruckBed > 0 ? $", with {inTruckBed} kinds of things left in the truck bed." : "."));
                    }
                    break;
            }
        }

        void EnterTrail()
        {
            phase = ArrivalPhase.OnTheTrail;
            panel.SetVisible(false);
            TripLog.Note($"Parked at the trailhead and shouldered the pack, {backpack.TotalWeight:0.0} kg. "
                         + $"Next stop on the trail north: {TripLog.Destination}.");
            if (tutorialPending && tutorial != null)
            {
                tutorialPending = false;
                tutorial.Begin(TripLog.HikerName);
                return;
            }
            Notifications.Post($"The trail starts here, at the posts. Head north to {TripLog.Destination} and sign the summit register. "
                               + "Your truck will be waiting here if you need to come back.", 10f);
        }

        string GoalText()
        {
            const string Key = "<color=#E07B39><b>";
            const string End = "</b></color>";
            return phase switch
            {
                ArrivalPhase.AtHome => backpack.HasPack
                    ? $"Your pack's on. Drive on east to the {Key}trailhead parking{End} where the road ends."
                    : $"Your pickup is parked outside. Look at it and press {Key}E{End} to drive, then follow the road east to "
                      + $"{Key}{TripLog.Outfitter}{End} (on your map, {Key}M{End}). You have {Key}${backpack.Money}{End} for gear.",
                ArrivalPhase.Shopping => !backpack.HasPack
                    ? $"Talk to the shopkeeper at the counter. Buy a {Key}backpack{End} first (you wear it out of the shop), then what you'll need: "
                      + "shelter, a sleeping bag and mat, a stove and gas, a water bottle, food, and warm and waterproof clothes. "
                      + $"Gear is carried out to your truck bed. ${backpack.Money} left."
                    : $"Buy what else you need (${backpack.Money} left), then go out to the truck to pack it.",
                ArrivalPhase.Packing => $"Look at the truck and choose {Key}Pack your backpack{End}. Heavy things carry best in the "
                                        + $"{Key}core{End}, against your back; light, bulky ones at the bottom. Leave what you won't need. "
                                        + $"Fill your water bottle at the {Key}tap{End} on the side of the store, then drive on east.",
                ArrivalPhase.Driving => $"Drive on east to the {Key}trailhead parking{End} where the road ends, then set off on foot with your pack on.",
                _ => "",
            };
        }
    }
}
