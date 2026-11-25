#nullable enable
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The aerial operator's panel. It takes the siren control panel's place (bottom right, same size and style) once the
    /// outriggers are properly set (at least 75% out and down), and gives it back when they are not, so it is never up in
    /// drive mode. It shows a top-down turntable dial with the bed position marked straight ahead and the ladder's
    /// pointer, a lamp that reads IN LINE WITH BED (green) or which way and how far to rotate home (amber), and the
    /// ladder angle, extension, outrigger state and any motion the interlocks refused.
    /// </summary>
    internal sealed class AerialHud : MonoBehaviour
    {
        private const string Title = "AERIAL CONTROL";

        /// <summary>Within this many degrees of straight ahead the ladder counts as in line with its bed (the lamp turns green). Raise it for an easier in-line signal; lower it to demand a precise alignment.</summary>
        private const float InLineTolerance = 1f;

        /// <summary>The aerial panel stands on top of the siren controls, which stay in view; this is the gap between them (HUD units).</summary>
        private const float StackGap = 12f;

        /// <summary>Size (HUD units) of the turntable dial. Raise it for a bigger dial; lower it for a smaller one.</summary>
        private const float DialSize = 116f;

        /// <summary>Width (HUD units) of the ladder pointer on the dial. Raise it for a thicker pointer; lower it for a thin one.</summary>
        private const float PointerWidth = 6f;

        /// <summary>Width (HUD units) of the in-line lamp. Raise it for a wider lamp; lower it for a narrower one.</summary>
        private const float LampWidth = 244f;

        /// <summary>Height (HUD units) of the in-line lamp. Raise it for a taller lamp; lower it for a shorter one.</summary>
        private const float LampHeight = 40f;

        /// <summary>Size in pixels of the generated dial sprite. Raise it for a crisper dial (more memory); lower it for a blockier one.</summary>
        private const int CircleSize = 128;

        private static readonly Color DialFace = new Color(0.1f, 0.11f, 0.13f, 1f);

        private static readonly Color DialRim = new Color(0.3f, 0.33f, 0.38f, 1f);

        private static readonly Color BedMark = new Color(0.9f, 0.93f, 0.97f, 0.9f);

        private static readonly Color Amber = new Color(1f, 0.72f, 0.18f, 1f);

        private static readonly Color LampText = new Color(0.04f, 0.05f, 0.06f, 1f);

        private AerialController _aerial = null!;

        private EmergencyHud? _sirenHud;

        private RectTransform _panel = null!;

        private RectTransform _pointer = null!;

        private Image _pointerImage = null!;

        private Image _lamp = null!;

        private TMP_Text _lampText = null!;

        private TMP_Text _readout = null!;

        private TMP_Text _details = null!;

        /// <summary>True while the panel is showing (outriggers set), for verification.</summary>
        internal bool Showing => _panel.gameObject.activeSelf;

        /// <summary>True when the last update found the ladder in line with its bed, for verification.</summary>
        internal bool InLine { get; private set; }

        /// <summary>The alignment lamp's text, for verification.</summary>
        internal string LampLabel => _lampText.text;

        /// <summary>Builds the panel (hidden) where the siren panel sits.</summary>
        internal void Initialize(AerialController aerial, Canvas canvas, EmergencyHud? sirenHud)
        {
            _aerial = aerial;

            _sirenHud = sirenHud;

            var rounded = EmergencyHud.CreateRoundedSprite();

            _panel = EmergencyHud.NewRect(
                "AerialHud",
                canvas.transform,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-EmergencyHud.Margin, EmergencyHud.BottomMargin + EmergencyHud.PanelHeight + StackGap),
                new Vector2(EmergencyHud.PanelWidth, EmergencyHud.PanelHeight));

            var background = _panel.gameObject.AddComponent<Image>();

            background.sprite = rounded;

            background.type = Image.Type.Sliced;

            background.color = EmergencyHud.Body;

            background.raycastTarget = false;

            var padding = EmergencyHud.Padding;

            var title = EmergencyHud.NewText("Title", _panel, Title, 15f, EmergencyHud.Accent, TextAlignmentOptions.TopLeft);

            EmergencyHud.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(padding, -8f), new Vector2(200f, 20f));

            _readout = EmergencyHud.NewText("Readout", _panel, "OUTRIGGERS SET", 15f, EmergencyHud.RimGreen, TextAlignmentOptions.TopRight);

            EmergencyHud.Place(_readout.rectTransform, new Vector2(1f, 1f), new Vector2(-padding, -8f), new Vector2(200f, 20f));

            BuildDial(padding);

            // Alignment lamp.
            var lampRect = EmergencyHud.NewRect("BedLamp", _panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(padding + DialSize + 16f, -34f), new Vector2(LampWidth, LampHeight));

            _lamp = lampRect.gameObject.AddComponent<Image>();

            _lamp.sprite = rounded;

            _lamp.type = Image.Type.Sliced;

            _lamp.raycastTarget = false;

            _lampText = EmergencyHud.NewText("Label", lampRect, "", 15f, LampText, TextAlignmentOptions.Center);

            _lampText.fontStyle = FontStyles.Bold;

            EmergencyHud.Stretch(_lampText.rectTransform, new Vector2(6f, 2f), new Vector2(-6f, -2f));

            // Ladder and outrigger readouts.
            _details = EmergencyHud.NewText("Details", _panel, "", 13f, EmergencyHud.TextDim, TextAlignmentOptions.TopLeft);

            _details.textWrappingMode = TextWrappingModes.Normal;

            EmergencyHud.Place(_details.rectTransform, new Vector2(0f, 1f), new Vector2(padding + DialSize + 16f, -82f), new Vector2(LampWidth, 78f));

            _panel.gameObject.SetActive(false);
        }

        /// <summary>The top-down turntable dial: the bed position is the fixed mark at the top, the pointer is the ladder.</summary>
        private void BuildDial(float padding)
        {
            var circle = CreateCircleSprite();

            var dial = EmergencyHud.NewRect("Dial", _panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(padding, -30f), new Vector2(DialSize, DialSize));

            var rim = dial.gameObject.AddComponent<Image>();

            rim.sprite = circle;

            rim.color = DialRim;

            rim.raycastTarget = false;

            var faceRect = EmergencyHud.NewRect("Face", dial, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            EmergencyHud.Stretch(faceRect, new Vector2(4f, 4f), new Vector2(-4f, -4f));

            var face = faceRect.gameObject.AddComponent<Image>();

            face.sprite = circle;

            face.color = DialFace;

            face.raycastTarget = false;

            // Bed mark: a short bar at the top of the dial (the truck's front), with a small cab outline label.
            var mark = EmergencyHud.NewRect("BedMark", dial, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -5f), new Vector2(18f, 5f));

            var markImage = mark.gameObject.AddComponent<Image>();

            markImage.color = BedMark;

            markImage.raycastTarget = false;

            var bedLabel = EmergencyHud.NewText("BedLabel", dial, "BED", 10f, BedMark, TextAlignmentOptions.Top);

            EmergencyHud.Place(bedLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(40f, 14f));

            // Ladder pointer: pivots at the dial centre and turns with the turntable.
            _pointer = EmergencyHud.NewRect("LadderPointer", dial, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(PointerWidth, DialSize / 2f - 14f));

            _pointerImage = _pointer.gameObject.AddComponent<Image>();

            _pointerImage.raycastTarget = false;

            var hub = EmergencyHud.NewRect("Hub", dial, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 14f));

            var hubImage = hub.gameObject.AddComponent<Image>();

            hubImage.sprite = circle;

            hubImage.color = DialRim;

            hubImage.raycastTarget = false;
        }

        /// <summary>Shows the panel (in place of the siren panel) while the outriggers are set, and updates it.</summary>
        private void Update()
        {
            if (_aerial == null)
            {
                return;
            }

            var show = _aerial.OutriggersSet;

            if (_panel.gameObject.activeSelf != show)
            {
                _panel.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            var azimuth = _aerial.Azimuth;

            InLine = Mathf.Abs(azimuth) <= InLineTolerance;

            var colour = InLine ? EmergencyHud.RimGreen : Amber;

            // UI rotation is counter-clockwise; the turntable's positive turn is to the right (clockwise from above).
            _pointer.localRotation = Quaternion.Euler(0f, 0f, -azimuth);

            _pointerImage.color = colour;

            _lamp.color = colour;

            _lampText.text = InLine
                ? (_aerial.LadderUp ? "IN LINE WITH BED" : "BEDDED  /  IN LINE")
                : $"ROTATE {(azimuth > 0f ? "LEFT" : "RIGHT")} {Mathf.Abs(azimuth):0}° TO BED";

            _readout.text = _aerial.LadderUp ? "LADDER UP" : "OUTRIGGERS SET";

            var notice = _aerial.Notice.Length > 0 ? $"\n<color=#FF5A4A>{_aerial.Notice}</color>" : "";

            _details.text =
                $"LADDER  {_aerial.Elevation:0}°     EXTENSION  {_aerial.Extension * 100f:0}%\n" +
                $"OUTRIGGERS  OUT {_aerial.OutriggerOut * 100f:0}%  DOWN {_aerial.OutriggerDown * 100f:0}%" +
                notice;
        }

        /// <summary>A white anti-aliased disc sprite, tinted by each Image.</summary>
        private static Sprite CreateCircleSprite()
        {
            var texture = new Texture2D(CircleSize, CircleSize, TextureFormat.RGBA32, false) { name = "AerialHudCircle", wrapMode = TextureWrapMode.Clamp };

            var radius = CircleSize / 2f;

            for (var y = 0; y < CircleSize; y++)
            {
                for (var x = 0; x < CircleSize; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance)));
                }
            }

            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, CircleSize, CircleSize), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
