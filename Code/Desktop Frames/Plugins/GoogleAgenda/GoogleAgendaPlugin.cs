using Desktop_Frames.Localization;
using Google.Apis.Auth.OAuth2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// Shows a calendar in a frame.
    ///
    /// This class owns the frame and decides what happens: the life cycle, the
    /// refresh timer, and the commands somebody triggers. It does not draw - that is
    /// <see cref="AgendaRenderer"/> - and it does not talk to Google - that is
    /// <see cref="GoogleCalendarSource"/>. What passes between them is
    /// <see cref="AgendaEvent"/>, so neither has to know about the other.
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
        private readonly AgendaRenderer _renderer = new AgendaRenderer();

        private IReadOnlyList<AgendaEvent> _events = new List<AgendaEvent>();
        private GoogleCalendarSource? _source;

        private AgendaSettings _settings = new AgendaSettings();
        private Dictionary<string, object>? _settingsRef;

        /// <summary>
        /// The day the view is built around: the one shown, the first of the three, the
        /// week it belongs to, the month it falls in. Moved by the arrows, and reset to
        /// today by the dot between them.
        /// </summary>
        private DateTime _anchor = DateTime.Today;

        /// <summary>Set for one redraw when somebody asks to be taken back to today.</summary>
        private bool _flashToday;

        /// <summary>True once an answer has arrived, so an empty list can be told apart from one nobody has asked for.</summary>
        private bool _loadedOnce;

        /// <summary>True when the last attempt failed. What is on screen stays: stale is more use than blank.</summary>
        private bool _offline;

        /// <summary>Guards against the timer starting a refresh that is already running.</summary>
        private bool _refreshing;

        private DispatcherTimer? _refreshTimer;

        /// <summary>
        /// How often the source is asked for changes. Not a compromise: Google pushes
        /// changes only to a public HTTPS address it can call, which a program on
        /// somebody's desktop does not have. Every desktop calendar client polls, and
        /// asks only for what changed, so a quiet calendar costs an empty answer.
        /// </summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

        // ==========================================================================
        // LIFE CYCLE
        // ==========================================================================

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

            _contentPanel = new StackPanel();
            _rootVisual.Content = _contentPanel;

            return _rootVisual;
        }

        public void Initialize(FrameworkElement visual, Dictionary<string, object> settings)
        {
            _settingsRef = settings;
            _settings = AgendaSettings.Read(settings);

            _renderer.OpenRequested += OpenInGoogle;
            _renderer.EditRequested += Edit;
            _renderer.DeleteRequested += Delete;
            _renderer.DayChosen += day => { _anchor = day; Render(); };

            _session.Changed += OnSessionChanged;
            Render();

            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshInterval };
            _refreshTimer.Tick += (s, e) => Refresh();

            // Not awaited: building a frame must not wait on a disk read, and the
            // session announces itself once it knows the answer.
            _ = _session.ResumeAsync();
        }

        public void Pause()
        {
            // A hidden frame must not keep asking: the answers are thrown away and the
            // quota is not.
            _refreshTimer?.Stop();
        }

        public void Resume()
        {
            if (_source == null) return;

            _refreshTimer?.Start();
            Refresh();
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
            AgendaSettingsWindow.Show(ownerWindow, _session, _settings,
                                      _source?.Calendars ?? new List<AgendaCalendar>(),
                                      OnSettingsSaved);
        }

        // ==========================================================================
        // REACTING
        // ==========================================================================

        /// <summary>
        /// Follows the session: opens a source when somebody signs in, closes it when
        /// they leave, and redraws either way.
        ///
        /// Separate from <see cref="Render"/> on purpose. Drawing must be safe to call
        /// at any moment; opening a connection to Google is not, and hiding that inside
        /// a redraw is how resizing a window ends up making network calls.
        /// </summary>
        private void OnSessionChanged()
        {
            if (_session.State == AgendaState.SignedIn && _session.Credential != null)
            {
                if (_source == null) StartSource(_session.Credential);
            }
            else
            {
                DropSource();
            }

            Render();
        }

        private void OnSettingsSaved(AgendaSettings saved)
        {
            (DateTime wasFrom, DateTime wasTo) = _settings.Range(_anchor);
            (DateTime nowFrom, DateTime nowTo) = saved.Range(_anchor);

            bool windowGrew = nowFrom < wasFrom || nowTo > wasTo;
            bool calendarsChanged = !saved.SameCalendarsAs(_settings);

            _settings = saved;
            _settings.WriteTo(_settingsRef);

            // The sync marker Google gave us covers the window of the request that
            // produced it. Asking for a wider one, or for a calendar we had not been
            // reading, cannot be answered from that marker - so the source starts afresh
            // rather than being left to report events it was never told about.
            if (windowGrew || calendarsChanged)
            {
                UserCredential? credential = _session.Credential;

                DropSource();
                if (credential != null) StartSource(credential);
            }
            else
            {
                Refresh();
            }

            Render();
        }

        private void StartSource(UserCredential credential)
        {
            _source = new GoogleCalendarSource(credential);
            _refreshTimer?.Start();
            Refresh();
        }

        private void DropSource()
        {
            _refreshTimer?.Stop();

            _source?.Dispose();
            _source = null;

            _events = new List<AgendaEvent>();
            _loadedOnce = false;
            _offline = false;
        }

        // ==========================================================================
        // TALKING TO THE SOURCE
        // ==========================================================================

        /// <summary>
        /// Asks the source for what changed and redraws.
        ///
        /// A failure keeps whatever is on screen and says so in one line: an agenda
        /// that empties itself because the network blinked is worse than one showing
        /// this morning's answer, and the next attempt is a minute away.
        /// </summary>
        private async void Refresh()
        {
            GoogleCalendarSource? source = _source;
            if (source == null || _refreshing) return;

            _refreshing = true;

            try
            {
                (DateTime from, DateTime to) = _settings.Range(_anchor);

                IReadOnlyList<AgendaEvent> events = await Task.Run(
                    () => source.RefreshAsync(from, to, _settings.Calendars, CancellationToken.None))
                    .ConfigureAwait(true);

                // The source may have been replaced while the answer was in flight - a
                // sign-out, a settings change - and applying it then would show events
                // belonging to a session that no longer exists.
                if (_source != source) return;

                _events = events;
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

        /// <summary>
        /// Hands the event to the browser.
        ///
        /// The address is checked before it is used. It arrives in a network answer,
        /// and UseShellExecute hands whatever it is given to Windows, which will
        /// happily open a file, a settings page or anything else with a registered
        /// protocol. Google has no reason to send such a thing, and that is exactly
        /// why the check costs nothing: it is here for the day something upstream is
        /// not what it is expected to be.
        /// </summary>
        private void OpenInGoogle(AgendaEvent item)
        {
            if (string.IsNullOrWhiteSpace(item.WebLink)) return;

            if (!Uri.TryCreate(item.WebLink, UriKind.Absolute, out Uri? address)
                || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    "GoogleAgenda: refused to open an event link that is not a web address.");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: could not open the event in a browser: {ex.Message}");
            }
        }

        private void Add()
        {
            GoogleCalendarSource? source = _source;
            if (source == null) return;

            DateTime start = _anchor.Date;

            // The next whole hour today, or mid-morning on another day: what somebody
            // adding an event from a desktop frame nearly always means.
            start = start.Date == DateTime.Today
                ? DateTime.Now.Date.AddHours(DateTime.Now.Hour + 1)
                : start.AddHours(9);

            var draft = new AgendaEvent { Start = start, End = start.AddHours(1), CanWrite = true };

            AgendaEvent? filled = AgendaEventWindow.Show(Window.GetWindow(_rootVisual), draft,
                                                        source.Calendars, isNew: true);
            if (filled != null) Save(filled, isNew: true);
        }

        private void Edit(AgendaEvent item)
        {
            GoogleCalendarSource? source = _source;
            if (source == null) return;

            AgendaEvent? changed = AgendaEventWindow.Show(Window.GetWindow(_rootVisual), item,
                                                         source.Calendars, isNew: false);
            if (changed != null) Save(changed, isNew: false);
        }

        private async void Save(AgendaEvent item, bool isNew)
        {
            GoogleCalendarSource? source = _source;
            if (source == null) return;

            try
            {
                if (isNew) await source.CreateAsync(item, CancellationToken.None).ConfigureAwait(true);
                else await source.UpdateAsync(item, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.PreconditionFailed
                                                    || ex.HttpStatusCode == HttpStatusCode.Conflict)
            {
                // Somebody changed the same event elsewhere between reading it and
                // saving. Google refused instead of overwriting, and saying so is the
                // whole reason for asking it to: a change that vanishes without a word
                // is the worst outcome available here.
                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.AgendaConflict, Strings.AgendaEditEvent);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: saving an event failed: {ex.Message}");

                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.AgendaSaveFailed, Strings.AgendaEditEvent);
            }

            Refresh();
        }

        private async void Delete(AgendaEvent item)
        {
            GoogleCalendarSource? source = _source;
            if (source == null) return;

            if (!MessageBoxesManager.ShowCustomYesNoMessageBox(
                    Strings.Get("AgendaConfirmDelete", item.Title), Strings.AgendaDeleteEvent))
                return;

            try
            {
                await source.DeleteAsync(item, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: deleting an event failed: {ex.Message}");

                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.AgendaSaveFailed, Strings.AgendaDeleteEvent);
            }

            Refresh();
        }

        // ==========================================================================
        // DRAWING
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
                    panel.Children.Add(AgendaRenderer.Message(Strings.AgendaNotConfigured));
                    return;

                case AgendaState.SigningIn:
                    panel.Children.Add(AgendaRenderer.Message(Strings.AgendaSigningIn));
                    return;

                case AgendaState.SignedOut:
                    panel.Children.Add(AgendaRenderer.Message(Strings.AgendaSignedOut));
                    panel.Children.Add(SignInButton());
                    return;

                case AgendaState.Failed:
                    panel.Children.Add(AgendaRenderer.Message(_session.LastError ?? Strings.AgendaFailed));
                    panel.Children.Add(SignInButton());
                    return;
            }

            // Above the list rather than instead of it: the events are still worth
            // reading, they are simply not known to be current.
            if (_offline) panel.Children.Add(AgendaRenderer.Message(Strings.AgendaOffline));

            if (!_loadedOnce)
            {
                // An empty calendar and one nobody has read yet look the same from here,
                // and saying "nothing scheduled" before asking is a guess dressed as an
                // answer.
                panel.Children.Add(AgendaRenderer.Message(Strings.AgendaLoading));
                return;
            }

            panel.Children.Add(Toolbar());
            _renderer.Draw(panel, _events, _settings.View, _anchor, _flashToday);

            // One redraw only: leaving it set would blink again every time the timer
            // brings new events in, which is a light going off in the corner of the eye
            // once a minute for no reason.
            _flashToday = false;
        }

        /// <summary>The one row of controls: move through time, and add an event.</summary>
        private UIElement Toolbar()
        {
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };

            // Every view except the list, and each moves by what it is made of: a day, a
            // group of three, a week, a month. The list answers "what is coming", which
            // is a question about now, so moving it backwards would make it a different
            // view rather than the same one elsewhere.
            if (_settings.CanNavigate)
            {
                bar.Children.Add(Small("‹", () => Move(-1)));
                bar.Children.Add(Small("›", () => Move(1)));
                bar.Children.Add(Small("•", ToToday, Strings.AgendaToday));
            }

            bar.Children.Add(Small("+", Add, Strings.AgendaNewEvent));
            return bar;
        }

        private void Move(int direction)
        {
            _anchor = _settings.Step(_anchor, direction);

            // Redrawn at once from what is already held, so the arrows answer
            // immediately; the request that fills in a range never asked for follows.
            Render();
            Refresh();
        }

        private void ToToday()
        {
            _anchor = DateTime.Today;
            _flashToday = true;

            Render();
            Refresh();
        }

        private Button Small(string caption, Action action, string? tooltip = null)
        {
            var button = new Button
            {
                Content = caption,
                Width = 24,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(0),
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = tooltip
            };

            button.Click += (s, e) => action();
            return button;
        }

        /// <summary>
        /// Offered in the frame as well as in the settings window: the frame is where
        /// somebody notices the agenda is showing nothing, and sending them hunting
        /// through a menu from there is a small cruelty.
        /// </summary>
        private Button SignInButton()
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
    }
}
