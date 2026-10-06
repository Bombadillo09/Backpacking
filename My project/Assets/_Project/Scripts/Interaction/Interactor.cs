using System.Collections.Generic;
using Backpacking.Camp;
using Backpacking.Gathering;
using Backpacking.Player;
using Backpacking.Saving;
using Backpacking.Survival;
using Backpacking.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Backpacking.Interaction
{
    /// <summary>
    /// Finds what the player is looking at and runs its interactions. One option runs straight away;
    /// several open a small menu. Also the hub other systems use to reach the player's backpack,
    /// vitals and activities.
    /// </summary>
    public class Interactor : MonoBehaviour
    {
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] Transform viewPoint;
        [SerializeField] float reach = 3.5f;

        [Header("Player Systems")]
        [SerializeField] Backpack backpack;
        [SerializeField] Vitals vitals;
        [SerializeField] PlayerActivity activity;
        [SerializeField] CampPlacer placer;
        [SerializeField] FishingSession fishing;
        [SerializeField] ShopView shop;
        [SerializeField] SaveSystem saves;

        readonly List<InteractionOption> options = new();
        InputAction interactAction;
        IInteractable target;
        IInteractable menuTarget;
        VisualElement crosshair, menu, menuOptions;
        Label prompt, menuTitle;
        string menuSignature;

        public Backpack Backpack => backpack;
        public Vitals Vitals => vitals;
        public PlayerActivity Activity => activity;
        public CampPlacer Placer => placer;
        public FishingSession Fishing => fishing;
        public ShopView Shop => shop;
        public SaveSystem Saves => saves;
        public bool MenuOpen => menuTarget != null;

        FirstPersonController player;

        void Awake()
        {
            interactAction = inputActions.FindActionMap("Player", true).FindAction("Interact", true);
            player = GetComponent<FirstPersonController>();
        }

        void Update()
        {
            if (MenuOpen)
            {
                // Right-click, B and Esc close it through the UI's cancel handling.
                if (interactAction.WasPressedThisFrame())
                    CloseMenu();
                return;
            }

            // Sitting down, E takes your boots on and off instead.
            bool busy = PlayerControlLock.MovementLocked || PlayerControlLock.JustReleased || placer.IsPlacing || RestMode.SeatedNow;
            target = busy ? null : FindTarget();
            if (target == null || !interactAction.WasPressedThisFrame())
                return;

            options.Clear();
            target.GetOptions(this, options);
            if (options.Count == 1 && options[0].Enabled)
                options[0].Execute();
            else if (options.Count > 0)
                OpenMenu(target);
        }

        IInteractable FindTarget()
        {
            // Triggers are included so water surfaces can be targeted. The ray starts inside our own
            // capsule, so it never hits the player.
            Vector3 origin = player != null ? player.AimOrigin : viewPoint.position;
            if (!Physics.Raycast(origin, viewPoint.forward, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Collide))
                return null;
            return hit.collider.GetComponentInParent<IInteractable>();
        }

        void OpenMenu(IInteractable menuFor)
        {
            menuTarget = menuFor;
            menuSignature = null;
            PlayerControlLock.Lock(this, needsCursor: true);
            GameUI.ClaimEscape(this, CloseMenu);
        }

        void CloseMenu()
        {
            menuTarget = null;
            PlayerControlLock.Unlock(this);
            GameUI.ReleaseEscape(this);
        }

        void OnDisable()
        {
            if (MenuOpen)
                CloseMenu();
        }

        // ---------- UI ----------

        void Start()
        {
            crosshair = UIBuild.Box("crosshair");
            prompt = UIBuild.Text("", "prompt", "shadowed");
            prompt.enableRichText = true;
            GameUI.Current.Hud.With(crosshair.IgnoreMouse(), prompt.IgnoreMouse());

            menuTitle = UIBuild.Text("", "title");
            menuOptions = UIBuild.Box();
            menu = UIBuild.Layer("centred").With(
                UIBuild.Box("panel", "menu-panel").With(
                    menuTitle,
                    menuOptions,
                    UIBuild.Button("Close  (E / right-click / Esc)", CloseMenu, "quiet")));
            menu.SetVisible(false);
            GameUI.Current.Screens.Add(menu);
        }

        void LateUpdate()
        {
            if (crosshair == null)
                return;

            bool showHud = !MenuOpen && !PlayerControlLock.MovementLocked && !placer.IsPlacing;
            crosshair.SetVisible(showHud);
            prompt.SetVisible(showHud && target != null);
            if (showHud && target != null)
                prompt.SetText(PromptText());

            menu.SetVisible(MenuOpen);
            if (MenuOpen)
                RefreshMenu();
        }

        string PromptText()
        {
            options.Clear();
            target.GetOptions(this, options);
            string action = options.Count == 1 ? options[0].Label : target.DisplayName;
            return options.Count == 1 && !options[0].Enabled
                ? $"{action}  ({options[0].DisabledReason})"
                : $"<color=#E07B39><b>[E]</b></color>  {action}";
        }

        /// <summary>Options are read every frame so they reflect what's possible right now; the buttons are rebuilt when they change.</summary>
        void RefreshMenu()
        {
            options.Clear();
            menuTarget.GetOptions(this, options);
            var signature = new System.Text.StringBuilder(menuTarget.DisplayName);
            foreach (InteractionOption option in options)
                signature.Append('|').Append(option.Label).Append('/').Append(option.DisabledReason);
            if (signature.ToString() == menuSignature)
                return;

            menuSignature = signature.ToString();
            menuTitle.text = menuTarget.DisplayName;
            menuOptions.Clear();
            foreach (InteractionOption option in options)
            {
                InteractionOption chosen = option;
                Button button = UIBuild.Button(option.Label, () =>
                {
                    CloseMenu();
                    chosen.Execute();
                }, "menu");
                button.SetEnabled(option.Enabled);
                menuOptions.Add(button);
                if (menuOptions.childCount == 1)
                    menuOptions.FocusFirstButton();
                // An empty reason greys an option out with nothing to explain, e.g. a status line.
                if (!string.IsNullOrEmpty(option.DisabledReason))
                    menuOptions.Add(UIBuild.Text(option.DisabledReason, "reason").Classes("menu-reason"));
            }
        }
    }
}
