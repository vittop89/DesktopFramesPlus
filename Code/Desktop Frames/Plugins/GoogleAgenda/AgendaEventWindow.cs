using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// The small form for adding or changing an entry.
    ///
    /// Deliberately small. It offers what somebody wants at a glance from a desktop
    /// frame - what, which calendar, when - and leaves guests, reminders, repetition
    /// and conferencing to Google's own editor, which is one click away and does all
    /// of it properly. Reproducing that here would mean maintaining a worse copy of
    /// it for ever.
    /// </summary>
    public static class AgendaEventWindow
    {
        /// <summary>
        /// Shows the form, and returns the edited entry, or null when it was closed
        /// without saving. Nothing is sent anywhere from here: the caller decides what
        /// to do with the answer.
        /// </summary>
        public static AgendaEvent? Show(Window? owner, AgendaEvent draft,
                                        IReadOnlyList<AgendaCalendar> calendars, bool isNew)
        {
            List<AgendaCalendar> writable = calendars.Where(c => c.CanWrite).ToList();
            if (writable.Count == 0)
            {
                MessageBoxesManager.ShowOKOnlyMessageBoxForm(
                    Strings.AgendaNoWritableCalendar, Strings.AgendaSettingsTitle);
                return null;
            }

            var window = new Window
            {
                Title = isNew ? Strings.AgendaNewEvent : Strings.AgendaEditEvent,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            var layout = new StackPanel { Margin = new Thickness(20) };

            var title = new TextBox { Text = draft.Title, Height = 26 };
            layout.Children.Add(Labelled(Strings.AgendaTitleLabel, title));

            var calendar = new ComboBox { Height = 26, DisplayMemberPath = nameof(AgendaCalendar.Title) };
            foreach (AgendaCalendar option in writable) calendar.Items.Add(option);
            calendar.SelectedItem = writable.FirstOrDefault(c => c.Id == draft.CalendarId)
                                 ?? writable.FirstOrDefault(c => c.IsPrimary)
                                 ?? writable[0];
            layout.Children.Add(Labelled(Strings.AgendaCalendarLabel, calendar));

            var allDay = new CheckBox
            {
                Content = Strings.AgendaAllDay,
                IsChecked = draft.IsAllDay,
                Margin = new Thickness(0, 6, 0, 10)
            };
            layout.Children.Add(allDay);

            var date = new DatePicker { SelectedDate = draft.Start.Date, Height = 26 };
            layout.Children.Add(Labelled(Strings.AgendaDateLabel, date));

            var times = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            times.ColumnDefinitions.Add(new ColumnDefinition());
            times.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            times.ColumnDefinitions.Add(new ColumnDefinition());

            var from = new TextBox { Text = draft.Start.ToString("HH:mm"), Height = 26 };
            var to = new TextBox { Text = draft.End.ToString("HH:mm"), Height = 26 };

            StackPanel fromBlock = Labelled(Strings.AgendaStartLabel, from);
            StackPanel toBlock = Labelled(Strings.AgendaEndLabel, to);
            Grid.SetColumn(fromBlock, 0);
            Grid.SetColumn(toBlock, 2);
            times.Children.Add(fromBlock);
            times.Children.Add(toBlock);
            layout.Children.Add(times);

            // The times mean nothing for an entry that lasts all day, and a field that
            // is read but ignored is a field that lies.
            void SyncTimes() => times.IsEnabled = allDay.IsChecked != true;
            allDay.Checked += (s, e) => SyncTimes();
            allDay.Unchecked += (s, e) => SyncTimes();
            SyncTimes();

            var location = new TextBox { Text = draft.Location, Height = 26 };
            layout.Children.Add(Labelled(Strings.AgendaLocationLabel, location));

            var error = new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.Firebrick,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            layout.Children.Add(error);

            AgendaEvent? result = null;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancel = new Button
            {
                Content = Strings.BtnCancel,
                Width = 100,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };
            cancel.Click += (s, e) => window.Close();

            var save = new Button
            {
                Content = Strings.BtnSave,
                Width = 100,
                Height = 30,
                IsDefault = true
            };

            save.Click += (s, e) =>
            {
                DateTime day = date.SelectedDate ?? draft.Start.Date;
                bool wholeDay = allDay.IsChecked == true;

                DateTime start, end;

                if (wholeDay)
                {
                    start = day.Date;
                    end = day.Date.AddDays(1);
                }
                else
                {
                    if (!TryReadTime(from.Text, out TimeSpan startTime) ||
                        !TryReadTime(to.Text, out TimeSpan endTime))
                    {
                        Complain(error, Strings.AgendaBadTime);
                        return;
                    }

                    start = day.Date + startTime;
                    end = day.Date + endTime;

                    // An end before the start is almost always an evening crossing
                    // midnight, and rejecting it would send somebody hunting for a
                    // second date field that is not there.
                    if (end <= start) end = end.AddDays(1);
                }

                var chosen = (AgendaCalendar)calendar.SelectedItem;

                result = new AgendaEvent
                {
                    Id = draft.Id,
                    CalendarId = chosen.Id,
                    Title = title.Text.Trim(),
                    Location = location.Text.Trim(),
                    Description = draft.Description,
                    Start = start,
                    End = end,
                    IsAllDay = wholeDay,
                    ColourHex = chosen.ColourHex,
                    WebLink = draft.WebLink,
                    ETag = draft.ETag,
                    CanWrite = true
                };

                window.Close();
            };

            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            layout.Children.Add(buttons);

            window.Content = layout;
            window.ShowDialog();

            // Moving an entry between calendars is not a change to it, it is a move,
            // and the API has a separate call for it. Rather than half-do it, the
            // calendar is left where it was on an entry that already exists.
            if (result != null && !isNew) result.CalendarId = draft.CalendarId;

            return result;
        }

        private static StackPanel Labelled(string caption, FrameworkElement field)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            block.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 3)
            });

            block.Children.Add(field);
            return block;
        }

        /// <summary>
        /// Reads a time the way people type one: 9, 9:30, 09.30, 0930. Refusing all but
        /// one of those would be correct and useless.
        /// </summary>
        private static bool TryReadTime(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string cleaned = text.Trim().Replace('.', ':');

            if (cleaned.Length == 4 && !cleaned.Contains(":") && int.TryParse(cleaned, out _))
                cleaned = cleaned.Substring(0, 2) + ":" + cleaned.Substring(2);

            if (!cleaned.Contains(":") && int.TryParse(cleaned, out int hourOnly)
                && hourOnly >= 0 && hourOnly <= 23)
            {
                time = TimeSpan.FromHours(hourOnly);
                return true;
            }

            if (TimeSpan.TryParseExact(cleaned, new[] { @"h\:mm", @"hh\:mm" },
                                       CultureInfo.InvariantCulture, out time))
                return time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);

            return false;
        }

        private static void Complain(TextBlock where, string message)
        {
            where.Text = message;
            where.Visibility = Visibility.Visible;
        }
    }
}
