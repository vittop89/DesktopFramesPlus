using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
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
        private readonly List<AgendaEvent> _events = new List<AgendaEvent>();

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

            _session.Changed += Render;
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
        /// Asks the source for what changed. Empty until the source exists; the timer
        /// that will drive it is already in place, so the wiring is one method.
        /// </summary>
        private void Refresh()
        {
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
                    RenderEvents(panel);
                    break;
            }
        }

        private void RenderEvents(StackPanel panel)
        {
            // Nothing has been asked for yet: the events arrive with the source, in the
            // step after this one.
            if (_events.Count == 0)
            {
                panel.Children.Add(CreateMessageCard(Strings.AgendaLoading));
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
            if (_session.State == AgendaState.SignedIn)
            {
                _refreshTimer?.Start();
                Refresh();
            }
        }

        public void Cleanup()
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;

            _session.Changed -= Render;
            _session.Cancel();
        }

        public void ShowSettingsWindow(Window ownerWindow, dynamic frameData)
        {
            AgendaSettingsWindow.Show(ownerWindow, _session, _settingsRef);
        }
    }
}
