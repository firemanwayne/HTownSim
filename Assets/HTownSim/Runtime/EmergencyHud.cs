#nullable enable
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HTownSim.Runtime
{
    /// <summary>
    /// A simplified emergency controller head: backlit keys for siren standby, wail, yelp, pier (phaser), horn and the light
    /// bar, with a LED strip that chases while a siren tone is sounding. Keys light from <see cref="FireResponse"/> state and
    /// can also be clicked while the mouse cursor is free (it is only captured while the right button is held to look around).
    /// </summary>
    internal sealed class EmergencyHud : MonoBehaviour
    {
        private const float PanelWidth = 404f;
        private const float PanelHeight = 168f;
        private const float Margin = 22f;
        private const float BottomMargin = 84f;
        private const float Padding = 14f;
        private const float KeyWidth = 68f;
        private const float KeyHeight = 52f;
        private const float KeyGap = 8f;
        private const float WideKeyWidth = 150f;
        private const float WideKeyHeight = 44f;
        private const int LedCount = 8;
        private const float LedSize = 9f;
        private const float LedStep = 22f;
        private const float LedChaseRate = 9f;
        private const float StateEase = 20f;
        private const float RimWidth = 3f;
        private const int RoundedSize = 32;
        private const int RoundedRadius = 8;
        private const string Title = "SIREN CONTROL";

        private static readonly Color Body = new Color(0.045f, 0.047f, 0.055f, 0.94f);
        private static readonly Color FaceOff = new Color(0.9f, 0.93f, 0.97f, 0.7f);
        private static readonly Color FaceOn = new Color(0.04f, 0.045f, 0.05f, 0.97f);
        private static readonly Color RimGreen = new Color(0.22f, 1f, 0.48f, 1f);
        private static readonly Color RimRed = new Color(1f, 0.2f, 0.17f, 1f);
        private static readonly Color TextOff = new Color(0.09f, 0.11f, 0.14f, 1f);
        private static readonly Color TextOn = Color.white;
        private static readonly Color HintOff = new Color(0.32f, 0.36f, 0.42f, 1f);
        private static readonly Color HintOn = new Color(0.62f, 0.68f, 0.74f, 1f);
        private static readonly Color TextDim = new Color(0.72f, 0.78f, 0.85f, 1f);
        private static readonly Color Accent = new Color(0.96f, 0.46f, 0.2f, 1f);
        private static readonly Color LedOff = new Color(0.16f, 0.17f, 0.2f, 1f);
        private static readonly Color LedOn = new Color(1f, 0.55f, 0.18f, 1f);

        private readonly List<EmergencyButton> _buttons = new List<EmergencyButton>();
        private readonly List<Image> _leds = new List<Image>();
        private FireResponse _response = null!;
        private Canvas _canvas = null!;
        private Sprite _rounded = null!;
        private TMP_Text _readout = null!;
        private EmergencyButton? _held;

        /// <summary>Builds the panel as a child of the HUD canvas.</summary>
        internal void Initialize(FireResponse response, Canvas canvas)
        {
            _response = response;

            _canvas = canvas;

            _rounded = CreateRoundedSprite();

            var panel = NewRect("EmergencyHud", canvas.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin, BottomMargin), new Vector2(PanelWidth, PanelHeight));

            transform.SetParent(panel, false);

            var background = panel.gameObject.AddComponent<Image>();

            background.sprite = _rounded;

            background.type = Image.Type.Sliced;

            background.color = Body;

            background.raycastTarget = false;

            var title = NewText("Title", panel, Title, 15f, Accent, TextAlignmentOptions.TopLeft);

            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(Padding, -8f), new Vector2(200f, 20f));

            _readout = NewText("Readout", panel, "", 15f, TextDim, TextAlignmentOptions.TopRight);

            Place(_readout.rectTransform, new Vector2(1f, 1f), new Vector2(-Padding, -8f), new Vector2(180f, 20f));

            var top = new[] { "STBY", "WAIL", "YELP", "PIER", "HORN" };

            var keys = new[] { "Q", "1", "2", "3", "H" };

            var keyCodes = new[] { Key.Q, Key.Digit1, Key.Digit2, Key.Digit3, Key.H };

            for (var i = 0; i < top.Length; i++)
            {
                var button = NewButton(panel, top[i], keys[i], new Vector2(Padding + i * (KeyWidth + KeyGap), -34f), new Vector2(KeyWidth, KeyHeight));

                button.Key = keyCodes[i];

                _buttons.Add(button);
            }

            _buttons[0].IsLit = () => _response.InStandby;

            _buttons[0].OnPress = _response.StandbyAll;

            for (var tone = 0; tone < 3; tone++)
            {
                var captured = tone;

                _buttons[1 + tone].IsLit = () => _response.SirenOn && _response.Tone == captured;

                _buttons[1 + tone].OnPress = () => _response.SelectTone(captured, true);
            }

            _buttons[4].OnPress = () => _response.HornPointerDown = true;

            _buttons[4].OnRelease = () => _response.HornPointerDown = false;

            _buttons[4].ExtraPressed = () => _response.HornSounding;

            var lights = NewButton(panel, "LIGHTS", "L", new Vector2(Padding, -94f), new Vector2(WideKeyWidth, WideKeyHeight));

            lights.Key = Key.L;

            lights.IsLit = () => _response.LightsOn;

            lights.OnPress = _response.ToggleLights;

            _buttons.Add(lights);

            for (var i = 0; i < LedCount; i++)
            {
                var led = NewRect("Led" + i, panel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Padding + 4f + i * LedStep, 12f), new Vector2(LedSize, LedSize));

                var image = led.gameObject.AddComponent<Image>();

                image.sprite = _rounded;

                image.type = Image.Type.Sliced;

                image.color = LedOff;

                image.raycastTarget = false;

                _leds.Add(image);
            }
        }

        /// <summary>Eases key brightness toward their state, animates the LED chase and handles mouse clicks.</summary>
        private void Update()
        {
            if (_response == null)
            {
                return;
            }

            var blend = 1f - Mathf.Exp(-StateEase * Time.unscaledDeltaTime);

            var keyboard = Keyboard.current;

            foreach (var button in _buttons)
            {
                var held = _held == button || button.ExtraPressed() || (keyboard != null && keyboard[button.Key].isPressed);

                button.Active = Mathf.Lerp(button.Active, button.IsLit() ? 1f : 0f, blend);

                button.Pressed = Mathf.Lerp(button.Pressed, held ? 1f : 0f, blend);

                var on = Mathf.Max(button.Active, button.Pressed);

                button.Face.color = Color.Lerp(FaceOff, FaceOn, on);

                var rim = Color.Lerp(RimGreen, RimRed, button.Pressed);

                button.Rim.color = new Color(rim.r, rim.g, rim.b, on);

                button.Glow.effectColor = new Color(rim.r, rim.g, rim.b, 0.65f * on);

                button.Label.color = Color.Lerp(TextOff, TextOn, on);

                button.Hint.color = Color.Lerp(HintOff, HintOn, on);
            }

            _readout.text = _response.SirenOn ? _response.ToneName : "STANDBY";

            AnimateLeds();

            HandleMouse();
        }

        /// <summary>Lights a pair of LEDs sweeping back and forth while a tone is sounding, and none otherwise.</summary>
        private void AnimateLeds()
        {
            var sweep = _response.SirenOn ? Mathf.PingPong(Time.unscaledTime * LedChaseRate, LedCount - 1) : -10f;

            for (var i = 0; i < _leds.Count; i++)
            {
                var near = Mathf.Clamp01(1.2f - Mathf.Abs(i - sweep));

                _leds[i].color = Color.Lerp(LedOff, LedOn, near);
            }
        }

        /// <summary>Presses keys under the cursor on left click; the horn stays on for as long as the button is held.</summary>
        private void HandleMouse()
        {
            var mouse = Mouse.current;

            if (mouse is null || Cursor.lockState == CursorLockMode.Locked)
            {
                Release();

                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                var point = mouse.position.ReadValue();

                foreach (var button in _buttons)
                {
                    if (RectTransformUtility.RectangleContainsScreenPoint(button.Rect, point, _canvas.worldCamera))
                    {
                        button.OnPress();

                        _held = button;

                        return;
                    }
                }
            }

            if (_held != null && !mouse.leftButton.isPressed)
            {
                Release();
            }
        }

        private void Release()
        {
            if (_held is null)
            {
                return;
            }

            _held.OnRelease?.Invoke();

            _held = null;
        }

        private EmergencyButton NewButton(RectTransform parent, string label, string keyHint, Vector2 position, Vector2 size)
        {
            var rect = NewRect(label, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), position, size);

            var rimRect = NewRect("Rim", rect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            Stretch(rimRect, new Vector2(-RimWidth, -RimWidth), new Vector2(RimWidth, RimWidth));

            var rim = rimRect.gameObject.AddComponent<Image>();

            rim.sprite = _rounded;

            rim.type = Image.Type.Sliced;

            rim.color = new Color(RimGreen.r, RimGreen.g, RimGreen.b, 0f);

            rim.raycastTarget = false;

            var glow = rimRect.gameObject.AddComponent<Outline>();

            glow.effectColor = new Color(RimGreen.r, RimGreen.g, RimGreen.b, 0f);

            glow.effectDistance = new Vector2(4f, 4f);

            var faceRect = NewRect("Face", rect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            Stretch(faceRect, Vector2.zero, Vector2.zero);

            var face = faceRect.gameObject.AddComponent<Image>();

            face.sprite = _rounded;

            face.type = Image.Type.Sliced;

            face.color = FaceOff;

            face.raycastTarget = false;

            var main = NewText("Label", rect, label, 17f, TextOff, TextAlignmentOptions.Center);

            main.fontStyle = FontStyles.Bold;

            Stretch(main.rectTransform, new Vector2(2f, 12f), new Vector2(-2f, -4f));

            var hint = NewText("Key", rect, keyHint, 11f, HintOff, TextAlignmentOptions.Bottom);

            Stretch(hint.rectTransform, new Vector2(2f, 3f), new Vector2(-2f, -34f));

            return new EmergencyButton { Rect = rect, Face = face, Rim = rim, Glow = glow, Label = main, Hint = hint };
        }

        private static RectTransform NewRect(string objectName, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();

            rect.SetParent(parent, false);

            rect.anchorMin = anchorMin;

            rect.anchorMax = anchorMax;

            rect.pivot = pivot;

            rect.anchoredPosition = position;

            rect.sizeDelta = size;

            return rect;
        }

        private static TMP_Text NewText(string objectName, RectTransform parent, string text, float size, Color color, TextAlignmentOptions alignment)
        {
            var rect = NewRect(objectName, parent, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();

            label.text = text;

            label.fontSize = size;

            label.color = color;

            label.alignment = alignment;

            label.raycastTarget = false;

            label.textWrappingMode = TextWrappingModes.NoWrap;

            return label;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;

            rect.anchorMax = anchor;

            rect.pivot = anchor;

            rect.anchoredPosition = position;

            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;

            rect.anchorMax = Vector2.one;

            rect.offsetMin = offsetMin;

            rect.offsetMax = offsetMax;
        }

        /// <summary>A small white rounded-rectangle sprite with nine-slice borders, tinted by each Image.</summary>
        private static Sprite CreateRoundedSprite()
        {
            var texture = new Texture2D(RoundedSize, RoundedSize, TextureFormat.RGBA32, false) { name = "EmergencyHudRounded", wrapMode = TextureWrapMode.Clamp };

            for (var y = 0; y < RoundedSize; y++)
            {
                for (var x = 0; x < RoundedSize; x++)
                {
                    var dx = Mathf.Max(RoundedRadius - 0.5f - x, x - (RoundedSize - RoundedRadius - 0.5f), 0f);

                    var dy = Mathf.Max(RoundedRadius - 0.5f - y, y - (RoundedSize - RoundedRadius - 0.5f), 0f);

                    var alpha = Mathf.Clamp01(RoundedRadius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, RoundedSize, RoundedSize), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(RoundedRadius, RoundedRadius, RoundedRadius, RoundedRadius));
        }
    }
}
