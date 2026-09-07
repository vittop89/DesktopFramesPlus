using System;
using System.Collections.Generic;
using System.Linq;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// What one frame remembers: which calendars it shows, how it lays them out, and
    /// how far ahead it looks.
    ///
    /// Per frame rather than per program, because two agenda frames side by side - a
    /// month on the left, today on the right - is the arrangement this plugin exists
    /// to allow.
    ///
    /// Values travel to disk as they are written here and never through the
    /// translations. A setting saved as "Week" has to still read as "Week" after
    /// somebody changes the program's language, or the frame would come back showing
    /// something else.
    /// </summary>
    public class AgendaSettings
    {
        private const string ViewKey = "AgendaView";
        private const string CalendarsKey = "AgendaCalendars";
        private const string DaysKey = "AgendaDays";

        public AgendaView View { get; set; } = AgendaView.List;

        /// <summary>Empty means every calendar, which is what an unconfigured frame should show.</summary>
        public HashSet<string> Calendars { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Days ahead the list view covers. Only the list view uses it; the others have a length of their own.</summary>
        public int DaysAhead { get; set; } = 14;

        public static AgendaSettings Read(IDictionary<string, object>? from)
        {
            var settings = new AgendaSettings();
            if (from == null) return settings;

            if (from.TryGetValue(ViewKey, out object? view)
                && Enum.TryParse(view?.ToString(), ignoreCase: true, out AgendaView parsed))
                settings.View = parsed;

            if (from.TryGetValue(DaysKey, out object? days)
                && int.TryParse(days?.ToString(), out int parsedDays))
                settings.DaysAhead = Math.Max(1, Math.Min(60, parsedDays));

            if (from.TryGetValue(CalendarsKey, out object? calendars))
            {
                // One line rather than a list, because the settings dictionary travels
                // as loosely typed values and a string survives that trip unharmed.
                foreach (string id in (calendars?.ToString() ?? string.Empty)
                         .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    settings.Calendars.Add(id.Trim());
            }

            return settings;
        }

        public void WriteTo(IDictionary<string, object>? into)
        {
            if (into == null) return;

            into[ViewKey] = View.ToString();
            into[DaysKey] = DaysAhead;
            into[CalendarsKey] = string.Join("\n", Calendars);
        }

        /// <summary>
        /// The days the current view actually shows.
        ///
        /// A pair of dates rather than a length, because two of the views begin before
        /// today: a week starts on its own first day, and a month grid starts on
        /// whatever day of the previous month fills the first row. Asking the source
        /// for "the next N days" would have quietly emptied the part of the week that
        /// has already happened.
        /// </summary>
        public (DateTime From, DateTime To) Range(DateTime anchor)
        {
            switch (View)
            {
                case AgendaView.Day:
                    return (anchor.Date, anchor.Date.AddDays(1));

                case AgendaView.ThreeDays:
                    return (anchor.Date, anchor.Date.AddDays(3));

                case AgendaView.Week:
                {
                    DateTime start = StartOfWeek(anchor);
                    return (start, start.AddDays(7));
                }

                case AgendaView.Month:
                {
                    DateTime first = new DateTime(anchor.Year, anchor.Month, 1);
                    DateTime start = StartOfWeek(first);
                    return (start, start.AddDays(42));
                }

                default:
                    // The list answers "what is coming", which is a question about now.
                    // Moving it backwards would turn it into a different view.
                    return (DateTime.Today, DateTime.Today.AddDays(DaysAhead));
            }
        }

        /// <summary>How far one press of the arrows moves, in the units the view is made of.</summary>
        public DateTime Step(DateTime anchor, int direction) => View switch
        {
            AgendaView.Day => anchor.AddDays(direction),
            AgendaView.ThreeDays => anchor.AddDays(3 * direction),
            AgendaView.Week => anchor.AddDays(7 * direction),
            AgendaView.Month => anchor.AddMonths(direction),
            _ => anchor
        };

        /// <summary>False for the list, which is anchored to today by definition.</summary>
        public bool CanNavigate => View != AgendaView.List;

        public static DateTime StartOfWeek(DateTime day)
        {
            DayOfWeek first = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            int back = ((int)day.DayOfWeek - (int)first + 7) % 7;
            return day.Date.AddDays(-back);
        }

        public bool SameCalendarsAs(AgendaSettings other) =>
            Calendars.SetEquals(other.Calendars);
    }
}
