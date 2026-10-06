using Backpacking.Navigation;
using Backpacking.Player;
using Backpacking.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.UI
{
    /// <summary>
    /// The readout in the top-left corner: clock, temperature, altitude, weather and speed, plus the key
    /// list (can be hidden in settings) and a banner for notifications. Hold T to fast-forward time.
    /// </summary>
    public class PrototypeHud : MonoBehaviour
    {
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] AmbientTemperature temperature;
        [SerializeField] FirstPersonController player;
        [SerializeField] WeatherSystem weather;
        [SerializeField] float fastForwardMultiplier = 60f;

        const string HelpText =
            "WASD move · Shift sprint · C crouch · Space jump\n" +
            "M map · Q compass · Tab backpack · E interact\n" +
            "Hold T fast-forward time · F5 save · F9 load · Esc menu\n" +
            "Gamepad: Y interact · View backpack · D-pad map/compass · LB fast-forward · Start menu";

        readonly Bindings bindings = new();
        VisualElement info;
        Label help;
        Label banner;
        float bannerHideTime;

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

        void Start()
        {
            VisualElement hud = GameUI.Current.Hud;
            info = UIBuild.Box("hud-info").With(
                bindings.Text(ClockLine, "hud-info-line", "shadowed"),
                bindings.Text(TemperatureLine, "hud-info-line", "shadowed"),
                bindings.Text(WeatherLine, "hud-info-line", "shadowed"),
                bindings.Text(SpeedLine, "hud-info-line", "shadowed"),
                help = UIBuild.Text(HelpText, "hud-help", "shadowed"));
            banner = UIBuild.Text("", "banner", "shadowed");
            banner.style.opacity = 0f;
            hud.With(info.IgnoreMouse(), banner.IgnoreMouse());
        }

        void ShowArrival(NavigationPoint point) => ShowMessage($"Arrived at {point.DisplayName}", 5f);

        void ShowMessage(string text, float seconds)
        {
            if (banner == null)
                return;
            banner.text = text;
            banner.style.opacity = 1f;
            bannerHideTime = Time.unscaledTime + seconds;
        }

        void Update()
        {
            bool fastForward = GameInput.FastForwardHeld && !PlayerControlLock.CursorNeeded;
            if (fastForward)
                timeOfDay.RequestSpeed(this, fastForwardMultiplier);
            else
                timeOfDay.ClearSpeed(this);

            if (info == null)
                return;
            bindings.Refresh();
            help.SetVisible(GameSettings.ShowControlHints);
            if (banner.style.opacity.value > 0f && Time.unscaledTime >= bannerHideTime)
                banner.style.opacity = 0f;
        }

        string ClockLine()
        {
            string fastForward = timeOfDay.TimeMultiplier > 1f ? $"   >> x{timeOfDay.TimeMultiplier:0}" : "";
            return $"Day {timeOfDay.Day}   {timeOfDay.ClockText}{fastForward}";
        }

        string TemperatureLine()
        {
            Vector3 position = player.transform.position;
            return $"{temperature.GetTemperature(position):0.0} °C   Altitude {temperature.GetAltitude(position):0} m";
        }

        string SpeedLine()
        {
            string state = player.IsCrouching ? " (crouching)" : player.IsSprinting ? " (sprinting)" : "";
            return $"Speed {player.HorizontalSpeed:0.0} m/s{state}";
        }

        string WeatherLine()
        {
            if (weather == null)
                return "";
            string kind = WeatherSystem.Describe(weather.Current);
            string snap = weather.IsColdSnap ? " · cold snap" : "";
            return $"{char.ToUpperInvariant(kind[0])}{kind.Substring(1)} · wind {weather.WindKmh:0} km/h{snap}";
        }
    }
}
