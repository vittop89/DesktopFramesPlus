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
        /// <param name="timeChosen">
        /// True when the time in the draft was picked by clicking the grid. A task made
        /// that way keeps it; a task made from the add button starts as a whole-day one,
        /// because the hour there is only a guess at what an event might want.
        /// </param>
        public static AgendaEvent? Show(Window? owner, AgendaEvent draft,
                                        IReadOnlyList<AgendaCalendar> calendars,
                                        IReadOnlyList<AgendaTaskList> taskLists, bool isNew,
                                        bool timeChosen = false)
        {
            List<AgendaCalendar> writable = calendars.Where(c => c.CanWrite).ToList();

            // An existing entry is what it is: Google has no way to turn an event into a
            // task, so the choice is offered only while making something new.
            bool canChooseKind = isNew && taskLists.Count > 0;
            bool startAsTask = draft.IsTask;

            // Google's own hour on a task is shown but cannot be changed from here: the
            // API takes no hour for a task, so a change would be accepted and then lost
            // without a word. The hour this program keeps is another matter.
            bool hourFromGoogle = !isNew && draft.IsTask && !draft.IsAllDay && !draft.HourIsLocal;

            if (writable.Count == 0 && !startAsTask)
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

            var asEvent = new RadioButton
            {
                Content = Strings.AgendaKindEvent,
                IsChecked = !startAsTask,
                Margin = new Thickness(0, 0, 14, 10)
            };

            var asTask = new RadioButton
            {
                Content = Strings.AgendaKindTask,
                IsChecked = startAsTask,
                Margin = new Thickness(0, 0, 0, 10)
            };

            if (canChooseKind)
            {
                var kinds = new StackPanel { Orientation = Orientation.Horizontal };
                kinds.Children.Add(asEvent);
                kinds.Children.Add(asTask);
                layout.Children.Add(kinds);
            }

            var title = new TextBox { Text = draft.Title, Height = 26 };
            layout.Children.Add(Labelled(Strings.AgendaTitleLabel, title));

            var calendar = new ComboBox { Height = 26, DisplayMemberPath = nameof(AgendaCalendar.Title) };
            foreach (AgendaCalendar option in writable) calendar.Items.Add(option);
            calendar.SelectedItem = writable.FirstOrDefault(c => c.Id == draft.CalendarId)
                                 ?? writable.FirstOrDefault(c => c.IsPrimary)
                                 ?? writable.FirstOrDefault();
            StackPanel calendarBlock = Labelled(Strings.AgendaCalendarLabel, calendar);
            layout.Children.Add(calendarBlock);

            var list = new ComboBox { Height = 26, DisplayMemberPath = nameof(AgendaTaskList.Title) };
            foreach (AgendaTaskList option in taskLists) list.Items.Add(option);
            list.SelectedItem = taskLists.FirstOrDefault(l => l.Id == draft.CalendarId)
                             ?? taskLists.FirstOrDefault();

            StackPanel listBlock = Labelled(Strings.AgendaListLabel, list);
            layout.Children.Add(listBlock);

            var allDay = new CheckBox
            {
                Content = Strings.AgendaAllDay,
                Margin = new Thickness(0, 6, 0, 10)
            };
            layout.Children.Add(allDay);

            var date = new DatePicker { SelectedDate = draft.Start.Date, Height = 26 };
            layout.Children.Add(Labelled(Strings.AgendaDateLabel, date));

            var times = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            times.ColumnDefinitions.Add(new ColumnDefinition());
            times.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            times.ColumnDefinitions.Add(new ColumnDefinition());

            // A whole-day entry has no hours to show, so the fields offer an ordinary
            // one instead of midnight to midnight - which, unticked, would read as an
            // entry twenty-four hours long.
            DateTime shownFrom = draft.IsAllDay ? draft.Start.Date.AddHours(9) : draft.Start;
            DateTime shownTo = draft.IsAllDay ? shownFrom.AddHours(1) : draft.End;

            var from = new TextBox { Text = shownFrom.ToString("HH:mm"), Height = 26 };
            var to = new TextBox { Text = shownTo.ToString("HH:mm"), Height = 26 };

            StackPanel fromBlock = Labelled(Strings.AgendaStartLabel, from);
            StackPanel toBlock = Labelled(Strings.AgendaEndLabel, to);
            Grid.SetColumn(fromBlock, 0);
            Grid.SetColumn(toBlock, 2);
            times.Children.Add(fromBlock);
            times.Children.Add(toBlock);
            layout.Children.Add(times);

            var hourNote = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                Margin = new Thickness(0, 0, 0, 10)
            };
            layout.Children.Add(hourNote);

            var location = new TextBox { Text = draft.Location, Height = 26 };
            StackPanel locationBlock = Labelled(Strings.AgendaLocationLabel, location);
            layout.Children.Add(locationBlock);

            // Whole-day is remembered for each kind separately, so trying the other kind
            // and coming back finds the form the way it was left.
            bool eventWholeDay = !draft.IsTask && draft.IsAllDay;
            bool taskWholeDay = draft.IsTask ? draft.IsAllDay : !timeChosen;
            bool showingTask = startAsTask;

            allDay.IsChecked = showingTask ? taskWholeDay : eventWholeDay;

            // The times mean nothing for an entry that lasts all day, and a field that
            // is read but ignored is a field that lies. Google's hour on a task is not
            // this form's to change, so it is shown and left alone.
            void SyncTimes()
            {
                bool locked = showingTask && hourFromGoogle;

                allDay.IsEnabled = !locked;
                times.IsEnabled = !locked && allDay.IsChecked != true;
            }

            allDay.Checked += (s, e) => SyncTimes();
            allDay.Unchecked += (s, e) => SyncTimes();

            // A task has a list rather than a calendar and nowhere to put a place. Rather
            // than show fields that are read and thrown away, the form becomes the shape
            // of what is being made.
            void SyncKind()
            {
                bool task = asTask.IsChecked == true;

                if (task != showingTask)
                {
                    if (showingTask) taskWholeDay = allDay.IsChecked == true;
                    else eventWholeDay = allDay.IsChecked == true;

                    showingTask = task;
                    allDay.IsChecked = task ? taskWholeDay : eventWholeDay;
                }

                calendarBlock.Visibility = task ? Visibility.Collapsed : Visibility.Visible;
                listBlock.Visibility = task ? Visibility.Visible : Visibility.Collapsed;
                locationBlock.Visibility = task ? Visibility.Collapsed : Visibility.Visible;

                // Which kind of hour a task has decides what can be done with it, and a
                // person cannot tell the two apart by looking - so the form says.
                hourNote.Text = hourFromGoogle ? Strings.AgendaTaskHourGoogle : Strings.AgendaTaskHourLocal;
                hourNote.Visibility = task ? Visibility.Visible : Visibility.Collapsed;

                SyncTimes();
            }

            asEvent.Checked += (s, e) => SyncKind();
            asTask.Checked += (s, e) => SyncKind();
            SyncKind();

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
                bool makingTask = asTask.IsChecked == true;
                bool wholeDay = allDay.IsChecked == true;

                DateTime start, end;

                if (makingTask && hourFromGoogle)
                {
                    // Carried over as it was, onto whichever day was chosen.
                    start = day.Date + draft.Start.TimeOfDay;
                    end = start + draft.Duration;
                    wholeDay = false;
                }
                else if (wholeDay)
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

                if (makingTask)
                {
                    if (list.SelectedItem is not AgendaTaskList chosenList)
                    {
                        Complain(error, Strings.AgendaNoWritableCalendar);
                        return;
                    }

                    result = new AgendaEvent
                    {
                        Id = draft.Id,
                        CalendarId = chosenList.Id,
                        Title = title.Text.Trim(),
                        Description = draft.Description,
                        Start = start,
                        End = end,
                        IsAllDay = wholeDay,
                        IsTask = true,
                        IsDone = draft.IsDone,

                        // Google is sent the date alone; an hour stays with this program.
                        HourIsLocal = !wholeDay && !hourFromGoogle,

                        ColourHex = draft.ColourHex,
                        WebLink = draft.WebLink,
                        CanWrite = true
                    };

                    window.Close();
                    return;
                }

                if (calendar.SelectedItem is not AgendaCalendar chosen)
                {
                    Complain(error, Strings.AgendaNoWritableCalendar);
                    return;
                }

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
