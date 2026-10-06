using System;
using System.Collections.Generic;
using Backpacking.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.UI
{
    /// <summary>
    /// The one UI Toolkit document every screen draws into, split into layers from back to front:
    /// condition effects, HUD, screens (backpack, shop, map), activity overlays, then menus on top.
    /// Also routes Esc: it closes the most recently opened screen, or opens the pause menu if none is open.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class GameUI : MonoBehaviour
    {
        [SerializeField] StyleSheet styleSheet;

        static GameUI current;
        static readonly List<(object owner, Action close)> escapeStack = new();

        /// <summary>Raised when Esc is pressed and no screen is open to close.</summary>
        public static event Action EscapeUnhandled;
        /// <summary>Raised when cancel (right-click / B) is pressed and no screen is open to close.</summary>
        public static event Action CancelUnhandled;

        VisualElement root;
        VisualElement effects, hud, screens, overlay, menus;

        /// <summary>The scene's UI. Available from Awake onward; build elements in Start or later.</summary>
        public static GameUI Current => current;

        public VisualElement Effects { get { Build(); return effects; } }
        public VisualElement Hud { get { Build(); return hud; } }
        public VisualElement Screens { get { Build(); return screens; } }
        public VisualElement Overlay { get { Build(); return overlay; } }
        public VisualElement Menus { get { Build(); return menus; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            escapeStack.Clear();
            EscapeUnhandled = null;
            CancelUnhandled = null;
        }

        void Awake() => current = this;

        void OnDestroy()
        {
            if (current == this)
                current = null;
        }

        void Start() => Build();

        void Build()
        {
            if (root != null)
                return;
            root = GetComponent<UIDocument>().rootVisualElement;
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0f;

            effects = AddLayer("effects");
            hud = AddLayer("hud");
            screens = AddLayer("screens");
            overlay = AddLayer("overlay");
            menus = AddLayer("menus");
        }

        VisualElement AddLayer(string layerName)
        {
            var layer = new VisualElement { name = layerName, pickingMode = PickingMode.Ignore };
            layer.AddToClassList("layer");
            root.Add(layer);
            return layer;
        }

        void Update()
        {
            // Esc / Start, or right-click / B, both close the newest screen. With nothing open,
            // Esc pauses while cancel only steps back through the menus.
            bool escape = GameInput.PausePressed;
            bool cancel = GameInput.CancelPressed;
            if (!escape && !cancel)
                return;

            if (escapeStack.Count > 0)
            {
                // Closing removes it from the stack.
                Action close = escapeStack[^1].close;
                escapeStack.RemoveAt(escapeStack.Count - 1);
                close();
            }
            else if (escape)
                EscapeUnhandled?.Invoke();
            else
                CancelUnhandled?.Invoke();
        }

        /// <summary>Lets Esc close a screen. Call when it opens; call <see cref="ReleaseEscape"/> when it closes.</summary>
        public static void ClaimEscape(object owner, Action close)
        {
            ReleaseEscape(owner);
            escapeStack.Add((owner, close));
        }

        public static void ReleaseEscape(object owner) => escapeStack.RemoveAll(entry => entry.owner == owner);

        /// <summary>Closes every open screen, newest first, e.g. when the player collapses.</summary>
        public static void CloseAllScreens()
        {
            while (escapeStack.Count > 0)
            {
                Action close = escapeStack[^1].close;
                escapeStack.RemoveAt(escapeStack.Count - 1);
                close();
            }
        }
    }

    /// <summary>Shorthand for building elements in code.</summary>
    public static class UIBuild
    {
        public static VisualElement Box(params string[] classes)
        {
            var element = new VisualElement();
            foreach (string className in classes)
                element.AddToClassList(className);
            return element;
        }

        public static Label Text(string text, params string[] classes)
        {
            var label = new Label(text);
            foreach (string className in classes)
                label.AddToClassList(className);
            return label;
        }

        public static Button Button(string text, Action onClick, params string[] classes)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("button");
            foreach (string className in classes)
                button.AddToClassList(className);
            return button;
        }

        /// <summary>A thin bar; set the fill's width as a percentage.</summary>
        public static VisualElement Bar(out VisualElement fill)
        {
            VisualElement bar = Box("bar");
            fill = Box("bar-fill");
            bar.Add(fill);
            return bar;
        }

        /// <summary>A full-screen layer that ignores the mouse, for HUD-style overlays.</summary>
        public static VisualElement Layer(params string[] classes)
        {
            VisualElement layer = Box(classes);
            layer.AddToClassList("layer");
            layer.pickingMode = PickingMode.Ignore;
            return layer;
        }

        public static T With<T>(this T parent, params VisualElement[] children) where T : VisualElement
        {
            foreach (VisualElement child in children)
                parent.Add(child);
            return parent;
        }

        public static T Classes<T>(this T element, params string[] classes) where T : VisualElement
        {
            foreach (string className in classes)
                element.AddToClassList(className);
            return element;
        }

        /// <summary>Makes an element and its children invisible to the mouse.</summary>
        public static T IgnoreMouse<T>(this T element) where T : VisualElement
        {
            element.pickingMode = PickingMode.Ignore;
            foreach (VisualElement child in element.Children())
                child.IgnoreMouse();
            return element;
        }

        /// <summary>Gives keyboard/gamepad focus to the first usable button, so a gamepad can navigate the screen.</summary>
        public static void FocusFirstButton(this VisualElement container)
        {
            // Wait a frame: elements shown this frame haven't been laid out yet and can't take focus.
            container.schedule.Execute(() =>
            {
                Button first = container.Query<Button>().Where(button => button.enabledInHierarchy && IsShown(button, container)).First();
                first?.Focus();
            });
        }

        static bool IsShown(VisualElement element, VisualElement container)
        {
            for (VisualElement e = element; e != null && e != container.parent; e = e.parent)
                if (e.style.display == DisplayStyle.None)
                    return false;
            return true;
        }

        public static void SetVisible(this VisualElement element, bool visible)
        {
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != display)
                element.style.display = display;
        }

        public static void SetText(this TextElement element, string text)
        {
            if (element.text != text)
                element.text = text;
        }

        public static void SetFill(this VisualElement fill, float fraction) =>
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
    }

    /// <summary>
    /// Keeps elements in step with the game. Register how each one reads its value, then call
    /// <see cref="Refresh"/> each frame while the screen is showing.
    /// </summary>
    public sealed class Bindings
    {
        readonly List<Action> updates = new();

        public void Clear() => updates.Clear();

        public void Refresh()
        {
            foreach (Action update in updates)
                update();
        }

        public void Add(Action update) => updates.Add(update);

        public Label Text(Func<string> read, params string[] classes)
        {
            Label label = UIBuild.Text(read(), classes);
            updates.Add(() => label.SetText(read()));
            return label;
        }

        public T Enabled<T>(T element, Func<bool> read) where T : VisualElement
        {
            updates.Add(() =>
            {
                bool enabled = read();
                if (element.enabledSelf != enabled)
                    element.SetEnabled(enabled);
            });
            return element;
        }

        public T Visible<T>(T element, Func<bool> read) where T : VisualElement
        {
            updates.Add(() => element.SetVisible(read()));
            return element;
        }

        /// <summary>
        /// A button with the reason it can't be used underneath. <paramref name="problem"/> returns null when it can.
        /// <paramref name="blocked"/> greys it out without a reason, e.g. while busy.
        /// </summary>
        public VisualElement ActionButton(Func<string> label, Action action, Func<string> problem, Func<bool> blocked = null,
            params string[] classes)
        {
            Button button = UIBuild.Button(label(), action, classes);
            Label reason = UIBuild.Text("", "reason");
            updates.Add(() =>
            {
                string why = problem?.Invoke();
                button.SetText(label());
                bool enabled = why == null && (blocked == null || !blocked());
                if (button.enabledSelf != enabled)
                    button.SetEnabled(enabled);
                reason.SetText(why ?? "");
                reason.SetVisible(!string.IsNullOrEmpty(why));
            });
            return UIBuild.Box().With(button, reason);
        }

        public VisualElement ActionButton(string label, Action action, Func<string> problem, Func<bool> blocked = null,
            params string[] classes) =>
            ActionButton(() => label, action, problem, blocked, classes);
    }
}
