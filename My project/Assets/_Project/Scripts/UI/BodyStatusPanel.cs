using System.Collections.Generic;
using System.Text;
using Backpacking.Survival;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.UI
{
    /// <summary>
    /// The journal's Status page: an outline of the hiker with each body part coloured by its worst condition
    /// (yellow developing, orange a problem, red serious), and a list of every condition with how to treat it.
    /// </summary>
    public class BodyStatusPanel
    {
        static readonly Color Fine = new(0.93f, 0.9f, 0.84f, 0.12f);
        static readonly Color Outline = new(0.93f, 0.9f, 0.84f, 0.7f);
        static readonly Color[] SeverityColours =
        {
            Fine,
            new(0.92f, 0.8f, 0.3f, 0.85f),
            new(0.93f, 0.55f, 0.2f, 0.9f),
            new(0.86f, 0.2f, 0.15f, 0.95f),
        };
        static readonly string[] SeverityWords = { "", "Developing", "Problem", "Serious" };

        readonly Vitals vitals;
        readonly Backpack backpack;
        readonly VisualElement figure;
        readonly ScrollView list;
        List<BodyCondition> conditions = new();
        string shownKey;

        public VisualElement Root { get; }

        public BodyStatusPanel(Vitals vitals, Backpack backpack)
        {
            this.vitals = vitals;
            this.backpack = backpack;

            figure = new VisualElement();
            figure.style.width = 240f;
            figure.style.height = 560f;
            figure.style.flexShrink = 0f;
            figure.generateVisualContent += DrawFigure;

            list = new ScrollView();
            list.style.height = 560f;
            list.AddToClassList("grow");
            list.style.marginLeft = 24f;

            Root = UIBuild.Box("row").With(figure, list);
            Root.style.alignItems = Align.FlexStart;
        }

        /// <summary>Re-reads the vitals; rebuilds the list only when the conditions change.</summary>
        public void Refresh()
        {
            conditions = BodyConditions.Evaluate(vitals, backpack);
            var key = new StringBuilder();
            foreach (BodyCondition condition in conditions)
                key.Append(condition.Name).Append(condition.Severity).Append(condition.Description).Append('|');
            figure.MarkDirtyRepaint();
            if (key.ToString() == shownKey)
                return;
            shownKey = key.ToString();

            list.Clear();
            if (conditions.Count == 0)
            {
                list.Add(UIBuild.Text("You're in good shape.", "title"));
                list.Add(UIBuild.Text("Nothing hurts. Keep eating, drinking, staying warm and resting your feet, and it'll stay that way.", "small"));
                return;
            }
            foreach (BodyCondition condition in conditions)
            {
                Label name = UIBuild.Text($"{condition.Name}", "heading");
                name.style.color = SeverityColours[condition.Severity];
                Label where = UIBuild.Text($"{SeverityWords[condition.Severity]}  ·  {PartName(condition.Part)}", "reason");
                list.Add(UIBuild.Box().With(
                    UIBuild.Box("row", "spread").With(name, where),
                    UIBuild.Text(condition.Description, "text"),
                    UIBuild.Text($"Treat: {condition.Treatment}", "small")));
            }
        }

        static string PartName(BodyPart part) => part switch
        {
            BodyPart.Head => "head",
            BodyPart.Torso => "body",
            BodyPart.Arms => "arms and hands",
            BodyPart.Legs => "legs",
            _ => "feet",
        };

        Color ColourFor(BodyPart part) => SeverityColours[BodyConditions.Worst(conditions, part)];

        /// <summary>A simple standing figure, front on: head, torso, arms, legs and feet.</summary>
        void DrawFigure(MeshGenerationContext context)
        {
            Painter2D paint = context.painter2D;
            paint.strokeColor = Outline;
            paint.lineWidth = 2f;
            paint.lineJoin = LineJoin.Round;

            Color head = ColourFor(BodyPart.Head), torso = ColourFor(BodyPart.Torso), arms = ColourFor(BodyPart.Arms),
                legs = ColourFor(BodyPart.Legs), feet = ColourFor(BodyPart.Feet);

            Circle(paint, new Vector2(120f, 48f), 32f, head);
            Shape(paint, torso, (109f, 80f), (131f, 80f), (131f, 96f), (109f, 96f));
            Shape(paint, torso, (72f, 98f), (168f, 98f), (160f, 200f), (154f, 284f), (86f, 284f), (80f, 200f));
            Shape(paint, arms, (70f, 102f), (50f, 112f), (34f, 266f), (54f, 270f), (72f, 160f));
            Shape(paint, arms, (170f, 102f), (190f, 112f), (206f, 266f), (186f, 270f), (168f, 160f));
            Circle(paint, new Vector2(43f, 284f), 13f, arms);
            Circle(paint, new Vector2(197f, 284f), 13f, arms);
            Shape(paint, legs, (86f, 286f), (118f, 286f), (114f, 500f), (92f, 500f));
            Shape(paint, legs, (122f, 286f), (154f, 286f), (148f, 500f), (126f, 500f));
            Shape(paint, feet, (88f, 502f), (116f, 502f), (118f, 532f), (78f, 532f));
            Shape(paint, feet, (124f, 502f), (152f, 502f), (162f, 532f), (122f, 532f));
        }

        static void Shape(Painter2D paint, Color fill, params (float x, float y)[] points)
        {
            paint.fillColor = fill;
            paint.BeginPath();
            paint.MoveTo(new Vector2(points[0].x, points[0].y));
            for (int i = 1; i < points.Length; i++)
                paint.LineTo(new Vector2(points[i].x, points[i].y));
            paint.ClosePath();
            paint.Fill();
            paint.Stroke();
        }

        static void Circle(Painter2D paint, Vector2 centre, float radius, Color fill)
        {
            paint.fillColor = fill;
            paint.BeginPath();
            paint.Arc(centre, radius, 0f, 360f);
            paint.ClosePath();
            paint.Fill();
            paint.Stroke();
        }
    }
}
