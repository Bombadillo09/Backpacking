using UnityEngine;
using UnityEngine.InputSystem;

namespace Backpacking.Navigation
{
    /// <summary>
    /// The paper map, opened with M. Shows a topographic rendering of the terrain, a lettered grid,
    /// a scale bar and the named destinations. Your own position is hidden unless enabled.
    /// </summary>
    public class MapView : MonoBehaviour
    {
        [SerializeField] Terrain terrain;
        [SerializeField] Transform player;
        [SerializeField] Key toggleKey = Key.M;

        [Header("Map Rendering")]
        [SerializeField] int resolution = 1024;
        [SerializeField] float contourInterval = 20f;
        [Tooltip("Every Nth contour is drawn darker.")]
        [SerializeField] int indexContourEvery = 5;
        [SerializeField] float gridSpacing = 250f;

        [Tooltip("Show a 'you are here' marker. Off by default: read your position from the land.")]
        [SerializeField] bool showPlayerPosition;

        static readonly Color Paper = new(0.93f, 0.9f, 0.82f);
        static readonly Color Ink = new(0.15f, 0.13f, 0.12f);
        static readonly Color UnvisitedColour = new(0.8f, 0.15f, 0.1f);
        static readonly Color VisitedColour = new(0.15f, 0.45f, 0.2f);

        Texture2D mapTexture;
        Texture2D dotTexture;
        GUIStyle labelStyle, titleStyle;

        public bool IsOpen { get; private set; }

        void Start()
        {
            mapTexture = TopographicMap.Generate(terrain, resolution, contourInterval, indexContourEvery, gridSpacing);
            dotTexture = CreateDotTexture(32);
        }

        void OnDestroy()
        {
            Destroy(mapTexture);
            Destroy(dotTexture);
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                IsOpen = !IsOpen;
        }

        void OnGUI()
        {
            if (!IsOpen || mapTexture == null)
                return;

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }

            float side = Mathf.Min(Screen.width, Screen.height) * 0.88f;
            var paper = new Rect((Screen.width - side) / 2f, (Screen.height - side) / 2f, side, side);
            float margin = side * 0.06f;
            var map = new Rect(paper.x + margin, paper.y + margin, side - margin * 2f, side - margin * 2f);

            labelStyle.fontSize = Mathf.RoundToInt(side * 0.018f);
            titleStyle.fontSize = Mathf.RoundToInt(side * 0.026f);
            labelStyle.normal.textColor = titleStyle.normal.textColor = Ink;

            FillRect(paper, Paper);
            GUI.DrawTexture(map, mapTexture);
            DrawFrame(map, Ink, 2f);

            GUI.Label(new Rect(paper.x, paper.y, paper.width, margin), "Prototype Valley", titleStyle);
            DrawGridLabels(map, margin);
            DrawScaleBar(map, margin);
            DrawNorthArrow(map, margin);
            GUI.Label(new Rect(paper.x + margin, map.yMax + margin * 0.45f, side, margin * 0.5f),
                $"Contours every {contourInterval:0} m", labelStyle);

            foreach (NavigationPoint point in NavigationPoint.All)
            {
                Vector2 position = WorldToScreen(point.transform.position, map);
                DrawDot(position, side * 0.016f, point.Visited ? VisitedColour : UnvisitedColour);
                GUI.Label(new Rect(position.x + side * 0.014f, position.y - side * 0.016f, side * 0.4f, side * 0.04f),
                    point.DisplayName, labelStyle);
            }

            if (showPlayerPosition && player != null)
                DrawDot(WorldToScreen(player.position, map), side * 0.014f, new Color(0.1f, 0.3f, 0.9f));
        }

        Vector2 WorldToScreen(Vector3 world, Rect map)
        {
            Vector3 size = terrain.terrainData.size;
            Vector3 local = world - terrain.transform.position;
            float u = local.x / size.x, v = local.z / size.z;
            return new Vector2(map.x + u * map.width, map.yMax - v * map.height);
        }

        /// <summary>Letters along the top for columns (west to east), numbers down the side for rows (north to south).</summary>
        void DrawGridLabels(Rect map, float margin)
        {
            int cells = Mathf.CeilToInt(terrain.terrainData.size.x / gridSpacing);
            float cell = map.width * gridSpacing / terrain.terrainData.size.x;
            var centred = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter };
            for (int i = 0; i < cells; i++)
            {
                GUI.Label(new Rect(map.x + i * cell, map.y - margin * 0.5f, cell, margin * 0.5f), ((char)('A' + i)).ToString(), centred);
                GUI.Label(new Rect(map.x - margin * 0.7f, map.y + i * cell, margin * 0.6f, cell), (i + 1).ToString(), centred);
            }
        }

        void DrawScaleBar(Rect map, float margin)
        {
            const float lengthMetres = 250f;
            float length = map.width * lengthMetres / terrain.terrainData.size.x;
            var bar = new Rect(map.xMax - length, map.yMax + margin * 0.3f, length, margin * 0.12f);
            FillRect(new Rect(bar.x, bar.y, bar.width / 2f, bar.height), Ink);
            DrawFrame(bar, Ink, 1f);
            var right = new GUIStyle(labelStyle) { alignment = TextAnchor.UpperRight };
            GUI.Label(new Rect(bar.x - length, bar.yMax, length * 2f, margin * 0.5f), $"{lengthMetres:0} m", right);
        }

        void DrawNorthArrow(Rect map, float margin)
        {
            float x = map.xMax + margin * 0.5f;
            FillRect(new Rect(x - 1.5f, map.y + margin * 0.5f, 3f, margin * 0.9f), Ink);
            var centred = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(x - margin * 0.5f, map.y, margin, margin * 0.5f), "N", centred);
        }

        void DrawDot(Vector2 centre, float diameter, Color colour)
        {
            GUI.color = colour;
            GUI.DrawTexture(new Rect(centre.x - diameter / 2f, centre.y - diameter / 2f, diameter, diameter), dotTexture);
            GUI.color = Color.white;
        }

        static void FillRect(Rect rect, Color colour)
        {
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        static void DrawFrame(Rect rect, Color colour, float thickness)
        {
            FillRect(new Rect(rect.x, rect.y, rect.width, thickness), colour);
            FillRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), colour);
            FillRect(new Rect(rect.x, rect.y, thickness, rect.height), colour);
            FillRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), colour);
        }

        static Texture2D CreateDotTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                // White fill with a dark rim, so it reads on any map colour once tinted.
                float alpha = Mathf.Clamp01((1f - r) * half);
                float rim = Mathf.InverseLerp(0.65f, 0.8f, r);
                texture.SetPixel(x, y, new Color(1f - rim * 0.8f, 1f - rim * 0.8f, 1f - rim * 0.8f, alpha));
            }
            texture.Apply();
            return texture;
        }
    }
}
