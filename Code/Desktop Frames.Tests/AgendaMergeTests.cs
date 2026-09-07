using Desktop_Frames.Plugins.GoogleAgenda;
using Xunit;

namespace Desktop_Frames.Tests
{
    /// <summary>
    /// The join between a task and the calendar entry standing in for it.
    ///
    /// This is where a whole evening went, diagnosed through log probes because
    /// nothing here could be run on its own. The rules were learned from a live
    /// account and are easy to break by accident, so they are written down as
    /// cases: a stand-in is recognised by the link it carries, a task keeps
    /// everything except the hour, and an orphan is kept rather than dropped.
    /// </summary>
    public class AgendaMergeTests
    {
        private const string TaskId = "CuI0-gJQLW-KxDM1";

        private static AgendaEvent StandIn(string taskId, DateTime start) => new AgendaEvent
        {
            Id = "stand-in",
            Title = "prova",
            Start = start,
            End = start.AddHours(1),
            ColourHex = "#B75433",
            MirrorOfTask = "https://tasks.google.com/task/" + taskId
        };

        private static AgendaEvent Task(string taskId, DateTime due) => new AgendaEvent
        {
            Id = taskId,
            Title = "prova",
            Start = due.Date,
            End = due.Date.AddDays(1),
            IsAllDay = true,
            IsTask = true,

            // As the API hands it back: the identifier followed by a query string.
            WebLink = "https://tasks.google.com/task/" + taskId + "?sa=6"
        };

        [Fact]
        public void A_task_takes_the_hour_from_its_stand_in()
        {
            var noon = new DateTime(2026, 9, 8, 12, 0, 0);

            var events = new List<AgendaEvent> { StandIn(TaskId, noon) };
            var tasks = new List<AgendaEvent> { Task(TaskId, noon) };

            List<AgendaEvent> joined = AgendaMerge.Join(events, tasks, out int unjoined);

            Assert.Equal(0, unjoined);

            // One entry, not two: the stand-in has nothing left to show.
            AgendaEvent only = Assert.Single(joined);

            Assert.True(only.IsTask);
            Assert.Equal(noon, only.Start);
            Assert.Equal(noon.AddHours(1), only.End);
            Assert.False(only.IsAllDay);
        }

        [Fact]
        public void The_joined_task_keeps_its_own_title_and_tick()
        {
            var noon = new DateTime(2026, 9, 8, 12, 0, 0);

            AgendaEvent task = Task(TaskId, noon);
            task.Title = "Comprare il pane";
            task.IsDone = true;

            AgendaEvent standIn = StandIn(TaskId, noon);
            standIn.Title = "something else entirely";

            List<AgendaEvent> joined = AgendaMerge.Join(
                new List<AgendaEvent> { standIn }, new List<AgendaEvent> { task }, out _);

            AgendaEvent only = Assert.Single(joined);

            Assert.Equal("Comprare il pane", only.Title);
            Assert.True(only.IsDone);
        }

        [Fact]
        public void The_joined_task_takes_the_colour_the_calendar_draws_it_in()
        {
            var noon = new DateTime(2026, 9, 8, 12, 0, 0);

            List<AgendaEvent> joined = AgendaMerge.Join(
                new List<AgendaEvent> { StandIn(TaskId, noon) },
                new List<AgendaEvent> { Task(TaskId, noon) },
                out _);

            Assert.Equal("#B75433", Assert.Single(joined).ColourHex);
        }

        [Fact]
        public void A_stand_in_with_no_task_is_kept_and_counted()
        {
            var noon = new DateTime(2026, 9, 8, 12, 0, 0);

            // The task exists but sits in a list this frame is not showing, so it
            // never arrives. Dropping the stand-in as well would make the
            // appointment disappear from the day altogether.
            List<AgendaEvent> joined = AgendaMerge.Join(
                new List<AgendaEvent> { StandIn("some-other-task", noon) },
                new List<AgendaEvent> { Task(TaskId, noon) },
                out int unjoined);

            Assert.Equal(1, unjoined);
            Assert.Equal(2, joined.Count);
        }

        [Fact]
        public void An_ordinary_event_passes_through_untouched()
        {
            var meeting = new AgendaEvent
            {
                Id = "meeting",
                Title = "Riunione",
                Start = new DateTime(2026, 9, 7, 14, 30, 0),
                End = new DateTime(2026, 9, 7, 16, 30, 0)
            };

            List<AgendaEvent> joined = AgendaMerge.Join(
                new List<AgendaEvent> { meeting }, new List<AgendaEvent>(), out int unjoined);

            Assert.Equal(0, unjoined);
            Assert.Same(meeting, Assert.Single(joined));
        }

        [Fact]
        public void A_task_with_no_stand_in_stays_a_whole_day()
        {
            var day = new DateTime(2026, 9, 8);

            List<AgendaEvent> joined = AgendaMerge.Join(
                new List<AgendaEvent>(), new List<AgendaEvent> { Task(TaskId, day) }, out int unjoined);

            AgendaEvent only = Assert.Single(joined);

            Assert.Equal(0, unjoined);
            Assert.True(only.IsAllDay);
            Assert.Equal(day, only.Start);
        }

        [Fact]
        public void An_event_that_merely_mentions_a_task_is_not_a_stand_in()
        {
            // MirrorOfTask is set by the source only for entries Google itself
            // created for a task. A meeting whose notes link to one has no such
            // mark, and must not be swallowed into somebody's to-do list.
            var meeting = new AgendaEvent
            {
                Id = "meeting",
                Title = "Riunione",
                Description = "see https://tasks.google.com/task/" + TaskId,
                Start = new DateTime(2026, 9, 8, 12, 0, 0),
                End = new DateTime(2026, 9, 8, 13, 0, 0)
            };

            List<AgendaEvent> joined = AgendaMerge.Join(
                new List<AgendaEvent> { meeting },
                new List<AgendaEvent> { Task(TaskId, new DateTime(2026, 9, 8)) },
                out int unjoined);

            Assert.Equal(0, unjoined);
            Assert.Equal(2, joined.Count);
            Assert.Contains(joined, e => ReferenceEquals(e, meeting));
        }

        [Fact]
        public void A_task_whose_link_is_missing_cannot_be_joined()
        {
            var noon = new DateTime(2026, 9, 8, 12, 0, 0);

            AgendaEvent task = Task(TaskId, noon);
            task.WebLink = string.Empty;

            List<AgendaEvent> joined = AgendaMerge.Join(
                new List<AgendaEvent> { StandIn(TaskId, noon) },
                new List<AgendaEvent> { task },
                out int unjoined);

            // Nothing is lost: both halves are still shown, and the count says why.
            Assert.Equal(1, unjoined);
            Assert.Equal(2, joined.Count);
        }
    }
}
