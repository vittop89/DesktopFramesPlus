using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// Days as columns, with the hours running down the side and every event drawn
    /// where it actually falls - the shape a calendar has had since before there were
    /// computers, and the one that answers "how full is Thursday" at a glance, which
    /// a list cannot.
    ///
    /// It draws and nothing else. What to do about a click goes back out through the
    /// callbacks it is given, the same way <see cref="AgendaRenderer"/> works.
    /// </summary>
    public static class AgendaTimeGrid
    {
        private const double HourHeight = 26;
        private const double HourColumnWidth = 34;

        /// <summary>
        /// The band shown when nothing forces it wider. A frame is short, and a
        /// midnight-to-midnight column would spend most of its height on hours nobody
        /// has ever booked.
        /// </summary>
        private const int DefaultFirstHour = 7;
        private const int DefaultLastHour = 20;

        private static readonly Brush Faint = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
        private static readonly Brush Line = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
        private static readonly Brush Strong = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));

        public static UIElement Build(IReadOnlyList<AgendaEvent> events, DateTime from, int days,
                                      Action<AgendaEvent>? open, Action<AgendaEvent>? edit,
                                      Action<AgendaEvent>? delete)
        {
            List<DateTime> columnDays = Enumerable.Range(0, days).Select(i => from.Date.AddDays(i)).ToList();

            (int firstHour, int lastHour) = Band(events, columnDays);

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HourColumnWidth) });
            foreach (DateTime _ in columnDays)
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // day names
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // all-day strip
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // hours

            AddHeaders(root, columnDays);
            AddAllDayStrip(root, events, columnDays, open, edit, delete);
            AddHourLabels(root, firstHour, lastHour);

            for (int i = 0; i < columnDays.Count; i++)
                AddDayColumn(root, i, columnDays[i], events, firstHour, lastHour, open, edit, delete);

            return root;
        }

        // ======================================================================
        // FRAME OF THE GRID
        // ======================================================================

        /// <summary>
        /// The hours worth drawing: the usual working band, widened to hold anything
        /// that falls outside it. An event at seven in the morning must be visible
        /// without scrolling for it, and an empty night must not push it off screen.
        /// </summary>
        private static (int, int) Band(IReadOnlyList<AgendaEvent> events, List<DateTime> days)
        {
            int first = DefaultFirstHour;
            int last = DefaultLastHour;

            foreach (AgendaEvent item in events.Where(e => !e.IsAllDay))
            {
                if (!days.Any(d => d == item.Start.Date || d == item.End.Date)) continue;

                first = Math.Min(first, item.Start.Hour);

                // An event ending at 18:30 needs the 18 o'clock row and the one below it.
                int endHour = item.End.Minute > 0 ? item.End.Hour + 1 : item.End.Hour;
                last = Math.Max(last, Math.Min(24, endHour));
            }

            return (Math.Max(0, first), Math.Min(24, Math.Max(last, first + 1)));
        }

        private static void AddHeaders(Grid root, List<DateTime> days)
        {
            CultureInfo culture = CultureInfo.CurrentCulture;

            for (int i = 0; i < days.Count; i++)
            {
                DateTime day = days[i];
                bool isToday = day == DateTime.Today;

                var caption = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4)
                };

                caption.Children.Add(new TextBlock
                {
                    Text = culture.DateTimeFormat.GetShortestDayName(day.DayOfWeek),
                    FontSize = 9,
                    Foreground = Faint,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                caption.Children.Add(new TextBlock
                {
                    Text = day.Day.ToString(culture),
                    FontSize = 12,
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = Strong,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                Grid.SetColumn(caption, i + 1);
                Grid.SetRow(caption, 0);
                root.Children.Add(caption);
            }
        }

        /// <summary>
        /// Entries with no time of their own, above the grid rather than inside it.
        /// A birthday does not happen at nine o'clock, and drawing it as if it did
        /// would make the day look busy at an hour it is not.
        /// </summary>
        private static void AddAllDayStrip(Grid root, IReadOnlyList<AgendaEvent> events, List<DateTime> days,
                                           Action<AgendaEvent>? open, Action<AgendaEvent>? edit,
                                           Action<AgendaEvent>? delete)
        {
            for (int i = 0; i < days.Count; i++)
            {
                DateTime day = days[i];

                List<AgendaEvent> ofDay = events
                    .Where(e => e.IsAllDay && e.Start.Date <= day && e.End.Date > day)
                    .ToList();

                if (ofDay.Count == 0) continue;

                var stack = new StackPanel { Margin = new Thickness(1, 0, 1, 4) };

                foreach (AgendaEvent item in ofDay)
                    stack.Children.Add(Chip(item, open, edit, delete));

                Grid.SetColumn(stack, i + 1);
                Grid.SetRow(stack, 1);
                root.Children.Add(stack);
            }
        }

        private static void AddHourLabels(Grid root, int firstHour, int lastHour)
        {
            var column = new Canvas { Height = (lastHour - firstHour) * HourHeight };

            for (int hour = firstHour; hour < lastHour; hour++)
            {
                var label = new TextBlock
                {
                    Text = hour.ToString("00", CultureInfo.CurrentCulture) + ":00",
                    FontSize = 9,
                    Foreground = Faint
                };

                // Straddling its own line rather than sitting in the band below it, so
                // that reading across from "10:00" lands on the line an event at ten
                // starts on. Off by half a line, the whole column reads an hour wrong.
                Canvas.SetTop(label, (hour - firstHour) * HourHeight - 6);
                Canvas.SetRight(label, 4);
                column.Children.Add(label);
            }

            Grid.SetColumn(column, 0);
            Grid.SetRow(column, 2);
            root.Children.Add(column);
        }

        // ======================================================================
        // ONE DAY
        // ======================================================================

        private static void AddDayColumn(Grid root, int index, DateTime day, IReadOnlyList<AgendaEvent> events,
                                         int firstHour, int lastHour,
                                         Action<AgendaEvent>? open, Action<AgendaEvent>? edit,
                                         Action<AgendaEvent>? delete)
        {
            double height = (lastHour - firstHour) * HourHeight;

            var canvas = new Canvas
            {
                Height = height,
                Background = Brushes.Transparent,
                ClipToBounds = true
            };

            // A line down the left of every column. Without them the blocks float and
            // an empty Wednesday has no edges at all - which is most of what a week
            // grid is for.
            var framed = new Border
            {
                BorderBrush = Line,
                BorderThickness = new Thickness(1, 0, index == 0 ? 0 : 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
                Child = canvas
            };

            for (int hour = firstHour + 1; hour < lastHour; hour++)
            {
                var rule = new Border { Height = 1, Background = Line };
                Canvas.SetTop(rule, (hour - firstHour) * HourHeight);
                Canvas.SetLeft(rule, 0);
                canvas.Children.Add(rule);

                // Widened with the column, because a Canvas does not stretch what it holds.
                canvas.SizeChanged += (s, e) => rule.Width = canvas.ActualWidth;
            }

            List<AgendaEvent> ofDay = events
                .Where(e => !e.IsAllDay && e.Start.Date <= day && e.End > day && e.Start < day.AddDays(1))
                .OrderBy(e => e.Start)
                .ToList();

            // Overlapping events share the width instead of hiding one another, which
            // is the whole reason a grid beats a list for a busy morning.
            List<List<AgendaEvent>> lanes = IntoLanes(ofDay);

            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                foreach (AgendaEvent item in lanes[laneIndex])
                {
                    DateTime start = item.Start < day ? day : item.Start;
                    DateTime end = item.End > day.AddDays(1) ? day.AddDays(1) : item.End;

                    double top = (start - day).TotalHours - firstHour;
                    double span = (end - start).TotalHours;

                    Border block = Block(item, span * HourHeight, open, edit, delete);

                    Canvas.SetTop(block, Math.Max(0, top * HourHeight));
                    block.Height = Math.Max(HourHeight / 2, span * HourHeight - 2);

                    int lane = laneIndex;
                    int laneCount = lanes.Count;

                    void Place()
                    {
                        double width = Math.Max(10, canvas.ActualWidth / laneCount);
                        block.Width = width - 2;
                        Canvas.SetLeft(block, lane * width);
                    }

                    canvas.SizeChanged += (s, e) => Place();
                    canvas.Children.Add(block);
                }
            }

            Grid.SetColumn(framed, index + 1);
            Grid.SetRow(framed, 2);
            root.Children.Add(framed);
        }

        /// <summary>
        /// Puts events into as few side-by-side lanes as their overlaps allow: an event
        /// goes in the first lane whose last entry has already finished.
        /// </summary>
        private static List<List<AgendaEvent>> IntoLanes(List<AgendaEvent> ofDay)
        {
            var lanes = new List<List<AgendaEvent>>();

            foreach (AgendaEvent item in ofDay)
            {
                List<AgendaEvent>? free = lanes.FirstOrDefault(l => l[l.Count - 1].End <= item.Start);

                if (free == null)
                {
                    free = new List<AgendaEvent>();
                    lanes.Add(free);
                }

                free.Add(item);
            }

            return lanes.Count == 0 ? new List<List<AgendaEvent>>() : lanes;
        }

        // ======================================================================
        // PIECES
        // ======================================================================

        /// <summary>
        /// One event, with as much in it as its height allows.
        ///
        /// A half-hour meeting has room for one line, so the time goes on it beside the
        /// title; an afternoon has room to wrap the title and put the hours underneath.
        /// Using one layout for both would either waste a tall block or clip a short
        /// one to nothing.
        /// </summary>
        private static Border Block(AgendaEvent item, double height, Action<AgendaEvent>? open,
                                    Action<AgendaEvent>? edit, Action<AgendaEvent>? delete)
        {
            string from = item.Start.ToString("HH:mm", CultureInfo.CurrentCulture);
            string to = item.End.ToString("HH:mm", CultureInfo.CurrentCulture);

            var block = new Border
            {
                Background = Tint(item.ColourHex, 200),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 3, 1),
                Cursor = Cursors.Hand,
                ToolTip = item.Title + Environment.NewLine + from + " - " + to
                        + (string.IsNullOrWhiteSpace(item.Location) ? "" : Environment.NewLine + item.Location)
            };

            if (height >= HourHeight * 1.4)
            {
                var stack = new StackPanel();

                stack.Children.Add(new TextBlock
                {
                    Text = item.Title,
                    FontSize = 10,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                stack.Children.Add(new TextBlock
                {
                    Text = from + " - " + to,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                block.Child = stack;
            }
            else
            {
                block.Child = new TextBlock
                {
                    Text = item.Title + ", " + from,
                    FontSize = 9,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap
                };
            }

            Wire(block, item, open, edit, delete);
            return block;
        }

        private static Border Chip(AgendaEvent item, Action<AgendaEvent>? open,
                                   Action<AgendaEvent>? edit, Action<AgendaEvent>? delete)
        {
            var chip = new Border
            {
                Background = Tint(item.ColourHex, 200),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 1, 3, 1),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand,
                ToolTip = item.Title + Environment.NewLine + Strings.AgendaAllDay,
                Child = new TextBlock
                {
                    Text = item.Title,
                    FontSize = 9,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };

            Wire(chip, item, open, edit, delete);
            return chip;
        }

        /// <summary>The same gestures as a card in the list, so one view does not teach habits the other refuses.</summary>
        private static void Wire(Border target, AgendaEvent item, Action<AgendaEvent>? open,
                                 Action<AgendaEvent>? edit, Action<AgendaEvent>? delete)
        {
            target.MouseLeftButtonUp += (s, e) =>
            {
                if (e.ClickCount == 2) open?.Invoke(item);
            };

            var menu = new ContextMenu();

            if (item.CanWrite)
            {
                var change = new MenuItem { Header = Strings.AgendaEditEvent };
                change.Click += (s, e) => edit?.Invoke(item);
                menu.Items.Add(change);

                var remove = new MenuItem { Header = Strings.AgendaDeleteEvent };
                remove.Click += (s, e) => delete?.Invoke(item);
                menu.Items.Add(remove);

                menu.Items.Add(new Separator());
            }

            var openItem = new MenuItem { Header = Strings.AgendaOpenInGoogle };
            openItem.Click += (s, e) => open?.Invoke(item);
            menu.Items.Add(openItem);

            target.ContextMenu = menu;
        }

        /// <summary>
        /// The calendar's colour at a fixed opacity, so a pale calendar and a dark one
        /// both keep white text readable on top of them.
        /// </summary>
        internal static Brush Tint(string hex, byte alpha)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex))
                {
                    var colour = (Color)ColorConverter.ConvertFromString(hex);
                    return new SolidColorBrush(Color.FromArgb(alpha, colour.R, colour.G, colour.B));
                }
            }
            catch (Exception)
            {
                // A colour the source invented is not worth a broken column.
            }

            return new SolidColorBrush(Color.FromArgb(alpha, 100, 150, 255));
        }
    }
}
