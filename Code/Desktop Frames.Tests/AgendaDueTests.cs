using Desktop_Frames.Plugins.GoogleAgenda;
using Xunit;

namespace Desktop_Frames.Tests
{
    /// <summary>
    /// Reading a task's due date.
    ///
    /// Google sends a date as midnight UTC. Read through the local time zone, that
    /// is the evening before anywhere west of Greenwich, which put every task a day
    /// early there. The date is now read from what is written, so the answer below
    /// does not depend on the zone of the machine running the test.
    /// </summary>
    public class AgendaDueTests
    {
        [Fact]
        public void A_due_date_is_the_day_written()
        {
            Assert.True(AgendaDue.TryRead("2026-09-07T00:00:00.000Z", out DateTime start, out bool hasTime));

            Assert.False(hasTime);
            Assert.Equal(new DateTime(2026, 9, 7), start);
            Assert.Equal(TimeSpan.Zero, start.TimeOfDay);
        }

        [Fact]
        public void The_day_is_not_moved_by_the_local_zone()
        {
            // The old reading went through local time: the kind of the result is the
            // trace it leaves. A date read as written carries no zone at all.
            Assert.True(AgendaDue.TryRead("2026-01-31T00:00:00.000Z", out DateTime start, out _));

            Assert.Equal(new DateTime(2026, 1, 31), start);
            Assert.NotEqual(DateTimeKind.Local, start.Kind);
        }

        [Fact]
        public void A_due_with_an_hour_keeps_it()
        {
            Assert.True(AgendaDue.TryRead("2026-09-07T14:30:00.000Z", out DateTime start, out bool hasTime));

            Assert.True(hasTime);
            Assert.Equal(new DateTimeOffset(2026, 9, 7, 14, 30, 0, TimeSpan.Zero).LocalDateTime, start);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("tomorrow")]
        public void Anything_that_is_not_a_date_is_refused(string? due)
        {
            Assert.False(AgendaDue.TryRead(due, out _, out _));
        }
    }
}
