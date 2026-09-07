using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// The one class that talks to Google.
    ///
    /// It keeps its own copy of the events it has been told about and asks only for
    /// what has changed since last time, which is what makes a refresh every minute
    /// reasonable: after the first load, a quiet calendar answers with an empty list
    /// and a new token.
    ///
    /// Only the primary calendar, because the scope this plugin asks for is events
    /// and nothing else - listing somebody's other calendars would mean asking for
    /// permission to read the list, which is more than showing an agenda needs.
    /// </summary>
    public class GoogleCalendarSource : IDisposable
    {
        private const string PrimaryCalendar = "primary";

        private readonly CalendarService _service;

        /// <summary>
        /// Everything Google has mentioned, by identifier. Held rather than rebuilt
        /// because an incremental answer says "this one changed", not "here is
        /// everything" - without somewhere to apply it to, the answer means nothing.
        /// </summary>
        private readonly Dictionary<string, AgendaEvent> _known =
            new Dictionary<string, AgendaEvent>(StringComparer.Ordinal);

        /// <summary>
        /// Google's marker for "you know everything up to here". Null before the first
        /// load, and cleared whenever Google says it has gone stale.
        /// </summary>
        private string? _syncToken;

        public GoogleCalendarSource(UserCredential credential)
        {
            _service = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Desktop Frames + Agenda"
            });
        }

        /// <summary>
        /// The events falling inside <paramref name="window"/> from today, after
        /// taking in whatever changed since the last call.
        /// </summary>
        public async Task<IReadOnlyList<AgendaEvent>> RefreshAsync(TimeSpan window, CancellationToken token)
        {
            try
            {
                if (_syncToken == null)
                    await LoadEverythingAsync(window, token).ConfigureAwait(false);
                else
                    await LoadChangesAsync(token).ConfigureAwait(false);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Gone)
            {
                // 410 is Google saying the marker is too old to be useful - it happens
                // after a long time offline, and it is part of the protocol rather than
                // a fault. The answer is to forget what we know and ask again in full.
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    "GoogleAgenda: sync marker expired, reloading the whole window.");

                _syncToken = null;
                _known.Clear();

                await LoadEverythingAsync(window, token).ConfigureAwait(false);
            }

            return Within(window);
        }

        /// <summary>
        /// The first load, which also decides what the marker will cover: Google ties
        /// it to the request that produced it, so every later call inherits these
        /// filters and must not repeat them.
        /// </summary>
        private async Task LoadEverythingAsync(TimeSpan window, CancellationToken token)
        {
            EventsResource.ListRequest request = _service.Events.List(PrimaryCalendar);

            // Repetitions expanded by Google into one entry per occurrence. Reading a
            // recurrence rule correctly - with its exceptions, its cancelled instances
            // and its time zones - is a project of its own, and this is not it.
            request.SingleEvents = true;

            // Yesterday rather than now, so something still running is not dropped
            // halfway through the afternoon.
            request.TimeMinDateTimeOffset = DateTimeOffset.Now.Date.AddDays(-1);
            request.TimeMaxDateTimeOffset = DateTimeOffset.Now.Date.Add(window);

            // Needed so a later change can tell "deleted" apart from "never mentioned".
            request.ShowDeleted = true;
            request.MaxResults = 250;

            await ReadPagesAsync(request, token).ConfigureAwait(false);
        }

        /// <summary>
        /// What changed since the marker. The filters of the first request are carried
        /// by the marker itself, and sending them again is refused by the API.
        /// </summary>
        private async Task LoadChangesAsync(CancellationToken token)
        {
            EventsResource.ListRequest request = _service.Events.List(PrimaryCalendar);
            request.SyncToken = _syncToken;
            request.MaxResults = 250;

            await ReadPagesAsync(request, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Walks the pages of an answer and applies each one.
        ///
        /// The new marker arrives only with the last page: taking one from an earlier
        /// page would record that we know things we have not read yet, and those
        /// events would never appear.
        /// </summary>
        private async Task ReadPagesAsync(EventsResource.ListRequest request, CancellationToken token)
        {
            string? pageToken = null;

            do
            {
                request.PageToken = pageToken;

                Events answer = await request.ExecuteAsync(token).ConfigureAwait(false);

                foreach (Event item in answer.Items ?? new List<Event>())
                    Apply(item);

                pageToken = answer.NextPageToken;

                if (!string.IsNullOrEmpty(answer.NextSyncToken))
                    _syncToken = answer.NextSyncToken;
            }
            while (!string.IsNullOrEmpty(pageToken) && !token.IsCancellationRequested);
        }

        /// <summary>Takes one event in, or removes it when Google says it is gone.</summary>
        private void Apply(Event item)
        {
            if (string.IsNullOrEmpty(item.Id)) return;

            if (string.Equals(item.Status, "cancelled", StringComparison.Ordinal))
            {
                _known.Remove(item.Id);
                return;
            }

            AgendaEvent? mapped = Map(item);
            if (mapped == null)
            {
                // An entry with no usable time cannot be placed on a day, and a frame
                // is a list of days. Better absent than drawn at midnight of an
                // arbitrary one.
                _known.Remove(item.Id);
                return;
            }

            _known[item.Id] = mapped;
        }

        private static AgendaEvent? Map(Event item)
        {
            bool allDay = item.Start?.Date != null;

            DateTime start, end;

            if (allDay)
            {
                if (!DateTime.TryParse(item.Start!.Date, out start)) return null;
                if (!DateTime.TryParse(item.End?.Date, out end)) end = start.AddDays(1);
            }
            else
            {
                DateTimeOffset? from = item.Start?.DateTimeDateTimeOffset;
                if (from == null) return null;

                start = from.Value.LocalDateTime;
                end = item.End?.DateTimeDateTimeOffset?.LocalDateTime ?? start.AddHours(1);
            }

            return new AgendaEvent
            {
                Id = item.Id ?? string.Empty,
                CalendarId = PrimaryCalendar,
                Title = string.IsNullOrWhiteSpace(item.Summary)
                    ? Localization.Strings.AgendaUntitled
                    : item.Summary,
                Location = item.Location ?? string.Empty,
                Description = item.Description ?? string.Empty,
                Start = start,
                End = end,
                IsAllDay = allDay,
                WebLink = item.HtmlLink ?? string.Empty,
                ETag = item.ETag ?? string.Empty
            };
        }

        /// <summary>
        /// The part of what we know that belongs on screen, in the order it will be
        /// drawn. The filter is applied here rather than when taking events in,
        /// because an incremental answer reports changes from the whole calendar and
        /// dropping them early would leave holes the next full load would have to fix.
        /// </summary>
        private IReadOnlyList<AgendaEvent> Within(TimeSpan window)
        {
            DateTime from = DateTime.Now.Date;
            DateTime to = from.Add(window);

            return _known.Values
                .Where(e => e.End >= from && e.Start < to)
                .OrderBy(e => e.IsAllDay ? e.Start.Date : e.Start)
                .ThenBy(e => e.Title, StringComparer.CurrentCulture)
                .ToList();
        }

        public void Dispose()
        {
            _service.Dispose();
        }
    }
}
