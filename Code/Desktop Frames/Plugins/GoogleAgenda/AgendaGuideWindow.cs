using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Desktop_Frames.Localization;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// How to get the agenda talking to Google, from inside the program.
    ///
    /// Written for the person who is not the author. The author set this up once, by
    /// hand, with somebody explaining each screen of Google Cloud Console; everybody
    /// else meets a frame that says it needs a "client" and has no idea what one is,
    /// where it comes from, or which of the five similar-looking options to pick.
    ///
    /// Each step opens the exact page it talks about rather than describing where to
    /// click to reach it. Google moves its menus around; the addresses of the pages
    /// have stayed put for years, and a button that lands on the right page does not
    /// care what the menu above it is called this month.
    ///
    /// The last step is the file chooser itself, so the guide ends with the agenda
    /// set up rather than with instructions for finding the settings.
    /// </summary>
    public static class AgendaGuideWindow
    {
        private const string ProjectPage = "https://console.cloud.google.com/projectcreate";
        private const string CalendarApiPage = "https://console.cloud.google.com/apis/library/calendar-json.googleapis.com";
        private const string TasksApiPage = "https://console.cloud.google.com/apis/library/tasks.googleapis.com";
        private const string ConsentPage = "https://console.cloud.google.com/apis/credentials/consent";
        private const string CredentialsPage = "https://console.cloud.google.com/apis/credentials";

        public static void Show(Window? owner, AgendaSession session)
        {
            var window = new Window
            {
                Title = Strings.GuideTitle,
                Width = 580,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 720,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            var layout = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };

            layout.Children.Add(new TextBlock
            {
                Text = Strings.GuideIntro,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16),
                Opacity = 0.85
            });

            layout.Children.Add(Step(1, Strings.GuideStep1,
                Link(Strings.GuideOpen, ProjectPage)));

            layout.Children.Add(Step(2, Strings.GuideStep2,
                Link(Strings.GuideCalendarApi, CalendarApiPage),
                Link(Strings.GuideTasksApi, TasksApiPage)));

            layout.Children.Add(Step(3, Strings.GuideStep3,
                Link(Strings.GuideOpen, ConsentPage)));

            layout.Children.Add(Step(4, Strings.GuideStep4,
                Link(Strings.GuideOpen, CredentialsPage)));

            // The last step does the thing instead of describing it.
            var choose = new Button
            {
                Content = Strings.AgendaChooseClient,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 8, 0)
            };

            var result = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };

            choose.Click += (s, e) =>
            {
                ClientFileVerdict verdict = ChooseClient(window, session);
                if (verdict == ClientFileVerdict.Cancelled) return;

                result.Visibility = Visibility.Visible;

                if (verdict == ClientFileVerdict.Usable)
                {
                    result.Text = Strings.GuideClientAdded;
                    result.Foreground = new SolidColorBrush(Color.FromRgb(30, 120, 60));
                }
                else
                {
                    result.Text = Strings.AgendaClientInvalid;
                    result.Foreground = Brushes.Firebrick;
                }
            };

            StackPanel last = Step(5, Strings.GuideStep5, choose);
            last.Children.Add(result);
            layout.Children.Add(last);

            // What surprises people, said before it happens rather than after.
            var notes = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 246, 248)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 6, 0, 14)
            };

            var noteList = new StackPanel();
            noteList.Children.Add(new TextBlock { Text = Strings.GuideNotes, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            noteList.Children.Add(Note(Strings.GuideNoteUnverified));
            noteList.Children.Add(Note(Strings.GuideNoteWeekly));
            noteList.Children.Add(Note(Strings.GuideNoteOthers));
            notes.Child = noteList;
            layout.Children.Add(notes);

            var close = new Button
            {
                Content = Strings.BtnClose,
                Width = 100,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsCancel = true,
                IsDefault = true
            };
            close.Click += (s, e) => window.Close();
            layout.Children.Add(close);

            window.Content = new ScrollViewer
            {
                Content = layout,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            window.ShowDialog();
        }

        /// <summary>
        /// Opens the file dialog, imports what was picked and asks the session to look
        /// again. Shared by the guide, the frame and the settings, so there is one way
        /// in rather than three that could drift apart.
        /// </summary>
        public static ClientFileVerdict ChooseClient(Window? owner, AgendaSession session)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Strings.AgendaChooseClient,
                Filter = "Google client (*.json)|*.json"
            };

            bool? picked = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();

            // Nothing chosen is not an invalid file, and saying it was would be wrong.
            if (picked != true) return ClientFileVerdict.Cancelled;

            ClientFileVerdict verdict;

            try
            {
                verdict = AgendaCredentials.Import(dialog.FileName);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: could not import the client file: {ex.Message}");
                verdict = ClientFileVerdict.NotJson;
            }

            if (verdict == ClientFileVerdict.Usable) _ = session.RecheckAsync();

            return verdict;
        }

        private static StackPanel Step(int number, string text, params UIElement[] actions)
        {
            var step = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(26, 115, 232)),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = number.ToString(),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var body = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(badge, 0);
            Grid.SetColumn(body, 1);
            line.Children.Add(badge);
            line.Children.Add(body);
            step.Children.Add(line);

            if (actions.Length > 0)
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(34, 6, 0, 0)
                };

                foreach (UIElement action in actions) row.Children.Add(action);
                step.Children.Add(row);
            }

            return step;
        }

        private static Button Link(string caption, string address)
        {
            var button = new Button
            {
                Content = caption + "  ↗",
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = address
            };

            button.Click += (s, e) => Open(address);
            return button;
        }

        private static TextBlock Note(string text) => new TextBlock
        {
            Text = "•  " + text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };

        /// <summary>
        /// Opens one of the fixed addresses above. They are written in this file, not
        /// received from anywhere, so there is nothing to check before handing them to
        /// the browser.
        /// </summary>
        private static void Open(string address)
        {
            try
            {
                Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: could not open the browser: {ex.Message}");
            }
        }
    }
}
