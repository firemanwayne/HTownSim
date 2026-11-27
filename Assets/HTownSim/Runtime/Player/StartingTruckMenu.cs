#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The start screen: pauses the game, asks which service to drive for (Houston Fire Department, Houston Police Department or a
    /// citizen car); for the fire department it then asks which station to start from, and finally which vehicle to drive. Picking the
    /// apparatus the scene already fits just resumes; picking another stores the choice and reloads the scene, where
    /// <see cref="ApparatusLoadout"/> fits it before anything else starts. Played from the small start scene (<see cref="StartScene"/>),
    /// which the editor and the build open first, the screen comes before the city: the pick is stored and the city is loaded once,
    /// behind a loading message, so no scene is ever loaded twice.
    /// </summary>
    internal sealed class StartingTruckMenu : MonoBehaviour
    {
        /// <summary>The three steps of the screen.</summary>
        private enum Step
        {
            Agency,
            Station,
            Apparatus,
        }

        /// <summary>The small scene that holds only the start screen, and the city scene it loads once a vehicle is picked.</summary>
        internal const string StartScene = "StartMenu";

        internal const string CityScene = "HoustonResponse";

        /// <summary>The command-line switch that skips the screen and starts with the last pick.</summary>
        private const string AutoStartArgument = "-autostart";

        /// <summary>Starts a city without a local player or apparatus selection.</summary>
        internal static bool CityServerRequested { get; } = System.Array.Exists(System.Environment.GetCommandLineArgs(),
            argument => string.Equals(argument, "-city-server", System.StringComparison.OrdinalIgnoreCase));

        /// <summary>True when the city runs as a spectator of the city server: the "-city-spectator" switch, or the Spectator card on the start screen.</summary>
        internal static bool CitySpectatorRequested { get; private set; } = !CityServerRequested && System.Array.Exists(System.Environment.GetCommandLineArgs(),
            argument => string.Equals(argument, "-city-spectator", System.StringComparison.OrdinalIgnoreCase));

        /// <summary>True when the player joins the city server's city with a unit of their own: the "-city-player" switch. The unit is picked on the start screen as usual.</summary>
        internal static bool CityPlayerRequested { get; private set; } = CityPlayerFromArguments;

        /// <summary>True when the "-city-player" switch was given; the Online card on the start screen sets <see cref="CityPlayerRequested"/> for one pick, and this is what it goes back to.</summary>
        private static bool CityPlayerFromArguments => !CityServerRequested && System.Array.Exists(System.Environment.GetCommandLineArgs(),
            argument => string.Equals(argument, "-city-player", System.StringComparison.OrdinalIgnoreCase));

        /// <summary>True while the start screen is on screen, so a spectator does not fly under it.</summary>
        internal static bool IsOpen { get; private set; }

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

        /// <summary>The frame the screen was opened on, so the key that opened it does not also close it.</summary>
        private int _openedFrame;

        /// <summary>True when the screen runs in the start scene, before the city is loaded.</summary>
        private bool _startMode;

        /// <summary>True when a spectator opened the screen (U): every unit can be picked, and U closes it to carry on spectating.</summary>
        private bool _fromSpectator;

        /// <summary>
        /// True from the pick until the scene is gone. The load is not started for a few frames, so the loading message is on screen first:
        /// the editor does much of a scene load on the main thread inside the call that starts it, and a frozen screen would never show it.
        /// </summary>
        private bool _loadRequested;

        private int _loadStartFrame;

        private System.Func<AsyncOperation?>? _startLoad;

        /// <summary>How many frames the loading message is drawn before the city load starts, so it is on screen when the editor stalls. Raise it to be sure the message appears; lower it to start loading sooner.</summary>
        private const int LoadDelayFrames = 4;

        /// <summary>How fast the dots after "Please wait" count up. Raise it for quicker dots; lower it for slower ones.</summary>
        private const float DotsPerSecond = 2f;

        private const int MaximumDots = 3;

        private Step _step = Step.Agency;

        /// <summary>The apparatus the scene is fitted with right now.</summary>
        private StartingTruck _current;

        /// <summary>The service whose vehicles are listed in the last step.</summary>
        private StartingAgency _agency;

        /// <summary>The station whose apparatus are listed in the last step (fire department only).</summary>
        private StartingStation _station;

        /// <summary>False when the scene has no parked HPD patrol unit, so HPD cannot be chosen yet.</summary>
        private bool _hpdReady;

        /// <summary>False when the scene has no parked citizen car, so the citizen car cannot be chosen yet.</summary>
        private bool _citizenReady;

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
            if (CityServerRequested)
            {
                if (SceneManager.GetActiveScene().name == StartScene)
                {
                    SceneManager.LoadSceneAsync(CityScene);
                }

                return;
            }

            if (CitySpectatorRequested)
            {
                if (SceneManager.GetActiveScene().name == StartScene)
                {
                    new GameObject(nameof(CityJoinMenu)).AddComponent<CityJoinMenu>();
                }

                return;
            }

            Debug.Log("StartingTruckMenu.Show: scene " + SceneManager.GetActiveScene().name + ", decided " + StartingTruckChoice.Decided);

            if (StartingTruckChoice.Decided)
            {
                return;
            }

            var response = FindAnyObjectByType<FireResponse>();

            if (response == null)
            {
                if (SceneManager.GetActiveScene().name == StartScene)
                {
                    ShowInStartScene();
                }

                return;
            }

            Create(response);
        }

        /// <summary>Opens the screen again while playing (the U key): picking the unit already driven resumes, picking another loads the city fitted with it.</summary>
        internal static void Reopen()
        {
            var response = FindAnyObjectByType<FireResponse>();

            if (response == null || FindAnyObjectByType<StartingTruckMenu>() != null)
            {
                return;
            }

            Create(response);
        }

        /// <summary>Builds the screen for the city scene: the unit fitted now is the current one, and what the scene holds is what can be picked.</summary>
        private static void Create(FireResponse response)
        {
            var menu = new GameObject(nameof(StartingTruckMenu)).AddComponent<StartingTruckMenu>();

            menu._openedFrame = Time.frameCount;

            menu._current = ApparatusLoadout.Detect(response.transform);

            menu._agency = StartingTruckChoice.AgencyOf(menu._current);

            menu._station = StartingTruckChoice.StationOf(menu._current);

            menu._hpdReady = ApparatusLoadout.HpdReady(response.gameObject.scene);

            menu._citizenReady = ApparatusLoadout.CitizenReady(response.gameObject.scene);

            menu._station8Ready = ApparatusLoadout.Station8Ready(response.gameObject.scene);

            menu._station16Ready = ApparatusLoadout.Station16Ready(response.gameObject.scene);

            menu.ShowStep(Step.Agency);
        }

        /// <summary>The start scene's screen: everything the city scene offers is taken as ready, and the last pick is the one shown as current.</summary>
        private static void ShowInStartScene()
        {
            var menu = CreateStartModeMenu();

            // "-autostart" on the command line takes the last pick without asking, for quick starts and for testing a build unattended.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), AutoStartArgument) >= 0)
            {
                menu.Choose(menu._current);
            }
        }

        /// <summary>Opens the screen from the spectator view (the U key): picking a unit leaves the spectator and loads the city with that unit.</summary>
        internal static void OpenFromSpectator()
        {
            if (IsOpen)
            {
                return;
            }

            var menu = CreateStartModeMenu();

            menu._fromSpectator = true;

            menu._openedFrame = Time.frameCount;
        }

        /// <summary>A screen that takes everything the city scene offers as ready and loads the city by name, as the start scene does.</summary>
        private static StartingTruckMenu CreateStartModeMenu()
        {
            var menu = new GameObject(nameof(StartingTruckMenu)).AddComponent<StartingTruckMenu>();

            menu._startMode = true;

            menu._current = StartingTruckChoice.Saved ?? StartingTruck.Engine;

            menu._agency = StartingTruckChoice.AgencyOf(menu._current);

            menu._station = StartingTruckChoice.StationOf(menu._current);

            menu._hpdReady = true;

            menu._citizenReady = true;

            menu._station8Ready = true;

            menu._station16Ready = true;

            menu.ShowStep(Step.Agency);

            return menu;
        }

        /// <summary>Freezes the game under the menu and frees the mouse.</summary>
        private void OnEnable()
        {
            IsOpen = true;

            _previousTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;

            Time.timeScale = 0f;

            Cursor.visible = true;

            Cursor.lockState = CursorLockMode.None;
        }

        /// <summary>Puts the time scale back if the menu is removed some other way.</summary>
        private void OnDestroy()
        {
            IsOpen = false;

            if (Time.timeScale == 0f)
            {
                Time.timeScale = _previousTimeScale;
            }
        }

        /// <summary>Keyboard: 1, 2, 3 pick directly; the arrows move; Enter or Space confirms; Backspace goes back to the stations.</summary>
        private void Update()
        {
            if (_loadRequested)
            {
                if (_startLoad != null && Time.frameCount >= _loadStartFrame)
                {
                    _startLoad();

                    _startLoad = null;
                }

                return;
            }

            _mouseMoved = Mouse.current is not null && Mouse.current.delta.ReadValue().sqrMagnitude > 0f;

            var keyboard = Keyboard.current;

            if (keyboard is null)
            {
                return;
            }

            // The key that opened the screen in the city also closes it, carrying on with the unit already driven.
            if ((!_startMode || _fromSpectator) && Time.frameCount > _openedFrame && keyboard.uKey.wasPressedThisFrame)
            {
                Destroy(gameObject);

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

            if (_step != Step.Agency && keyboard.backspaceKey.wasPressedThisFrame)
            {
                ShowStep(_step == Step.Apparatus && _agency == StartingAgency.Hfd ? Step.Station : Step.Agency);

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
                3 => keyboard.digit4Key,
                _ => keyboard.digit5Key,
            };
        }

        /// <summary>Moves to a step and lists its cards, starting on the one that matches what is fitted now.</summary>
        private void ShowStep(Step step)
        {
            _step = step;

            _cards.Clear();

            if (step == Step.Agency)
            {
                foreach (var agency in StartingTruckChoice.Agencies)
                {
                    var available = agency == StartingAgency.Hfd || (agency == StartingAgency.Hpd && _hpdReady) || (agency == StartingAgency.Citizen && _citizenReady);

                    _cards.Add(new MenuCard(
                        StartingTruckChoice.AgencyShortName(agency).ToUpperInvariant(),
                        StartingTruckChoice.AgencyDescription(agency),
                        available ? (StartingTruckChoice.AgencyOf(_current) == agency ? "YOUR LAST SERVICE" : "") : "NOT SET UP IN THIS SCENE",
                        available));
                }

                _cards.Add(new MenuCard(
                    "SPECTATOR",
                    "Fly a free camera over the city and watch Engine 07 live from the city server.",
                    "NEEDS A RUNNING CITY SERVER",
                    true));

                _cards.Add(new MenuCard(
                    "ONLINE",
                    "Take an engine, ladder or medic unit in the city server's city and answer its calls beside other players.",
                    "NEEDS A RUNNING CITY SERVER",
                    true));

                // Coming back to this step cancels an Online pick that was not finished.
                CityPlayerRequested = CityPlayerFromArguments;

                _highlight = System.Array.IndexOf(StartingTruckChoice.Agencies, StartingTruckChoice.AgencyOf(_current));

                return;
            }

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

            var trucks = ApparatusChoices();

            foreach (var truck in trucks)
            {
                _cards.Add(new MenuCard(
                    StartingTruckChoice.Designation(truck),
                    StartingTruckChoice.DisplayName(truck) + ". " + StartingTruckChoice.Description(truck),
                    truck == _current ? (_agency == StartingAgency.Hfd ? "IN THE BAY NOW" : "PARKED NOW") : "",
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

            if (_step == Step.Agency && index == StartingTruckChoice.Agencies.Length)
            {
                BeginSpectating();

                return;
            }

            if (_step == Step.Agency && index == StartingTruckChoice.Agencies.Length + 1)
            {
                // Online play is the fire department's: pick a station and a unit as usual, and the city that loads is the server's.
                _agency = StartingAgency.Hfd;

                ShowStep(Step.Station);

                CityPlayerRequested = true;

                return;
            }

            if (_step == Step.Agency)
            {
                _agency = StartingTruckChoice.Agencies[index];

                ShowStep(_agency == StartingAgency.Hfd ? Step.Station : Step.Apparatus);

                return;
            }

            if (_step == Step.Station)
            {
                _station = StartingTruckChoice.Stations[index];

                ShowStep(Step.Apparatus);

                return;
            }

            Choose(ApparatusChoices()[index]);
        }

        /// <summary>The vehicles listed in the last step: a station's apparatus for the fire department, else the service's own vehicles.</summary>
        private StartingTruck[] ApparatusChoices()
        {
            return _agency == StartingAgency.Hfd ? StartingTruckChoice.TrucksAt(_station) : StartingTruckChoice.VehiclesOf(_agency);
        }

        /// <summary>Resumes with the fitted apparatus, or stores the pick and reloads the scene to fit another.</summary>
        private void Choose(StartingTruck truck)
        {
            // Picking a unit always leaves the spectator view; a spectator who picks one takes it in the server's city.
            if (_fromSpectator)
            {
                CityPlayerRequested = true;
            }

            CitySpectatorRequested = false;

            Debug.Log("StartingTruckMenu.Choose: " + truck + (_startMode ? " (start scene)" : " (city)"));

            StartingTruckChoice.Select(truck);

            if (!_startMode && truck == _current)
            {
                Destroy(gameObject);

                return;
            }

            // Switching unit in the city reloads it: save the fleet, fires and incidents so the new scene carries on from here.
            if (!_startMode)
            {
                CityCarryOver.Capture();
            }

            // The game stays frozen under the loading message; the menu putting the time scale back as it is unloaded lets the new scene run.
            _loadRequested = true;

            _loadStartFrame = Time.frameCount + LoadDelayFrames;

            _startLoad = _startMode ? () => SceneManager.LoadSceneAsync(CityScene) : Reload;
        }

        /// <summary>Loads the city as a spectator of the city server, with a free camera and no unit of its own.</summary>
        private void BeginSpectating()
        {
            Debug.Log("StartingTruckMenu.BeginSpectating" + (_startMode ? " (start scene)" : " (city)"));

            CitySpectatorRequested = true;

            CityJoinMenu.StartTiming();

            _loadRequested = true;

            _loadStartFrame = Time.frameCount + LoadDelayFrames;

            _startLoad = _startMode ? () => SceneManager.LoadSceneAsync(CityScene) : Reload;
        }

        /// <summary>
        /// Reloads the active scene in the background, so the screen can show that it is loading; in the editor a scene missing from the
        /// build settings is reloaded through play mode instead (which has no progress to show).
        /// </summary>
        private static AsyncOperation? Reload()
        {
            var scene = SceneManager.GetActiveScene();

#if UNITY_EDITOR
            if (scene.buildIndex < 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));

                return null;
            }
#endif

            return SceneManager.LoadSceneAsync(scene.buildIndex);
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

            if (_loadRequested)
            {
                GUI.Label(new Rect(0f, (height / 2f) - 40f, width, 64f), "LOADING THE CITY", _title);

                var dots = new string('.', 1 + (Mathf.FloorToInt(Time.unscaledTime * DotsPerSecond) % MaximumDots));

                GUI.Label(new Rect(0f, (height / 2f) + 30f, width, 36f), "Please wait" + dots, _subtitle);

                return;
            }

            var title = _step switch
            {
                Step.Agency => "CHOOSE YOUR SERVICE",
                Step.Station => "CHOOSE YOUR STATION",
                _ => _agency == StartingAgency.Hfd ? "CHOOSE YOUR APPARATUS" : "CHOOSE YOUR VEHICLE",
            };

            var subtitle = _step switch
            {
                Step.Agency => "HTown emergency response",
                Step.Station => "Houston Fire Department",
                _ => _agency == StartingAgency.Hfd ? StartingTruckChoice.StationName(_station) : StartingTruckChoice.AgencyName(_agency),
            };

            GUI.Label(new Rect(0f, 150f, width, 64f), title, _title);

            GUI.Label(new Rect(0f, 214f, width, 36f), subtitle, _subtitle);

            var cardWidth = Mathf.Min(CardWidth, (width - 80f - ((_cards.Count - 1) * CardGap)) / _cards.Count);

            var total = (_cards.Count * cardWidth) + ((_cards.Count - 1) * CardGap);

            var left = (width - total) / 2f;

            var top = (height - CardHeight) / 2f;

            for (var i = 0; i < _cards.Count; i++)
            {
                DrawCard(new Rect(left + (i * (cardWidth + CardGap)), top, cardWidth, CardHeight), i);
            }

            var hint = _step != Step.Apparatus
                ? (_step == Step.Agency ? "Press 1 to 5, or use the arrow keys and Enter" : "Press 1, 2 or 3, or use the arrow keys and Enter") + (_step == Step.Station ? ".   Backspace: change service" : string.Empty) + (_fromSpectator ? ".   U: back to spectating" : string.Empty)
                : "Press a number, or use the arrow keys and Enter.   Backspace: " + (_agency == StartingAgency.Hfd ? "change station" : "change service");

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
