using System;
using System.Linq;
using Backpacking.Camp;
using Backpacking.Navigation;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Trip;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Compass = Backpacking.Navigation.Compass;

namespace Backpacking.UI
{
    /// <summary>
    /// A guided first day at the trailhead: walking, map and compass, the backpack, your body's status, resting
    /// and caring for your feet, treating infections, firewood, the machete, and making camp. Each step finishes
    /// itself when you do it; steps that only explain continue with Enter (or D-pad right). Skip it from the
    /// pause menu.
    /// </summary>
    public class Tutorial : MonoBehaviour
    {
        [SerializeField] FirstPersonController player;
        [SerializeField] Backpack backpack;
        [SerializeField] MapView map;
        [SerializeField] Compass compass;
        [SerializeField] CampPlacer placer;
        [SerializeField] GroundClearing clearing;
        [Tooltip("Seconds an explanation stays up before moving on by itself.")]
        [SerializeField] float readSeconds = 20f;

        class Step
        {
            public string Title;
            public string Text;
            /// <summary>Done when true; null for an explanation that continues with Enter.</summary>
            public Func<bool> Done;
            public Action Begin;
        }

        Step[] steps;
        int index = -1;
        float stepStarted;
        float doneAt = -1f;
        Vector3 startPosition;
        float startWater;
        int startClearings;
        bool swung;
        string hikerName = "";

        VisualElement panel;
        Label counter, title, body, footer;

        public bool IsRunning => index >= 0 && steps != null && index < steps.Length;
        /// <summary>The step in progress, or -1. Saved with the trip.</summary>
        public int CurrentStep => IsRunning ? index : -1;

        void OnEnable() => Undergrowth.Swung += OnSwung;
        void OnDisable() => Undergrowth.Swung -= OnSwung;
        void OnSwung() => swung = true;

        void Start()
        {
            counter = UIBuild.Text("", "reason");
            title = UIBuild.Text("", "heading");
            body = UIBuild.Text("", "text");
            body.enableRichText = true;
            footer = UIBuild.Text("", "reason");
            panel = UIBuild.Box("panel", "tutorial").With(counter, title, body, footer);
            panel.SetVisible(false);
            GameUI.Current.Hud.Add(panel.IgnoreMouse());
            BuildSteps();
        }

        /// <summary>Starts from the first step.</summary>
        public void Begin(string name)
        {
            hikerName = name;
            BuildSteps();
            ShowStep(0);
        }

        /// <summary>Picks up a saved tutorial where it left off.</summary>
        public void Resume(int step)
        {
            hikerName = TripLog.HikerName;
            BuildSteps();
            if (step >= 0)
                ShowStep(step);
            else
                Stop();
        }

        public void Stop()
        {
            index = -1;
            if (panel != null)
                panel.SetVisible(false);
        }

        void BuildSteps()
        {
            const string Key = "<color=#E07B39><b>";
            const string End = "</b></color>";
            steps = new[]
            {
                new Step { Title = "Welcome to the trail",
                    Text = $"Walk with {Key}WASD{End} and look around with the mouse. Try walking a little way.",
                    Begin = () => startPosition = player.transform.position,
                    Done = () => Vector3.Distance(player.transform.position, startPosition) > 6f },
                new Step { Title = "Your map",
                    Text = $"Press {Key}M{End} for your paper map. The {Key}red dashes{End} are the trail; it runs north past every cairn and trading post to {TripLog.Destination}. There's no you-are-here dot: read the land.",
                    Done = () => map != null && map.IsOpen },
                new Step { Title = "Your compass",
                    Text = $"Close the map ({Key}M{End}) and press {Key}Q{End} for your compass. The number at the top is your heading; the trail heads roughly north (0°).",
                    Done = () => compass != null && compass.IsOpen },
                new Step { Title = "Your backpack",
                    Text = $"Press {Key}Tab{End} to open your backpack: water, food, gear, clothing and first aid. Drink some safe water now.",
                    Begin = () => startWater = backpack.TotalWater,
                    Done = () => backpack.TotalWater < startWater - 0.01f },
                new Step { Title = "How you are",
                    Text = $"Press {Key}J{End} for your journal, then open the {Key}Status{End} tab. It shows any injury on an outline of your body, how bad it is, and how to treat it.",
                    Done = () => JournalView.StatusViewed },
                new Step { Title = "Rest your feet",
                    Text = $"Walking tires your feet; keep going on aching feet and you'll get blisters. Sit down with {Key}Z{End}, then press {Key}E{End} to take your boots off. Bare feet rest, dry and air out much faster.",
                    Done = () => RestMode.Current != null && RestMode.Current.BootsOff },
                new Step { Title = "Infection",
                    Text = $"Feet kept wet or blistered too long get infected. Two cures: take {Key}antibiotics{End} (Backpack > First aid; trading posts sell them), or sit by a lit campfire with your boots off to hold your feet in its smoke.\n\nStand up with {Key}Z{End} or by walking. Press {Key}Enter{End} to go on." },
                new Step { Title = "Firewood",
                    Text = $"Pick up {Key}3 pieces of firewood{End} with {Key}E{End}. There's fallen wood lying around the trailhead.",
                    Done = () => backpack.Firewood >= 3 },
                new Step { Title = "Through the brush",
                    Text = $"Off the trail, thick brush slows you to a crawl. Walk into the woods and {Key}click{End} to swing your machete and hack a way through. The trail is always the easy way.",
                    Begin = () => swung = false,
                    Done = () => swung },
                new Step { Title = "Clear a campsite",
                    Text = $"In the woods you need bare ground for a tent or a fire. Open your backpack and choose {Key}Clear campsite{End}, then pick a spot. Felled saplings become firewood. (In an open meadow you can skip this: press {Key}Enter{End}.)",
                    Begin = () => startClearings = clearing != null ? clearing.Cleared.Count : 0,
                    Done = () => clearing != null && clearing.Cleared.Count > startClearings },
                new Step { Title = "Pitch your tent",
                    Text = $"Backpack > {Key}Pitch tent{End}, then place it on flat, clear ground. Sleeping in it keeps you warm and saves the game.",
                    Done = () => placer.PlacedItems.Any(item => item.kind == CampItem.Tent) },
                new Step { Title = "Light a fire",
                    Text = $"Backpack > {Key}Build fire ring{End}, place it, then look at it and press {Key}E{End} to light it with a match. A fire warms you, dries you, cooks and boils water.",
                    Done = () => FindObjectsByType<Campfire>().Any(fire => fire.IsBurning) },
                new Step { Title = "By the fire",
                    Text = $"Look at the fire and choose {Key}Sit by the fire{End} (or sit close with {Key}Z{End}), then take your boots off with {Key}E{End}. Your bare feet warm, dry and sit in the smoke, the old cure for infected feet.",
                    Done = () => RestMode.Current != null && RestMode.Current.SmokingFeet },
                new Step { Title = "You're ready",
                    Text = $"That's the basics. Follow the trail north, rest your feet, keep dry and fed, and check your Status page (J) when something hurts. Good luck, {hikerName}!\n\nPress {Key}Enter{End} to finish." },
            };
        }

        void ShowStep(int step)
        {
            if (steps == null)
                BuildSteps();
            if (step >= steps.Length)
            {
                Stop();
                Notifications.Post("Tutorial complete. Head north along the trail.");
                return;
            }
            index = step;
            stepStarted = Time.time;
            doneAt = -1f;
            steps[index].Begin?.Invoke();
            panel.SetVisible(true);
        }

        void Update()
        {
            if (!IsRunning || panel == null)
                return;
            Step step = steps[index];

            counter.SetText($"TUTORIAL  ·  {index + 1} / {steps.Length}");
            title.SetText(step.Title);
            body.SetText(step.Text);
            // Out of the way while a menu or screen is open.
            panel.SetVisible(!PlayerControlLock.CursorNeeded);

            bool next = Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
                        || Gamepad.current != null && Gamepad.current.dpad.right.wasPressedThisFrame;

            if (step.Done == null)
            {
                footer.SetText("Enter (or D-pad right): continue");
                if (next || Time.time - stepStarted > readSeconds)
                    ShowStep(index + 1);
                return;
            }

            // A step that can be skipped (clearing in the open) also takes Enter.
            bool skippable = step.Title == "Clear a campsite";
            footer.SetText(doneAt >= 0f ? "Well done!" : skippable ? "Do it, or Enter to skip" : "");
            if (doneAt < 0f && step.Done())
                doneAt = Time.time;
            if (doneAt >= 0f && Time.time - doneAt > 1.5f || skippable && next)
                ShowStep(index + 1);
        }
    }
}
