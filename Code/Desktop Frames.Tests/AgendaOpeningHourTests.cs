using Desktop_Frames.Plugins.GoogleAgenda;
using Xunit;

namespace Desktop_Frames.Tests
{
    /// <summary>
    /// Where a column of hours opens now that it holds the whole day.
    ///
    /// Twenty-four hours do not fit a frame, so the first thing shown decides whether
    /// the grid looks useful or empty. Today opens on now; any other day opens at the
    /// start of a working day, earlier only when something happens earlier.
    /// </summary>
    public class AgendaOpeningHourTests
    {
        private static readonly DateTime Day = new DateTime(2026, 9, 8);
        private static readonly List<DateTime> OneDay = new List<DateTime> { Day };
        private static readonly DateTime NextWeek = Day.AddDays(7);

        private static AgendaEvent At(double fromHour, double toHour) => new AgendaEvent
        {
            Title = "x",
            Start = Day.AddHours(fromHour),
            End = Day.AddHours(toHour)
        };

        [Fact]
        public void Today_opens_an_hour_before_now()
        {
            Assert.Equal(9, AgendaLayout.DefaultTopHour(new List<AgendaEvent>(), OneDay, Day.AddHours(10).AddMinutes(46)));
        }

        [Fact]
        public void Just_after_midnight_today_opens_at_the_top()
        {
            Assert.Equal(0, AgendaLayout.DefaultTopHour(new List<AgendaEvent>(), OneDay, Day.AddMinutes(20)));
        }

        [Fact]
        public void An_empty_day_opens_at_seven()
        {
            Assert.Equal(7, AgendaLayout.DefaultTopHour(new List<AgendaEvent>(), OneDay, NextWeek));
        }

        [Fact]
        public void Something_early_opens_the_day_an_hour_before_it()
        {
            Assert.Equal(4, AgendaLayout.DefaultTopHour(new List<AgendaEvent> { At(5.5, 6.5) }, OneDay, NextWeek));
        }

        [Fact]
        public void Something_late_does_not_push_the_opening_down()
        {
            Assert.Equal(7, AgendaLayout.DefaultTopHour(new List<AgendaEvent> { At(14, 15) }, OneDay, NextWeek));
        }

        [Fact]
        public void Something_all_day_does_not_count()
        {
            var birthday = new AgendaEvent { Title = "b", IsAllDay = true, Start = Day, End = Day.AddDays(1) };

            Assert.Equal(7, AgendaLayout.DefaultTopHour(new List<AgendaEvent> { birthday }, OneDay, NextWeek));
        }
    }
}
