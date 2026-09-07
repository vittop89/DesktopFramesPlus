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

        /// <summary>
        /// The current-time line. Red because no calendar hands red to an event by
        /// default, so the marker cannot be mistaken for something booked.
        /// </summary>
        private static readonly Brush Now = new SolidColorBrush(Color.FromRgb(234, 67, 53));

        public static UIElement Build(IReadOnlyList<AgendaEvent> events, DateTime from, int days,
                                      bool flashToday,
                                      AgendaActions actions)
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
            AddAllDayStrip(root, events, columnDays, actions);
            AddHourLabels(root, firstHour, lastHour);

            for (int i = 0; i < columnDays.Count; i++)
                AddDayColumn(root, i, columnDays[i], events, firstHour, lastHour, flashToday,
                             actions);

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
                                           AgendaActions actions)
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
                    stack.Children.Add(Chip(item, actions));

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
                                         int firstHour, int lastHour, bool flashToday,
                                         AgendaActions actions)
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

            // Where everything goes is worked out first, and separately: that part is
            // arithmetic and is checked by tests, while what follows is only drawing.
            List<AgendaEvent> ofDay = AgendaLayout.OfDay(events, day);

            foreach (AgendaPlacement placement in AgendaLayout.Place(ofDay, day, firstHour))
            {
                AgendaEvent item = placement.Item;

                Border block = Block(item, placement.SpanHours * HourHeight, actions);

                Canvas.SetTop(block, placement.TopHours * HourHeight);
                block.Height = Math.Max(HourHeight / 2, placement.SpanHours * HourHeight - 2);

                int lane = placement.Lane;
                int laneCount = placement.Lanes;

                void Place()
                {
                    double width = Math.Max(10, canvas.ActualWidth / laneCount);
                    block.Width = width - 2;
                    Canvas.SetLeft(block, lane * width);
                }

                canvas.SizeChanged += (s, e) => Place();
                canvas.Children.Add(block);
            }

            // Last, so it lies over the events rather than under them: a line hidden
            // behind a block would be worse than no line at all.
            AddNowLine(canvas, day, firstHour, lastHour);

            if (flashToday && day == DateTime.Today) FlashToday(framed);

            Grid.SetColumn(framed, index + 1);
            Grid.SetRow(framed, 2);
            root.Children.Add(framed);
        }

        /// <summary>
        /// The line marking the current time across today's column.
        ///
        /// It answers, without reading anything, the two questions somebody glances at
        /// a calendar for: what is happening now, and how much of the day is left.
        ///
        /// It moves with the minute refresh rather than a timer of its own. The grid is
        /// rebuilt each time the events are asked for, so the line lands in its new
        /// place for free - and a second timer running all day to shift a line by two
        /// pixels would be paying for precision nobody looks for.
        /// </summary>
        private static void AddNowLine(Canvas canvas, DateTime day, int firstHour, int lastHour)
        {
            if (day != DateTime.Today) return;

            double hours = DateTime.Now.TimeOfDay.TotalHours;
            if (hours < firstHour || hours > lastHour) return;

            double top = (hours - firstHour) * HourHeight;

            var line = new Border { Height = 1, Background = Now };
            Canvas.SetTop(line, top);
            Canvas.SetLeft(line, 0);
            Panel.SetZIndex(line, 100);
            canvas.Children.Add(line);
            canvas.SizeChanged += (s, e) => line.Width = canvas.ActualWidth;

            // The dot at the left end is what makes the line read as a marker rather
            // than as another hour rule.
            var knob = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Background = Now
            };
            Canvas.SetTop(knob, top - 3);
            Canvas.SetLeft(knob, -3);
            Panel.SetZIndex(knob, 101);
            canvas.Children.Add(knob);
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
        private static Border Block(AgendaEvent item, double height, AgendaActions actions)
        {
            string from = item.Start.ToString("HH:mm", CultureInfo.CurrentCulture);
            string to = item.End.ToString("HH:mm", CultureInfo.CurrentCulture);

            var block = new Border
            {
                Background = Tint(item.ColourHex, 200),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 3, 1),
                Cursor = Cursors.Hand,

                // A backstop for the awkward sizes: whatever the text does, it stops at
                // the edge of its own event rather than being read across somebody
                // else's.
                ClipToBounds = true,
                ToolTip = item.Title + Environment.NewLine + from + " - " + to
                        + (string.IsNullOrWhiteSpace(item.Location) ? "" : Environment.NewLine + item.Location)
            };

            if (height >= HourHeight * 1.4)
            {
                // A grid rather than a stack, because a stack gives each child all the
                // height it asks for. A long title then wraps to three lines and grows
                // straight out of the block and over the next one, and the ellipsis
                // never appears - nothing ever told the text it had run out of room.
                // Here the hours take what they need and the title takes what is left,
                // which is the constraint that makes trimming happen at all.
                var stack = new Grid();
                stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var title = new TextBlock
                {
                    Text = item.Title,
                    FontSize = 10,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextDecorations = item.IsDone ? TextDecorations.Strikethrough : null
                };

                var hours = new TextBlock
                {
                    Text = from + " - " + to,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                Grid.SetRow(hours, 1);
                stack.Children.Add(hours);

                if (item.IsTask)
                {
                    // The box takes what it needs and the title takes the rest, so a
                    // long title still runs out of room and trims instead of pushing
                    // the box off the block.
                    var titleRow = new Grid();
                    titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    CheckBox tick = Tick(item, actions);
                    Grid.SetColumn(tick, 0);
                    Grid.SetColumn(title, 1);

                    titleRow.Children.Add(tick);
                    titleRow.Children.Add(title);

                    Grid.SetRow(titleRow, 0);
                    stack.Children.Add(titleRow);
                }
                else
                {
                    Grid.SetRow(title, 0);
                    stack.Children.Add(title);
                }

                block.Child = stack;
            }
            else
            {
                var caption = new TextBlock
                {
                    Text = item.Title + ", " + from,
                    FontSize = 9,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    TextDecorations = item.IsDone ? TextDecorations.Strikethrough : null
                };

                if (item.IsTask)
                {
                    // The short layout needs the box as much as the tall one does, and
                    // arguably more: a task Google gives an hour to is exactly the size
                    // that lands here, so leaving it out hid the tick on the ordinary
                    // case and showed it only on the long ones.
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    CheckBox tick = Tick(item, actions);
                    Grid.SetColumn(tick, 0);
                    Grid.SetColumn(caption, 1);

                    row.Children.Add(tick);
                    row.Children.Add(caption);

                    block.Child = row;
                }
                else
                {
                    block.Child = caption;
                }
            }

            Wire(block, item, actions);
            return block;
        }

        /// <summary>
        /// The box that says a task is done, shrunk to sit inside a block.
        ///
        /// The click is swallowed here on purpose. The block underneath opens the entry
        /// when clicked, and ticking something off should tick it off - not tick it off
        /// and then open a browser on top of the calendar somebody was reading.
        /// </summary>
        private static CheckBox Tick(AgendaEvent item, AgendaActions actions)
        {
            var box = new CheckBox
            {
                IsChecked = item.IsDone,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 3, 0),
                LayoutTransform = new ScaleTransform(0.8, 0.8)
            };

            box.Checked += (s, e) => actions.SetDone?.Invoke(item, true);
            box.Unchecked += (s, e) => actions.SetDone?.Invoke(item, false);
            box.MouseLeftButtonUp += (s, e) => e.Handled = true;

            return box;
        }

        private static Border Chip(AgendaEvent item, AgendaActions actions)
        {
            var chip = new Border
            {
                Background = Tint(item.ColourHex, 200),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 1, 3, 1),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand,
                ToolTip = item.Title + Environment.NewLine + Strings.AgendaAllDay
            };

            var caption = new TextBlock
            {
                Text = item.Title,
                FontSize = 9,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextDecorations = item.IsDone ? TextDecorations.Strikethrough : null
            };

            if (item.IsTask)
            {
                // A task keeps its box wherever it is drawn. A tick that exists in the
                // list but not in the week view teaches that the week view is only for
                // looking at, which is not true of anything else in it.
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(Tick(item, actions));
                row.Children.Add(caption);
                chip.Child = row;
            }
            else
            {
                chip.Child = caption;
            }

            Wire(chip, item, actions);
            return chip;
        }

        /// <summary>The same gestures as a card in the list, so one view does not teach habits the other refuses.</summary>
        private static void Wire(Border target, AgendaEvent item, AgendaActions actions)
        {
            target.MouseLeftButtonUp += (s, e) =>
            {
                if (e.ClickCount == 2) actions.Open?.Invoke(item);
            };

            var menu = new ContextMenu();

            if (item.CanWrite)
            {
                var change = new MenuItem { Header = Strings.AgendaEditEvent };
                change.Click += (s, e) => actions.Edit?.Invoke(item);
                menu.Items.Add(change);

                var remove = new MenuItem { Header = Strings.AgendaDeleteEvent };
                remove.Click += (s, e) => actions.Delete?.Invoke(item);
                menu.Items.Add(remove);

                menu.Items.Add(new Separator());
            }

            var openItem = new MenuItem { Header = Strings.AgendaOpenInGoogle };
            openItem.Click += (s, e) => actions.Open?.Invoke(item);
            menu.Items.Add(openItem);

            target.ContextMenu = menu;
        }

        /// <summary>
        /// Blinks a day's background, so that pressing "today" after paging through
        /// three months says where today went instead of leaving somebody to find the
        /// bold number themselves.
        ///
        /// Three quick pulses rather than one slow fade: a single change of colour on a
        /// grid this busy is easy to miss entirely, and anything longer turns into
        /// decoration on a frame somebody has to keep looking at all day.
        /// </summary>
        internal static void FlashToday(Border target)
        {
            var brush = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255));
            target.Background = brush;

            var pulse = new System.Windows.Media.Animation.ColorAnimation
            {
                To = Color.FromArgb(150, 255, 214, 0),
                Duration = TimeSpan.FromMilliseconds(200),
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(3)
            };

            // Started when the element reaches the screen: an animation begun on a
            // control that is not in the visual tree yet plays to nobody.
            target.Loaded += (s, e) =>
                brush.BeginAnimation(SolidColorBrush.ColorProperty, pulse);
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
