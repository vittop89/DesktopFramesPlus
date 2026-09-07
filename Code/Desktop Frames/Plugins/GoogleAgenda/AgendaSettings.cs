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
        /// How far the source has to look for the current view. A month needs the whole
        /// grid, which can start in the previous month and end in the next.
        /// </summary>
        public TimeSpan FetchWindow => View switch
        {
            AgendaView.Day => TimeSpan.FromDays(2),
            AgendaView.Week => TimeSpan.FromDays(8),
            AgendaView.Month => TimeSpan.FromDays(45),
            _ => TimeSpan.FromDays(DaysAhead + 1)
        };

        public bool SameCalendarsAs(AgendaSettings other) =>
            Calendars.SetEquals(other.Calendars);
    }
}
