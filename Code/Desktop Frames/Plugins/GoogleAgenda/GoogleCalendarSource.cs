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
    /// It keeps its own copy of what it has been told and asks only for what has
    /// changed since last time, which is what makes a refresh every minute
    /// reasonable: after the first load, a quiet calendar answers with an empty list
    /// and a fresh marker.
    ///
    /// Everything above this class speaks in <see cref="AgendaEvent"/> and
    /// <see cref="AgendaCalendar"/> and knows nothing about Google.
    /// </summary>
    public class GoogleCalendarSource : IDisposable
    {
        private readonly CalendarService _service;

        private readonly Dictionary<string, AgendaCalendar> _calendars =
            new Dictionary<string, AgendaCalendar>(StringComparer.Ordinal);

        /// <summary>
        /// Google's "you know everything up to here", one per calendar: each has its
        /// own history, and a single marker for all of them would be meaningless.
        /// </summary>
        private readonly Dictionary<string, string?> _markers =
            new Dictionary<string, string?>(StringComparer.Ordinal);

        /// <summary>
        /// Everything Google has mentioned, keyed by calendar and event together -
        /// identifiers are unique inside a calendar, not across them.
        ///
        /// Held rather than rebuilt because an incremental answer says "this one
        /// changed", not "here is everything": without somewhere to apply it, the
        /// answer means nothing.
        /// </summary>
        private readonly Dictionary<string, AgendaEvent> _known =
            new Dictionary<string, AgendaEvent>(StringComparer.Ordinal);

        private bool _calendarsRead;

        public GoogleCalendarSource(UserCredential credential)
        {
            _service = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Desktop Frames + Agenda"
            });
        }

        /// <summary>The calendars found, own calendar first, then by name.</summary>
        public IReadOnlyList<AgendaCalendar> Calendars =>
            _calendars.Values
                .OrderByDescending(c => c.IsPrimary)
                .ThenBy(c => c.Title, StringComparer.CurrentCulture)
                .ToList();

        /// <summary>
        /// The events between <paramref name="from"/> and <paramref name="to"/>, after
        /// taking in whatever changed since the last call.
        /// </summary>
        /// <param name="chosen">
        /// Calendars to look at. Empty means all of them, which is what a frame nobody
        /// has configured should show.
        /// </param>
        public async Task<IReadOnlyList<AgendaEvent>> RefreshAsync(
            DateTime from, DateTime to, ISet<string> chosen, CancellationToken token)
        {
            await EnsureCalendarsAsync(token).ConfigureAwait(false);

            foreach (AgendaCalendar calendar in _calendars.Values.ToList())
            {
                if (token.IsCancellationRequested) break;
                if (!IsChosen(calendar.Id, chosen)) continue;

                await RefreshOneAsync(calendar, from, to, token).ConfigureAwait(false);
            }

            return Within(from, to, chosen);
        }

        // ======================================================================
        // CALENDARS
        // ======================================================================

        /// <summary>
        /// Reads the calendar list once per session.
        ///
        /// Once, because it changes when somebody adds or removes a calendar - a thing
        /// that happens on the order of months, not minutes - and asking every minute
        /// would spend quota to be told the same answer.
        /// </summary>
        private async Task EnsureCalendarsAsync(CancellationToken token)
        {
            if (_calendarsRead) return;

            string? page = null;

            do
            {
                CalendarListResource.ListRequest request = _service.CalendarList.List();
                request.PageToken = page;
                request.ShowHidden = false;

                CalendarList answer = await request.ExecuteAsync(token).ConfigureAwait(false);

                foreach (CalendarListEntry entry in answer.Items ?? new List<CalendarListEntry>())
                {
                    if (string.IsNullOrEmpty(entry.Id)) continue;

                    _calendars[entry.Id] = new AgendaCalendar
                    {
                        Id = entry.Id,
                        Title = entry.Summary ?? entry.Id,
                        ColourHex = entry.BackgroundColor ?? string.Empty,
                        IsPrimary = entry.Primary ?? false,

                        // "owner" and "writer" may add events; "reader" and
                        // "freeBusyReader" may not. Holiday feeds and calendars shared
                        // for viewing land in the second group.
                        CanWrite = entry.AccessRole == "owner" || entry.AccessRole == "writer"
                    };
                }

                page = answer.NextPageToken;
            }
            while (!string.IsNullOrEmpty(page) && !token.IsCancellationRequested);

            _calendarsRead = true;
        }

        // ======================================================================
        // EVENTS
        // ======================================================================

        private async Task RefreshOneAsync(AgendaCalendar calendar, DateTime from, DateTime to,
                                           CancellationToken token)
        {
            try
            {
                if (!_markers.TryGetValue(calendar.Id, out string? marker) || marker == null)
                    await LoadWindowAsync(calendar, from, to, token).ConfigureAwait(false);
                else
                    await LoadChangesAsync(calendar, marker, token).ConfigureAwait(false);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Gone)
            {
                // 410 is Google saying the marker is too old to be useful. It happens
                // after a long time offline and is part of the protocol, not a fault:
                // forget this calendar and read its window again.
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"GoogleAgenda: sync marker expired for {calendar.Title}, reloading it.");

                Forget(calendar.Id);
                await LoadWindowAsync(calendar, from, to, token).ConfigureAwait(false);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound
                                                    || ex.HttpStatusCode == HttpStatusCode.Forbidden)
            {
                // A calendar removed or no longer shared with us. Dropping it is the
                // whole response: retrying every minute would fail every minute.
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"GoogleAgenda: calendar {calendar.Title} is no longer readable, dropping it.");

                Forget(calendar.Id);
                _calendars.Remove(calendar.Id);
            }
        }

        /// <summary>
        /// The first read of a calendar, which also decides what its marker covers:
        /// Google ties the marker to the request that produced it, so every later call
        /// inherits these filters and must not repeat them.
        /// </summary>
        private async Task LoadWindowAsync(AgendaCalendar calendar, DateTime from, DateTime to,
                                           CancellationToken token)
        {
            EventsResource.ListRequest request = _service.Events.List(calendar.Id);

            // Repetitions expanded by Google into one entry per occurrence. Reading a
            // recurrence rule correctly - with its exceptions, its cancelled instances
            // and its time zones - is a project of its own, and this is not it.
            request.SingleEvents = true;

            // A day either side of what is shown: an event that began yesterday and is
            // still running has to appear, and so does one starting on the last evening
            // of the range.
            request.TimeMinDateTimeOffset = new DateTimeOffset(from.AddDays(-1));
            request.TimeMaxDateTimeOffset = new DateTimeOffset(to.AddDays(1));

            // Needed so a later change can tell "deleted" apart from "never mentioned".
            request.ShowDeleted = true;
            request.MaxResults = 250;

            await ReadPagesAsync(calendar, request, token).ConfigureAwait(false);
        }

        /// <summary>
        /// What changed since the marker. The filters of the first request travel with
        /// the marker, and sending them again is refused by the API.
        /// </summary>
        private async Task LoadChangesAsync(AgendaCalendar calendar, string marker, CancellationToken token)
        {
            EventsResource.ListRequest request = _service.Events.List(calendar.Id);
            request.SyncToken = marker;
            request.MaxResults = 250;

            await ReadPagesAsync(calendar, request, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Walks the pages of an answer and applies each one.
        ///
        /// The new marker arrives only with the last page: taking one from an earlier
        /// page would record that we know about events we have not read, and they
        /// would never appear.
        /// </summary>
        private async Task ReadPagesAsync(AgendaCalendar calendar, EventsResource.ListRequest request,
                                          CancellationToken token)
        {
            string? page = null;

            do
            {
                request.PageToken = page;

                Events answer = await request.ExecuteAsync(token).ConfigureAwait(false);

                foreach (Event item in answer.Items ?? new List<Event>())
                    Apply(calendar, item);

                page = answer.NextPageToken;

                if (!string.IsNullOrEmpty(answer.NextSyncToken))
                    _markers[calendar.Id] = answer.NextSyncToken;
            }
            while (!string.IsNullOrEmpty(page) && !token.IsCancellationRequested);
        }

        /// <summary>Takes one event in, or removes it when Google says it is gone.</summary>
        private void Apply(AgendaCalendar calendar, Event item)
        {
            if (string.IsNullOrEmpty(item.Id)) return;

            string key = KeyOf(calendar.Id, item.Id);

            if (string.Equals(item.Status, "cancelled", StringComparison.Ordinal))
            {
                _known.Remove(key);
                return;
            }

            AgendaEvent? mapped = Map(calendar, item);

            // An entry with no usable time cannot be placed on a day, and the frame is
            // a list of days. Better absent than drawn at midnight of an arbitrary one.
            if (mapped == null) _known.Remove(key);
            else _known[key] = mapped;
        }

        private static AgendaEvent? Map(AgendaCalendar calendar, Event item)
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
                CalendarId = calendar.Id,
                Title = string.IsNullOrWhiteSpace(item.Summary)
                    ? Localization.Strings.AgendaUntitled
                    : item.Summary,
                Location = item.Location ?? string.Empty,
                Description = item.Description ?? string.Empty,
                Start = start,
                End = end,
                IsAllDay = allDay,
                ColourHex = calendar.ColourHex,
                WebLink = item.HtmlLink ?? string.Empty,
                ETag = item.ETag ?? string.Empty,
                CanWrite = calendar.CanWrite
            };
        }

        // ======================================================================
        // WRITING
        // ======================================================================

        /// <summary>Adds an event and returns it as Google recorded it.</summary>
        public async Task<AgendaEvent> CreateAsync(AgendaEvent draft, CancellationToken token)
        {
            AgendaCalendar calendar = CalendarFor(draft.CalendarId);

            Event created = await _service.Events
                .Insert(ToGoogle(draft, new Event()), calendar.Id)
                .ExecuteAsync(token).ConfigureAwait(false);

            Apply(calendar, created);
            return Map(calendar, created) ?? draft;
        }

        /// <summary>
        /// Saves a change to an existing event.
        ///
        /// The version marker read with the event is sent back, so Google refuses the
        /// write if the event changed elsewhere in the meantime rather than quietly
        /// overwriting it. The refusal arrives as 412, and the caller is expected to
        /// tell somebody rather than swallow it: a change that disappears without a
        /// word is the worst outcome available here.
        /// </summary>
        public async Task<AgendaEvent> UpdateAsync(AgendaEvent edited, CancellationToken token)
        {
            AgendaCalendar calendar = CalendarFor(edited.CalendarId);

            Event current = await _service.Events.Get(calendar.Id, edited.Id)
                                                 .ExecuteAsync(token).ConfigureAwait(false);

            EventsResource.UpdateRequest request =
                _service.Events.Update(ToGoogle(edited, current), calendar.Id, edited.Id);

            if (!string.IsNullOrEmpty(edited.ETag)) request.ETagAction = Google.Apis.ETagAction.IfMatch;

            Event saved = await request.ExecuteAsync(token).ConfigureAwait(false);

            Apply(calendar, saved);
            return Map(calendar, saved) ?? edited;
        }

        /// <summary>Removes an event. An event already gone is not an error.</summary>
        public async Task DeleteAsync(AgendaEvent item, CancellationToken token)
        {
            AgendaCalendar calendar = CalendarFor(item.CalendarId);

            try
            {
                await _service.Events.Delete(calendar.Id, item.Id)
                                     .ExecuteAsync(token).ConfigureAwait(false);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound
                                                    || ex.HttpStatusCode == HttpStatusCode.Gone)
            {
                // Deleted from the phone a moment ago. The desired state has been
                // reached by someone else, which is not a failure.
            }

            _known.Remove(KeyOf(calendar.Id, item.Id));
        }

        /// <summary>
        /// Copies our fields onto a Google event, leaving everything we do not model -
        /// guests, reminders, conferencing, recurrence - exactly as it was. That is
        /// why an update reads the event first: writing a fresh object would silently
        /// strip whatever this program does not know about.
        /// </summary>
        private static Event ToGoogle(AgendaEvent source, Event target)
        {
            target.Summary = source.Title;
            target.Location = string.IsNullOrWhiteSpace(source.Location) ? null : source.Location;
            target.Description = string.IsNullOrWhiteSpace(source.Description) ? null : source.Description;

            if (source.IsAllDay)
            {
                target.Start = new EventDateTime { Date = source.Start.ToString("yyyy-MM-dd") };

                // Google treats the end of an all-day event as exclusive: a single day
                // ends on the following one. Sending the same date makes an event with
                // no length, which the API rejects.
                DateTime last = source.End.Date <= source.Start.Date
                    ? source.Start.Date.AddDays(1)
                    : source.End.Date;

                target.End = new EventDateTime { Date = last.ToString("yyyy-MM-dd") };
            }
            else
            {
                target.Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(source.Start) };
                target.End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(source.End) };
            }

            return target;
        }

        // ======================================================================
        // HELPERS
        // ======================================================================

        private AgendaCalendar CalendarFor(string id)
        {
            if (!string.IsNullOrEmpty(id) && _calendars.TryGetValue(id, out AgendaCalendar? found))
                return found;

            AgendaCalendar? primary = _calendars.Values.FirstOrDefault(c => c.IsPrimary);
            if (primary != null) return primary;

            throw new InvalidOperationException("No writable calendar is known yet.");
        }

        private static string KeyOf(string calendarId, string eventId) => calendarId + "\n" + eventId;

        private static bool IsChosen(string calendarId, ISet<string> chosen) =>
            chosen.Count == 0 || chosen.Contains(calendarId);

        private void Forget(string calendarId)
        {
            _markers.Remove(calendarId);

            foreach (string key in _known.Keys.Where(k => k.StartsWith(calendarId + "\n", StringComparison.Ordinal)).ToList())
                _known.Remove(key);
        }

        /// <summary>
        /// The part of what we know that belongs on screen, in the order it is drawn.
        ///
        /// The window is applied here rather than when taking events in, because an
        /// incremental answer reports changes from the whole calendar and dropping
        /// them early would leave holes only a full reload could fill.
        /// </summary>
        private IReadOnlyList<AgendaEvent> Within(DateTime from, DateTime to, ISet<string> chosen)
        {
            return _known.Values
                .Where(e => IsChosen(e.CalendarId, chosen))
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
