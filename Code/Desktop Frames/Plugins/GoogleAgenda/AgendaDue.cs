using System;
using System.Globalization;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// Reads the due date Google Tasks sends.
    ///
    /// Google writes a date as midnight in UTC: "2026-09-07T00:00:00.000Z" means the
    /// seventh, wherever the reader is. Converted to local time like any other moment,
    /// it moves - west of Greenwich, midnight UTC is still the evening before, and every
    /// task landed a day early. So the date is taken as written instead.
    /// </summary>
    public static class AgendaDue
    {
        /// <summary>
        /// The day a task is due, or its moment when it carries an hour. False for
        /// anything that is not a timestamp, which leaves the task off the agenda: it
        /// cannot be put on a day nobody can read.
        /// </summary>
        public static bool TryRead(string? due, out DateTime start, out bool hasTime)
        {
            start = default;
            hasTime = false;

            if (string.IsNullOrWhiteSpace(due)) return false;

            if (!DateTimeOffset.TryParse(due, CultureInfo.InvariantCulture,
                                         DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
                return false;

            // Measured against a real account rather than assumed: a task given
            // 18:00-19:00 in the Google Calendar interface comes back from this API as
            // 00:00:00.000Z. The hour is real on Google's side and simply does not
            // cross the wire, so in practice this is always false - but it is written
            // as a question rather than as "false" so that a task that ever does carry
            // an hour lands at that hour instead of silently becoming an all-day one.
            hasTime = parsed.TimeOfDay != TimeSpan.Zero;

            start = hasTime ? parsed.LocalDateTime : parsed.Date;
            return true;
        }
    }
}
