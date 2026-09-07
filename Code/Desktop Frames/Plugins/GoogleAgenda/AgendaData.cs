using Google.Apis.Auth.OAuth2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// Everything the frame knows, and nothing about how it looks.
    ///
    /// Two Google services, their lifetime, the join between them and the four ways
    /// an entry can be changed. Pulled out of the plugin because that class had
    /// become the thing it is easiest for a plugin to become - the place where the
    /// window, the timer, the network and the drawing all live, each making the
    /// others harder to follow.
    ///
    /// The split is on where failure shows up. A refresh that fails keeps what it
    /// has and says so through <see cref="Offline"/>, because a frame that empties
    /// itself when the network blinks is worse than one showing this morning's
    /// answer. A write that fails throws, because somebody is waiting to be told.
    /// Neither case opens a message box: this class has no idea there is a screen.
    /// </summary>
    public class AgendaData : IDisposable
    {
        private GoogleCalendarSource? _source;
        private GoogleTaskSource? _tasks;

        /// <summary>Guards against the timer starting a refresh that is already running.</summary>
        private bool _refreshing;

        /// <summary>Said once, not on every refresh, when a stand-in finds no task.</summary>
        private bool _warnedAboutStandIns;

        private static readonly IReadOnlyList<AgendaCalendar> NoCalendars = new List<AgendaCalendar>();
        private static readonly IReadOnlyList<AgendaTaskList> NoLists = new List<AgendaTaskList>();

        /// <summary>Every task list, until there is a reason to choose between them.</summary>
        private static readonly HashSet<string> EveryList = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>What was last read, in the order it should be drawn.</summary>
        public IReadOnlyList<AgendaEvent> Events { get; private set; } = new List<AgendaEvent>();

        /// <summary>
        /// True once an answer has arrived, so an empty agenda can be told apart from
        /// one nobody has asked for yet.
        /// </summary>
        public bool LoadedOnce { get; private set; }

        /// <summary>True when the last attempt failed. What was read before is kept.</summary>
        public bool Offline { get; private set; }

        /// <summary>Whether there is a signed-in session to ask.</summary>
        public bool IsOpen => _source != null;

        public IReadOnlyList<AgendaCalendar> Calendars => _source?.Calendars ?? NoCalendars;

        public IReadOnlyList<AgendaTaskList> TaskLists => _tasks?.TaskLists ?? NoLists;

        /// <summary>
        /// Opens the services for a granted session, or closes them when there is
        /// none. Called whenever the session changes, including when it turns out
        /// there was never one.
        /// </summary>
        public void Follow(UserCredential? credential)
        {
            Close();

            if (credential == null) return;

            _source = new GoogleCalendarSource(credential);
            _tasks = new GoogleTaskSource(credential);
        }

        private void Close()
        {
            _source?.Dispose();
            _source = null;

            _tasks?.Dispose();
            _tasks = null;

            Events = new List<AgendaEvent>();
            LoadedOnce = false;
            Offline = false;
        }

        /// <summary>
        /// Asks both services for the range and puts the answers together.
        ///
        /// Does not throw. A failure leaves <see cref="Events"/> as it was and sets
        /// <see cref="Offline"/>; the next attempt is a minute away.
        /// </summary>
        public async Task RefreshAsync(DateTime from, DateTime to, ISet<string> calendars)
        {
            GoogleCalendarSource? source = _source;
            if (source == null || _refreshing) return;

            _refreshing = true;

            try
            {
                GoogleTaskSource? tasks = _tasks;

                IReadOnlyList<AgendaEvent> events = await Task.Run(
                    () => source.RefreshAsync(from, to, calendars, CancellationToken.None))
                    .ConfigureAwait(true);

                IReadOnlyList<AgendaEvent> due = tasks == null
                    ? new List<AgendaEvent>()
                    : await Task.Run(() => tasks.LoadAsync(from, to, EveryList, CancellationToken.None))
                                .ConfigureAwait(true);

                // The source may have been replaced while the answer was in flight - a
                // sign-out, a settings change - and applying it then would show events
                // belonging to a session that no longer exists.
                if (_source != source) return;

                // Joined, not concatenated: a task with an hour arrives from both
                // services, and showing both halves is showing one thing twice.
                List<AgendaEvent> together = AgendaMerge.Join(events, due, out int unjoined);

                if (unjoined > 0 && !_warnedAboutStandIns)
                {
                    _warnedAboutStandIns = true;
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                        $"GoogleAgenda: {unjoined} scheduled tasks came from Calendar with no " +
                        "matching task, so they stay as untitled blocks.");
                }

                // Sorted here rather than kept apart, because a day is one thing: a
                // task due at eleven belongs between the ten o'clock meeting and the
                // noon one, not in a list of its own underneath.
                Events = together
                    .OrderBy(e => e.IsAllDay ? e.Start.Date : e.Start)
                    .ThenBy(e => e.Title, StringComparer.CurrentCulture)
                    .ToList();

                LoadedOnce = true;
                Offline = false;
            }
            catch (Exception ex)
            {
                Offline = true;

                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: refresh failed: {ex.Message}");
            }
            finally
            {
                _refreshing = false;
            }
        }

        // ======================================================================
        // CHANGING THINGS
        //
        // These throw. Somebody pressed a button and is waiting to hear whether it
        // worked, and only the caller knows how to tell them.
        // ======================================================================

        public async Task SaveAsync(AgendaEvent item, bool isNew)
        {
            if (item.IsTask)
            {
                GoogleTaskSource? tasks = _tasks;
                if (tasks == null) return;

                if (isNew) await tasks.CreateAsync(item, CancellationToken.None).ConfigureAwait(true);
                else await tasks.UpdateAsync(item, CancellationToken.None).ConfigureAwait(true);

                return;
            }

            GoogleCalendarSource? source = _source;
            if (source == null) return;

            if (isNew) await source.CreateAsync(item, CancellationToken.None).ConfigureAwait(true);
            else await source.UpdateAsync(item, CancellationToken.None).ConfigureAwait(true);
        }

        public async Task DeleteAsync(AgendaEvent item)
        {
            if (item.IsTask)
            {
                GoogleTaskSource? tasks = _tasks;
                if (tasks == null) return;

                await tasks.DeleteAsync(item, CancellationToken.None).ConfigureAwait(true);
                return;
            }

            GoogleCalendarSource? source = _source;
            if (source == null) return;

            await source.DeleteAsync(item, CancellationToken.None).ConfigureAwait(true);
        }

        public async Task SetDoneAsync(AgendaEvent item, bool done)
        {
            GoogleTaskSource? tasks = _tasks;
            if (tasks == null || !item.IsTask) return;

            await tasks.SetDoneAsync(item, done, CancellationToken.None).ConfigureAwait(true);
        }

        public void Dispose()
        {
            Close();
        }
    }
}
