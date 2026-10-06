using Backpacking.Survival;
using UnityEngine;

namespace Backpacking.UI
{
    /// <summary>Health, food, water, warmth and energy bars in the bottom-left corner.</summary>
    public class VitalsHud : MonoBehaviour
    {
        [SerializeField] Vitals vitals;
        [SerializeField] Backpack backpack;

        static readonly Color Low = new(0.9f, 0.25f, 0.2f);
        GUIStyle labelStyle;

        void OnGUI()
        {
            labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14 };

            const float width = 220f, barHeight = 10f, rowHeight = 30f;
            float x = 16f;
            float y = Screen.height - 16f - rowHeight * 5f - 44f;

            string trend = vitals.HealthRate < -0.05f ? "  ▼" : vitals.HealthRate > 0.05f && vitals.Health < Vitals.Max ? "  ▲" : "";
            DrawBar(x, ref y, width, barHeight, rowHeight, "Health", vitals.Health, new Color(0.85f, 0.2f, 0.3f), trend);
            DrawBar(x, ref y, width, barHeight, rowHeight, "Food", vitals.Satiety, new Color(0.9f, 0.65f, 0.25f));
            DrawBar(x, ref y, width, barHeight, rowHeight, "Water", vitals.Hydration, new Color(0.3f, 0.6f, 0.95f));
            DrawBar(x, ref y, width, barHeight, rowHeight, "Warmth", vitals.Warmth, new Color(0.95f, 0.45f, 0.3f));
            DrawBar(x, ref y, width, barHeight, rowHeight, "Energy", vitals.Energy, new Color(0.5f, 0.85f, 0.45f));

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
            DrawShadowed(new Rect(x, y, 500f, 22f), status);

            float weight = backpack.TotalWeight;
            string load = backpack.IsOverloaded ? "  OVERLOADED" : weight > backpack.ComfortableLoad ? "  heavy" : "";
            DrawShadowed(new Rect(x, y + 22f, 500f, 22f), $"Pack {weight:0.0} / {backpack.ComfortableLoad:0} kg{load}");
        }

        void DrawBar(float x, ref float y, float width, float barHeight, float rowHeight, string label, float value, Color colour,
            string suffix = "")
        {
            float fraction = value / Vitals.Max;
            DrawShadowed(new Rect(x, y, width, 18f), $"{label}  {value:0}{suffix}");
            var bar = new Rect(x, y + 18f, width, barHeight);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = fraction < 0.25f ? Low : colour;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * fraction, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            y += rowHeight;
        }

        void DrawShadowed(Rect rect, string text)
        {
            labelStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, labelStyle);
            labelStyle.normal.textColor = Color.white;
            GUI.Label(rect, text, labelStyle);
        }
    }
}
