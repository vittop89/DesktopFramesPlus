using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// What a grid of hours keeps across being rebuilt, held by the frame.
    ///
    /// The grid is rebuilt whenever the events come in - once a minute. A new scroll
    /// viewer starts at the top, and a new grid knows nothing of a drag under way.
    /// Without somewhere to keep both, reading the evening meant being sent back to the
    /// morning every minute, and a refresh landing mid-drag would drop the entry
    /// wherever the pointer happened to be.
    /// </summary>
    public sealed class AgendaGridState
    {
        /// <summary>Distance from the top, or null to open at the default hour.</summary>
        public double? Offset { get; set; }

        /// <summary>
        /// True while an entry is held under the pointer. Nothing may rebuild the grid
        /// then: the grid it would throw away is the one holding the drag.
        /// </summary>
        public bool Dragging { get; private set; }

        /// <summary>When a drag ends, however it ends - so that a redraw held back for it can happen.</summary>
        public event Action? DragEnded;

        internal void BeginDrag() => Dragging = true;

        internal void EndDrag()
        {
            Dragging = false;
            DragEnded?.Invoke();
        }
    }

    /// <summary>
    /// Days as columns, with the hours running down the side and every event drawn
    /// where it actually falls - the shape a calendar has had since before there were
    /// computers, and the one that answers "how full is Thursday" at a glance, which
    /// a list cannot.
    ///
    /// It draws, and turns gestures into requests. What to do about a click or a drag
    /// goes back out through the callbacks it is given, the same way
    /// <see cref="AgendaRenderer"/> works.
    /// </summary>
    public static class AgendaTimeGrid
    {
        private const double HourHeight = 26;
        private const double HourColumnWidth = 34;

        private static readonly Brush Faint = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
        private static readonly Brush Line = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
        private static readonly Brush Strong = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));

        /// <summary>The half hour under the pointer in an empty stretch of a day: where a click would put something.</summary>
        private static readonly Brush Hint = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));

        /// <summary>
        /// The current-time line. Red because no calendar hands red to an event by
        /// default, so the marker cannot be mistaken for something booked.
        /// </summary>
        private static readonly Brush Now = new SolidColorBrush(Color.FromRgb(234, 67, 53));

        public static UIElement Build(IReadOnlyList<AgendaEvent> events, DateTime from, int days,
                                      bool flashToday, AgendaActions actions, AgendaGridState state)
        {
            List<DateTime> columnDays = Enumerable.Range(0, days).Select(i => from.Date.AddDays(i)).ToList();

            // The top: day names, and the entries with no hour. It stays put, so the day
            // a column belongs to can be read however far down the hours have gone.
            Grid top = Columns(columnDays.Count);
            top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // day names
            top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // all-day strip

            AddHeaders(top, columnDays);
            AddAllDayStrip(top, events, columnDays, actions);

            // The hours: all of them, midnight to midnight. A band fitted around the
            // day's events hid the rest of it - nothing late in the evening could be
            // seen, or added, until an event there had widened the band first.
            Grid hours = Columns(columnDays.Count);
            AddHourLabels(hours);

            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            // Above every day, for an entry being dragged. It spans all of them so the
            // entry can be carried from one day to the next, and it takes no clicks.
            var overlay = new Canvas { IsHitTestVisible = false };
            Grid.SetColumn(overlay, 1);
            Grid.SetColumnSpan(overlay, columnDays.Count);
            Panel.SetZIndex(overlay, 1000);

            var drags = new EntryDrags(overlay, scroller, columnDays, actions, state);

            for (int i = 0; i < columnDays.Count; i++)
                AddDayColumn(hours, i, columnDays[i], events, flashToday, actions, drags);

            hours.Children.Add(overlay);
            scroller.Content = hours;

            KeepPlace(scroller, top, state,
                      state.Offset ?? AgendaLayout.DefaultTopHour(events, columnDays, DateTime.Now) * HourHeight);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(top, 0);
            Grid.SetRow(scroller, 1);
            root.Children.Add(top);
            root.Children.Add(scroller);

            return root;
        }

        // ======================================================================
        // FRAME OF THE GRID
        // ======================================================================

        /// <summary>The hour column and one column per day: the shape both halves of the grid share.</summary>
        private static Grid Columns(int days)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HourColumnWidth) });

            for (int i = 0; i < days; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            return grid;
        }

        /// <summary>
        /// Opens the hours at <paramref name="offset"/>, and remembers where they are
        /// taken after that.
        ///
        /// Scrolled in the first size change rather than on Loaded. Loaded arrives after
        /// the grid has been drawn once, so the hours showed midnight for a moment and
        /// then jumped - once a minute, with every refresh. A size change happens inside
        /// the layout pass, and a scroll asked for there is in place before anything is
        /// drawn.
        /// </summary>
        private static void KeepPlace(ScrollViewer scroller, FrameworkElement top, AgendaGridState state,
                                      double offset)
        {
            bool placed = false;

            scroller.SizeChanged += (s, e) =>
            {
                if (placed) return;

                placed = true;
                scroller.ScrollToVerticalOffset(offset);
            };

            scroller.ScrollChanged += (s, e) =>
            {
                if (placed) state.Offset = scroller.VerticalOffset;

                // The scroll bar takes its width from the hours and not from the header
                // above them, so the header gives up the same width - otherwise every day
                // name sits a little to the right of its own column.
                double bar = Math.Max(0, scroller.ActualWidth - scroller.ViewportWidth);
                if (Math.Abs(top.Margin.Right - bar) > 0.5) top.Margin = new Thickness(0, 0, bar, 0);
            };
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

        private static void AddHourLabels(Grid root)
        {
            var column = new Canvas { Height = 24 * HourHeight };

            for (int hour = 0; hour < 24; hour++)
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
                // Midnight is the exception: its line is the top edge, and the half of a
                // label above the edge would be cut off.
                Canvas.SetTop(label, hour == 0 ? 0 : hour * HourHeight - 6);
                Canvas.SetRight(label, 4);
                column.Children.Add(label);
            }

            Grid.SetColumn(column, 0);
            root.Children.Add(column);
        }

        // ======================================================================
        // ONE DAY
        // ======================================================================

        private static void AddDayColumn(Grid root, int index, DateTime day, IReadOnlyList<AgendaEvent> events,
                                         bool flashToday, AgendaActions actions, EntryDrags drags)
        {
            var canvas = new Canvas
            {
                Height = 24 * HourHeight,
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

            for (int hour = 1; hour < 24; hour++)
            {
                // Not hit-testable, so the line between two hours belongs to the empty
                // day it crosses: a click landing exactly on it still means "here".
                var rule = new Border { Height = 1, Background = Line, IsHitTestVisible = false };
                Canvas.SetTop(rule, hour * HourHeight);
                Canvas.SetLeft(rule, 0);
                canvas.Children.Add(rule);

                // Widened with the column, because a Canvas does not stretch what it holds.
                canvas.SizeChanged += (s, e) => rule.Width = canvas.ActualWidth;
            }

            // Where everything goes is worked out first, and separately: that part is
            // arithmetic and is checked by tests, while what follows is only drawing.
            List<AgendaEvent> ofDay = AgendaLayout.OfDay(events, day);

            foreach (AgendaPlacement placement in AgendaLayout.Place(ofDay, day, 0))
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

                if (CanReschedule(item)) drags.Attach(block, item, index, day);
            }

            // Last, so it lies over the events rather than under them: a line hidden
            // behind a block would be worse than no line at all.
            AddNowLine(canvas, day);

            if (actions.CreateAt != null) OfferNew(canvas, day, actions.CreateAt);

            if (flashToday && day == DateTime.Today) FlashToday(framed);

            Grid.SetColumn(framed, index + 1);
            root.Children.Add(framed);
        }

        /// <summary>
        /// A click in an empty stretch of a day starts something new at that half hour -
        /// the gesture every calendar has, and the one somebody tries first.
        ///
        /// Only the empty stretches: a click on an entry belongs to the entry. And only
        /// a click, not the end of a drag - a press that wanders off before it is let go
        /// was going somewhere else. The press is claimed as well, so that it does not
        /// carry on up to the frame and start moving the whole window.
        ///
        /// While the pointer is over an empty stretch, the half hour under it is lit
        /// with its time, so a click says in advance what it will do.
        /// </summary>
        private static void OfferNew(Canvas canvas, DateTime day, Action<DateTime> createAt)
        {
            var caption = new TextBlock
            {
                FontSize = 9,
                Foreground = Strong,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var hint = new Border
            {
                Height = HourHeight / 2,
                CornerRadius = new CornerRadius(3),
                Background = Hint,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                Child = caption
            };

            Panel.SetZIndex(hint, 50);
            canvas.Children.Add(hint);
            canvas.SizeChanged += (s, e) => hint.Width = canvas.ActualWidth;

            // Everything drawn on the canvas that is not an entry is left out of hit
            // testing, so "the canvas itself was hit" means "an empty stretch was hit".
            bool OnEmpty(RoutedEventArgs e) => ReferenceEquals(e.OriginalSource, canvas);

            canvas.MouseMove += (s, e) =>
            {
                if (!OnEmpty(e))
                {
                    hint.Visibility = Visibility.Collapsed;
                    return;
                }

                double slot = SlotAt(e.GetPosition(canvas).Y);

                Canvas.SetTop(hint, slot * HourHeight);
                caption.Text = "+ " + day.Date.AddHours(slot).ToString("HH:mm", CultureInfo.CurrentCulture);
                hint.Visibility = Visibility.Visible;
            };

            canvas.MouseLeave += (s, e) => hint.Visibility = Visibility.Collapsed;

            Point? pressed = null;

            canvas.MouseLeftButtonDown += (s, e) =>
            {
                pressed = null;
                if (!OnEmpty(e)) return;

                pressed = e.GetPosition(canvas);
                e.Handled = true;
            };

            canvas.MouseLeftButtonUp += (s, e) =>
            {
                Point? from = pressed;
                pressed = null;

                if (from == null || !OnEmpty(e)) return;
                if ((e.GetPosition(canvas) - from.Value).Length > 4) return;

                e.Handled = true;
                hint.Visibility = Visibility.Collapsed;

                createAt(day.Date.AddHours(SlotAt(from.Value.Y)));
            };
        }

        /// <summary>The half hour a height in a day column falls in, as hours from midnight.</summary>
        private static double SlotAt(double y) =>
            Math.Max(0, Math.Min(23.5, Math.Floor(y / (HourHeight / 2)) / 2));

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
        private static void AddNowLine(Canvas canvas, DateTime day)
        {
            if (day != DateTime.Today) return;

            double top = DateTime.Now.TimeOfDay.TotalHours * HourHeight;

            var line = new Border { Height = 1, Background = Now, IsHitTestVisible = false };
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
                Background = Now,
                IsHitTestVisible = false
            };
            Canvas.SetTop(knob, top - 3);
            Canvas.SetLeft(knob, -3);
            Panel.SetZIndex(knob, 101);
            canvas.Children.Add(knob);
        }

        // ======================================================================
        // DRAGGING
        // ======================================================================

        /// <summary>
        /// Whether an entry can be dragged: one that can be written to, with an hour to
        /// move. Not a task whose hour Google keeps - it cannot be changed from here - and
        /// not a calendar entry that only stands in for a task.
        /// </summary>
        private static bool CanReschedule(AgendaEvent item) =>
            item.CanWrite
            && !item.IsAllDay
            && string.IsNullOrEmpty(item.MirrorOfTask)
            && (!item.IsTask || item.HourIsLocal);

        private enum DragKind
        {
            /// <summary>The whole entry, to another time or day.</summary>
            Move,

            /// <summary>Only its end, by the bottom edge.</summary>
            Stretch
        }

        /// <summary>One press on an entry, from the moment the button goes down.</summary>
        private sealed class Drag
        {
            public Drag(AgendaEvent item, Border block, DragKind kind, int column, DateTime day, Point press)
            {
                Item = item;
                Block = block;
                Kind = kind;
                Column = column;
                Day = day;
                Press = press;
                Start = item.Start;
                End = item.End;
            }

            public AgendaEvent Item { get; }

            public Border Block { get; }

            public DragKind Kind { get; }

            /// <summary>The column the press was in.</summary>
            public int Column { get; }

            /// <summary>That column's day.</summary>
            public DateTime Day { get; }

            /// <summary>Where the press was, measured over the days.</summary>
            public Point Press { get; }

            /// <summary>False until the pointer has moved far enough to mean a drag rather than a click.</summary>
            public bool Started { get; set; }

            /// <summary>Where the entry would land if the button were let go now.</summary>
            public DateTime Start { get; set; }

            public DateTime End { get; set; }

            public Border? Shadow { get; set; }

            public TextBlock? Caption { get; set; }
        }

        /// <summary>
        /// Moving entries with the mouse, stretching them by their bottom edge, and copying
        /// them by holding Ctrl - the gestures a desktop calendar has.
        ///
        /// One per grid, drawing above all of its days rather than inside one: an entry
        /// carried to another day leaves the column it started in, and anything drawn
        /// inside that column would vanish at its edge.
        ///
        /// Nothing is changed until the button is let go. Until then there is only a shadow
        /// where the entry would land, with the time it would have, and the entry itself
        /// stays where it is - faded when moving, whole when copying. Letting go where it
        /// started changes nothing, and neither does losing the mouse to another window.
        /// </summary>
        private sealed class EntryDrags
        {
            /// <summary>How far the pointer moves before a press is a drag. Short of it, a click stays a click.</summary>
            private const double Threshold = 4;

            /// <summary>The steps a drag moves in, in minutes: the quarter hours a calendar is usually cut into.</summary>
            private const int Step = 15;

            /// <summary>The strip at the bottom of an entry that stretches it rather than moving it.</summary>
            private const double Edge = 5;

            private readonly Canvas _overlay;
            private readonly ScrollViewer _scroller;
            private readonly List<DateTime> _days;
            private readonly AgendaActions _actions;
            private readonly AgendaGridState _state;

            private Drag? _drag;
            private DispatcherTimer? _edgeScroll;

            public EntryDrags(Canvas overlay, ScrollViewer scroller, List<DateTime> days,
                              AgendaActions actions, AgendaGridState state)
            {
                _overlay = overlay;
                _scroller = scroller;
                _days = days;
                _actions = actions;
                _state = state;
            }

            public void Attach(Border block, AgendaEvent item, int column, DateTime day)
            {
                // Only where the end is in this column. The bottom of an entry that runs on
                // past midnight is the edge of the day, not the end of the entry.
                bool canStretch = item.End > day && item.End <= day.AddDays(1);

                block.MouseMove += (s, e) =>
                {
                    if (_drag != null)
                    {
                        if (_drag.Block == block) Follow(_drag, e.GetPosition(_overlay));
                        return;
                    }

                    block.Cursor = canStretch && OnEdge(block, e) ? Cursors.SizeNS : Cursors.Hand;
                };

                block.MouseLeftButtonDown += (s, e) =>
                {
                    if (_drag != null) return;

                    DragKind kind = canStretch && OnEdge(block, e) ? DragKind.Stretch : DragKind.Move;
                    _drag = new Drag(item, block, kind, column, day, e.GetPosition(_overlay));

                    block.CaptureMouse();

                    // Kept from the day underneath, where a press starts a new entry.
                    e.Handled = true;
                };

                block.MouseLeftButtonUp += (s, e) =>
                {
                    Drag? drag = _drag;
                    if (drag == null || drag.Block != block) return;

                    // Cleared before letting go of the mouse, which announces a lost
                    // capture - and that must not read as the drag being called off.
                    _drag = null;
                    block.ReleaseMouseCapture();

                    // A click rather than a drag, and a click belongs to the entry as before.
                    if (!drag.Started) return;

                    e.Handled = true;
                    End(drag, keep: true);
                };

                block.LostMouseCapture += (s, e) =>
                {
                    Drag? drag = _drag;
                    if (drag == null || drag.Block != block) return;

                    // Taken away - by a dialog, by another window - rather than let go.
                    _drag = null;
                    if (drag.Started) End(drag, keep: false);
                };
            }

            private static bool OnEdge(Border block, MouseEventArgs e) =>
                e.GetPosition(block).Y >= block.ActualHeight - Edge;

            private void Follow(Drag drag, Point pointer)
            {
                if (!drag.Started)
                {
                    if ((pointer - drag.Press).Length < Threshold) return;
                    Begin(drag);
                }

                Place(drag, pointer);
            }

            private void Begin(Drag drag)
            {
                drag.Started = true;
                _state.BeginDrag();

                drag.Caption = new TextBlock
                {
                    FontSize = 9,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                drag.Shadow = new Border
                {
                    Background = Tint(drag.Item.ColourHex, 235),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 3, 1),
                    ClipToBounds = true,
                    Child = drag.Caption
                };

                _overlay.Children.Add(drag.Shadow);
                drag.Block.Cursor = drag.Kind == DragKind.Stretch ? Cursors.SizeNS : Cursors.SizeAll;

                // Near the top or bottom of the hours, they scroll: most of a day is out of
                // sight in a frame, and otherwise an entry could only be carried as far as
                // the part that shows. On a timer, so holding still at the edge keeps
                // scrolling, as it does in any list.
                _edgeScroll = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(40) };
                _edgeScroll.Tick += (s, e) => AtEdge(drag);
                _edgeScroll.Start();
            }

            private void AtEdge(Drag drag)
            {
                const double Zone = 24;
                const double Fastest = 8;

                double y = Mouse.GetPosition(_scroller).Y;
                double bottom = _scroller.ViewportHeight;

                double step = y < Zone ? -Math.Min(Fastest, (Zone - y) / 3)
                            : y > bottom - Zone ? Math.Min(Fastest, (y - (bottom - Zone)) / 3)
                            : 0;

                if (step != 0) _scroller.ScrollToVerticalOffset(_scroller.VerticalOffset + step);

                // Placed again even when nothing scrolled: Ctrl can be pressed or let go
                // without the mouse moving, and the shadow should say at once which it is.
                Place(drag, Mouse.GetPosition(_overlay));
            }

            private void Place(Drag drag, Point pointer)
            {
                double columnWidth = _overlay.ActualWidth / _days.Count;
                if (columnWidth <= 0) return;

                AgendaEvent item = drag.Item;

                // Moved by whole steps from where it was, not snapped to the clock: an entry
                // at 10:10 dragged down an hour lands at 11:10, and one dragged back to where
                // it started has not moved at all.
                int minutes = (int)Math.Round((pointer.Y - drag.Press.Y) / HourHeight * 60 / Step) * Step;

                if (drag.Kind == DragKind.Stretch)
                {
                    DateTime shortest = Later(item.Start, drag.Day).AddMinutes(Step);

                    drag.Start = item.Start;
                    drag.End = Clamp(item.End.AddMinutes(minutes), shortest, drag.Day.AddDays(1));
                }
                else
                {
                    int column = Math.Max(0, Math.Min(_days.Count - 1, (int)Math.Floor(pointer.X / columnWidth)));

                    // A task keeps its day. Its hour is this program's to keep, but its day is
                    // Google's, and a drag is too casual a gesture to be rewriting that - the
                    // form is the place for it.
                    DateTime moved = item.Start.AddDays(item.IsTask ? 0 : column - drag.Column);

                    drag.Start = Clamp(moved.AddMinutes(minutes), moved.Date, moved.Date.AddDays(1).AddMinutes(-Step));
                    drag.End = drag.Start + (item.End - item.Start);
                }

                bool copy = IsCopy(drag);
                drag.Block.Opacity = copy ? 1 : 0.35;

                DrawShadow(drag, columnWidth, copy);
            }

            /// <summary>
            /// Ctrl held while moving an event. Read again every time, and once more when the
            /// button is let go, which is the moment it counts. A task is never copied: there
            /// is no copy of a task a drag could ask Google for.
            ///
            /// Asked of the system rather than of WPF. A frame never takes keyboard focus -
            /// that is what keeps it from stealing it from whatever somebody is typing in -
            /// so no key ever reaches it, and Keyboard.Modifiers would say Ctrl is up
            /// however hard it is held.
            /// </summary>
            private static bool IsCopy(Drag drag) =>
                drag.Kind == DragKind.Move
                && !drag.Item.IsTask
                && ControlIsDown();

            private const int VirtualKeyControl = 0x11;

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern short GetAsyncKeyState(int virtualKey);

            private static bool ControlIsDown() => (GetAsyncKeyState(VirtualKeyControl) & 0x8000) != 0;

            private void DrawShadow(Drag drag, double columnWidth, bool copy)
            {
                if (drag.Shadow == null || drag.Caption == null) return;

                DateTime day = drag.Kind == DragKind.Stretch ? drag.Day : drag.Start.Date;
                int column = _days.IndexOf(day);

                // Carried onto a day this view does not show. There is nowhere to draw the
                // shadow, but letting go still puts the entry there.
                if (column < 0)
                {
                    drag.Shadow.Visibility = Visibility.Collapsed;
                    return;
                }

                DateTime from = Later(drag.Start, day);
                DateTime to = Earlier(drag.End, day.AddDays(1));

                drag.Shadow.Visibility = Visibility.Visible;
                drag.Shadow.Width = Math.Max(10, columnWidth - 4);
                drag.Shadow.Height = Math.Max(HourHeight / 2, (to - from).TotalHours * HourHeight - 2);
                Canvas.SetLeft(drag.Shadow, column * columnWidth + 2);
                Canvas.SetTop(drag.Shadow, (from - day).TotalHours * HourHeight);

                drag.Caption.Text = (copy ? "+ " : "") + drag.Item.Title + Environment.NewLine
                                  + drag.Start.ToString("HH:mm", CultureInfo.CurrentCulture) + " - "
                                  + drag.End.ToString("HH:mm", CultureInfo.CurrentCulture);
            }

            private void End(Drag drag, bool keep)
            {
                _edgeScroll?.Stop();
                _edgeScroll = null;

                if (drag.Shadow != null) _overlay.Children.Remove(drag.Shadow);
                drag.Block.Opacity = 1;
                drag.Block.Cursor = Cursors.Hand;

                bool copy = IsCopy(drag);
                bool changed = drag.Start != drag.Item.Start || drag.End != drag.Item.End;

                _state.EndDrag();

                if (keep && changed) _actions.Reschedule?.Invoke(drag.Item, drag.Start, drag.End, copy);
            }
        }

        private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

        private static DateTime Earlier(DateTime a, DateTime b) => a < b ? a : b;

        private static DateTime Clamp(DateTime value, DateTime low, DateTime high) =>
            value < low ? low : value > high ? high : value;

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
            // A double click opens the form here when the entry can be changed - what a
            // double click means in every calendar - and Google's own page when it
            // cannot, the only place a read-only entry can be looked at in full.
            target.MouseLeftButtonUp += (s, e) =>
            {
                if (e.ClickCount != 2) return;

                if (item.CanWrite) actions.Edit?.Invoke(item);
                else actions.Open?.Invoke(item);
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
