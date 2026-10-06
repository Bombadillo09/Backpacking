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
        [SerializeField] WeatherSystem weather;
        [SerializeField] float fastForwardMultiplier = 60f;

        GUIStyle style, bannerStyle;
        string message;
        float messageHideTime;

        void OnEnable()
        {
            NavigationPoint.Arrived += ShowArrival;
            Notifications.Posted += ShowMessage;
        }

        void OnDisable()
        {
            NavigationPoint.Arrived -= ShowArrival;
            Notifications.Posted -= ShowMessage;
        }

        void ShowArrival(NavigationPoint point) => ShowMessage($"Arrived at {point.DisplayName}", 5f);

        void ShowMessage(string text, float seconds)
        {
            message = text;
            messageHideTime = Time.time + seconds;
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tKey.isPressed)
                timeOfDay.RequestSpeed(this, fastForwardMultiplier);
            else
                timeOfDay.ClearSpeed(this);
        }

        void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 16 };
            bannerStyle ??= new GUIStyle(style) { alignment = TextAnchor.UpperCenter, fontSize = 24, wordWrap = true };

            Vector3 position = player.transform.position;
            string state = player.IsCrouching ? " (crouching)" : player.IsSprinting ? " (sprinting)" : "";
            string fastForward = timeOfDay.TimeMultiplier > 1f ? $"   >> x{timeOfDay.TimeMultiplier:0}" : "";
            string text =
                $"Day {timeOfDay.Day}   {timeOfDay.ClockText}{fastForward}\n" +
                $"{temperature.GetTemperature(position):0.0} °C   Altitude {temperature.GetAltitude(position):0} m\n" +
                $"{WeatherLine()}\n" +
                $"Speed {player.HorizontalSpeed:0.0} m/s{state}\n\n" +
                "WASD move · Shift sprint · C crouch · Space jump\n" +
                "M map · Q compass · Tab backpack · E interact\n" +
                "Hold T fast-forward time · F5 save · F9 load · Esc release cursor";

            DrawShadowedLabel(new Rect(14f, 12f, 700f, 200f), text, style);

            if (message != null && Time.time < messageHideTime)
                DrawShadowedLabel(new Rect(Screen.width * 0.15f, Screen.height * 0.18f, Screen.width * 0.7f, 120f), message, bannerStyle);
        }

        string WeatherLine()
        {
            if (weather == null)
                return "";
            string kind = WeatherSystem.Describe(weather.Current);
            string snap = weather.IsColdSnap ? " · cold snap" : "";
            return $"{char.ToUpperInvariant(kind[0])}{kind.Substring(1)} · wind {weather.WindKmh:0} km/h{snap}";
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
