using Desktop_Frames.Plugins.GoogleAgenda;
using Xunit;

namespace Desktop_Frames.Tests
{
    /// <summary>
    /// What this program adds to Google's tasks: a colour per list, and an hour where
    /// Google gives none.
    ///
    /// The rule worth holding down is the order. Google's hour, when there is one, is
    /// the truth; a kept hour only fills the gap Google leaves. Get that backwards and
    /// a task moved in Google's own app stays where it used to be, here, for ever.
    /// </summary>
    public class AgendaOverridesTests
    {
        private static readonly DateTime Day = new DateTime(2026, 9, 10);

        private static AgendaEvent Task(string list, string title, bool allDay = true) => new AgendaEvent
        {
            Id = title,
            CalendarId = list,
            Title = title,
            IsTask = true,
            IsAllDay = allDay,
            Start = allDay ? Day : Day.AddHours(18),
            End = allDay ? Day.AddDays(1) : Day.AddHours(19),
            ColourHex = "#5C6BC0"
        };

        private static readonly Dictionary<string, string> NoColours = new Dictionary<string, string>();
        private static readonly Dictionary<string, AgendaHour> NoHours = new Dictionary<string, AgendaHour>();

        private static Dictionary<string, AgendaHour> HourFor(string list, string title, int hour, int minutes) =>
            new Dictionary<string, AgendaHour>
            {
                [AgendaOverrides.TaskKey(list, title)] =
                    new AgendaHour(TimeSpan.FromHours(hour), TimeSpan.FromMinutes(minutes))
            };

        // ------------------------------------------------------------ colours

        [Fact]
        public void A_list_colour_paints_the_tasks_in_that_list()
        {
            AgendaEvent task = Task("school", "Homework");
            var colours = new Dictionary<string, string> { ["school"] = "#2E7D32" };

            AgendaOverrides.Apply(new[] { task }, colours, NoHours);

            Assert.Equal("#2E7D32", task.ColourHex);
        }

        [Fact]
        public void Another_list_keeps_its_own_colour()
        {
            AgendaEvent task = Task("home", "Laundry");
            var colours = new Dictionary<string, string> { ["school"] = "#2E7D32" };

            AgendaOverrides.Apply(new[] { task }, colours, NoHours);

            Assert.Equal("#5C6BC0", task.ColourHex);
        }

        [Fact]
        public void An_event_is_never_painted_as_a_list()
        {
            // A calendar and a task list can share nothing but the field the frame
            // keeps them in - an identifier colliding must not repaint a meeting.
            var meeting = new AgendaEvent { CalendarId = "school", Title = "Meeting", ColourHex = "#039BE5" };
            var colours = new Dictionary<string, string> { ["school"] = "#2E7D32" };

            AgendaOverrides.Apply(new[] { meeting }, colours, NoHours);

            Assert.Equal("#039BE5", meeting.ColourHex);
        }

        // ------------------------------------------------------------ hours

        [Fact]
        public void A_kept_hour_puts_an_all_day_task_at_that_hour()
        {
            AgendaEvent task = Task("health", "Water the plants");

            AgendaOverrides.Apply(new[] { task }, NoColours, HourFor("health", "Water the plants", 19, 30));

            Assert.False(task.IsAllDay);
            Assert.True(task.HourIsLocal);
            Assert.Equal(Day.AddHours(19), task.Start);
            Assert.Equal(Day.AddHours(19).AddMinutes(30), task.End);
        }

        [Fact]
        public void Google_hour_wins_over_a_kept_one()
        {
            AgendaEvent task = Task("health", "Water the plants", allDay: false);

            AgendaOverrides.Apply(new[] { task }, NoColours, HourFor("health", "Water the plants", 8, 60));

            Assert.Equal(Day.AddHours(18), task.Start);
            Assert.False(task.HourIsLocal);
        }

        [Fact]
        public void A_kept_hour_follows_the_title_to_another_day()
        {
            // The point of filing by title: tomorrow's occurrence of a repeating task
            // is found under the same key as today's.
            AgendaEvent tomorrow = Task("health", "Water the plants");
            tomorrow.Start = Day.AddDays(1);
            tomorrow.End = Day.AddDays(2);

            AgendaOverrides.Apply(new[] { tomorrow }, NoColours, HourFor("health", "Water the plants", 19, 30));

            Assert.Equal(Day.AddDays(1).AddHours(19), tomorrow.Start);
        }

        [Fact]
        public void An_hour_with_no_length_is_ignored()
        {
            AgendaEvent task = Task("health", "Water the plants");

            AgendaOverrides.Apply(new[] { task }, NoColours, HourFor("health", "Water the plants", 9, 0));

            Assert.True(task.IsAllDay);
        }

        // ------------------------------------------------------------ keys

        [Fact]
        public void The_key_ignores_case_and_surrounding_spaces()
        {
            Assert.Equal(AgendaOverrides.TaskKey("l", "Water the plants"),
                         AgendaOverrides.TaskKey("l", "  WATER THE PLANTS "));
        }

        [Fact]
        public void The_same_title_in_another_list_is_another_task()
        {
            Assert.NotEqual(AgendaOverrides.TaskKey("home", "Call"),
                            AgendaOverrides.TaskKey("work", "Call"));
        }

        [Fact]
        public void The_key_does_not_carry_the_title()
        {
            string key = AgendaOverrides.TaskKey("health", "Water the plants");

            Assert.Equal(32, key.Length);
            Assert.DoesNotContain("WATER", key, StringComparison.OrdinalIgnoreCase);
        }
    }
}
