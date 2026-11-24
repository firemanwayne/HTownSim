#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The start screen: pauses the game and lets the player pick Engine 07, Ladder 07 or Ambulance 07. Picking the apparatus
    /// the scene already fits just resumes; picking another stores the choice and reloads the scene, where
    /// <see cref="ApparatusLoadout"/> fits it before anything else starts.
    /// </summary>
    internal sealed class StartingTruckMenu : MonoBehaviour
    {
        /// <summary>The layout is designed for a 1080 pixel high screen and scaled from there.</summary>
        private const float DesignHeight = 1080f;

        private const float CardWidth = 420f;

        private const float CardHeight = 300f;

        private const float CardGap = 36f;

        /// <summary>The apparatus in the order the cards and the 1, 2, 3 keys list them.</summary>
        private static readonly StartingTruck[] Trucks = { StartingTruck.Engine, StartingTruck.Ladder, StartingTruck.Ambulance };

        private static readonly Color Dim = new Color(0.02f, 0.04f, 0.08f, 0.88f);

        private static readonly Color Card = new Color(0.07f, 0.11f, 0.19f, 0.96f);

        private static readonly Color CardLit = new Color(0.1f, 0.16f, 0.27f, 0.98f);

        private static readonly Color Amber = new Color(1f, 0.69f, 0.16f, 1f);

        private static readonly Color Text = new Color(0.9f, 0.93f, 0.97f, 1f);

        private static readonly Color Muted = new Color(0.62f, 0.69f, 0.8f, 1f);

        /// <summary>The apparatus the scene is fitted with right now.</summary>
        private StartingTruck _current;

        /// <summary>The card the player is on (mouse hover, arrow keys or number keys).</summary>
        private StartingTruck _highlight;

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

            menu._highlight = menu._current;
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

        /// <summary>Keyboard choice: 1, 2 and 3 pick directly; the arrows move; Enter or Space confirms.</summary>
        private void Update()
        {
            _mouseMoved = Mouse.current is not null && Mouse.current.delta.ReadValue().sqrMagnitude > 0f;

            var keyboard = Keyboard.current;

            if (keyboard is null)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                Choose(Trucks[0]);

                return;
            }

            if (keyboard.digit2Key.wasPressedThisFrame)
            {
                Choose(Trucks[1]);

                return;
            }

            if (keyboard.digit3Key.wasPressedThisFrame)
            {
                Choose(Trucks[2]);

                return;
            }

            var index = System.Array.IndexOf(Trucks, _highlight);

            if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
            {
                _highlight = Trucks[(index + 1) % Trucks.Length];
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
            {
                _highlight = Trucks[(index + Trucks.Length - 1) % Trucks.Length];
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
            {
                Choose(_highlight);
            }
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

        /// <summary>Draws the dimmed backdrop and one card per apparatus.</summary>
        private void OnGUI()
        {
            BuildStyles();

            var scale = Screen.height / DesignHeight;

            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var width = Screen.width / scale;

            var height = DesignHeight;

            Fill(new Rect(0f, 0f, width, height), Dim);

            GUI.Label(new Rect(0f, 150f, width, 64f), "CHOOSE YOUR APPARATUS", _title);

            GUI.Label(new Rect(0f, 214f, width, 36f), "Station 7", _subtitle);

            var total = (Trucks.Length * CardWidth) + ((Trucks.Length - 1) * CardGap);

            var left = (width - total) / 2f;

            var top = (height - CardHeight) / 2f;

            for (var i = 0; i < Trucks.Length; i++)
            {
                DrawCard(new Rect(left + (i * (CardWidth + CardGap)), top, CardWidth, CardHeight), i);
            }

            GUI.Label(new Rect(0f, top + CardHeight + 56f, width, 32f), "Press 1, 2 or 3, or use the arrow keys and Enter", _hint);
        }

        /// <summary>One apparatus card: number, name, description, and a marker on the one already fitted.</summary>
        private void DrawCard(Rect rect, int index)
        {
            var truck = Trucks[index];

            if (_mouseMoved && Event.current.type == EventType.Repaint && rect.Contains(Event.current.mousePosition))
            {
                _highlight = truck;
            }

            var lit = _highlight == truck;

            Fill(rect, lit ? CardLit : Card);

            if (lit)
            {
                Fill(new Rect(rect.x, rect.y, rect.width, 4f), Amber);
            }

            GUI.Label(new Rect(rect.x + 28f, rect.y + 26f, rect.width - 56f, 30f), (index + 1).ToString(), _tag);

            _name!.normal.textColor = lit ? Amber : Text;

            GUI.Label(new Rect(rect.x + 28f, rect.y + 58f, rect.width - 56f, 54f), StartingTruckChoice.DisplayName(truck).ToUpperInvariant(), _name);

            GUI.Label(new Rect(rect.x + 28f, rect.y + 126f, rect.width - 56f, 130f), StartingTruckChoice.Description(truck), _body);

            if (_current == truck)
            {
                GUI.Label(new Rect(rect.x + 28f, rect.y + CardHeight - 40f, rect.width - 56f, 26f), "IN THE BAY NOW", _tag);
            }

            if (GUI.Button(rect, GUIContent.none, _invisible))
            {
                Choose(truck);
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
