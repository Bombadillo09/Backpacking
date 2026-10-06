using Backpacking.Navigation;
using Backpacking.Player;
using Backpacking.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backpacking.UI
{
    /// <summary>
    /// Temporary on-screen readout for testing: clock, temperature, altitude and speed,
    /// plus a banner when arriving at a destination. Hold T to fast-forward time.
    /// </summary>
    public class PrototypeHud : MonoBehaviour
    {
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] AmbientTemperature temperature;
        [SerializeField] FirstPersonController player;
        [SerializeField] float fastForwardMultiplier = 60f;

        GUIStyle style, bannerStyle;
        string message;
        float messageHideTime;

        void OnEnable() => NavigationPoint.Arrived += ShowArrival;
        void OnDisable() => NavigationPoint.Arrived -= ShowArrival;

        void ShowArrival(NavigationPoint point)
        {
            message = $"Arrived at {point.DisplayName}";
            messageHideTime = Time.time + 5f;
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            timeOfDay.TimeMultiplier = keyboard != null && keyboard.tKey.isPressed ? fastForwardMultiplier : 1f;
        }

        void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 16 };
            bannerStyle ??= new GUIStyle(style) { alignment = TextAnchor.MiddleCenter, fontSize = 26 };

            Vector3 position = player.transform.position;
            string state = player.IsCrouching ? " (crouching)" : player.IsSprinting ? " (sprinting)" : "";
            string fastForward = timeOfDay.TimeMultiplier > 1f ? $"   >> x{timeOfDay.TimeMultiplier:0}" : "";
            string text =
                $"Day {timeOfDay.Day}   {timeOfDay.ClockText}{fastForward}\n" +
                $"{temperature.GetTemperature(position):0.0} °C   Altitude {temperature.GetAltitude(position):0} m\n" +
                $"Speed {player.HorizontalSpeed:0.0} m/s{state}\n\n" +
                "WASD move · Shift sprint · C crouch · Space jump\n" +
                "M map · Q compass\n" +
                "Hold T fast-forward time · Esc release cursor";

            DrawShadowedLabel(new Rect(14f, 12f, 700f, 200f), text, style);

            if (message != null && Time.time < messageHideTime)
                DrawShadowedLabel(new Rect(0f, Screen.height * 0.18f, Screen.width, 40f), message, bannerStyle);
        }

        static void DrawShadowedLabel(Rect rect, string text, GUIStyle labelStyle)
        {
            labelStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, labelStyle);
            labelStyle.normal.textColor = Color.white;
            GUI.Label(rect, text, labelStyle);
        }
    }
}
