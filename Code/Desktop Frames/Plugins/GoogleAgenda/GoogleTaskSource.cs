using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Tasks.v1;

// Aliased rather than imported wholesale: Google's own type for a task is called
// Task, exactly like the one every async method in this file returns. Spelling out
// which is which here is shorter than qualifying it at every use.
using GoogleTask = Google.Apis.Tasks.v1.Data.Task;
using GoogleTaskList = Google.Apis.Tasks.v1.Data.TaskList;
using GoogleTaskLists = Google.Apis.Tasks.v1.Data.TaskLists;
using GoogleTaskPage = Google.Apis.Tasks.v1.Data.Tasks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// The things with a tick box.
    ///
    /// Tasks are a different service from Calendar, with their own lists, their own
    /// permission and their own idea of time - so they get their own class, and meet
    /// the events only as <see cref="AgendaEvent"/>, where the frame cannot tell
    /// which service either of them came from.
    /// </summary>
    public class GoogleTaskSource : IDisposable
    {
        private readonly TasksService _service;

        /// <summary>
        /// The lists an account has, read once per session: somebody adds a list every
        /// few months, not every minute.
        /// </summary>
        private readonly Dictionary<string, string> _lists =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private bool _listsRead;

        /// <summary>
        /// Whether a task has ever been seen carrying a time of day.
        ///
        /// The API documents its due date as a date, with the time discarded - but
        /// Calendar has been letting people put an hour on a task for a while now, and
        /// whether that hour reaches the API is the sort of thing worth measuring on a
        /// real account rather than reading about. The mapping below handles both, and
        /// this records which one actually arrived, once, in the log.
        /// </summary>
        private bool _reportedTimeShape;

        public GoogleTaskSource(UserCredential credential)
        {
            _service = new TasksService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Desktop Frames + Agenda"
            });
        }

        /// <summary>The task lists found, by identifier and title.</summary>
        public IReadOnlyDictionary<string, string> Lists => _lists;

        /// <summary>
        /// The tasks due between <paramref name="from"/> and <paramref name="to"/>.
        ///
        /// Read in full each time rather than incrementally. Tasks has no sync marker
        /// of the kind Calendar hands out, and a person's open tasks are counted in
        /// dozens - the whole answer costs less than the machinery to avoid asking for
        /// it would.
        /// </summary>
        public async Task<IReadOnlyList<AgendaEvent>> LoadAsync(
            DateTime from, DateTime to, ISet<string> chosen, CancellationToken token)
        {
            await EnsureListsAsync(token).ConfigureAwait(false);

            var found = new List<AgendaEvent>();

            foreach (KeyValuePair<string, string> list in _lists.ToList())
            {
                if (token.IsCancellationRequested) break;
                if (chosen.Count > 0 && !chosen.Contains(list.Key)) continue;

                try
                {
                    await LoadListAsync(list.Key, from, to, found, token).ConfigureAwait(false);
                }
                catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
                {
                    // A list deleted elsewhere. Dropping it is the whole response.
                    _lists.Remove(list.Key);
                }
            }

            return found;
        }

        private async Task EnsureListsAsync(CancellationToken token)
        {
            if (_listsRead) return;

            string? page = null;

            do
            {
                TasklistsResource.ListRequest request = _service.Tasklists.List();
                request.MaxResults = 100;
                request.PageToken = page;

                GoogleTaskLists answer = await request.ExecuteAsync(token).ConfigureAwait(false);

                foreach (GoogleTaskList list in answer.Items ?? new List<GoogleTaskList>())
                    if (!string.IsNullOrEmpty(list.Id))
                        _lists[list.Id] = list.Title ?? list.Id;

                page = answer.NextPageToken;
            }
            while (!string.IsNullOrEmpty(page) && !token.IsCancellationRequested);

            _listsRead = true;
        }

        private async Task LoadListAsync(string listId, DateTime from, DateTime to,
                                         List<AgendaEvent> into, CancellationToken token)
        {
            string? page = null;

            do
            {
                TasksResource.ListRequest request = _service.Tasks.List(listId);
                request.MaxResults = 100;
                request.PageToken = page;

                // Completed ones are asked for on purpose: a tick has to be visible in
                // the grids, and a task that vanished the moment it was done would look
                // like it had been deleted.
                request.ShowCompleted = true;
                request.ShowHidden = true;

                request.DueMin = from.ToString("yyyy-MM-dd'T'00:00:00'Z'", CultureInfo.InvariantCulture);
                request.DueMax = to.ToString("yyyy-MM-dd'T'00:00:00'Z'", CultureInfo.InvariantCulture);

                GoogleTaskPage answer = await request.ExecuteAsync(token).ConfigureAwait(false);

                foreach (GoogleTask item in answer.Items ?? new List<GoogleTask>())
                {
                    AgendaEvent? mapped = Map(listId, item);
                    if (mapped != null) into.Add(mapped);
                }

                page = answer.NextPageToken;
            }
            while (!string.IsNullOrEmpty(page) && !token.IsCancellationRequested);
        }

        /// <summary>
        /// Turns a task into something the frame can draw.
        ///
        /// A task with no due date is left out. It belongs on a list, not on a day, and
        /// putting it on today would be inventing a deadline nobody set.
        /// </summary>
        private AgendaEvent? Map(string listId, GoogleTask item)
        {
            if (string.IsNullOrEmpty(item.Id) || string.IsNullOrWhiteSpace(item.Due)) return null;

            if (!DateTimeOffset.TryParse(item.Due, CultureInfo.InvariantCulture,
                                         DateTimeStyles.AdjustToUniversal, out DateTimeOffset due))
                return null;

            // The measurement: written once per session, so the log says what this
            // account's tasks actually carry rather than what the documentation says
            // they carry.
            if (!_reportedTimeShape)
            {
                _reportedTimeShape = true;
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"GoogleAgenda: first task due value from the API is \"{item.Due}\" " +
                    $"(time of day {(due.TimeOfDay == TimeSpan.Zero ? "absent" : "present")}).");
            }

            DateTime local = due.LocalDateTime;
            bool hasTime = due.TimeOfDay != TimeSpan.Zero;

            return new AgendaEvent
            {
                Id = item.Id,
                CalendarId = listId,
                Title = string.IsNullOrWhiteSpace(item.Title)
                    ? Localization.Strings.AgendaUntitled
                    : item.Title,
                Description = item.Notes ?? string.Empty,

                // Given an hour, it sits at that hour for an hour; given only a date, it
                // belongs to the whole day, alongside a birthday rather than a meeting.
                Start = hasTime ? local : local.Date,
                End = hasTime ? local.AddHours(1) : local.Date.AddDays(1),
                IsAllDay = !hasTime,

                IsTask = true,
                IsDone = string.Equals(item.Status, "completed", StringComparison.Ordinal),

                WebLink = item.WebViewLink ?? string.Empty,
                ETag = item.ETag ?? string.Empty,
                CanWrite = true
            };
        }

        /// <summary>Ticks a task, or unticks it.</summary>
        public async Task SetDoneAsync(AgendaEvent item, bool done, CancellationToken token)
        {
            GoogleTask current =
                await _service.Tasks.Get(item.CalendarId, item.Id).ExecuteAsync(token).ConfigureAwait(false);

            current.Status = done ? "completed" : "needsAction";

            // Google refuses a completion date on something not completed, and keeps
            // the old one otherwise - so it is cleared rather than left to argue about.
            if (!done) current.Completed = null;

            await _service.Tasks.Update(current, item.CalendarId, item.Id)
                          .ExecuteAsync(token).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _service.Dispose();
        }
    }
}
