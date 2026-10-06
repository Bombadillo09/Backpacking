using System.Collections.Generic;
using Backpacking.Camp;
using Backpacking.Gathering;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.UI;
using UnityEngine;
using UnityEngine.InputSystem;

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

        readonly List<InteractionOption> options = new();
        InputAction interactAction;
        IInteractable target;
        IInteractable menuTarget;
        GUIStyle promptStyle, titleStyle, reasonStyle;

        public Backpack Backpack => backpack;
        public Vitals Vitals => vitals;
        public PlayerActivity Activity => activity;
        public CampPlacer Placer => placer;
        public FishingSession Fishing => fishing;
        public ShopView Shop => shop;
        public bool MenuOpen => menuTarget != null;

        void Awake() => interactAction = inputActions.FindActionMap("Player", true).FindAction("Interact", true);

        void Update()
        {
            if (MenuOpen)
            {
                Mouse mouse = Mouse.current;
                bool cancel = interactAction.WasPressedThisFrame() || (mouse != null && mouse.rightButton.wasPressedThisFrame);
                if (cancel)
                    CloseMenu();
                return;
            }

            bool busy = PlayerControlLock.MovementLocked || PlayerControlLock.JustReleased || placer.IsPlacing;
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
            if (!Physics.Raycast(viewPoint.position, viewPoint.forward, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Collide))
                return null;
            return hit.collider.GetComponentInParent<IInteractable>();
        }

        void OpenMenu(IInteractable menuFor)
        {
            menuTarget = menuFor;
            PlayerControlLock.Lock(this, needsCursor: true);
        }

        void CloseMenu()
        {
            menuTarget = null;
            PlayerControlLock.Unlock(this);
        }

        void OnDisable()
        {
            if (MenuOpen)
                CloseMenu();
        }

        void OnGUI()
        {
            if (promptStyle == null)
            {
                promptStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
                titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold };
                reasonStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            }

            if (MenuOpen)
            {
                DrawMenu();
                return;
            }
            if (PlayerControlLock.MovementLocked || placer.IsPlacing)
                return;

            // Crosshair dot.
            var centre = new Vector2(Screen.width / 2f, Screen.height / 2f);
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.DrawTexture(new Rect(centre.x - 2f, centre.y - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (target == null)
                return;

            options.Clear();
            target.GetOptions(this, options);
            string action = options.Count == 1 ? options[0].Label : target.DisplayName;
            string prompt = options.Count == 1 && !options[0].Enabled
                ? $"{action}  ({options[0].DisabledReason})"
                : $"[E]  {action}";
            var rect = new Rect(0f, centre.y + 30f, Screen.width, 30f);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), prompt, promptStyle);
            GUI.color = Color.white;
            GUI.Label(rect, prompt, promptStyle);
        }

        void DrawMenu()
        {
            // Options are rebuilt every frame so they reflect what's possible right now.
            options.Clear();
            menuTarget.GetOptions(this, options);

            const float width = 380f, rowHeight = 44f;
            float height = 70f + options.Count * rowHeight + 40f;
            var area = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(area.x, area.y + 12f, width, 30f), menuTarget.DisplayName, titleStyle);

            float y = area.y + 55f;
            foreach (InteractionOption option in options)
            {
                GUI.enabled = option.Enabled;
                if (GUI.Button(new Rect(area.x + 20f, y, width - 40f, option.Enabled ? 34f : 24f), option.Label))
                {
                    CloseMenu();
                    option.Execute();
                    GUI.enabled = true;
                    return;
                }
                GUI.enabled = true;
                if (!option.Enabled)
                    GUI.Label(new Rect(area.x, y + 22f, width, 18f), option.DisabledReason, reasonStyle);
                y += rowHeight;
            }

            if (GUI.Button(new Rect(area.x + 20f, area.yMax - 40f, width - 40f, 28f), "Close  (E / right-click)"))
                CloseMenu();
        }
    }
}
