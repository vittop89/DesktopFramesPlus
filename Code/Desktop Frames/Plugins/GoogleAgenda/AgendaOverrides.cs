using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>A time of day and a length: the hour of a task that only this program knows.</summary>
    public readonly struct AgendaHour
    {
        public AgendaHour(TimeSpan start, TimeSpan length)
        {
            Start = start;
            Length = length;
        }

        /// <summary>From midnight.</summary>
        public TimeSpan Start { get; }

        public TimeSpan Length { get; }

        /// <summary>
        /// A time inside the day and a length between a quarter of an hour and a day -
        /// the shapes a column of hours can draw. Anything else is refused rather than
        /// drawn wrong.
        /// </summary>
        public bool IsUsable =>
            Start >= TimeSpan.Zero && Start < TimeSpan.FromDays(1)
            && Length >= TimeSpan.FromMinutes(15) && Length <= TimeSpan.FromDays(1);
    }

    /// <summary>
    /// What this program adds to the tasks Google sends: a colour for the list each one
    /// is filed under, and an hour where Google gives none.
    ///
    /// Both exist because the Tasks service has no room for them. A list has no colour
    /// there at all. A task does have an hour in Google's own apps, but the service
    /// hands out only the date, and the one route the hour does travel - a stand-in
    /// entry in the calendar, see <see cref="AgendaMerge"/> - is not taken for a task
    /// that repeats. Measured, not assumed: a daily task with an hour had no stand-in
    /// on any day of the week that was read, while a one-off task beside it had one.
    ///
    /// Kept apart from the storing, so that the rule - what wins over what - is plain
    /// logic a test can hold down.
    /// </summary>
    public static class AgendaOverrides
    {
        /// <summary>
        /// Applies both to the tasks among <paramref name="items"/>, changing them in
        /// place - which holds for the same reason it does in <see cref="AgendaMerge"/>:
        /// the tasks are built fresh on every refresh.
        ///
        /// Google's hour wins over the one kept here. A task Google has placed stays
        /// where Google put it; the hour kept here is for the tasks Google leaves to the
        /// whole day, and only for those.
        /// </summary>
        public static void Apply(IEnumerable<AgendaEvent> items,
                                 IReadOnlyDictionary<string, string> listColours,
                                 IReadOnlyDictionary<string, AgendaHour> hours)
        {
            foreach (AgendaEvent item in items)
            {
                if (!item.IsTask) continue;

                if (listColours.TryGetValue(item.CalendarId, out string? colour)
                    && !string.IsNullOrWhiteSpace(colour))
                    item.ColourHex = colour;

                if (!item.IsAllDay) continue;

                if (!hours.TryGetValue(TaskKey(item.CalendarId, item.Title), out AgendaHour hour)
                    || !hour.IsUsable)
                    continue;

                DateTime day = item.Start.Date;

                item.Start = day + hour.Start;
                item.End = item.Start + hour.Length;
                item.IsAllDay = false;
                item.HourIsLocal = true;
            }
        }

        /// <summary>
        /// What a kept hour is filed under: the list and the title, not the task's
        /// identifier.
        ///
        /// A repeating task is the case this exists for, and nothing promises that its
        /// next occurrence keeps the identifier of this one - while it certainly keeps
        /// its title. Filed by list and title, "every day at eight" stays at eight.
        ///
        /// Hashed, so that the file on disk says nothing about what is on anybody's
        /// list. Titles can be private, and a settings file is not a place they should
        /// turn up.
        /// </summary>
        public static string TaskKey(string listId, string title)
        {
            string normal = (listId ?? string.Empty) + "\n"
                          + (title ?? string.Empty).Trim().ToUpperInvariant();

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normal));
            return Convert.ToHexString(hash, 0, 16);
        }
    }
}
