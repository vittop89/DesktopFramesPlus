using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>How the frame lays the events out.</summary>
    public enum AgendaView
    {
        /// <summary>The next few days one after another, compact. What a narrow frame suits.</summary>
        List,
        /// <summary>Today as a single column of hours.</summary>
        Day,
        /// <summary>Today, tomorrow and the day after, side by side.</summary>
        ThreeDays,
        /// <summary>Seven columns of hours.</summary>
        Week,
        /// <summary>A month of day cells, each listing what falls in it.</summary>
        Month
    }

    /// <summary>
    /// Turns a list of events into the contents of a frame.
    ///
    /// Kept away from the plugin because they change for different reasons: the
    /// plugin changes when the life cycle or the source does, this changes when
    /// somebody wants a different layout. It holds no state of its own and reaches
    /// for nothing - everything it draws is passed in, and everything it wants done
    /// goes back out through the callbacks.
    /// </summary>
    public class AgendaRenderer
    {
        public Action<AgendaEvent>? OpenRequested;
        public Action<AgendaEvent>? EditRequested;
        public Action<AgendaEvent>? DeleteRequested;
        public Action<DateTime>? DayChosen;

        private static readonly Brush Faint = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));
        private static readonly Brush Strong = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));

        public void Draw(Panel panel, IReadOnlyList<AgendaEvent> events, AgendaView view,
                         DateTime anchor, bool flashToday = false)
        {
            switch (view)
            {
                // Columns of hours, the only shape that answers how full a day is
                // without anything having to be read.
                case AgendaView.Day:
                    panel.Children.Add(Columns(events, anchor.Date, 1, flashToday));
                    break;

                case AgendaView.ThreeDays:
                    panel.Children.Add(Columns(events, anchor.Date, 3, flashToday));
                    break;

                case AgendaView.Week:
                    // From the first day of the week as this language counts it, rather
                    // than from the day being looked at: a week beginning on a Wednesday
                    // is not a week.
                    panel.Children.Add(Columns(events, StartOfWeek(anchor), 7, flashToday));
                    break;

                case AgendaView.Month:
                    DrawMonth(panel, events, anchor, flashToday);
                    break;

                default:
                    DrawList(panel, events);
                    break;
            }
        }

        private UIElement Columns(IReadOnlyList<AgendaEvent> events, DateTime from, int days, bool flashToday) =>
            AgendaTimeGrid.Build(events, from, days, flashToday,
                                 e => OpenRequested?.Invoke(e),
                                 e => EditRequested?.Invoke(e),
                                 e => DeleteRequested?.Invoke(e));

        private static DateTime StartOfWeek(DateTime day)
        {
            DayOfWeek first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            int back = ((int)day.DayOfWeek - (int)first + 7) % 7;
            return day.Date.AddDays(-back);
        }

        // ======================================================================
        // LIST AND DAYS
        // ======================================================================

        private void DrawList(Panel panel, IReadOnlyList<AgendaEvent> events)
        {
            if (events.Count == 0)
            {
                panel.Children.Add(Message(Strings.AgendaNothingScheduled));
                return;
            }

            DateTime day = DateTime.MinValue;

            foreach (AgendaEvent item in events)
            {
                if (item.Day != day)
                {
                    day = item.Day;
                    panel.Children.Add(DayHeader(day));
                }

                panel.Children.Add(Card(item));
            }
        }

        // ======================================================================
        // MONTH
        // ======================================================================

        /// <summary>
        /// The month around the chosen day as a grid, then that day's events below.
        ///
        /// Titles do not fit a cell in a frame this narrow, so a busy day is marked
        /// with a dot and reading it takes one click. Pretending otherwise would give
        /// six characters of every title, which is worse than none.
        /// </summary>
        private void DrawMonth(Panel panel, IReadOnlyList<AgendaEvent> events, DateTime chosenDay,
                               bool flashToday = false)
        {
            DateTime first = new DateTime(chosenDay.Year, chosenDay.Month, 1);

            CultureInfo culture = CultureInfo.CurrentCulture;
            DayOfWeek weekStarts = culture.DateTimeFormat.FirstDayOfWeek;

            int lead = ((int)first.DayOfWeek - (int)weekStarts + 7) % 7;
            DateTime gridStart = first.AddDays(-lead);

            panel.Children.Add(new TextBlock
            {
                Text = culture.TextInfo.ToTitleCase(first.ToString("MMMM yyyy", culture)),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = Strong,
                Margin = new Thickness(2, 0, 2, 6)
            });

            var grid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 8) };

            for (int i = 0; i < 7; i++)
            {
                DateTime headed = gridStart.AddDays(i);
                grid.Children.Add(new TextBlock
                {
                    Text = culture.DateTimeFormat.GetShortestDayName(headed.DayOfWeek),
                    FontSize = 9,
                    Foreground = Faint,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 2)
                });
            }

            for (int i = 0; i < 42; i++)
            {
                DateTime day = gridStart.AddDays(i);
                grid.Children.Add(MonthCell(day, first.Month, chosenDay, OnDay(events, day), flashToday));
            }

            panel.Children.Add(grid);
            panel.Children.Add(DayHeader(chosenDay));

            List<AgendaEvent> chosen = OnDay(events, chosenDay);
            if (chosen.Count == 0) panel.Children.Add(Message(Strings.AgendaNoEventsToday));
            else foreach (AgendaEvent item in chosen) panel.Children.Add(Card(item));
        }

        private Border MonthCell(DateTime day, int month, DateTime chosenDay, List<AgendaEvent> ofDay,
                                 bool flashToday)
        {
            bool thisMonth = day.Month == month;
            bool isToday = day == DateTime.Today;
            bool isChosen = day == chosenDay.Date;

            var cell = new Border
            {
                Margin = new Thickness(1),
                MinHeight = 44,
                Padding = new Thickness(0, 3, 0, 3),
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand,
                Background = isChosen
                    ? new SolidColorBrush(Color.FromArgb(60, 255, 255, 255))
                    : Brushes.Transparent
            };

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = day.Day.ToString(CultureInfo.CurrentCulture),
                FontSize = 10,
                FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                Foreground = thisMonth ? Strong : Faint,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            // The entries themselves rather than a dot. In a narrow frame the titles
            // trim to a few characters, but the colour still says which calendar and
            // the count still says how full - and widening the frame turns this into a
            // real month view rather than a different one.
            const int Room = 3;

            foreach (AgendaEvent item in ofDay.Take(Room))
            {
                stack.Children.Add(new Border
                {
                    Background = AgendaTimeGrid.Tint(item.ColourHex, 200),
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(1, 1, 1, 0),
                    Padding = new Thickness(2, 0, 2, 0),
                    Child = new TextBlock
                    {
                        Text = item.Title,
                        FontSize = 8,
                        Foreground = Brushes.White,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                });
            }

            if (ofDay.Count > Room)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "+" + (ofDay.Count - Room),
                    FontSize = 8,
                    Foreground = Faint,
                    Margin = new Thickness(2, 1, 0, 0)
                });
            }

            cell.Child = stack;
            cell.MouseLeftButtonUp += (s, e) => DayChosen?.Invoke(day);

            if (flashToday && isToday) AgendaTimeGrid.FlashToday(cell);

            return cell;
        }

        // ======================================================================
        // PIECES
        // ======================================================================

        /// <summary>
        /// What falls on one day.
        ///
        /// The end is exclusive, for entries lasting all day as much as for timed
        /// ones: something covering all of yesterday ends at midnight today, and a
        /// meeting finishing at midnight ends then too. Comparing the end date with
        /// "on or after" put both of them on today as well.
        /// </summary>
        private static List<AgendaEvent> OnDay(IReadOnlyList<AgendaEvent> events, DateTime day) =>
            events.Where(e => e.Start.Date <= day.Date && e.End > day.Date)
                  .OrderBy(e => e.IsAllDay ? 0 : 1)
                  .ThenBy(e => e.Start)
                  .ToList();

        private static TextBlock DayHeader(DateTime day)
        {
            return new TextBlock
            {
                Text = HeaderText(day),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = Strong,
                Margin = new Thickness(2, 10, 2, 4)
            };
        }

        /// <summary>
        /// "Today" and "Tomorrow" rather than a date, because those are the two days
        /// somebody glancing at a desktop frame is asking about. The words follow the
        /// program's language; the date follows the regional format, which Windows
        /// keeps separate on purpose.
        /// </summary>
        private static string HeaderText(DateTime day)
        {
            if (day.Date == DateTime.Today) return Strings.AgendaToday;
            if (day.Date == DateTime.Today.AddDays(1)) return Strings.AgendaTomorrow;

            return day.ToString("ddd d MMM", CultureInfo.CurrentCulture);
        }

        private Border Card(AgendaEvent item)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand,
                ToolTip = Tooltip(item)
            };

            var layout = new StackPanel { Orientation = Orientation.Horizontal };

            // The calendar's own colour, so entries from different calendars stay apart
            // without a line of text explaining which is which.
            layout.Children.Add(new Border
            {
                Width = 4,
                CornerRadius = new CornerRadius(2),
                Background = Colour(item.ColourHex),
                Margin = new Thickness(0, 0, 8, 0)
            });

            var texts = new StackPanel();

            texts.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brushes.White
            });

            texts.Children.Add(new TextBlock
            {
                Text = When(item),
                FontSize = 11,
                Foreground = Faint
            });

            layout.Children.Add(texts);
            card.Child = layout;

            // A double click hands the event to Google's own editor, which can do
            // everything a frame should not try to.
            card.MouseLeftButtonUp += (s, e) =>
            {
                if (e.ClickCount == 2) OpenRequested?.Invoke(item);
            };

            card.ContextMenu = Menu(item);
            return card;
        }

        private ContextMenu Menu(AgendaEvent item)
        {
            var menu = new ContextMenu();

            if (item.CanWrite)
            {
                var edit = new MenuItem { Header = Strings.AgendaEditEvent };
                edit.Click += (s, e) => EditRequested?.Invoke(item);
                menu.Items.Add(edit);

                var remove = new MenuItem { Header = Strings.AgendaDeleteEvent };
                remove.Click += (s, e) => DeleteRequested?.Invoke(item);
                menu.Items.Add(remove);

                menu.Items.Add(new Separator());
            }

            var open = new MenuItem { Header = Strings.AgendaOpenInGoogle };
            open.Click += (s, e) => OpenRequested?.Invoke(item);
            menu.Items.Add(open);

            return menu;
        }

        private static string Tooltip(AgendaEvent item)
        {
            var parts = new List<string> { item.Title, When(item) };

            if (!string.IsNullOrWhiteSpace(item.Location)) parts.Add(item.Location);
            if (!string.IsNullOrWhiteSpace(item.Description)) parts.Add(item.Description);

            return string.Join(Environment.NewLine, parts);
        }

        private static string When(AgendaEvent item)
        {
            if (item.IsAllDay) return Strings.AgendaAllDay;

            return item.Start.ToString("HH:mm", CultureInfo.CurrentCulture)
                 + " - "
                 + item.End.ToString("HH:mm", CultureInfo.CurrentCulture);
        }

        private static SolidColorBrush Colour(string hex)
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

        public static Border Message(string text)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 4),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255))
                }
            };
        }
    }
}
