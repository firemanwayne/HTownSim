#nullable enable
using System.Text;
using HTownSim.Runtime.Incidents.Models;
using HTownSim.Runtime.Incidents.Sources;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The active-incidents board on the HUD: every live City incident released so far, newest first, with its type, place and
    /// units. Incidents the player was dispatched to are highlighted with the reason (YOUR UNIT or FIRST DUE); incidents outside the
    /// drivable map are dimmed. B shows or hides it. It only listens to the feed (UI depends on the feature, never the reverse).
    /// </summary>
    internal sealed class ActiveIncidentsHud : MonoBehaviour
    {
        /// <summary>Width (HUD units) of the active-incidents panel (top right). Raise it for wider lines (more text before it is shortened); lower it for a narrower panel.</summary>
        private const float PanelWidth = 560f;

        /// <summary>Height (HUD units) of each incident line. Raise it for more spacing between lines (a taller panel for the same number of lines); lower it for tighter lines.</summary>
        private const float LineHeight = 21f;

        /// <summary>How often (seconds) the list is rebuilt. Raise it to refresh less often (cheaper, but changes appear later); lower it to track the board closely.</summary>
        private const float RefreshSeconds = 2f;

        private const string Highlight = "#FFAF52";

        private const string Dim = "#8A9199";

        /// <summary>Longest line, in characters, before it is shortened with an ellipsis. Raise it to show more of each incident (wide lines); lower it to cut lines sooner.</summary>
        private const int MaxChars = 70;

        private LiveIncidentFeed _feed = null!;

        private GameObject _panel = null!;

        private TextMeshProUGUI _text = null!;

        private float _nextRefresh;

        private bool _dirty = true;

        /// <summary>Builds the panel on the HUD canvas and follows the feed.</summary>
        internal void Initialize(LiveIncidentFeed feed, Canvas canvas)
        {
            _feed = feed;

            var lines = feed.Settings.HudLines;

            _panel = new GameObject("ActiveIncidentsPanel", typeof(RectTransform), typeof(Image));

            var rect = (RectTransform)_panel.transform;

            rect.SetParent(canvas.transform, false);

            rect.anchorMin = new Vector2(1f, 1f);

            rect.anchorMax = new Vector2(1f, 1f);

            rect.pivot = new Vector2(1f, 1f);

            rect.anchoredPosition = new Vector2(-24f, -84f);

            rect.sizeDelta = new Vector2(PanelWidth, (lines + 2) * LineHeight + 20f);

            _panel.GetComponent<Image>().color = new Color(0.02f, 0.06f, 0.09f, 0.86f);

            var textObject = new GameObject("Lines", typeof(RectTransform));

            var textRect = (RectTransform)textObject.transform;

            textRect.SetParent(rect, false);

            textRect.anchorMin = Vector2.zero;

            textRect.anchorMax = Vector2.one;

            textRect.offsetMin = new Vector2(14f, 8f);

            textRect.offsetMax = new Vector2(-14f, -8f);

            _text = textObject.AddComponent<TextMeshProUGUI>();

            _text.fontSize = 15f;

            _text.richText = true;

            _text.textWrappingMode = TextWrappingModes.NoWrap;

            // Long lines are shortened one by one (see Fit); an ellipsis overflow mode would cut off every line after the first long one.
            _text.overflowMode = TextOverflowModes.Overflow;

            _panel.AddComponent<RectMask2D>();

            _text.color = new Color(0.9f, 0.85f, 0.72f);

            feed.Queue.Released += OnChanged;

            feed.Queue.Updated += OnChanged;

            feed.Queue.Closed += OnClosed;
        }

        private void OnChanged(LiveBoardEntry entry)
        {
            _dirty = true;
        }

        private void OnClosed(string id)
        {
            _dirty = true;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;

            if (keyboard != null && keyboard.bKey.wasPressedThisFrame)
            {
                _panel.SetActive(!_panel.activeSelf);
            }

            if (!_panel.activeSelf || (!_dirty && Time.unscaledTime < _nextRefresh))
            {
                return;
            }

            _dirty = false;

            _nextRefresh = Time.unscaledTime + RefreshSeconds;

            _text.text = Compose();
        }

        /// <summary>The panel text: a title line, then one line per incident.</summary>
        private string Compose()
        {
            var queue = _feed.Queue;

            var builder = new StringBuilder();

            builder.Append("<color=").Append(Highlight).Append("><b>ACTIVE INCIDENTS</b></color>   ")
                .Append(queue.Board.Count).Append(" on board, ").Append(queue.Waiting).Append(" waiting")
                .Append(_feed.IsLive ? "" : "   <color=" + Dim + ">(no feed: run HoustonIncidents)</color>")
                .Append("   <size=12>B HIDE</size>\n");

            var shown = 0;

            foreach (var entry in queue.Board)
            {
                if (shown++ >= _feed.Settings.HudLines)
                {
                    break;
                }

                var record = entry.Record;

                var time = record.CallTimeOpened.Length >= 5 ? record.CallTimeOpened.Substring(record.CallTimeOpened.Length - 5) : record.CallTimeOpened;

                var place = record.CrossStreet.Length > 0 ? record.Address + " / " + record.CrossStreet.Replace("BLK ", "") : record.Address;

                var units = record.Units.Length > 0 ? "  [" + string.Join(",", record.Units) + "]" : "";

                var line = Fit(time + "  " + record.IncidentType + "  " + place + units, entry.DispatchedToPlayer ? MaxChars - entry.DispatchReason.Length - 4 : MaxChars);

                if (entry.DispatchedToPlayer)
                {
                    builder.Append("<color=").Append(Highlight).Append("><b>> ").Append(entry.DispatchReason).Append(":</b> ").Append(line).Append("</color>\n");
                }
                else if (!entry.InMap)
                {
                    builder.Append("<color=").Append(Dim).Append(">").Append(line).Append("</color>\n");
                }
                else
                {
                    builder.Append(line).Append('\n');
                }
            }

            return builder.ToString();
        }

        /// <summary>A line shortened to <paramref name="max"/> characters with an ellipsis; rich-text brackets are removed from City text.</summary>
        private static string Fit(string line, int max)
        {
            line = line.Replace("<", "(").Replace(">", ")");

            return line.Length <= max ? line : line.Substring(0, Mathf.Max(0, max - 1)) + "\u2026";
        }

        private void OnDestroy()
        {
            if (_feed != null && _feed.Queue != null)
            {
                _feed.Queue.Released -= OnChanged;

                _feed.Queue.Updated -= OnChanged;

                _feed.Queue.Closed -= OnClosed;
            }
        }
    }
}
