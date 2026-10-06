using UnityEngine;
using UnityEngine.InputSystem;

namespace Backpacking.Navigation
{
    /// <summary>
    /// A handheld compass shown in the corner of the screen. The dial turns with the player so its
    /// needle always points to world north (+Z); the red index line at the top reads the current heading.
    /// </summary>
    public class Compass : MonoBehaviour
    {
        [SerializeField] Transform holder;
        [SerializeField] Key toggleKey = Key.Q;
        [Tooltip("On-screen diameter in pixels at 1080p; scales with screen height.")]
        [SerializeField] float sizeAt1080p = 280f;
        [Tooltip("How long the needle takes to settle after turning, in seconds.")]
        [SerializeField] float needleSmoothTime = 0.25f;

        static readonly string[] CardinalNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        float displayedHeading;
        float needleVelocity;
        Texture2D dial;
        GUIStyle letterStyle, numberStyle, readoutStyle;

        public bool IsOpen { get; private set; }

        /// <summary>Degrees clockwise from north that the transform faces, 0–360.</summary>
        public static float HeadingOf(Transform t)
        {
            Vector3 forward = t.forward;
            return Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360f);
        }

        public static string CardinalName(float heading) =>
            CardinalNames[Mathf.RoundToInt(Mathf.Repeat(heading, 360f) / 45f) % 8];

        void Start() => displayedHeading = HeadingOf(holder);

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                IsOpen = !IsOpen;

            displayedHeading = Mathf.SmoothDampAngle(displayedHeading, HeadingOf(holder), ref needleVelocity, needleSmoothTime);
        }

        void OnGUI()
        {
            if (!IsOpen)
                return;

            if (dial == null)
                dial = CreateDialTexture(512);
            if (letterStyle == null)
            {
                letterStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                numberStyle = new GUIStyle(letterStyle) { fontStyle = FontStyle.Normal };
                readoutStyle = new GUIStyle(letterStyle);
            }

            float size = sizeAt1080p * Screen.height / 1080f;
            var rect = new Rect(Screen.width - size - 24f, Screen.height - size - 24f, size, size);
            Vector2 centre = rect.center;

            letterStyle.fontSize = Mathf.RoundToInt(size * 0.085f);
            numberStyle.fontSize = Mathf.RoundToInt(size * 0.05f);
            readoutStyle.fontSize = Mathf.RoundToInt(size * 0.075f);
            letterStyle.normal.textColor = numberStyle.normal.textColor = new Color(0.12f, 0.12f, 0.14f);

            // Turn the whole dial so north on it lines up with north in the world.
            Matrix4x4 savedMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(-displayedHeading, centre);
            GUI.DrawTexture(rect, dial);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f;
                bool cardinal = i % 3 == 0;
                string label = cardinal ? CardinalNames[i / 3 * 2] : angle.ToString("0");
                float radius = size * (cardinal ? 0.25f : 0.27f);
                Vector2 position = centre + new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), -Mathf.Cos(angle * Mathf.Deg2Rad)) * radius;
                GUI.Label(new Rect(position.x - 30f, position.y - 15f, 60f, 30f), label, cardinal ? letterStyle : numberStyle);
            }
            GUI.matrix = savedMatrix;

            // Fixed index line and heading readout.
            GUI.color = new Color(0.85f, 0.1f, 0.1f);
            GUI.DrawTexture(new Rect(centre.x - 1.5f, rect.y - 8f, 3f, size * 0.17f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float heading = Mathf.Repeat(displayedHeading, 360f);
            string readout = $"{Mathf.RoundToInt(heading) % 360:000}°  {CardinalName(heading)}";
            var readoutRect = new Rect(rect.x, rect.y - size * 0.2f, size, size * 0.12f);
            readoutStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(readoutRect.x + 1f, readoutRect.y + 1f, readoutRect.width, readoutRect.height), readout, readoutStyle);
            readoutStyle.normal.textColor = Color.white;
            GUI.Label(readoutRect, readout, readoutStyle);
        }

        /// <summary>Draws the compass face: bezel, degree ticks and a red/white needle pointing to north.</summary>
        static Texture2D CreateDialTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var face = new Color(0.93f, 0.91f, 0.85f);
            var bezel = new Color(0.16f, 0.16f, 0.18f);
            var ink = new Color(0.12f, 0.12f, 0.14f);
            var red = new Color(0.8f, 0.12f, 0.1f);
            var needleSouth = new Color(0.85f, 0.85f, 0.85f);

            float half = size / 2f;
            float pixel = 1f / half;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // -1..1 with +y toward north (the top of the texture).
                var p = new Vector2((x + 0.5f - half) / half, (y + 0.5f - half) / half);
                float r = p.magnitude;
                float angle = Mathf.Repeat(Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg, 360f);

                Color colour = r > 0.9f ? bezel : face;
                float alpha = 1f - Mathf.InverseLerp(0.98f - pixel, 0.98f, r);

                // Degree ticks every 5°, longer every 10°, longest at the cardinal points.
                int degree = Mathf.RoundToInt(angle / 5f) * 5 % 360;
                float arc = Mathf.Abs(Mathf.DeltaAngle(angle, degree)) * Mathf.Deg2Rad * r;
                float inner = degree % 90 == 0 ? 0.62f : degree % 10 == 0 ? 0.72f : 0.78f;
                float halfWidth = degree % 30 == 0 ? 0.012f : 0.006f;
                float tick = (1f - Mathf.InverseLerp(halfWidth - pixel, halfWidth, arc))
                             * Mathf.InverseLerp(inner - pixel, inner, r)
                             * (1f - Mathf.InverseLerp(0.88f, 0.88f + pixel, r));
                colour = Color.Lerp(colour, degree == 0 ? red : ink, tick);

                // Diamond needle: red half to the north, pale half to the south.
                const float needleLength = 0.42f, needleWidth = 0.065f;
                float edge = needleWidth * (1f - Mathf.Abs(p.y) / needleLength) - Mathf.Abs(p.x);
                float needle = Mathf.Clamp01(edge / pixel + 0.5f);
                colour = Color.Lerp(colour, p.y > 0f ? red : needleSouth, needle);

                // Pivot.
                colour = Color.Lerp(colour, ink, 1f - Mathf.InverseLerp(0.03f - pixel, 0.03f, r));

                colour.a = alpha;
                pixels[y * size + x] = colour;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
