using UnityEngine;
using UnityEngine.InputSystem;

namespace Backpacking.Player
{
    /// <summary>
    /// The game's own buttons beyond moving and looking, each bound to a key and a gamepad button in the
    /// Player action map: pause, cancel/back, backpack, map, compass, journal and fast-forward.
    /// The first-person controller sets it up from its input actions.
    /// </summary>
    public static class GameInput
    {
        static InputAction pause, cancel, backpack, map, compass, fastForward, journal, toggleView;

        public static void Initialize(InputActionAsset actions)
        {
            InputActionMap player = actions.FindActionMap("Player", throwIfNotFound: true);
            pause = player.FindAction("Pause");
            cancel = player.FindAction("Cancel");
            backpack = player.FindAction("Backpack");
            map = player.FindAction("Map");
            compass = player.FindAction("Compass");
            fastForward = player.FindAction("FastForward");
            journal = player.FindAction("Journal");
            toggleView = player.FindAction("ToggleView");
            if (pause == null || cancel == null || backpack == null)
                Debug.LogWarning("The Player action map is missing Pause, Cancel or Backpack; those buttons won't work.");
        }

        /// <summary>Esc or Start.</summary>
        public static bool PausePressed => Pressed(pause);
        /// <summary>Right-click or B: back out of a screen, stop fishing, cancel placement.</summary>
        public static bool CancelPressed => Pressed(cancel);
        /// <summary>Tab or View/Select.</summary>
        public static bool BackpackPressed => Pressed(backpack);
        /// <summary>M or D-pad up.</summary>
        public static bool MapPressed => Pressed(map);
        /// <summary>Q or D-pad down.</summary>
        public static bool CompassPressed => Pressed(compass);
        /// <summary>J (on a gamepad, open it from the backpack).</summary>
        public static bool JournalPressed => Pressed(journal);
        /// <summary>V or right bumper: first or third person.</summary>
        public static bool ToggleViewPressed => Pressed(toggleView);
        /// <summary>T or left bumper, held.</summary>
        public static bool FastForwardHeld => fastForward != null && fastForward.IsPressed();

        static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => pause = cancel = backpack = map = compass = fastForward = journal = toggleView = null;
    }
}
