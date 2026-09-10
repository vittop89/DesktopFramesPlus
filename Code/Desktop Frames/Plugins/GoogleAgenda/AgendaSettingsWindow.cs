using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// The plugin's own settings window: the account, the layout, and which calendars
    /// this frame shows.
    ///
    /// Separate from the plugin because the two change for different reasons, and
    /// because keeping them together is how the other plugins grew past a thousand
    /// lines. It reads the state from the same <see cref="AgendaSession"/> the frame
    /// reads, so the window and the frame behind it cannot disagree about whether
    /// somebody is signed in.
    /// </summary>
    public static class AgendaSettingsWindow
    {
        public static void Show(Window? ownerWindow, AgendaSession session, AgendaSettings current,
                                IReadOnlyList<AgendaCalendar> calendars, Action<AgendaSettings> onSaved)
        {
            var window = new Window
            {
                Title = Strings.AgendaSettingsTitle,
                Width = 430,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 640,
                WindowStartupLocation = ownerWindow != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                Owner = ownerWindow,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            var layout = new StackPanel { Margin = new Thickness(20) };

            // --- account -------------------------------------------------------
            var status = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            layout.Children.Add(status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };

            // The way in for somebody who has never had a client in this profile. Without
            // it the message below asked them to add one and every button was greyed
            // out, which is a door with no handle.
            var chooseClient = new Button
            {
                Content = Strings.AgendaChooseClient,
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 0)
            };

            chooseClient.Click += (s, e) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = Strings.AgendaChooseClient,
                    Filter = "Google client (*.json)|*.json"
                };

                if (dialog.ShowDialog(Window.GetWindow(chooseClient)) != true) return;

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

                if (verdict != ClientFileVerdict.Usable)
                {
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.AgendaClientInvalid,
                                                                 Strings.AgendaSettingsTitle);
                    return;
                }

                // The answer was "not set up" and is no longer true; ask again.
                _ = session.RecheckAsync();
            };

            var signIn = new Button { Width = 170, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            signIn.Click += (s, e) => _ = session.SignInAsync();

            var signOut = new Button { Content = Strings.AgendaSignOut, Width = 110, Height = 30 };
            signOut.Click += (s, e) => _ = session.SignOutAsync();

            buttons.Children.Add(chooseClient);
            buttons.Children.Add(signIn);
            buttons.Children.Add(signOut);
            layout.Children.Add(buttons);

            // --- view ----------------------------------------------------------
            var view = new ComboBox { Height = 26, Margin = new Thickness(0, 0, 0, 12) };
            foreach ((AgendaView value, string caption) in Views())
                view.Items.Add(new ComboBoxItem { Content = caption, Tag = value });

            view.SelectedItem = view.Items.OfType<ComboBoxItem>()
                                    .FirstOrDefault(i => (AgendaView)i.Tag! == current.View)
                             ?? view.Items[0];
            layout.Children.Add(Labelled(Strings.AgendaViewLabel, view));

            // --- days ahead ----------------------------------------------------
            var days = new TextBox
            {
                Text = current.DaysAhead.ToString(System.Globalization.CultureInfo.CurrentCulture),
                Height = 26,
                Margin = new Thickness(0, 0, 0, 12)
            };
            StackPanel daysBlock = Labelled(Strings.AgendaDaysLabel, days);
            layout.Children.Add(daysBlock);

            // Only the list view has a length somebody chooses; the others are a day, a
            // week and a month by definition, and a field that changes nothing is worse
            // than no field.
            void SyncDays() =>
                daysBlock.Visibility = ((AgendaView)((ComboBoxItem)view.SelectedItem).Tag! == AgendaView.List)
                    ? Visibility.Visible : Visibility.Collapsed;

            view.SelectionChanged += (s, e) => SyncDays();
            SyncDays();

            // --- calendars -----------------------------------------------------
            var boxes = new List<CheckBox>();

            if (calendars.Count > 0)
            {
                var list = new StackPanel();

                foreach (AgendaCalendar calendar in calendars)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

                    row.Children.Add(new Border
                    {
                        Width = 10,
                        Height = 10,
                        CornerRadius = new CornerRadius(5),
                        Margin = new Thickness(0, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Background = Swatch(calendar.ColourHex)
                    });

                    var box = new CheckBox
                    {
                        Content = calendar.Title,
                        Tag = calendar.Id,

                        // No choice recorded yet means every calendar, so the boxes have
                        // to show that rather than an empty list nobody asked for.
                        IsChecked = current.Calendars.Count == 0 || current.Calendars.Contains(calendar.Id),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    boxes.Add(box);
                    row.Children.Add(box);
                    list.Children.Add(row);
                }

                layout.Children.Add(Labelled(Strings.AgendaCalendarsLabel,
                    new ScrollViewer { Content = list, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
            }

            // --- footer --------------------------------------------------------
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var cancel = new Button
            {
                Content = Strings.BtnCancel,
                Width = 100,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };
            cancel.Click += (s, e) => window.Close();

            var save = new Button { Content = Strings.BtnSave, Width = 100, Height = 30, IsDefault = true };
            save.Click += (s, e) =>
            {
                var saved = new AgendaSettings
                {
                    View = (AgendaView)((ComboBoxItem)view.SelectedItem).Tag!,
                    DaysAhead = int.TryParse(days.Text, out int parsed) ? parsed : current.DaysAhead
                };

                // Everything ticked is recorded as no choice at all: a frame that means
                // "all of them" should keep meaning that when a calendar is added later.
                if (boxes.Count > 0 && boxes.Any(b => b.IsChecked != true))
                {
                    foreach (CheckBox box in boxes.Where(b => b.IsChecked == true))
                        saved.Calendars.Add((string)box.Tag!);
                }

                onSaved(saved);
                window.Close();
            };

            footer.Children.Add(cancel);
            footer.Children.Add(save);
            layout.Children.Add(footer);

            // --- keeping the account section honest ----------------------------
            void Draw()
            {
                status.Text = session.State switch
                {
                    AgendaState.NotConfigured => Strings.AgendaNeedsClient,
                    AgendaState.SignedOut => Strings.AgendaSignedOut,
                    AgendaState.SigningIn => Strings.AgendaSigningIn,
                    AgendaState.SignedIn => Strings.AgendaSignedIn,
                    _ => session.LastError ?? Strings.AgendaFailed
                };

                signIn.Content = session.State == AgendaState.SignedIn
                    ? Strings.AgendaSignInAgain
                    : Strings.AgendaSignIn;

                signIn.IsEnabled = session.State != AgendaState.SigningIn
                                && session.State != AgendaState.NotConfigured;

                signOut.IsEnabled = session.State == AgendaState.SignedIn;

                // Offered while there is no client, and after a failure - a client from
                // a project without the calendar enabled fails at sign-in, and the way
                // out of that is choosing a different file.
                chooseClient.Visibility = session.State == AgendaState.NotConfigured
                                       || session.State == AgendaState.Failed
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            session.Changed += Draw;
            // Left attached, the handler would hold this window alive as long as the
            // frame, and every reopening would stack another one on top.
            window.Closed += (s, e) => session.Changed -= Draw;

            Draw();

            window.Content = layout;
            window.ShowDialog();
        }

        private static IEnumerable<(AgendaView, string)> Views()
        {
            yield return (AgendaView.List, Strings.AgendaViewList);
            yield return (AgendaView.Day, Strings.AgendaViewDay);
            yield return (AgendaView.ThreeDays, Strings.AgendaViewThreeDays);
            yield return (AgendaView.Week, Strings.AgendaViewWeek);
            yield return (AgendaView.Month, Strings.AgendaViewMonth);
        }

        private static StackPanel Labelled(string caption, FrameworkElement field)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

            block.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 3)
            });

            block.Children.Add(field);
            return block;
        }

        private static Brush Swatch(string hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex))
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch (Exception)
            {
                // A colour Google invented is not worth a broken window.
            }

            return new SolidColorBrush(Color.FromRgb(100, 150, 255));
        }
    }
}
