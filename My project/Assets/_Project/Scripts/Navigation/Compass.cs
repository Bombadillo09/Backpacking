using Backpacking.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

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
        [Tooltip("How long the needle takes to settle after turning, in seconds.")]
        [SerializeField] float needleSmoothTime = 0.25f;

        static readonly string[] CardinalNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        float displayedHeading;
        float needleVelocity;
        Texture2D dial;
        VisualElement root, face;
        Label readout;

        public bool IsOpen { get; private set; }

        /// <summary>Degrees clockwise from north that the transform faces, 0–360.</summary>
        public static float HeadingOf(Transform t)
        {
            Vector3 forward = t.forward;
            return Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360f);
        }

        public static string CardinalName(float heading) =>
            CardinalNames[Mathf.RoundToInt(Mathf.Repeat(heading, 360f) / 45f) % 8];

        void Awake() => displayedHeading = HeadingOf(holder);

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame && !Player.PlayerControlLock.CursorNeeded)
                IsOpen = !IsOpen;

            displayedHeading = Mathf.SmoothDampAngle(displayedHeading, HeadingOf(holder), ref needleVelocity, needleSmoothTime);
        }

        void Start()
        {
            dial = CreateDialTexture(512);
            face = UIBuild.Box("compass-dial");
            face.style.backgroundImage = dial;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f;
                bool cardinal = i % 3 == 0;
                Label mark = UIBuild.Text(cardinal ? CardinalNames[i / 3 * 2] : angle.ToString("0"), "compass-mark");
                mark.style.fontSize = cardinal ? 24f : 14f;
                mark.style.unityFontStyleAndWeight = cardinal ? FontStyle.Bold : FontStyle.Normal;
                float radius = cardinal ? 0.25f : 0.27f;
                mark.style.left = Length.Percent(50f + Mathf.Sin(angle * Mathf.Deg2Rad) * radius * 100f);
                mark.style.top = Length.Percent(50f - Mathf.Cos(angle * Mathf.Deg2Rad) * radius * 100f);
                face.Add(mark);
            }

            readout = UIBuild.Text("", "compass-readout", "shadowed");
            root = UIBuild.Box("compass").With(face, UIBuild.Box("compass-index"), readout);
            root.SetVisible(false);
            GameUI.Current.Hud.Add(root.IgnoreMouse());
        }

        void LateUpdate()
        {
            if (root == null)
                return;
            root.SetVisible(IsOpen);
            if (!IsOpen)
                return;

            // Turn the whole dial so north on it lines up with north in the world.
            face.style.rotate = new Rotate(new Angle(-displayedHeading, AngleUnit.Degree));
            float heading = Mathf.Repeat(displayedHeading, 360f);
            readout.SetText($"{Mathf.RoundToInt(heading) % 360:000}°  {CardinalName(heading)}");
        }

        void OnDestroy()
        {
            if (dial != null)
                Destroy(dial);
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
