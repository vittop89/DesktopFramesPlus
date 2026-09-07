using Desktop_Frames.Plugins.GoogleAgenda;
using Xunit;

namespace Desktop_Frames.Tests
{
    /// <summary>
    /// Where a day's entries land in the grid.
    ///
    /// Arithmetic that used to sit inside the drawing code, where the only way to
    /// check it was to look at a frame and judge. The awkward cases are the ones at
    /// the edges of the day - something that began yesterday, something running past
    /// midnight - and those are exactly the ones nobody has on screen when they
    /// change the code.
    /// </summary>
    public class AgendaLayoutTests
    {
        private static readonly DateTime Day = new DateTime(2026, 9, 8);

        private static AgendaEvent At(int fromHour, int toHour, string title = "x") => new AgendaEvent
        {
            Id = title,
            Title = title,
            Start = Day.AddHours(fromHour),
            End = Day.AddHours(toHour)
        };

        // ------------------------------------------------------------ lanes

        [Fact]
        public void Entries_that_do_not_overlap_share_one_lane()
        {
            var ofDay = new List<AgendaEvent> { At(9, 10), At(10, 11), At(14, 15) };

            List<AgendaPlacement> placed = AgendaLayout.Place(ofDay, Day, 0);

            Assert.All(placed, p => Assert.Equal(0, p.Lane));
            Assert.All(placed, p => Assert.Equal(1, p.Lanes));
        }

        [Fact]
        public void Overlapping_entries_get_a_lane_each()
        {
            var ofDay = new List<AgendaEvent> { At(9, 11, "a"), At(10, 12, "b"), At(10, 11, "c") };

            List<AgendaPlacement> placed = AgendaLayout.Place(ofDay, Day, 0);

            Assert.All(placed, p => Assert.Equal(3, p.Lanes));
            Assert.Equal(new[] { 0, 1, 2 }, placed.Select(p => p.Lane));
        }

        [Fact]
        public void A_lane_is_reused_once_it_is_free()
        {
            // a and b overlap; c starts after a ends, so it goes back in a's lane
            // rather than opening a third.
            var ofDay = new List<AgendaEvent> { At(9, 10, "a"), At(9, 12, "b"), At(10, 11, "c") };

            List<AgendaPlacement> placed = AgendaLayout.Place(ofDay, Day, 0);

            Assert.Equal(2, placed[0].Lanes);
            Assert.Equal(0, placed.Single(p => p.Item.Title == "a").Lane);
            Assert.Equal(1, placed.Single(p => p.Item.Title == "b").Lane);
            Assert.Equal(0, placed.Single(p => p.Item.Title == "c").Lane);
        }

        [Fact]
        public void Touching_entries_do_not_count_as_overlapping()
        {
            // One ends exactly when the next begins. An end is exclusive, so they
            // share a lane; treating them as overlapping would halve the width of
            // every back-to-back morning.
            List<AgendaPlacement> placed =
                AgendaLayout.Place(new List<AgendaEvent> { At(9, 10), At(10, 11) }, Day, 0);

            Assert.Equal(1, placed[0].Lanes);
        }

        // ------------------------------------------------------------ geometry

        [Fact]
        public void The_window_start_moves_everything_up()
        {
            List<AgendaPlacement> placed =
                AgendaLayout.Place(new List<AgendaEvent> { At(9, 10) }, Day, firstHour: 7);

            Assert.Equal(2, Assert.Single(placed).TopHours);
        }

        [Fact]
        public void An_entry_from_yesterday_starts_at_the_top_of_today()
        {
            var overnight = new AgendaEvent
            {
                Title = "overnight",
                Start = Day.AddHours(-3),      // 21:00 yesterday
                End = Day.AddHours(2)          // 02:00 today
            };

            AgendaPlacement placed =
                Assert.Single(AgendaLayout.Place(new List<AgendaEvent> { overnight }, Day, 0));

            Assert.Equal(0, placed.TopHours);
            Assert.Equal(2, placed.SpanHours);   // only today's part
        }

        [Fact]
        public void An_entry_running_into_tomorrow_stops_at_midnight()
        {
            var late = new AgendaEvent
            {
                Title = "late",
                Start = Day.AddHours(23),
                End = Day.AddHours(26)         // 02:00 tomorrow
            };

            AgendaPlacement placed =
                Assert.Single(AgendaLayout.Place(new List<AgendaEvent> { late }, Day, 0));

            Assert.Equal(23, placed.TopHours);
            Assert.Equal(1, placed.SpanHours);
        }

        [Fact]
        public void Nothing_is_placed_above_the_window()
        {
            // An entry earlier than the first hour shown would otherwise be given a
            // negative offset and drawn off the top of the column.
            List<AgendaPlacement> placed =
                AgendaLayout.Place(new List<AgendaEvent> { At(6, 8) }, Day, firstHour: 9);

            Assert.Equal(0, Assert.Single(placed).TopHours);
        }

        // ------------------------------------------------------------ selection

        [Fact]
        public void All_day_entries_belong_to_the_strip_not_the_grid()
        {
            var birthday = new AgendaEvent
            {
                Title = "compleanno",
                Start = Day,
                End = Day.AddDays(1),
                IsAllDay = true
            };

            Assert.Empty(AgendaLayout.OfDay(new List<AgendaEvent> { birthday }, Day));
        }

        [Fact]
        public void Only_this_day_is_taken()
        {
            var events = new List<AgendaEvent>
            {
                At(9, 10, "today"),
                new AgendaEvent { Title = "tomorrow", Start = Day.AddDays(1).AddHours(9),
                                  End = Day.AddDays(1).AddHours(10) },
                new AgendaEvent { Title = "yesterday", Start = Day.AddDays(-1).AddHours(9),
                                  End = Day.AddDays(-1).AddHours(10) }
            };

            AgendaEvent only = Assert.Single(AgendaLayout.OfDay(events, Day));
            Assert.Equal("today", only.Title);
        }

        [Fact]
        public void Something_still_running_from_yesterday_is_taken()
        {
            var overnight = new AgendaEvent
            {
                Title = "overnight",
                Start = Day.AddHours(-3),
                End = Day.AddHours(2)
            };

            Assert.Single(AgendaLayout.OfDay(new List<AgendaEvent> { overnight }, Day));
        }

        [Fact]
        public void Something_that_ended_exactly_at_midnight_is_not_taken()
        {
            // Its end is the first instant of today, and an end is exclusive: it
            // belongs entirely to yesterday.
            var yesterday = new AgendaEvent
            {
                Title = "yesterday",
                Start = Day.AddHours(-3),
                End = Day
            };

            Assert.Empty(AgendaLayout.OfDay(new List<AgendaEvent> { yesterday }, Day));
        }

        [Fact]
        public void The_day_is_taken_in_order_of_start()
        {
            var events = new List<AgendaEvent> { At(14, 15, "late"), At(9, 10, "early") };

            List<AgendaEvent> ofDay = AgendaLayout.OfDay(events, Day);

            Assert.Equal(new[] { "early", "late" }, ofDay.Select(e => e.Title));
        }
    }
}
