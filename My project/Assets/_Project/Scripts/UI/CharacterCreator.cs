using System;
using System.Collections.Generic;
using Backpacking.Character;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Background = Backpacking.Character.Background;

namespace Backpacking.UI
{
    /// <summary>
    /// The new-trip screen: name your hiker, pick who they are from the roster (men or women, then step through
    /// the outfits), the colour of their pack, and a background with its own strengths. A turntable preview
    /// shows the result; drag it to turn the hiker around.
    /// </summary>
    public class CharacterCreator : MonoBehaviour
    {
        [SerializeField] CharacterLibrary library;

        // A layer only the preview camera draws, far above the map, so the preview never shows in the world.
        const int PreviewLayer = 31;
        static readonly Vector3 StagePosition = new(0f, 3000f, 0f);

        CharacterProfile profile;
        Action<CharacterProfile> onStart;
        Action onBack;

        VisualElement screen, previewImage;
        TextField nameField;
        Button maleButton, femaleButton;
        Label hikerLabel, backgroundText;
        readonly List<(Button button, Background background)> backgroundButtons = new();
        readonly Dictionary<string, List<(Button button, Color colour)>> swatchRows = new();

        Transform stage;
        CharacterAppearance preview;
        Camera previewCamera;
        RenderTexture previewTexture;
        float spin = 200f;
        bool dragging;

        public bool IsOpen { get; private set; }
        /// <summary>Whether to run the tutorial on the trip about to start.</summary>
        public bool TutorialWanted => tutorialToggle == null || tutorialToggle.value;

        Toggle tutorialToggle;

        void Start()
        {
            Camera main = Camera.main;
            if (main != null)
                main.cullingMask &= ~(1 << PreviewLayer);
            Build();
        }

        void OnDestroy()
        {
            if (previewTexture != null)
                previewTexture.Release();
        }

        /// <summary>Shows the screen with the last hiker made on this machine.</summary>
        public void Open(Action<CharacterProfile> start, Action back)
        {
            onStart = start;
            onBack = back;
            profile = CharacterProfile.LoadLast();
            EnsureStage();
            previewCamera.enabled = true;
            nameField.SetValueWithoutNotify(profile.name);
            Refresh();
            IsOpen = true;
            screen.SetVisible(true);
            screen.FocusFirstButton();
        }

        /// <summary>Back to the title, as if pressing the Back button.</summary>
        public void Back()
        {
            Close();
            onBack?.Invoke();
        }

        void Close()
        {
            IsOpen = false;
            screen.SetVisible(false);
            if (previewCamera != null)
                previewCamera.enabled = false;
        }

        void Update()
        {
            if (!IsOpen || stage == null)
                return;
            if (!dragging)
                spin += 12f * Time.unscaledDeltaTime;
            preview.transform.localRotation = Quaternion.Euler(0f, spin, 0f);
        }

        // ---------- Screen ----------

        void Build()
        {
            previewImage = UIBuild.Box("creator-preview");
            previewImage.Add(UIBuild.Text("Drag to turn", "reason").Classes("creator-hint"));
            previewImage.RegisterCallback<PointerDownEvent>(e =>
            {
                dragging = true;
                previewImage.CapturePointer(e.pointerId);
            });
            previewImage.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (dragging)
                    spin -= e.deltaPosition.x * 0.6f;
            });
            previewImage.RegisterCallback<PointerUpEvent>(e =>
            {
                dragging = false;
                previewImage.ReleasePointer(e.pointerId);
            });

            nameField = new TextField { maxLength = 24 };
            nameField.AddToClassList("creator-name");
            nameField.RegisterValueChangedCallback(change => profile.name = change.newValue);

            maleButton = UIBuild.Button("Male", () => SetBody(false));
            femaleButton = UIBuild.Button("Female", () => SetBody(true));
            hikerLabel = UIBuild.Text("", "creator-choice");

            VisualElement backgrounds = UIBuild.Box("row", "creator-wrap");
            foreach (Background background in Backgrounds.All)
            {
                Background chosen = background;
                Button button = UIBuild.Button(Backgrounds.Name(background), () =>
                {
                    profile.background = chosen;
                    Refresh();
                });
                backgroundButtons.Add((button, background));
                backgrounds.Add(button);
            }
            backgroundText = UIBuild.Text("", "small", "creator-description");

            VisualElement form = UIBuild.Box("panel", "creator-form").With(
                UIBuild.Text("Your hiker", "title"),
                Row("Name", nameField),
                Row("Body", UIBuild.Box("row").With(maleButton, femaleButton)),
                Row("Hiker", UIBuild.Box("row").With(
                    UIBuild.Button("<", () => StepHiker(-1)), hikerLabel, UIBuild.Button(">", () => StepHiker(1)))),
                Row("Pack", Swatches("pack", CharacterProfile.GearColours, c => profile.packColour = c)),
                UIBuild.Text("BACKGROUND", "heading"),
                backgrounds,
                backgroundText,
                Row("Tutorial", tutorialToggle = new Toggle { value = PlayerPrefs.GetInt("tutorial.done", 0) == 0 }),
                UIBuild.Box("footer").With(
                    UIBuild.Button("Back", Back),
                    UIBuild.Button("Start trip", StartTrip, "primary")));

            screen = UIBuild.Layer("screen-dim", "centred").With(UIBuild.Box("row").With(previewImage, form));
            screen.pickingMode = PickingMode.Position;
            screen.SetVisible(false);
            GameUI.Current.Menus.Add(screen);
        }

        static VisualElement Row(string label, VisualElement control) =>
            UIBuild.Box("setting").With(UIBuild.Text(label, "setting-label"), control.Classes("grow"));

        VisualElement Swatches(string key, Color[] colours, Action<Color> set)
        {
            VisualElement row = UIBuild.Box("row");
            var buttons = new List<(Button, Color)>();
            foreach (Color colour in colours)
            {
                Color chosen = colour;
                Button swatch = UIBuild.Button("", () =>
                {
                    set(chosen);
                    Refresh();
                }, "swatch");
                swatch.style.backgroundColor = colour;
                buttons.Add((swatch, colour));
                row.Add(swatch);
            }
            swatchRows[key] = buttons;
            return row;
        }

        bool Female => library != null && library.Find(profile.hiker) is { female: true };

        /// <summary>The roster entries for one body, in roster order.</summary>
        List<CharacterLibrary.Hiker> Hikers(bool female)
        {
            var hikers = new List<CharacterLibrary.Hiker>();
            if (library != null && library.hikers != null)
                foreach (CharacterLibrary.Hiker hiker in library.hikers)
                    if (hiker.model != null && hiker.female == female)
                        hikers.Add(hiker);
            return hikers;
        }

        void SetBody(bool female)
        {
            List<CharacterLibrary.Hiker> hikers = Hikers(female);
            if (female != Female && hikers.Count > 0)
                profile.hiker = hikers[0].id;
            Refresh();
        }

        void StepHiker(int direction)
        {
            List<CharacterLibrary.Hiker> hikers = Hikers(Female);
            if (hikers.Count == 0)
                return;
            int index = hikers.FindIndex(hiker => hiker.id == profile.hiker);
            profile.hiker = hikers[((index < 0 ? 0 : index + direction) % hikers.Count + hikers.Count) % hikers.Count].id;
            Refresh();
        }

        /// <summary>Shows the profile's choices on the controls and rebuilds the preview.</summary>
        void Refresh()
        {
            // A saved hiker who has left the roster becomes the first one.
            CharacterLibrary.Hiker current = library != null ? library.Find(profile.hiker) : null;
            if (current != null)
                profile.hiker = current.id;
            Select(maleButton, !Female);
            Select(femaleButton, Female);
            hikerLabel.text = current != null ? current.label : "";
            HighlightSwatch("pack", profile.packColour);
            foreach ((Button button, Background background) in backgroundButtons)
                Select(button, background == profile.background);
            backgroundText.text = Backgrounds.Description(profile.background);
            RebuildPreview();
        }

        void HighlightSwatch(string key, Color chosen)
        {
            foreach ((Button button, Color colour) in swatchRows[key])
                Select(button, (Vector4)colour == (Vector4)chosen);
        }

        static void Select(VisualElement element, bool selected)
        {
            if (selected)
                element.AddToClassList("selected");
            else
                element.RemoveFromClassList("selected");
        }

        void StartTrip()
        {
            if (string.IsNullOrWhiteSpace(profile.name))
                profile.name = "Sam";
            profile.name = profile.name.Trim();
            profile.SaveAsLast();
            Close();
            onStart?.Invoke(profile.Clone());
        }

        // ---------- Preview ----------

        void EnsureStage()
        {
            if (stage != null)
                return;
            stage = new GameObject("Character Preview").transform;
            stage.position = StagePosition;

            var model = new GameObject("Hiker");
            model.transform.SetParent(stage, false);
            preview = model.AddComponent<CharacterAppearance>();
            preview.Library = library;

            var cameraObject = new GameObject("Preview Camera");
            cameraObject.transform.SetParent(stage, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.05f, 4.2f);
            cameraObject.transform.LookAt(stage.position + Vector3.up * 0.95f);
            previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.09f, 0.1f, 0.09f);
            previewCamera.fieldOfView = 30f;
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = 20f;
            previewTexture = new RenderTexture(720, 1040, 24) { name = "Character Preview" };
            previewCamera.targetTexture = previewTexture;
            previewCamera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            previewCamera.enabled = false;
            previewImage.style.backgroundImage = UnityEngine.UIElements.Background.FromRenderTexture(previewTexture);

            // A warm key light from the front left, just for the preview.
            var lightObject = new GameObject("Preview Light");
            lightObject.transform.SetParent(stage, false);
            lightObject.transform.localPosition = new Vector3(-2f, 2.6f, 3f);
            lightObject.transform.LookAt(stage.position + Vector3.up);
            var key = lightObject.AddComponent<Light>();
            key.type = LightType.Spot;
            key.range = 10f;
            key.spotAngle = 50f;
            key.intensity = 18f;
            key.color = new Color(1f, 0.95f, 0.88f);
            key.cullingMask = 1 << PreviewLayer;
        }

        /// <summary>Head to toe in frame, with a little room above and below.</summary>
        void FrameCamera(float height)
        {
            if (previewCamera == null || height <= 0.1f)
                return;
            float halfView = Mathf.Tan(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float distance = height * 0.58f / halfView;
            Vector3 centre = stage.position + Vector3.up * (height * 0.5f);
            previewCamera.transform.position = centre + new Vector3(0f, height * 0.05f, distance);
            previewCamera.transform.LookAt(centre);
            previewCamera.farClipPlane = distance + 5f;
        }

        void RebuildPreview()
        {
            if (preview == null)
                return;
            preview.Build(profile);
            preview.SetLayer(PreviewLayer);
            preview.SetFirstPerson(false);
            FrameCamera(preview.Height);
            if (preview.Animator != null)
                preview.Animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        }
    }
}
