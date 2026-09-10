using Desktop_Frames.Localization;
using Google.Apis.Auth.OAuth2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private readonly AgendaSession _session = AgendaSession.ForCurrentProfile();
        private readonly AgendaRenderer _renderer = new AgendaRenderer();

        /// <summary>
        /// The services, the entries and the writing. Everything this frame knows and
        /// nothing about how it looks - which is the line this class kept crossing
        /// before it was drawn.
        /// </summary>
        private readonly AgendaData _data = new AgendaData();

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

            // Applied to this frame rather than to the program: these suit a frame, and
            // deciding how every button in the application looks is not a plugin's
            // business.
            _rootVisual.Resources.MergedDictionaries.Add(AgendaStyles.Resources);

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
            _renderer.DoneChanged += SetDone;

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
            if (!_data.IsOpen) return;

            _refreshTimer?.Start();
            Refresh();
        }

        public void Cleanup()
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;

            _session.Changed -= OnSessionChanged;

            // Deliberately not cancelled: the session belongs to the profile now, not to
            // this frame. Closing one frame while a consent page is open would otherwise
            // revoke a sign-in the person is in the middle of granting for all of them.

            _data.Dispose();
        }

        public void ShowSettingsWindow(Window ownerWindow, dynamic frameData)
        {
            // frameData carried into the callback because that, not the dictionary
            // handed to Initialize, is what the host writes to disk - see Persist.
            AgendaSettingsWindow.Show(ownerWindow, _session, _settings,
                                      _data.Calendars,
                                      saved => OnSettingsSaved(saved, frameData));
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
                if (!_data.IsOpen) Open(_session.Credential);
            }
            else
            {
                _refreshTimer?.Stop();
                _data.Follow(null);
            }

            Render();
        }

        private void OnSettingsSaved(AgendaSettings saved, dynamic frameData)
        {
            (DateTime wasFrom, DateTime wasTo) = _settings.Range(_anchor);
            (DateTime nowFrom, DateTime nowTo) = saved.Range(_anchor);

            bool windowGrew = nowFrom < wasFrom || nowTo > wasTo;
            bool calendarsChanged = !saved.SameCalendarsAs(_settings);

            _settings = saved;

            // Twice, because they are two different things: the dictionary keeps this
            // running instance in step, and the frame is what survives a restart.
            _settings.WriteTo(_settingsRef);
            Persist(frameData);

            // The sync marker Google gave us covers the window of the request that
            // produced it. Asking for a wider one, or for a calendar we had not been
            // reading, cannot be answered from that marker - so the source starts afresh
            // rather than being left to report events it was never told about.
            if (windowGrew || calendarsChanged)
            {
                UserCredential? credential = _session.Credential;

                _refreshTimer?.Stop();
                _data.Follow(null);

                if (credential != null) Open(credential);
            }
            else
            {
                Refresh();
            }

            Render();
        }

        /// <summary>
        /// Writes the settings where the host will read them back.
        ///
        /// The dictionary handed to Initialize is a copy the host makes from the
        /// frame and never looks at again: writing to it keeps this instance
        /// consistent and is forgotten the moment the program closes. What lasts is
        /// PluginSettings on the frame itself, saved with the rest of frames.json -
        /// the same route the other plugins take.
        /// </summary>
        private void Persist(dynamic frameData)
        {
            try
            {
                var stored = new Dictionary<string, object>();
                _settings.WriteTo(stored);

                if (frameData is Newtonsoft.Json.Linq.JObject asJson)
                    asJson["PluginSettings"] = Newtonsoft.Json.Linq.JObject.FromObject(stored);
                else
                    ((IDictionary<string, object>)frameData)["PluginSettings"] = stored;

                FrameDataManager.SaveFrameData();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Settings,
                    $"GoogleAgenda: could not save the frame settings: {ex.Message}");
            }
        }

        /// <summary>Points the data at a granted session and starts asking.</summary>
        private void Open(UserCredential credential)
        {
            _data.Follow(credential);
            _refreshTimer?.Start();
            Refresh();
        }

        // ==========================================================================
        // TALKING TO THE SOURCE
        // ==========================================================================

        /// <summary>
        /// Asks for the range this view covers, then redraws whatever came back.
        ///
        /// The asking does not throw: a failure keeps what is on screen and shows a
        /// line saying so, because an agenda that empties itself when the network
        /// blinks is worse than one showing this morning's answer.
        /// </summary>
        private async void Refresh()
        {
            (DateTime from, DateTime to) = _settings.Range(_anchor);

            await _data.RefreshAsync(from, to, _settings.Calendars).ConfigureAwait(true);

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
        /// <summary>
        /// Ticks a task, or unticks it.
        ///
        /// The list is redrawn from what Google confirms rather than from the click, so
        /// a refusal leaves the box where it was instead of showing a tick that exists
        /// only on this screen.
        /// </summary>
        private async void SetDone(AgendaEvent item, bool done)
        {
            try
            {
                await _data.SetDoneAsync(item, done).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: could not change the task: {ex.Message}");

                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.AgendaSaveFailed, Strings.AgendaDeleteEvent);
            }

            Refresh();
        }

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
            if (!_data.IsOpen) return;

            DateTime start = _anchor.Date;

            // The next whole hour today, or mid-morning on another day: what somebody
            // adding an event from a desktop frame nearly always means.
            start = start.Date == DateTime.Today
                ? DateTime.Now.Date.AddHours(DateTime.Now.Hour + 1)
                : start.AddHours(9);

            var draft = new AgendaEvent { Start = start, End = start.AddHours(1), CanWrite = true };

            AgendaEvent? filled = AgendaEventWindow.Show(Window.GetWindow(_rootVisual), draft,
                                                        _data.Calendars, _data.TaskLists, isNew: true);
            if (filled != null) Save(filled, isNew: true);
        }

        private void Edit(AgendaEvent item)
        {
            if (!_data.IsOpen) return;

            AgendaEvent? changed = AgendaEventWindow.Show(Window.GetWindow(_rootVisual), item,
                                                         _data.Calendars, _data.TaskLists, isNew: false);
            if (changed != null) Save(changed, isNew: false);
        }

        private async void Save(AgendaEvent item, bool isNew)
        {
            try
            {
                await _data.SaveAsync(item, isNew).ConfigureAwait(true);
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
            if (!MessageBoxesManager.ShowCustomYesNoMessageBox(
                    Strings.Get("AgendaConfirmDelete", item.Title), Strings.AgendaDeleteEvent))
                return;

            try
            {
                await _data.DeleteAsync(item).ConfigureAwait(true);
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
                    panel.Children.Add(AgendaRenderer.Message(Strings.AgendaNeedsClient));
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
            if (_data.Offline) panel.Children.Add(AgendaRenderer.Message(Strings.AgendaOffline));

            if (!_data.LoadedOnce)
            {
                // An empty calendar and one nobody has read yet look the same from here,
                // and saying "nothing scheduled" before asking is a guess dressed as an
                // answer.
                panel.Children.Add(AgendaRenderer.Message(Strings.AgendaLoading));
                return;
            }

            panel.Children.Add(Toolbar());
            _renderer.Draw(panel, _data.Events, _settings.View, _anchor, _flashToday);

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
                bar.Children.Add(Small(Glyph.Back, () => Move(-1)));
                bar.Children.Add(Small(Glyph.Forward, () => Move(1)));
                bar.Children.Add(Small(Glyph.Today, ToToday, Strings.AgendaToday));
            }

            bar.Children.Add(Small(Glyph.Add, Add, Strings.AgendaNewEvent));
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

        /// <summary>
        /// The characters on the small buttons.
        ///
        /// Taken from the icon font Windows ships with rather than from punctuation. A
        /// guillemet is a quotation mark being asked to act as an arrow: it sits on the
        /// text baseline, it is drawn at text weight, and it is smaller than the button
        /// around it. These are drawn as icons, on the centre line, at the size the
        /// button was made for.
        /// </summary>
        private static class Glyph
        {
            public const string Back = "";      // ChevronLeft
            public const string Forward = "";   // ChevronRight
            public const string Today = "";     // GoToToday
            public const string Add = "";       // Add
        }

        private Button Small(string caption, Action action, string? tooltip = null)
        {
            var button = new Button
            {
                Content = caption,
                Width = 28,
                Height = 24,
                Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(0),

                // Segoe Fluent Icons on Windows 11, the older Segoe MDL2 Assets behind
                // it: the glyphs used here carry the same code points in both.
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 13,

                // An icon font has one weight. Asking for a bolder one makes Windows
                // thicken it artificially, which on a chevron reads as a smudge.
                FontWeight = FontWeights.Normal,

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
