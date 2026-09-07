using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// Shows a calendar in a frame.
    ///
    /// This class owns the frame: the visual tree, the refresh timer, and the plugin
    /// life cycle. It does not talk to Google. Signing in belongs to
    /// <see cref="AgendaSession"/>, and the events themselves arrive as
    /// <see cref="AgendaEvent"/>, so nothing here changes when the source does.
    /// </summary>
    public class GoogleAgendaPlugin : IFramePlugin
    {
        public string PluginId => "GoogleAgenda";
        public string DisplayName => "Agenda";

        // 3 while it is being built: the plugin list only offers it to somebody who
        // has raised the availability level on purpose.
        public int DevelopmentState => 3;

        // --- Visual tree -------------------------------------------------------
        private ScrollViewer? _rootVisual;
        private StackPanel? _contentPanel;

        // --- State -------------------------------------------------------------
        private readonly AgendaSession _session = new AgendaSession();
        private List<AgendaEvent> _events = new List<AgendaEvent>();
        private GoogleCalendarSource? _source;

        /// <summary>True once an answer has arrived, so an empty list can be told apart from a list nobody has asked for.</summary>
        private bool _loadedOnce;

        /// <summary>True when the last attempt failed. The events already on screen stay: stale is more use than blank.</summary>
        private bool _offline;

        /// <summary>Guards against a slow refresh being started again by the timer while it is still running.</summary>
        private bool _refreshing;

        /// <summary>
        /// How far ahead the frame looks. Two weeks is what fits the question a
        /// desktop agenda answers - what is coming - without turning the frame into
        /// something to scroll.
        /// </summary>
        private static readonly TimeSpan Window = TimeSpan.FromDays(14);

        // --- Settings and refresh ---------------------------------------------
        private Dictionary<string, object>? _settingsRef;
        private DispatcherTimer? _refreshTimer;

        /// <summary>
        /// How often the source is asked for changes. Not a compromise: Google pushes
        /// changes only to a public HTTPS address it can call, which a program on
        /// somebody's desktop does not have. Every desktop calendar client polls, and
        /// asks only for what changed since last time, so a quiet calendar costs an
        /// empty answer a minute.
        /// </summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

        public FrameworkElement CreateVisualElement()
        {
            // Same shape and spacing as the other plugins, so a frame holding this one
            // sits at the same distance from its edges as the rest.
            _rootVisual = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(10),
                Margin = new Thickness(5, 5, 5, 10)
            };

            _contentPanel = new StackPanel { Orientation = Orientation.Vertical };
            _rootVisual.Content = _contentPanel;

            return _rootVisual;
        }

        public void Initialize(FrameworkElement visual, Dictionary<string, object> settings)
        {
            _settingsRef = settings;

            _session.Changed += OnSessionChanged;
            Render();

            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = RefreshInterval
            };
            _refreshTimer.Tick += (s, e) => Refresh();

            // Not awaited: building a frame must not wait on a disk read, and the
            // session announces itself once it knows the answer.
            _ = _session.ResumeAsync();
        }

        /// <summary>
        /// Follows the session: opens a source when somebody signs in, closes it when
        /// they leave, and redraws either way.
        ///
        /// Separate from <see cref="Render"/> on purpose. Drawing must be something
        /// that can be called at any moment without consequences; opening a connection
        /// to Google is not, and hiding it inside a redraw is how a window resize ends
        /// up making network calls.
        /// </summary>
        private void OnSessionChanged()
        {
            if (_session.State == AgendaState.SignedIn && _session.Credential != null)
            {
                if (_source == null)
                {
                    _source = new GoogleCalendarSource(_session.Credential);
                    _refreshTimer?.Start();
                    Refresh();
                }
            }
            else
            {
                _refreshTimer?.Stop();

                _source?.Dispose();
                _source = null;

                _events = new List<AgendaEvent>();
                _loadedOnce = false;
                _offline = false;
            }

            Render();
        }

        /// <summary>
        /// Asks the source for what changed and redraws.
        ///
        /// A failure keeps whatever is already on screen and says so in one line: an
        /// agenda that empties itself because the network blinked is worse than one
        /// showing this morning's answer, and the next attempt is sixty seconds away.
        /// </summary>
        private async void Refresh()
        {
            GoogleCalendarSource? source = _source;
            if (source == null || _refreshing) return;

            _refreshing = true;

            try
            {
                IReadOnlyList<AgendaEvent> events =
                    await Task.Run(() => source.RefreshAsync(Window, CancellationToken.None))
                              .ConfigureAwait(true);

                // The source may have been closed while the answer was in flight - a
                // sign-out, or the frame going away - and applying it then would put
                // events under a session that no longer exists.
                if (_source != source) return;

                _events = events.ToList();
                _loadedOnce = true;
                _offline = false;
            }
            catch (Exception ex)
            {
                _offline = true;

                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: refresh failed: {ex.Message}");
            }
            finally
            {
                _refreshing = false;
            }

            Render();
        }

        // ==========================================================================
        // RENDERING
        // ==========================================================================

        /// <summary>
        /// Draws whatever the current state allows. One entry point, so a change of
        /// state can never leave half the frame behind.
        /// </summary>
        private void Render()
        {
            StackPanel? panel = _contentPanel;
            if (panel == null) return;

            panel.Children.Clear();

            switch (_session.State)
            {
                case AgendaState.NotConfigured:
                    panel.Children.Add(CreateMessageCard(Strings.AgendaNotConfigured));
                    break;

                case AgendaState.SignedOut:
                    panel.Children.Add(CreateMessageCard(Strings.AgendaSignedOut));
                    panel.Children.Add(CreateSignInButton());
                    break;

                case AgendaState.SigningIn:
                    panel.Children.Add(CreateMessageCard(Strings.AgendaSigningIn));
                    break;

                case AgendaState.Failed:
                    panel.Children.Add(CreateMessageCard(_session.LastError ?? Strings.AgendaFailed));
                    panel.Children.Add(CreateSignInButton());
                    break;

                case AgendaState.SignedIn:
                    // Above the list rather than instead of it: the events are still
                    // worth reading, they are simply not known to be current.
                    if (_offline) panel.Children.Add(CreateMessageCard(Strings.AgendaOffline));
                    RenderEvents(panel);
                    break;
            }
        }

        private void RenderEvents(StackPanel panel)
        {
            if (_events.Count == 0)
            {
                // An empty calendar and a calendar nobody has read yet look the same
                // from here, and telling somebody there is nothing scheduled before
                // having asked would be a guess dressed as an answer.
                panel.Children.Add(CreateMessageCard(
                    _loadedOnce ? Strings.AgendaNothingScheduled : Strings.AgendaLoading));
                return;
            }

            // Grouped by day, with the day written once above its entries. Repeating
            // the date on every line reads as noise on a frame this narrow.
            DateTime currentDay = DateTime.MinValue;

            foreach (AgendaEvent item in _events)
            {
                if (item.Day != currentDay)
                {
                    currentDay = item.Day;
                    panel.Children.Add(CreateDayHeader(currentDay));
                }

                panel.Children.Add(CreateEventCard(item));
            }
        }

        /// <summary>
        /// The only control in the frame that starts something. It is here as well as
        /// in the settings window because the frame is where somebody notices that the
        /// agenda is not showing anything.
        /// </summary>
        private Button CreateSignInButton()
        {
            var button = new Button
            {
                Content = Strings.AgendaSignIn,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            button.Click += (s, e) => _ = _session.SignInAsync();
            return button;
        }

        private TextBlock CreateDayHeader(DateTime day)
        {
            return new TextBlock
            {
                Text = FormatDayHeader(day),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                Margin = new Thickness(2, 10, 2, 4)
            };
        }

        /// <summary>
        /// "Today" and "Tomorrow" rather than a date, because those are the two days
        /// somebody glancing at a desktop frame is actually asking about. The words
        /// follow the program's language; the date follows the regional format, which
        /// Windows keeps separate on purpose.
        /// </summary>
        private static string FormatDayHeader(DateTime day)
        {
            DateTime today = DateTime.Today;

            if (day == today) return Strings.AgendaToday;
            if (day == today.AddDays(1)) return Strings.AgendaTomorrow;

            return day.ToString("ddd d MMM", System.Globalization.CultureInfo.CurrentCulture);
        }

        private Border CreateEventCard(AgendaEvent item)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var layout = new StackPanel { Orientation = Orientation.Horizontal };

            // The calendar's own colour, so entries from different calendars stay apart
            // without a second line of text explaining which is which.
            layout.Children.Add(new Border
            {
                Width = 4,
                CornerRadius = new CornerRadius(2),
                Background = ParseColourOrDefault(item.ColourHex),
                Margin = new Thickness(0, 0, 8, 0)
            });

            var texts = new StackPanel { Orientation = Orientation.Vertical };

            texts.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Colors.White)
            });

            texts.Children.Add(new TextBlock
            {
                Text = FormatWhen(item),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255))
            });

            layout.Children.Add(texts);
            card.Child = layout;

            return card;
        }

        private static string FormatWhen(AgendaEvent item)
        {
            if (item.IsAllDay) return Strings.AgendaAllDay;

            return item.Start.ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture)
                 + " - "
                 + item.End.ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture);
        }

        private static SolidColorBrush ParseColourOrDefault(string hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex))
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch (Exception)
            {
                // A colour the source invented is not worth a blank frame.
            }

            return new SolidColorBrush(Color.FromRgb(100, 150, 255));
        }

        /// <summary>The one card every state that is not a list of events shows.</summary>
        private Border CreateMessageCard(string message)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Child = new TextBlock
                {
                    Text = message,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255))
                }
            };
        }

        // ==========================================================================
        // LIFE CYCLE
        // ==========================================================================

        public void Pause()
        {
            // A hidden frame must not keep asking: the answers are thrown away and the
            // quota is not.
            _refreshTimer?.Stop();
        }

        public void Resume()
        {
            if (_session.State == AgendaState.SignedIn && _source != null)
            {
                _refreshTimer?.Start();
                Refresh();
            }
        }

        public void Cleanup()
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;

            _session.Changed -= OnSessionChanged;
            _session.Cancel();

            _source?.Dispose();
            _source = null;
        }

        public void ShowSettingsWindow(Window ownerWindow, dynamic frameData)
        {
            AgendaSettingsWindow.Show(ownerWindow, _session, _settingsRef);
        }
    }
}
