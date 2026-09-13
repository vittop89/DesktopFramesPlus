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

        /// <summary>
        /// Below the toolbar, for a grid of hours: filled only by the views that are
        /// one, and given whatever height the frame has left.
        /// </summary>
        private Border? _gridHost;

        // --- State -------------------------------------------------------------
        private readonly AgendaSession _session = AgendaSession.ForCurrentProfile();
        private readonly AgendaRenderer _renderer = new AgendaRenderer();

        /// <summary>The list colours and kept hours, shared by every agenda frame of the profile.</summary>
        private readonly AgendaPreferences _preferences = AgendaPreferences.ForCurrentProfile();

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

        /// <summary>
        /// What a grid of hours keeps across being rebuilt: where it was scrolled to,
        /// so a refresh does not throw somebody back to the morning, and whether an
        /// entry is being dragged. The scroll place is kept across the arrows, as a
        /// paper diary stays open at the same hour when the page is turned, and
        /// forgotten when the view changes or somebody asks for today - both of which
        /// mean starting from now.
        /// </summary>
        private readonly AgendaGridState _gridState = new AgendaGridState();

        /// <summary>
        /// True when a redraw was asked for while an entry was being dragged, and is
        /// owed once the drag ends.
        /// </summary>
        private bool _renderHeld;

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
            DockPanel.SetDock(_contentPanel, Dock.Top);

            _gridHost = new Border();

            var layout = new DockPanel { LastChildFill = true };
            layout.Children.Add(_contentPanel);
            layout.Children.Add(_gridHost);

            _rootVisual.Content = layout;

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
            _renderer.CreateRequested += AddAt;
            _renderer.RescheduleRequested += Reschedule;

            _gridState.DragEnded += () =>
            {
                if (!_renderHeld) return;

                _renderHeld = false;
                Render();
            };

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
                                      _data.Calendars, _data.TaskLists, _preferences,
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

            // A different view is a different grid, and it opens on now.
            if (saved.View != _settings.View) _gridState.Offset = null;

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

            // The list answers "what is left to do", so it is the one view that also
            // needs the tasks that slipped past their day - its range starts today.
            await _data.RefreshAsync(from, to, _settings.Calendars,
                                     withOverdueTasks: _settings.View == AgendaView.List,
                                     _preferences).ConfigureAwait(true);

            Render();
        }

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
            DateTime start = _anchor.Date;

            // The next whole hour today, or mid-morning on another day: what somebody
            // adding an event from a desktop frame nearly always means.
            start = start.Date == DateTime.Today
                ? DateTime.Now.Date.AddHours(DateTime.Now.Hour + 1)
                : start.AddHours(9);

            Create(start, timeChosen: false);
        }

        /// <summary>Something new at the half hour somebody clicked in a grid.</summary>
        private void AddAt(DateTime start) => Create(start, timeChosen: true);

        /// <summary>
        /// An entry dragged to another time, stretched to another end, or - with Ctrl
        /// held - copied there. The grid decided where; what Google is told is decided
        /// here.
        /// </summary>
        private void Reschedule(AgendaEvent item, DateTime start, DateTime end, bool copy)
        {
            if (!_data.IsOpen) return;

            if (item.IsTask)
            {
                // Only a task whose hour this program keeps can be dragged, and the hour
                // is all a drag changes on it - the day stays, see the grid. Kept rather
                // than written: Google has no field for it.
                _preferences.RememberHour(item.CalendarId, TitleOf(item), start.TimeOfDay, end - start);
                Refresh();
                return;
            }

            // A copy carries what the entry says and not what identifies it; a move
            // carries both, so that Google refuses it if the entry changed elsewhere
            // in the meantime.
            var moved = new AgendaEvent
            {
                Id = copy ? string.Empty : item.Id,
                CalendarId = item.CalendarId,
                Title = item.Title,
                Location = item.Location,
                Description = item.Description,
                Start = start,
                End = end,
                ColourHex = item.ColourHex,
                ColourId = item.ColourId,
                WebLink = copy ? string.Empty : item.WebLink,
                ETag = copy ? string.Empty : item.ETag,
                CanWrite = true
            };

            Save(moved, isNew: copy);
        }

        private void Create(DateTime start, bool timeChosen)
        {
            if (!_data.IsOpen) return;

            var draft = new AgendaEvent { Start = start, End = start.AddHours(1), CanWrite = true };

            AgendaEvent? filled = AgendaEventWindow.Show(Window.GetWindow(_rootVisual), draft,
                                                        _data.Calendars, _data.TaskLists, isNew: true,
                                                        timeChosen: timeChosen);
            if (filled != null) Save(filled, isNew: true);
        }

        private void Edit(AgendaEvent item)
        {
            if (!_data.IsOpen) return;

            AgendaEvent? changed = AgendaEventWindow.Show(Window.GetWindow(_rootVisual), item,
                                                         _data.Calendars, _data.TaskLists, isNew: false);
            if (changed == null) return;

            // Nothing Google keeps has changed - only the hour this program keeps - so
            // Google is not written to at all. The API does not show a task's
            // repetition, and nothing promises that writing a repeating task back
            // leaves its repetition alone; the repeating tasks are exactly the ones a
            // kept hour is for.
            if (item.IsTask && changed.IsTask && SameForGoogle(item, changed))
            {
                RememberHour(item, changed);
                Refresh();
                return;
            }

            Save(changed, isNew: false, before: item);
        }

        /// <summary>True when an edited task differs from what Google holds in nothing Google keeps.</summary>
        private static bool SameForGoogle(AgendaEvent before, AgendaEvent after) =>
            string.Equals(before.Title, after.Title, StringComparison.Ordinal)
            && string.Equals(before.Description, after.Description, StringComparison.Ordinal)
            && before.Start.Date == after.Start.Date
            && string.Equals(before.CalendarId, after.CalendarId, StringComparison.Ordinal);

        private async void Save(AgendaEvent item, bool isNew, AgendaEvent? before = null)
        {
            try
            {
                await _data.SaveAsync(item, isNew).ConfigureAwait(true);
                RememberHour(before, item);
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

        /// <summary>
        /// Keeps, moves or drops the hour of a task that only this program knows.
        ///
        /// After the save, not before it: Google has the date by then, and an hour kept
        /// for a task Google refused would be an hour for nothing.
        /// </summary>
        private void RememberHour(AgendaEvent? before, AgendaEvent after)
        {
            if (!after.IsTask) return;

            if (before != null && before.IsTask && before.HourIsLocal)
                _preferences.ForgetHour(before.CalendarId, TitleOf(before));

            if (after.HourIsLocal)
                _preferences.RememberHour(after.CalendarId, TitleOf(after),
                                          after.Start.TimeOfDay, after.End - after.Start);
        }

        /// <summary>
        /// The title a task comes back from Google with, which is what a kept hour is
        /// found by. A task saved with no title returns as "untitled", so its hour has to
        /// be filed under that same word to be found again.
        /// </summary>
        private static string TitleOf(AgendaEvent task) =>
            string.IsNullOrWhiteSpace(task.Title) ? Strings.AgendaUntitled : task.Title;

        private async void Delete(AgendaEvent item)
        {
            // A task gets a longer question. The API does not say whether a task
            // repeats, so neither can this program; what it can say is that only this
            // occurrence goes, and that Google makes the next one if there is a series
            // - which is what surprised the first person to delete one from here.
            string question = item.IsTask
                ? Strings.Get("AgendaConfirmDeleteTask", item.Title)
                : Strings.Get("AgendaConfirmDelete", item.Title);

            if (!MessageBoxesManager.ShowCustomYesNoMessageBox(question, Strings.AgendaDeleteEvent))
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

            // Not while an entry is held under the pointer: the grid this would throw
            // away is the one holding the drag. Drawn once the drag ends instead.
            if (_gridState.Dragging)
            {
                _renderHeld = true;
                return;
            }

            panel.Children.Clear();
            ShowColumns(null);

            switch (_session.State)
            {
                case AgendaState.NotConfigured:
                    panel.Children.Add(AgendaRenderer.Message(Strings.AgendaNeedsClient));
                    panel.Children.Add(SetupRow());
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
            if (_data.Offline)
            {
                panel.Children.Add(AgendaRenderer.Message(ProblemText()));

                // A switched-off API is fixed in a web console, and the guide opens
                // the page where the switch is.
                if (_data.Problem == AgendaProblem.ApiDisabled) panel.Children.Add(GuideRow());
            }

            if (!_data.LoadedOnce)
            {
                // An empty calendar and one nobody has read yet look the same from here,
                // and saying "nothing scheduled" before asking is a guess dressed as an
                // answer.
                panel.Children.Add(AgendaRenderer.Message(Strings.AgendaLoading));
                return;
            }

            panel.Children.Add(Toolbar());

            if (AgendaRenderer.IsColumns(_settings.View))
                ShowColumns(_renderer.Columns(_data.Events, _settings.View, _anchor, _flashToday, _gridState));
            else
                _renderer.Draw(panel, _data.Events, _settings.View, _anchor, _flashToday);

            // One redraw only: leaving it set would blink again every time the timer
            // brings new events in, which is a light going off in the corner of the eye
            // once a minute for no reason.
            _flashToday = false;
        }

        /// <summary>
        /// Puts a grid of hours below the toolbar, or takes it away.
        ///
        /// A grid holds the whole day and scrolls its own hours, which needs a height to
        /// scroll within - so while one is shown the frame stops scrolling as a page and
        /// hands the grid the height it has. Everything else scrolls as a page, as it
        /// always did.
        /// </summary>
        private void ShowColumns(UIElement? grid)
        {
            if (_gridHost == null || _rootVisual == null) return;

            _gridHost.Child = grid;
            _rootVisual.VerticalScrollBarVisibility = grid == null
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;
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

            // Today means now, so the hours open on it again.
            _gridState.Offset = null;

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
                Content = Wrapping(Strings.AgendaSignIn),
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            button.Click += (s, e) => _ = _session.SignInAsync();
            return button;
        }

        /// <summary>What went wrong, said in terms of what to do about it.</summary>
        private string ProblemText() => _data.Problem switch
        {
            AgendaProblem.ApiDisabled => Strings.Get("AgendaApiDisabled",
                _data.ProblemApi.Length > 0 ? _data.ProblemApi : "Google API"),
            AgendaProblem.Refused => Strings.AgendaRefused,
            _ => Strings.AgendaOffline
        };

        /// <summary>
        /// The way in, on the frame itself. The message used to send people to "the
        /// settings", which live behind a right-click on the frame that nobody new
        /// knows about - so the two things they need are right here instead.
        /// </summary>
        private UIElement SetupRow()
        {
            var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

            Button choose = FrameButton(Strings.AgendaChooseClient);
            choose.Click += (s, e) =>
            {
                ClientFileVerdict verdict = AgendaGuideWindow.ChooseClient(Window.GetWindow(_rootVisual), _session);

                if (verdict != ClientFileVerdict.Usable && verdict != ClientFileVerdict.Cancelled)
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.AgendaClientInvalid, Strings.AgendaSettingsTitle);
            };

            row.Children.Add(choose);
            row.Children.Add(GuideButton());
            return row;
        }

        private UIElement GuideRow()
        {
            var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
            row.Children.Add(GuideButton());
            return row;
        }

        private Button GuideButton()
        {
            Button guide = FrameButton(Strings.GuideButton);
            guide.Click += (s, e) => AgendaGuideWindow.Show(Window.GetWindow(_rootVisual), _session);
            return guide;
        }

        /// <summary>
        /// A button whose caption wraps. A frame can be dragged narrow, and a caption
        /// that cannot wrap is cut off mid-word - "Guida alla config..." tells nobody
        /// what the button does. Found by looking at a narrow frame, not by any test.
        /// </summary>
        private static Button FrameButton(string caption) => new Button
        {
            Content = Wrapping(caption),
            Margin = new Thickness(0, 0, 8, 6),
            Padding = new Thickness(10, 5, 10, 5),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        private static TextBlock Wrapping(string caption) => new TextBlock
        {
            Text = caption,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        };
    }
}
