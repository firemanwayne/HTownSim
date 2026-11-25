#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The start screen: pauses the game, asks which station to start from, then which of its apparatus to drive. Picking the
    /// apparatus the scene already fits just resumes; picking another stores the choice and reloads the scene, where
    /// <see cref="ApparatusLoadout"/> fits it before anything else starts.
    /// </summary>
    internal sealed class StartingTruckMenu : MonoBehaviour
    {
        /// <summary>The two steps of the screen.</summary>
        private enum Step
        {
            Station,
            Apparatus,
        }

        /// <summary>The layout is designed for a 1080 pixel high screen and scaled from there.</summary>
        private const float DesignHeight = 1080f;

        private const float CardWidth = 420f;

        private const float CardHeight = 300f;

        private const float CardGap = 36f;

        private static readonly Color Dim = new Color(0.02f, 0.04f, 0.08f, 0.88f);

        private static readonly Color Card = new Color(0.07f, 0.11f, 0.19f, 0.96f);

        private static readonly Color CardLit = new Color(0.1f, 0.16f, 0.27f, 0.98f);

        private static readonly Color CardOff = new Color(0.07f, 0.08f, 0.1f, 0.9f);

        private static readonly Color Amber = new Color(1f, 0.69f, 0.16f, 1f);

        private static readonly Color Text = new Color(0.9f, 0.93f, 0.97f, 1f);

        private static readonly Color Muted = new Color(0.62f, 0.69f, 0.8f, 1f);

        private static readonly Color Faint = new Color(0.4f, 0.44f, 0.5f, 1f);

        private readonly List<MenuCard> _cards = new List<MenuCard>();

        private Step _step = Step.Station;

        /// <summary>The apparatus the scene is fitted with right now.</summary>
        private StartingTruck _current;

        /// <summary>The station whose apparatus are listed in the second step.</summary>
        private StartingStation _station;

        /// <summary>False when the scene has no parked Station 8 units, so Station 8 cannot be chosen yet.</summary>
        private bool _station8Ready;

        /// <summary>False when the scene has no parked Station 16 units, so Station 16 cannot be chosen yet.</summary>
        private bool _station16Ready;

        /// <summary>The card the player is on (mouse hover, arrow keys or number keys).</summary>
        private int _highlight;

        /// <summary>The game's time scale before the menu froze it.</summary>
        private float _previousTimeScale = 1f;

        /// <summary>True on frames where the mouse moved, so a resting pointer does not override the arrow keys.</summary>
        private bool _mouseMoved;

        private Texture2D? _pixel;

        private GUIStyle? _title;

        private GUIStyle? _subtitle;

        private GUIStyle? _name;

        private GUIStyle? _tag;

        private GUIStyle? _body;

        private GUIStyle? _hint;

        private GUIStyle? _invisible;

        /// <summary>Shows the screen once per session, when the scene holds the fire response game.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Show()
        {
            if (StartingTruckChoice.Decided)
            {
                return;
            }

            var response = FindAnyObjectByType<FireResponse>();

            if (response == null)
            {
                return;
            }

            var menu = new GameObject(nameof(StartingTruckMenu)).AddComponent<StartingTruckMenu>();

            menu._current = ApparatusLoadout.Detect(response.transform);

            menu._station = StartingTruckChoice.StationOf(menu._current);

            menu._station8Ready = ApparatusLoadout.Station8Ready(response.gameObject.scene);

            menu._station16Ready = ApparatusLoadout.Station16Ready(response.gameObject.scene);

            menu.ShowStep(Step.Station);
        }

        /// <summary>Freezes the game under the menu and frees the mouse.</summary>
        private void OnEnable()
        {
            _previousTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;

            Time.timeScale = 0f;

            Cursor.visible = true;

            Cursor.lockState = CursorLockMode.None;
        }

        /// <summary>Puts the time scale back if the menu is removed some other way.</summary>
        private void OnDestroy()
        {
            if (Time.timeScale == 0f)
            {
                Time.timeScale = _previousTimeScale;
            }
        }

        /// <summary>Keyboard: 1, 2, 3 pick directly; the arrows move; Enter or Space confirms; Backspace goes back to the stations.</summary>
        private void Update()
        {
            _mouseMoved = Mouse.current is not null && Mouse.current.delta.ReadValue().sqrMagnitude > 0f;

            var keyboard = Keyboard.current;

            if (keyboard is null)
            {
                return;
            }

            for (var i = 0; i < _cards.Count; i++)
            {
                if (DigitKey(keyboard, i).wasPressedThisFrame)
                {
                    Select(i);

                    return;
                }
            }

            if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
            {
                _highlight = (_highlight + 1) % _cards.Count;
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
            {
                _highlight = (_highlight + _cards.Count - 1) % _cards.Count;
            }

            if (_step == Step.Apparatus && keyboard.backspaceKey.wasPressedThisFrame)
            {
                ShowStep(Step.Station);

                return;
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
            {
                Select(_highlight);
            }
        }

        /// <summary>The 1, 2, 3 or 4 key for a card index.</summary>
        private static UnityEngine.InputSystem.Controls.KeyControl DigitKey(Keyboard keyboard, int index)
        {
            return index switch
            {
                0 => keyboard.digit1Key,
                1 => keyboard.digit2Key,
                2 => keyboard.digit3Key,
                _ => keyboard.digit4Key,
            };
        }

        /// <summary>Moves to a step and lists its cards, starting on the one that matches what is fitted now.</summary>
        private void ShowStep(Step step)
        {
            _step = step;

            _cards.Clear();

            if (step == Step.Station)
            {
                foreach (var station in StartingTruckChoice.Stations)
                {
                    var available = station == StartingStation.Station7 || (station == StartingStation.Station8 && _station8Ready) || (station == StartingStation.Station16 && _station16Ready);

                    _cards.Add(new MenuCard(
                        StartingTruckChoice.StationName(station).ToUpperInvariant(),
                        StartingTruckChoice.StationDescription(station),
                        available ? (StartingTruckChoice.StationOf(_current) == station ? "YOUR LAST STATION" : "") : "NOT SET UP IN THIS SCENE",
                        available));
                }

                _highlight = System.Array.IndexOf(StartingTruckChoice.Stations, StartingTruckChoice.StationOf(_current));

                return;
            }

            var trucks = StartingTruckChoice.TrucksAt(_station);

            foreach (var truck in trucks)
            {
                _cards.Add(new MenuCard(
                    StartingTruckChoice.Designation(truck),
                    StartingTruckChoice.DisplayName(truck) + ". " + StartingTruckChoice.Description(truck),
                    truck == _current ? "IN THE BAY NOW" : "",
                    true));
            }

            var index = System.Array.IndexOf(trucks, _current);

            _highlight = index >= 0 ? index : 0;
        }

        /// <summary>Acts on a card: a station opens its apparatus; an apparatus is chosen.</summary>
        private void Select(int index)
        {
            if (index < 0 || index >= _cards.Count || !_cards[index].Enabled)
            {
                return;
            }

            if (_step == Step.Station)
            {
                _station = StartingTruckChoice.Stations[index];

                ShowStep(Step.Apparatus);

                return;
            }

            Choose(StartingTruckChoice.TrucksAt(_station)[index]);
        }

        /// <summary>Resumes with the fitted apparatus, or stores the pick and reloads the scene to fit another.</summary>
        private void Choose(StartingTruck truck)
        {
            StartingTruckChoice.Decided = true;

            Time.timeScale = _previousTimeScale;

            StartingTruckChoice.Save(truck);

            if (truck == _current)
            {
                Destroy(gameObject);

                return;
            }

            Reload();
        }

        /// <summary>Reloads the active scene; in the editor a scene missing from the build settings is reloaded through play mode.</summary>
        private static void Reload()
        {
            var scene = SceneManager.GetActiveScene();

#if UNITY_EDITOR
            if (scene.buildIndex < 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));

                return;
            }
#endif

            SceneManager.LoadScene(scene.buildIndex);
        }

        /// <summary>Draws the dimmed backdrop, the heading for the step and one card per choice.</summary>
        private void OnGUI()
        {
            BuildStyles();

            var scale = Screen.height / DesignHeight;

            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var width = Screen.width / scale;

            var height = DesignHeight;

            Fill(new Rect(0f, 0f, width, height), Dim);

            var stationStep = _step == Step.Station;

            GUI.Label(new Rect(0f, 150f, width, 64f), stationStep ? "CHOOSE YOUR STATION" : "CHOOSE YOUR APPARATUS", _title);

            GUI.Label(new Rect(0f, 214f, width, 36f), stationStep ? "Houston Fire Department" : StartingTruckChoice.StationName(_station), _subtitle);

            var cardWidth = Mathf.Min(CardWidth, (width - 80f - ((_cards.Count - 1) * CardGap)) / _cards.Count);

            var total = (_cards.Count * cardWidth) + ((_cards.Count - 1) * CardGap);

            var left = (width - total) / 2f;

            var top = (height - CardHeight) / 2f;

            for (var i = 0; i < _cards.Count; i++)
            {
                DrawCard(new Rect(left + (i * (cardWidth + CardGap)), top, cardWidth, CardHeight), i);
            }

            var hint = stationStep
                ? "Press 1, 2 or 3, or use the arrow keys and Enter"
                : "Press a number, or use the arrow keys and Enter.   Backspace: change station";

            GUI.Label(new Rect(0f, top + CardHeight + 56f, width, 32f), hint, _hint);
        }

        /// <summary>One card: number, title, description and footer marker.</summary>
        private void DrawCard(Rect rect, int index)
        {
            var card = _cards[index];

            if (card.Enabled && _mouseMoved && Event.current.type == EventType.Repaint && rect.Contains(Event.current.mousePosition))
            {
                _highlight = index;
            }

            var lit = card.Enabled && _highlight == index;

            Fill(rect, !card.Enabled ? CardOff : lit ? CardLit : Card);

            if (lit)
            {
                Fill(new Rect(rect.x, rect.y, rect.width, 4f), Amber);
            }

            GUI.Label(new Rect(rect.x + 28f, rect.y + 26f, rect.width - 56f, 30f), (index + 1).ToString(), _tag);

            _name!.normal.textColor = !card.Enabled ? Faint : lit ? Amber : Text;

            GUI.Label(new Rect(rect.x + 28f, rect.y + 58f, rect.width - 56f, 54f), card.Title, _name);

            _body!.normal.textColor = card.Enabled ? Muted : Faint;

            GUI.Label(new Rect(rect.x + 28f, rect.y + 126f, rect.width - 56f, 130f), card.Description, _body);

            if (card.Footer.Length > 0)
            {
                GUI.Label(new Rect(rect.x + 28f, rect.y + CardHeight - 40f, rect.width - 56f, 26f), card.Footer, _tag);
            }

            if (card.Enabled && GUI.Button(rect, GUIContent.none, _invisible))
            {
                Select(index);
            }
        }

        /// <summary>Fills a rectangle with a flat colour.</summary>
        private void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;

            GUI.color = color;

            GUI.DrawTexture(rect, _pixel);

            GUI.color = previous;
        }

        /// <summary>Builds the one-pixel texture and text styles on first use (they need the GUI skin).</summary>
        private void BuildStyles()
        {
            if (_pixel != null)
            {
                return;
            }

            _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };

            _pixel.SetPixel(0, 0, Color.white);

            _pixel.Apply();

            _title = Style(44, FontStyle.Bold, Text, TextAnchor.MiddleCenter);

            _subtitle = Style(24, FontStyle.Normal, Muted, TextAnchor.MiddleCenter);

            _name = Style(38, FontStyle.Bold, Text, TextAnchor.MiddleLeft);

            _tag = Style(18, FontStyle.Bold, Amber, TextAnchor.MiddleLeft);

            _body = Style(21, FontStyle.Normal, Muted, TextAnchor.UpperLeft);

            _body.wordWrap = true;

            _hint = Style(20, FontStyle.Normal, Muted, TextAnchor.MiddleCenter);

            _invisible = new GUIStyle();
        }

        /// <summary>A label style with the given size, weight, colour and alignment.</summary>
        private static GUIStyle Style(int size, FontStyle weight, Color color, TextAnchor anchor)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = weight,
                alignment = anchor,
            };

            style.normal.textColor = color;

            return style;
        }
    }
}
