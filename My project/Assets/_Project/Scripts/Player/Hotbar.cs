using Backpacking.Camp;
using Backpacking.Interaction;
using Backpacking.Survival;
using Backpacking.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Backpacking.Player
{
    /// <summary>
    /// Five slots of things carried to hand (belt, hip pockets): the machete, the water bottle, a snack, first
    /// aid, the fishing rod, a flashlight, or anything else from the pack. Press 1–5 to hold one (again to put it
    /// away; D-pad right cycles on a gamepad), then click (X) to use it: swing, drink, eat, take or apply, cast at
    /// the water, switch the light on; other gear does its first action (set up the stove, put on the fleece).
    /// Works with the pack on. Fill the slots from the backpack screen, by button or by dragging.
    /// </summary>
    public class Hotbar : MonoBehaviour
    {
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] Backpack backpack;
        [SerializeField] Vitals vitals;
        [SerializeField] PlayerActivity activity;
        [SerializeField] CampPlacer placer;
        [SerializeField] ItemIconLibrary icons;
        [Tooltip("Game minutes to clean and wrap a cut.")]
        [SerializeField] float bandageMinutes = 3f;
        [Tooltip("How far you can cast a line, in metres.")]
        [SerializeField] float castReach = 14f;

        static Hotbar current;
        FirstPersonController player;
        Interactor interactor;
        Light beam;

        InputAction attack;
        VisualElement bar;
        readonly Label[] names = new Label[Backpack.HotbarSize];
        readonly Label[] counts = new Label[Backpack.HotbarSize];
        readonly VisualElement[] slots = new VisualElement[Backpack.HotbarSize];
        readonly VisualElement[] pictures = new VisualElement[Backpack.HotbarSize];
        readonly string[] shownIcons = new string[Backpack.HotbarSize];

        /// <summary>The slot in hand, or −1 for empty hands.</summary>
        public int Selected { get; private set; } = -1;
        public static Hotbar Current => current;
        public Backpack Backpack => backpack;

        /// <summary>Raised when the held item is used (drunk, eaten, taken, applied), for the hand animation.</summary>
        public static event System.Action<HotbarKind> Used;

        /// <summary>What's in hand right now (Empty with nothing held).</summary>
        public HotbarSlot Held => Selected >= 0 && Selected < backpack.Hotbar.Count ? backpack.Hotbar[Selected] : new HotbarSlot(HotbarKind.Empty);

        /// <summary>The machete is in hand: clicking swings it (handled by <see cref="Undergrowth"/>).</summary>
        public static bool HoldingMachete => current != null && current.Held.kind == HotbarKind.Machete && current.backpack.HasMachete;

        /// <summary>The bow is in hand: holding the button draws it (handled by <see cref="Hunting.Bow"/>).</summary>
        public static bool HoldingBow => current != null && current.Held.kind == HotbarKind.Bow && current.backpack.HasBow;

        /// <summary>The rod (or hand line) is in hand: click at the water to fish.</summary>
        public static bool HoldingRod => current != null && current.Held.kind == HotbarKind.FishingRod && current.backpack.HasFishingKit;

        /// <summary>Switched on, whether or not it's in hand right now (it lights only while held).</summary>
        public bool FlashlightOn { get; private set; }

        /// <summary>The flashlight is in hand and shining.</summary>
        public bool Shining => FlashlightOn && Held.kind == HotbarKind.Flashlight && backpack.HasFlashlight && !activity.IsBusy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            Used = null;
        }

        void Awake()
        {
            current = this;
            attack = inputActions.FindActionMap("Player", true).FindAction("Attack", true);
            player = GetComponentInParent<FirstPersonController>();
            if (player == null)
                player = FindAnyObjectByType<FirstPersonController>();
            interactor = GetComponentInParent<Interactor>();
            if (interactor == null)
                interactor = FindAnyObjectByType<Interactor>();
        }

        void OnDestroy()
        {
            if (current == this)
                current = null;
        }

        void Start()
        {
            bar = UIBuild.Box("hotbar");
            for (int i = 0; i < Backpack.HotbarSize; i++)
            {
                names[i] = UIBuild.Text("", "hotbar-name");
                counts[i] = UIBuild.Text("", "hotbar-count");
                pictures[i] = UIBuild.Box("hotbar-icon");
                slots[i] = UIBuild.Box("hotbar-slot").With(pictures[i], UIBuild.Text((i + 1).ToString(), "hotbar-key"), names[i], counts[i]);
                bar.Add(slots[i]);
            }
            GameUI.Current.Hud.Add(bar.IgnoreMouse());
        }

        bool Free => !PlayerControlLock.MovementLocked && !placer.IsPlacing && UnityEngine.Cursor.lockState == CursorLockMode.Locked;

        void Update()
        {
            if (Free)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
                    for (int i = 0; i < keys.Length; i++)
                        if (keyboard[keys[i]].wasPressedThisFrame)
                            Select(Selected == i ? -1 : i);
                }
                if (Gamepad.current != null && Gamepad.current.dpad.right.wasPressedThisFrame)
                    Select(Selected + 1 >= Backpack.HotbarSize ? -1 : Selected + 1);

                // The machete's swing is the brush code's job and the bow shoots itself; everything else is used here.
                if (attack.WasPressedThisFrame() && !RestMode.SeatedNow)
                    UseHeld();
            }
            UpdateBeam();
            Refresh();
        }

        /// <summary>The flashlight's beam, from just below the eyes along the view, while it's held and switched on.</summary>
        void UpdateBeam()
        {
            bool shining = Shining;
            if (shining && beam == null && player != null && player.CameraPivot != null)
            {
                var go = new GameObject("Flashlight Beam");
                go.transform.SetParent(player.CameraPivot, false);
                go.transform.localPosition = new Vector3(0.18f, -0.2f, 0.25f);
                beam = go.AddComponent<Light>();
                beam.type = LightType.Spot;
                beam.spotAngle = 48f;
                beam.innerSpotAngle = 22f;
                beam.range = 32f;
                beam.intensity = 9f;
                beam.color = new Color(1f, 0.95f, 0.86f);
                beam.shadows = LightShadows.None;
            }
            if (beam == null)
                return;
            beam.enabled = shining;
            // Aimed a touch in from the hand, so the pool of light sits in the middle of the view.
            if (shining)
                beam.transform.localRotation = Quaternion.Euler(1.5f, -3f, 0f);
        }

        void Select(int slot)
        {
            Selected = slot;
            HotbarSlot held = Held;
            if (held.kind != HotbarKind.Empty)
                Notifications.Post($"In hand: {Describe(held)}", 1.5f);
        }

        /// <summary>Uses whatever's in hand. The machete swings itself (see Undergrowth).</summary>
        void UseHeld()
        {
            HotbarSlot held = Held;
            switch (held.kind)
            {
                case HotbarKind.Water:
                    if (backpack.SafeWater > 0f)
                    {
                        backpack.DrinkSafeWater();
                        Used?.Invoke(held.kind);
                    }
                    else if (backpack.UntreatedWater > 0f)
                        Notifications.Post("Only untreated water left. Drink it from the backpack if you must, or boil it first.", 3f);
                    else
                        Notifications.Post("Your bottle's empty.", 2f);
                    break;
                case HotbarKind.Food:
                    if (backpack.CountFood(held.food) <= 0)
                        Notifications.Post($"No {FoodCatalog.Get(held.food).Name.ToLowerInvariant()} left.", 2f);
                    else if (FoodCatalog.Get(held.food).NotEdibleReason is { } reason)
                        Notifications.Post(reason, 2.5f);
                    else
                    {
                        backpack.Eat(held.food);
                        Used?.Invoke(held.kind);
                    }
                    break;
                case HotbarKind.Antibiotics:
                    if (!vitals.IsInfected)
                        Notifications.Post("No infection to treat.", 2f);
                    else if (vitals.OnAntibiotics)
                        Notifications.Post("You're already taking a course.", 2f);
                    else if (backpack.TryUseAntibiotics())
                    {
                        vitals.TakeAntibiotics();
                        Used?.Invoke(held.kind);
                        Notifications.Post("You take a course of antibiotics.", 3f);
                    }
                    else
                        Notifications.Post("No antibiotics left. Trading posts sell them.", 2.5f);
                    break;
                case HotbarKind.Bandage:
                    if (!vitals.IsBleeding)
                        Notifications.Post("No cut to bandage.", 2f);
                    else if (backpack.Bandages <= 0)
                        Notifications.Post("No bandages left. Trading posts sell them.", 2.5f);
                    else if (activity.Begin("Cleaning and bandaging the cut", bandageMinutes, () =>
                        {
                            if (backpack.TryUseBandage() && vitals.Bandage())
                                Notifications.Post("Cut cleaned and bandaged.", 2.5f);
                        }))
                        Used?.Invoke(held.kind);
                    break;
                case HotbarKind.FishingRod:
                    Cast();
                    break;
                case HotbarKind.Flashlight:
                    if (!backpack.HasFlashlight)
                        break;
                    FlashlightOn = !FlashlightOn;
                    break;
                case HotbarKind.Gear:
                    if (BackpackView.Current != null && !BackpackView.Current.UseGear(held.key))
                        backpack.ClearHotbar(Selected);
                    break;
            }
        }

        /// <summary>Casts a line at the water you're looking at, if there's any within a cast.</summary>
        void Cast()
        {
            if (!backpack.HasFishingKit || interactor == null || player == null)
                return;
            Transform view = player.CameraPivot;
            RaycastHit[] hits = Physics.RaycastAll(player.AimOrigin, view.forward, castReach, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.GetComponent<WaterSource>() != null)
                {
                    interactor.Fishing.Begin(hit.point);
                    return;
                }
                // Anything solid in the way (the bank, a tree) blocks the cast.
                if (!hit.collider.isTrigger && !hit.transform.IsChildOf(player.transform))
                    break;
            }
            Notifications.Post("Look out over a lake or stream close by, then click to cast.", 2.5f);
        }

        /// <summary>What a slot shows, e.g. "Trail mix".</summary>
        public static string Describe(HotbarSlot slot) => slot.kind switch
        {
            HotbarKind.Machete => "Machete",
            HotbarKind.Water => "Water bottle",
            HotbarKind.Food => FoodCatalog.Get(slot.food).Name,
            HotbarKind.Antibiotics => "Antibiotics",
            HotbarKind.Bandage => "Bandages",
            HotbarKind.Bow => "Bow",
            HotbarKind.FishingRod => current != null && current.backpack.HasGoodRod ? "Fishing rod" : "Hand line",
            HotbarKind.Flashlight => "Flashlight",
            HotbarKind.Gear => slot.label ?? "",
            _ => "",
        };

        string Count(HotbarSlot slot) => slot.kind switch
        {
            HotbarKind.Water => $"{backpack.SafeWater:0.0} L",
            HotbarKind.Food => $"×{backpack.CountFood(slot.food)}",
            HotbarKind.Antibiotics => $"×{backpack.Antibiotics}",
            HotbarKind.Bandage => $"×{backpack.Bandages}",
            HotbarKind.Bow => $"×{backpack.Arrows}",
            HotbarKind.Flashlight => FlashlightOn ? "on" : "off",
            HotbarKind.Gear => BackpackView.Current != null ? BackpackView.Current.CountOf(slot.key) : "",
            _ => "",
        };

        float nextRefresh;
        int shownSelection = -2;

        void Refresh()
        {
            if (bar == null)
                return;
            // Counts change slowly: ten times a second, or straight away when the selection changes.
            if (Selected == shownSelection && Time.unscaledTime < nextRefresh)
                return;
            shownSelection = Selected;
            nextRefresh = Time.unscaledTime + 0.1f;
            // Hidden with menus open, while busy or asleep.
            bar.SetVisible(!activity.IsBusy && UnityEngine.Cursor.lockState == CursorLockMode.Locked);
            for (int i = 0; i < Backpack.HotbarSize; i++)
            {
                HotbarSlot slot = i < backpack.Hotbar.Count ? backpack.Hotbar[i] : new HotbarSlot(HotbarKind.Empty);
                names[i].SetText(Describe(slot));
                string icon = BackpackView.IconKey(slot);
                if (icon != shownIcons[i])
                {
                    shownIcons[i] = icon;
                    Texture2D picture = icons != null ? icons.Get(icon) : null;
                    pictures[i].style.backgroundImage = picture != null ? Background.FromTexture2D(picture) : StyleKeyword.None;
                }
                counts[i].SetText(Count(slot));
                if (i == Selected)
                    slots[i].AddToClassList("selected");
                else
                    slots[i].RemoveFromClassList("selected");
            }
        }
    }
}
