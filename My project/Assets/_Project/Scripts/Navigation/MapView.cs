using System.Collections.Generic;
using Backpacking.UI;
using Backpacking.World;
using UnityEngine;
using UnityEngine.UIElements;

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

        [Header("Map Rendering")]
        [SerializeField] int resolution = 1024;
        [SerializeField] float contourInterval = 25f;
        [Tooltip("Every Nth contour is drawn darker.")]
        [SerializeField] int indexContourEvery = 4;
        [SerializeField] float gridSpacing = 500f;
        [SerializeField] float scaleBarMetres = 1000f;

        [Tooltip("Show a 'you are here' marker. Off by default: read your position from the land.")]
        [SerializeField] bool showPlayerPosition;

        static readonly Color Ink = new(0.15f, 0.13f, 0.12f);
        static readonly Color UnvisitedColour = new(0.8f, 0.15f, 0.1f);
        static readonly Color VisitedColour = new(0.15f, 0.45f, 0.2f);

        Texture2D mapTexture;
        VisualElement screen, paper;
        VisualElement playerMarker;
        readonly List<(NavigationPoint point, VisualElement marker)> markers = new();
        float laidOutSide, mapMargin, mapSize;

        public bool IsOpen { get; private set; }

        void Start()
        {
            mapTexture = TopographicMap.Generate(terrain, resolution, contourInterval, indexContourEvery, gridSpacing);

            paper = UIBuild.Box("map-paper");
            paper.style.position = Position.Absolute;
            screen = UIBuild.Layer().With(paper);
            screen.RegisterCallback<GeometryChangedEvent>(_ => LayOut());
            screen.SetVisible(false);
            GameUI.Current.Screens.Add(screen.IgnoreMouse());
        }

        void OnDestroy() => Destroy(mapTexture);

        void Update()
        {
            if (Player.GameInput.MapPressed && !Player.PlayerControlLock.CursorNeeded)
                SetOpen(!IsOpen);

            if (!IsOpen)
                return;
            foreach ((NavigationPoint point, VisualElement marker) in markers)
                if (point != null)
                    marker.style.backgroundColor = point.Visited ? VisitedColour : UnvisitedColour;
            if (playerMarker != null)
                Place(playerMarker, player.position, laidOutSide * 0.014f);
        }

        void SetOpen(bool open)
        {
            IsOpen = open;
            screen.SetVisible(open);
            if (open)
                GameUI.ClaimEscape(this, () => SetOpen(false));
            else
                GameUI.ReleaseEscape(this);
        }

        /// <summary>Sizes the paper to the screen and places everything on it, in pixels scaled from the paper's size.</summary>
        void LayOut()
        {
            Rect area = screen.contentRect;
            float side = Mathf.Min(area.width, area.height) * 0.88f;
            if (side <= 0f || Mathf.Approximately(side, laidOutSide))
                return;
            laidOutSide = side;

            paper.style.width = paper.style.height = side;
            paper.style.left = (area.width - side) / 2f;
            paper.style.top = (area.height - side) / 2f;
            paper.Clear();
            markers.Clear();

            float margin = mapMargin = side * 0.06f;
            mapSize = side - margin * 2f;
            float labelSize = side * 0.018f;

            var map = new VisualElement();
            map.style.position = Position.Absolute;
            map.style.left = map.style.top = margin;
            map.style.width = map.style.height = mapSize;
            map.style.backgroundImage = mapTexture;
            SetBorder(map, 2f, Ink);
            paper.Add(map);
            AddTrail(side);

            Label title = Label("Prototype Valley", side * 0.026f);
            title.style.left = 0f;
            title.style.width = side;
            title.style.top = margin * 0.2f;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;

            // Letters along the top for columns (west to east), numbers down the side for rows (north to south).
            int cells = Mathf.CeilToInt(terrain.terrainData.size.x / gridSpacing);
            float cell = mapSize * gridSpacing / terrain.terrainData.size.x;
            for (int i = 0; i < cells; i++)
            {
                Label column = Label(((char)('A' + i)).ToString(), labelSize);
                column.style.left = margin + i * cell;
                column.style.width = cell;
                column.style.top = margin * 0.55f;
                column.style.unityTextAlign = TextAnchor.MiddleCenter;
                Label row = Label((i + 1).ToString(), labelSize);
                row.style.left = margin * 0.25f;
                row.style.width = margin * 0.6f;
                row.style.top = margin + i * cell + cell * 0.5f - labelSize;
                row.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            // Scale bar: half filled, half outlined, under the map's right edge.
            float barLength = mapSize * scaleBarMetres / terrain.terrainData.size.x;
            var bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.left = margin + mapSize - barLength;
            bar.style.top = margin + mapSize + margin * 0.3f;
            bar.style.width = barLength;
            bar.style.height = margin * 0.12f;
            SetBorder(bar, 1f, Ink);
            var filled = new VisualElement();
            filled.style.width = Length.Percent(50f);
            filled.style.height = Length.Percent(100f);
            filled.style.backgroundColor = Ink;
            bar.Add(filled);
            paper.Add(bar);
            Label scale = Label(scaleBarMetres >= 1000f ? $"{scaleBarMetres / 1000f:0.#} km" : $"{scaleBarMetres:0} m", labelSize);
            scale.style.left = margin + mapSize - barLength;
            scale.style.width = barLength;
            scale.style.top = margin + mapSize + margin * 0.45f;
            scale.style.unityTextAlign = TextAnchor.UpperRight;

            // North arrow in the right margin.
            var arrow = new VisualElement();
            arrow.style.position = Position.Absolute;
            arrow.style.left = margin + mapSize + margin * 0.5f - 1.5f;
            arrow.style.top = margin + margin * 0.5f;
            arrow.style.width = 3f;
            arrow.style.height = margin * 0.9f;
            arrow.style.backgroundColor = Ink;
            paper.Add(arrow);
            Label north = Label("N", labelSize);
            north.style.left = margin + mapSize;
            north.style.width = margin;
            north.style.top = margin * 0.95f - labelSize;
            north.style.unityTextAlign = TextAnchor.MiddleCenter;

            Label legend = Label($"Contours every {contourInterval:0} m   ·   Red dashes: trail   ·   Large markers: trading posts and the summit   ·   Small: checkpoints", labelSize);
            legend.style.left = margin;
            legend.style.top = margin + mapSize + margin * 0.4f;

            foreach (NavigationPoint point in NavigationPoint.All)
            {
                // Trading posts and the summit get big markers and capital letters.
                bool post = point.Kind != NavigationPointKind.Checkpoint;
                float size = side * (post ? 0.024f : 0.013f);
                VisualElement marker = UIBuild.Box("map-marker");
                marker.style.width = marker.style.height = size;
                Place(marker, point.transform.position, size);
                paper.Add(marker);
                markers.Add((point, marker));

                Label name = Label(post ? point.DisplayName.ToUpperInvariant() : point.DisplayName, labelSize);
                Vector2 at = MapPosition(point.transform.position);
                name.style.left = at.x + size * 0.7f;
                name.style.top = at.y - labelSize * 0.9f;
            }

            playerMarker = null;
            if (showPlayerPosition && player != null)
            {
                playerMarker = UIBuild.Box("map-marker");
                playerMarker.style.width = playerMarker.style.height = side * 0.014f;
                playerMarker.style.backgroundColor = new Color(0.1f, 0.3f, 0.9f);
                paper.Add(playerMarker);
            }

            Label Label(string text, float fontSize)
            {
                Label label = UIBuild.Text(text, "map-label");
                label.style.fontSize = fontSize;
                paper.Add(label);
                return label;
            }

        }

        /// <summary>The footpath as a dashed red line, the way trail maps show it.</summary>
        void AddTrail(float side)
        {
            TrailPath trail = FindAnyObjectByType<TrailPath>();
            if (trail == null || trail.Points.Count < 2)
                return;

            var points = new Vector2[trail.Points.Count];
            for (int i = 0; i < points.Length; i++)
                points[i] = MapPosition(trail.Points[i]);
            float dash = side * 0.009f, lineWidth = Mathf.Max(1.5f, side * 0.0028f);

            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.style.position = Position.Absolute;
            line.style.left = line.style.top = 0f;
            line.style.width = line.style.height = side;
            line.generateVisualContent += context =>
            {
                Painter2D painter = context.painter2D;
                painter.strokeColor = TrailColour;
                painter.lineWidth = lineWidth;
                painter.lineCap = LineCap.Round;
                // Walk the line, drawing every other dash-length stretch.
                float along = 0f;
                for (int i = 0; i < points.Length - 1; i++)
                {
                    Vector2 a = points[i], b = points[i + 1];
                    float length = Vector2.Distance(a, b);
                    float done = 0f;
                    while (done < length)
                    {
                        float phase = along % (dash * 2f);
                        bool drawing = phase < dash;
                        float step = Mathf.Min(length - done, (drawing ? dash : dash * 2f) - phase);
                        if (drawing)
                        {
                            painter.BeginPath();
                            painter.MoveTo(Vector2.Lerp(a, b, done / length));
                            painter.LineTo(Vector2.Lerp(a, b, (done + step) / length));
                            painter.Stroke();
                        }
                        done += step;
                        along += step;
                    }
                }
            };
            paper.Add(line);
        }

        static readonly Color TrailColour = new(0.72f, 0.12f, 0.08f, 0.9f);

        /// <summary>Where a world position falls on the paper, in pixels from its top-left corner.</summary>
        Vector2 MapPosition(Vector3 world)
        {
            Vector3 size = terrain.terrainData.size;
            Vector3 local = world - terrain.transform.position;
            return new Vector2(mapMargin + local.x / size.x * mapSize, mapMargin + (1f - local.z / size.z) * mapSize);
        }

        void Place(VisualElement marker, Vector3 world, float size)
        {
            Vector2 at = MapPosition(world);
            marker.style.left = at.x - size / 2f;
            marker.style.top = at.y - size / 2f;
        }

        static void SetBorder(VisualElement element, float width, Color colour)
        {
            element.style.borderLeftWidth = element.style.borderRightWidth = element.style.borderTopWidth = element.style.borderBottomWidth = width;
            element.style.borderLeftColor = element.style.borderRightColor = element.style.borderTopColor = element.style.borderBottomColor = colour;
        }
    }
}
