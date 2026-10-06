using System;
using Backpacking.Survival;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.UI
{
    /// <summary>Health, food, water, warmth and energy bars in the bottom-left corner, with how it feels out there.</summary>
    public class VitalsHud : MonoBehaviour
    {
        [SerializeField] Vitals vitals;
        [SerializeField] Backpack backpack;

        static readonly Color Low = new(0.9f, 0.25f, 0.2f);

        readonly Bindings bindings = new();
        bool built;

        void Start()
        {
            VisualElement panel = UIBuild.Box("vitals");
            panel.Add(Vital("Health", () => vitals.Health, new Color(0.85f, 0.2f, 0.3f), HealthTrend));
            panel.Add(Vital("Food", () => vitals.Satiety, new Color(0.9f, 0.65f, 0.25f)));
            panel.Add(Vital("Water", () => vitals.Hydration, new Color(0.3f, 0.6f, 0.95f)));
            panel.Add(Vital("Warmth", () => vitals.Warmth, new Color(0.95f, 0.45f, 0.3f)));
            panel.Add(Vital("Energy", () => vitals.Energy, new Color(0.5f, 0.85f, 0.45f)));
            panel.Add(Vital("Feet", () => vitals.Feet, new Color(0.85f, 0.6f, 0.5f), FeetState));
            panel.Add(bindings.Text(Status, "vitals-status", "shadowed"));
            panel.Add(bindings.Text(Load, "vitals-status", "shadowed"));
            GameUI.Current.Hud.Add(panel.IgnoreMouse());
            built = true;
        }

        void Update()
        {
            if (built)
                bindings.Refresh();
        }

        VisualElement Vital(string label, Func<float> read, Color colour, Func<string> suffix = null)
        {
            Label text = bindings.Text(() => $"{label}  {read():0}{suffix?.Invoke()}", "vital-label", "shadowed");
            VisualElement bar = UIBuild.Bar(out VisualElement fill);
            bindings.Add(() =>
            {
                float fraction = read() / Vitals.Max;
                fill.SetFill(fraction);
                fill.style.backgroundColor = fraction < 0.25f ? Low : colour;
            });
            return UIBuild.Box("vital").With(text, bar);
        }

        string FeetState() =>
            vitals.IsInfected ? (vitals.OnAntibiotics ? "   infected, treating" : "   infected")
            : vitals.Feet < 40f ? "   raw" : vitals.HasBlisters ? "   blisters" : vitals.FootStrain > 60f ? "   aching" : vitals.FootStrain > 35f ? "   tired" : "";

        string HealthTrend() =>
            vitals.HealthRate < -0.05f ? "   falling" : vitals.HealthRate > 0.05f && vitals.Health < Vitals.Max ? "   recovering" : "";

        string Status()
        {
            string status = $"Feels like {vitals.FeltTemperature:0} °C  ·  comfortable to {vitals.ComfortTemperature:0} °C";
            if (vitals.WindChill > 0.5f)
                status += $"  ·  wind chill −{vitals.WindChill:0} °C";
            if (vitals.Wetness > 5f)
                status += vitals.Wetness > 60f ? "  ·  soaked" : "  ·  damp";
            if (vitals.IsSick)
                status += "  ·  sick";
            if (vitals.IsHypothermic)
                status += "  ·  hypothermic";
            if (vitals.IsDehydrated)
                status += "  ·  dehydrated";
            if (vitals.IsStarving)
                status += "  ·  starving";
            return status;
        }

        string Load()
        {
            float weight = backpack.TotalWeight;
            string load = backpack.IsOverloaded ? "  OVERLOADED" : weight > backpack.ComfortableLoad ? "  heavy" : "";
            return $"Pack {weight:0.0} / {backpack.ComfortableLoad:0} kg{load}";
        }
    }
}
